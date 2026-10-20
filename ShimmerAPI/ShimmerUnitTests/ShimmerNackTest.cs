using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// A Shimmer answers a command it refuses with NACK (0xFE), framed like any reply: with the link CRC
    /// on, the CRC follows it (log-and-stream-common Comms/shimmer_bt_uart.c, ShimBt_sendRsp). The NACK's
    /// CRC must be dropped exactly once. While connected the drop after every reply that does not start
    /// with an ACK takes it; while streaming the NACK case does. A refused SET_CRC_COMMAND is refused
    /// before the firmware switches, so the link keeps its old mode. Each case runs the API's own read
    /// loop against CrcLinkDevice, refusing the command under test.
    /// </summary>
    [TestClass]
    public class ShimmerNackTest
    {
        const int Shimmer3R = (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R;
        const byte SetGsrRangeCommand = (byte)ShimmerBluetooth.PacketTypeShimmer2.SET_GSR_RANGE_COMMAND;
        const byte StartStreamingCommand = (byte)ShimmerBluetooth.PacketTypeShimmer2.START_STREAMING_COMMAND;
        const byte SetCrcCommand = 0x8B;
        static readonly ShimmerBluetooth.BTCRCMode Off = ShimmerBluetooth.BTCRCMode.OFF;
        static readonly ShimmerBluetooth.BTCRCMode OneByte = ShimmerBluetooth.BTCRCMode.ONE_BYTE;
        static readonly ShimmerBluetooth.BTCRCMode TwoByte = ShimmerBluetooth.BTCRCMode.TWO_BYTE;

        /// <summary>
        /// With the NACK's CRC dropped twice, the second drop took the next reply's first bytes: with a
        /// two-byte CRC that was the ACK and the response code, so the reply was lost.
        /// </summary>
        [TestMethod]
        public void TestAReplyAfterARefusedSetCommandIsRead()
        {
            foreach (var mode in new[] { Off, OneByte, TwoByte })
            {
                var device = new CrcLinkDevice(Shimmer3R, 1, 1, 17, mode);
                device.Refuse.Add(SetGsrRangeCommand);
                device.StartReading();
                try
                {
                    device.WriteGSRRange(1);
                    ShimmerCrcSwitchTest.AssertReadsSamplingRate(device, 320, mode + " after a refused SET");
                    device.WriteGSRRange(2);
                    ShimmerCrcSwitchTest.AssertReadsSamplingRate(device, 1280, mode + " after a second refused SET");
                }
                finally
                {
                    device.StopReadingAndWait();
                }
            }
        }

        /// <summary>
        /// The second drop took the start command's ACK, in either CRC mode, so the API stayed connected
        /// and delivered no data while the device streamed.
        /// </summary>
        [TestMethod]
        public void TestAStreamStartedAfterARefusedSetCommandIsRead()
        {
            foreach (var mode in new[] { Off, OneByte, TwoByte })
            {
                var device = new CrcLinkDevice(Shimmer3R, 1, 1, 17, mode);
                device.Refuse.Add(SetGsrRangeCommand);
                var packets = CountPackets(device);
                device.StartReading();
                try
                {
                    device.WriteGSRRange(1);
                    device.StartStreaming();
                    WaitFor(() => Volatile.Read(ref packets.Value) >= 10, 2 * device.ReadTimeout);
                    Assert.AreEqual("Streaming", device.GetStateString(), "The start command's ACK was not seen, " + mode);
                    Assert.IsTrue(Volatile.Read(ref packets.Value) >= 10, packets.Value + " packets delivered of " + device.PacketsSent + " sent, " + mode);
                }
                finally
                {
                    device.StopReadingAndWait();
                }
            }
        }

        /// <summary>
        /// While streaming only the NACK case drops the NACK's CRC. Left in the stream, its bytes would be
        /// read as the start of a packet, and the packet held back for the next one's check would be thrown
        /// away. So every packet sent must arrive, apart from the last, which the API holds back.
        /// </summary>
        [TestMethod]
        public void TestARefusalWhileStreamingLosesNoPacket()
        {
            foreach (var mode in new[] { OneByte, TwoByte })
            {
                var device = new CrcLinkDevice(Shimmer3R, 1, 1, 17, mode);
                var packets = CountPackets(device);
                device.StartReading();
                try
                {
                    device.StartStreaming();
                    Assert.IsTrue(WaitFor(() => Volatile.Read(ref packets.Value) >= 10, 2 * device.ReadTimeout), "The stream did not start, " + mode);
                    device.Refuse.Add(SetGsrRangeCommand);
                    for (int i = 0; i < 3; i++)
                    {
                        device.WriteGSRRange(i);
                    }
                    Thread.Sleep(200);
                    device.StopSendingPackets();
                    // Let the read loop take everything sent, then compare
                    int last = -1;
                    while (last != Volatile.Read(ref packets.Value))
                    {
                        last = Volatile.Read(ref packets.Value);
                        Thread.Sleep(200);
                    }
                    Assert.AreEqual("Streaming", device.GetStateString(), mode.ToString());
                    Assert.IsTrue(last >= device.PacketsSent - 1,
                        last + " packets delivered of " + device.PacketsSent + " sent across three refusals, " + mode);
                }
                finally
                {
                    device.StopReadingAndWait();
                }
            }
        }

        /// <summary>
        /// The firmware refuses SET_CRC_COMMAND before it switches, and frames the NACK in the old mode.
        /// WriteCRCMode used to apply the new mode anyway when its wait ended, so the API and the device
        /// then disagreed and replies after that were misread.
        /// </summary>
        [TestMethod]
        public void TestARefusedCrcSwitchKeepsTheOldMode()
        {
            var switches = new[] { new[] { Off, TwoByte }, new[] { Off, OneByte }, new[] { OneByte, TwoByte }, new[] { TwoByte, Off } };
            foreach (var s in switches)
            {
                var device = new CrcLinkDevice(Shimmer3R, 1, 1, 17, s[0]);
                device.Refuse.Add(SetCrcCommand);
                device.StartReading();
                try
                {
                    device.WriteCRCMode(s[1]);
                    string when = s[0] + " to " + s[1] + ", refused";
                    Assert.AreEqual(s[0], device.DeviceCRCMode, "The device switched, " + when);
                    Assert.AreEqual(s[0], device.GetCRCMode(), "The API switched, " + when);
                    ShimmerCrcSwitchTest.AssertReadsSamplingRate(device, 320, when);
                    ShimmerCrcSwitchTest.AssertReadsSamplingRate(device, 1280, when + ", second read");
                }
                finally
                {
                    device.StopReadingAndWait();
                }
            }
        }

        /// <summary>A switch the device accepts after a refused one is applied as usual.</summary>
        [TestMethod]
        public void TestACrcSwitchAfterARefusedOneIsApplied()
        {
            var device = new CrcLinkDevice(Shimmer3R, 1, 1, 17, Off);
            device.Refuse.Add(SetCrcCommand);
            device.StartReading();
            try
            {
                device.WriteCRCMode(TwoByte);
                Assert.AreEqual(Off, device.GetCRCMode());
                device.Refuse.Remove(SetCrcCommand);
                device.WriteCRCMode(TwoByte);
                Assert.AreEqual(TwoByte, device.DeviceCRCMode);
                Assert.AreEqual(TwoByte, device.GetCRCMode());
                ShimmerCrcSwitchTest.AssertReadsSamplingRate(device, 320, "after an accepted switch");
            }
            finally
            {
                device.StopReadingAndWait();
            }
        }

        /// <summary>
        /// A refused START_STREAMING_COMMAND clears the wait for its ACK, so the next command's ACK does
        /// not put the API into the streaming state.
        /// </summary>
        [TestMethod]
        public void TestARefusedStartLeavesTheApiConnected()
        {
            foreach (var mode in new[] { Off, OneByte, TwoByte })
            {
                var device = new CrcLinkDevice(Shimmer3R, 1, 1, 17, mode);
                device.Refuse.Add(StartStreamingCommand);
                device.StartReading();
                try
                {
                    device.StartStreaming();
                    Thread.Sleep(300);
                    ShimmerCrcSwitchTest.AssertReadsSamplingRate(device, 320, mode + " after a refused start");
                    Assert.AreEqual("Connected", device.GetStateString(), "A later ACK was taken for the start command's, " + mode);
                }
                finally
                {
                    device.StopReadingAndWait();
                }
            }
        }

        /// <summary>The refusal is reported, where it used to end in "Connection lost".</summary>
        [TestMethod]
        public void TestARefusalIsReported()
        {
            var device = new CrcLinkDevice(Shimmer3R, 1, 1, 17, OneByte);
            device.Refuse.Add(SetGsrRangeCommand);
            int refusals = 0;
            device.UICallback += (sender, args) =>
            {
                CustomEventArgs e = (CustomEventArgs)args;
                if (e.getIndicator() == (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_NOTIFICATION_MESSAGE
                    && e.getObject() is string && ((string)e.getObject()).Contains("refused"))
                {
                    Interlocked.Increment(ref refusals);
                }
            };
            device.StartReading();
            try
            {
                device.WriteGSRRange(1);
                Assert.IsTrue(WaitFor(() => Volatile.Read(ref refusals) == 1, device.ReadTimeout), "The refusal was not reported");
                Assert.AreEqual("Connected", device.GetStateString());
            }
            finally
            {
                device.StopReadingAndWait();
            }
        }

        sealed class Counter
        {
            public int Value;
        }

        /// <summary>Counts the data packets the API delivers through its callback.</summary>
        static Counter CountPackets(CrcLinkDevice device)
        {
            var counter = new Counter();
            device.UICallback += (sender, args) =>
            {
                if (((CustomEventArgs)args).getIndicator() == (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_DATA_PACKET)
                {
                    Interlocked.Increment(ref counter.Value);
                }
            };
            return counter;
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
