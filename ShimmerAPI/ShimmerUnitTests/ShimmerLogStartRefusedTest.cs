using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// DEV-1431: LogAndStream that cannot start a log (docked, on USB power, no SD card or an SD file
    /// error, logging turned off in its configuration) NACKs START_SDBT_COMMAND and starts nothing, not
    /// even the stream (log-and-stream-common#228). Earlier firmware ACKed it and did nothing. The API
    /// already read the NACK, and a refused start left it connected, but StartStreamingandLog had set
    /// isLogging when it sent the command, and nothing cleared it: the device was taken to be logging.
    /// <para>
    /// Now the refusal clears isLogging, leaves the API connected, raises LogStartRefusedMessage in
    /// place of the generic refusal, and the NACK's link CRC (C5, or C5 56) is dropped once, so the
    /// next reply is read. An ACKed start is unchanged. The Java driver's tests for the same refusal
    /// are API_00036_LogStartRefusedTest and API_00037_StreamingLogStartRefusedTest
    /// (Shimmer-Java-Android-API#345).
    /// </para>
    /// <para>
    /// This API sends no START_LOGGING_COMMAND (0x92), and StartStreamingandLog sends START_SDBT only
    /// from connected, so no refusal arrives between data packets of a running stream as it can in
    /// Java. ShimmerSDBT32Feet does take the stream as started before it sends START_SDBT, so its NACK
    /// is read by the streaming branch of ReadData: that is the mid-stream case here.
    /// </para>
    /// Each case runs the API's own read loop against CrcLinkDevice, as ShimmerNackTest does.
    /// </summary>
    [TestClass]
    public class ShimmerLogStartRefusedTest
    {
        const int Shimmer3R = (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R;
        const byte StartSdbtCommand = (byte)ShimmerBluetooth.PacketTypeShimmer3SDBT.START_SDBT_COMMAND;
        const byte SetGsrRangeCommand = (byte)ShimmerBluetooth.PacketTypeShimmer2.SET_GSR_RANGE_COMMAND;
        const byte Nack = (byte)ShimmerBluetooth.PacketTypeShimmer2.NACK_COMMAND;
        static readonly ShimmerBluetooth.BTCRCMode[] Modes =
            { ShimmerBluetooth.BTCRCMode.OFF, ShimmerBluetooth.BTCRCMode.ONE_BYTE, ShimmerBluetooth.BTCRCMode.TWO_BYTE };

        /// <summary>The NACK as the firmware frames it with each link CRC: FE, FE C5, FE C5 56.</summary>
        [TestMethod]
        public void TestTheNackCarriesTheLinkCrc()
        {
            CollectionAssert.AreEqual(new byte[] { 0xC5, 0x56 }, ShimmerBluetooth.ShimmerUartCrcCalc(new byte[] { Nack }, 1));
        }

        /// <summary>
        /// Refused while idle: the device is not taken to be logging, the API stays connected and gets
        /// no data, the refusal is reported as a log start, and the next reply is read past the NACK's CRC.
        /// </summary>
        [TestMethod]
        public void TestARefusedLogStartLeavesTheDeviceNotLogging()
        {
            foreach (var mode in Modes)
            {
                var device = new CrcLinkDevice(Shimmer3R, 1, 1, 25, mode);
                device.Refuse.Add(StartSdbtCommand);
                var events = Record(device);
                device.StartReading();
                try
                {
                    device.StartStreamingandLog();
                    Assert.IsTrue(WaitFor(() => Volatile.Read(ref events.LogStartsRefused) == 1, device.ReadTimeout), "The refusal was not reported, " + mode);
                    Assert.IsFalse(Volatile.Read(ref device.isLogging), "The device was taken to be logging, " + mode);
                    Assert.AreEqual("Connected", device.GetStateString(), mode.ToString());
                    ShimmerCrcSwitchTest.AssertReadsSamplingRate(device, 320, mode + " after a refused log start");
                    Assert.AreEqual(0, Volatile.Read(ref events.Packets), "No stream was started, " + mode);
                    Assert.AreEqual(1, Volatile.Read(ref events.LogStartsRefused), "Reported once, " + mode);
                    Assert.AreEqual(0, Volatile.Read(ref events.OtherRefusals), "Reported as a log start, " + mode);
                    Assert.IsFalse(Volatile.Read(ref device.isLogging), "Still not logging, " + mode);
                    Assert.AreEqual("Connected", device.GetStateString(), "Still connected, " + mode);
                }
                finally
                {
                    device.StopReadingAndWait();
                }
            }
        }

        /// <summary>
        /// A start the firmware ACKs is unchanged: the device is taken to be logging and streaming, and
        /// nothing is reported refused. Firmware that is already logging ACKs it too.
        /// </summary>
        [TestMethod]
        public void TestAnAcknowledgedLogStartStillLogs()
        {
            foreach (var mode in Modes)
            {
                var device = new CrcLinkDevice(Shimmer3R, 1, 1, 25, mode);
                var events = Record(device);
                device.StartReading();
                try
                {
                    device.StartStreamingandLog();
                    Assert.IsTrue(WaitFor(() => Volatile.Read(ref events.Packets) >= 10, 2 * device.ReadTimeout), "The stream did not start, " + mode);
                    Assert.AreEqual("Streaming", device.GetStateString(), mode.ToString());
                    Assert.IsTrue(Volatile.Read(ref device.isLogging), "Not taken to be logging, " + mode);
                    Assert.AreEqual(0, Volatile.Read(ref events.LogStartsRefused) + Volatile.Read(ref events.OtherRefusals), "Nothing was refused, " + mode);
                }
                finally
                {
                    device.StopReadingAndWait();
                }
            }
        }

        /// <summary>
        /// Once a log start is ACKed, a later refusal is not taken for it: a SET refused mid-stream, its
        /// NACK and CRC between two data packets, leaves the device logging and the stream going.
        /// </summary>
        [TestMethod]
        public void TestARefusalWhileLoggingKeepsTheLog()
        {
            foreach (var mode in Modes)
            {
                var device = new CrcLinkDevice(Shimmer3R, 1, 1, 25, mode);
                var events = Record(device);
                device.StartReading();
                try
                {
                    device.StartStreamingandLog();
                    Assert.IsTrue(WaitFor(() => Volatile.Read(ref events.Packets) >= 10, 2 * device.ReadTimeout), "The stream did not start, " + mode);
                    device.Refuse.Add(SetGsrRangeCommand);
                    device.WriteGSRRange(1);
                    Assert.IsTrue(WaitFor(() => Volatile.Read(ref events.OtherRefusals) == 1, device.ReadTimeout), "The refusal was not reported, " + mode);
                    int before = Volatile.Read(ref events.Packets);
                    Assert.IsTrue(WaitFor(() => Volatile.Read(ref events.Packets) >= before + 10, 2 * device.ReadTimeout), "The stream stopped, " + mode);
                    Assert.AreEqual("Streaming", device.GetStateString(), mode.ToString());
                    Assert.IsTrue(Volatile.Read(ref device.isLogging), "The log was taken to be refused, " + mode);
                    Assert.AreEqual(0, Volatile.Read(ref events.LogStartsRefused), "Reported as a log start, " + mode);
                }
                finally
                {
                    device.StopReadingAndWait();
                }
            }
        }

        /// <summary>
        /// Refused while the API is streaming, as ShimmerSDBT32Feet's start leaves it: the streaming
        /// branch drops the NACK's CRC, and the refusal takes the API back to connected and not logging,
        /// so the next reply is read as a reply and not as stream.
        /// </summary>
        [TestMethod]
        public void TestALogStartRefusedWhileTakenToBeStreaming()
        {
            foreach (var mode in Modes)
            {
                var device = new StreamFirstDevice(mode);
                device.Refuse.Add(StartSdbtCommand);
                var events = Record(device);
                device.StartReading();
                try
                {
                    device.StartStreamingandLogStreamFirst();
                    Assert.IsTrue(WaitFor(() => Volatile.Read(ref events.LogStartsRefused) == 1, device.ReadTimeout), "The refusal was not reported, " + mode);
                    Assert.AreEqual("Connected", device.GetStateString(), "Still taken to be streaming, " + mode);
                    Assert.IsFalse(Volatile.Read(ref device.isLogging), "The device was taken to be logging, " + mode);
                    ShimmerCrcSwitchTest.AssertReadsSamplingRate(device, 320, mode + " after a log start refused while streaming");
                    Assert.AreEqual(0, Volatile.Read(ref events.Packets), "No stream was started, " + mode);
                    Assert.AreEqual(0, Volatile.Read(ref events.OtherRefusals), "Reported as a log start, " + mode);
                }
                finally
                {
                    device.StopReadingAndWait();
                }
            }
        }

        /// <summary>The same start, ACKed, leaves the API streaming and logging as before.</summary>
        [TestMethod]
        public void TestALogStartAcknowledgedWhileTakenToBeStreamingStillLogs()
        {
            foreach (var mode in Modes)
            {
                var device = new StreamFirstDevice(mode);
                var events = Record(device);
                device.StartReading();
                try
                {
                    device.StartStreamingandLogStreamFirst();
                    Assert.IsTrue(WaitFor(() => Volatile.Read(ref events.Packets) >= 10, 2 * device.ReadTimeout), "The stream was not read, " + mode);
                    Assert.AreEqual("Streaming", device.GetStateString(), mode.ToString());
                    Assert.IsTrue(Volatile.Read(ref device.isLogging), "Not taken to be logging, " + mode);
                    Assert.AreEqual(0, Volatile.Read(ref events.LogStartsRefused) + Volatile.Read(ref events.OtherRefusals), "Nothing was refused, " + mode);
                }
                finally
                {
                    device.StopReadingAndWait();
                }
            }
        }

        /// <summary>
        /// Starts as ShimmerSDBT32Feet.StartStreamingandLog does, which the tests cannot build (it needs
        /// 32feet): the API is put into streaming, then START_SDBT_COMMAND is sent.
        /// </summary>
        sealed class StreamFirstDevice : CrcLinkDevice
        {
            public StreamFirstDevice(ShimmerBluetooth.BTCRCMode crcMode) : base(Shimmer3R, 1, 1, 25, crcMode)
            {
            }

            public void StartStreamingandLogStreamFirst()
            {
                StreamingACKReceived = false;
                SetState(SHIMMER_STATE_STREAMING);
                isLogging = true;
                WriteStartSDBTCommand();
            }
        }

        sealed class Events
        {
            public int Packets;
            public int LogStartsRefused;
            public int OtherRefusals;
        }

        /// <summary>Counts the data packets and refusals the API reports through its callback.</summary>
        static Events Record(CrcLinkDevice device)
        {
            var events = new Events();
            device.UICallback += (sender, args) =>
            {
                CustomEventArgs e = (CustomEventArgs)args;
                if (e.getIndicator() == (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_DATA_PACKET)
                {
                    Interlocked.Increment(ref events.Packets);
                }
                else if (e.getIndicator() == (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_NOTIFICATION_MESSAGE)
                {
                    string message = e.getObject() as string;
                    if (message == ShimmerBluetooth.LogStartRefusedMessage)
                    {
                        Interlocked.Increment(ref events.LogStartsRefused);
                    }
                    else if (message != null && message.Contains("refused"))
                    {
                        Interlocked.Increment(ref events.OtherRefusals);
                    }
                }
            };
            return events;
        }

        static bool WaitFor(Func<bool> condition, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(10);
            }
            return condition();
        }
    }
}
