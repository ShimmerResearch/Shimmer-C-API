using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// The firmware switches its Bluetooth CRC while it processes SET_CRC_COMMAND, before it builds the
    /// ACK, so the ACK is framed in the new mode: a bare 0xFF when the CRC goes off, and 0xFF followed by
    /// the CRC of 0xFF when it goes on (shimmer3r-firmware shimmer_bt_comms.c:1262 and :2344 at
    /// LogAndStream_Shimmer3R_v1.00.010, log-and-stream-common Comms/shimmer_bt_uart.c:944 and :2422 at
    /// v1.01.017). Every tagged LogAndStream release with the command does this, Shimmer3's too. The CRC
    /// of a bare ACK is always F4 65, and 0x65 is DETECT_EXPANSION_BOARD_RESPONSE. Each case runs the
    /// API's own read loop against a device that answers as the firmware does, switches the CRC, and
    /// then reads the sampling rate, whose reply must arrive intact in the new mode.
    /// </summary>
    [TestClass]
    public class ShimmerCrcSwitchTest
    {
        const int Shimmer3R = (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R;
        static readonly ShimmerBluetooth.BTCRCMode Off = ShimmerBluetooth.BTCRCMode.OFF;
        static readonly ShimmerBluetooth.BTCRCMode OneByte = ShimmerBluetooth.BTCRCMode.ONE_BYTE;
        static readonly ShimmerBluetooth.BTCRCMode TwoByte = ShimmerBluetooth.BTCRCMode.TWO_BYTE;

        /// <summary>
        /// The CRC of a bare ACK, computed with the API's own function, is what follows the ACK on the wire.
        /// </summary>
        [TestMethod]
        public void TestTheCRCOfABareAckIsF465()
        {
            CollectionAssert.AreEqual(new byte[] { 0xF4, 0x65 }, ShimmerBluetooth.ShimmerUartCrcCalc(new byte[] { 0xFF }, 1));
            Assert.AreEqual(0x65, (int)ShimmerBluetooth.PacketTypeShimmer3.DETECT_EXPANSION_BOARD_RESPONSE);
        }

        [TestMethod]
        public void TestOffToOneByte()
        {
            AssertSwitch(Off, OneByte);
        }

        /// <summary>
        /// The ACK is FF F4 65. Read in the old mode, 0x65 was taken for an expansion board response, which
        /// took the next reply's ACK for a length of 255 and swallowed the rest of that reply, and whatever
        /// followed, until a read timed out. ShimmerConsoleAppExample connects this way.
        /// </summary>
        [TestMethod]
        public void TestOffToTwoByte()
        {
            AssertSwitch(Off, TwoByte);
        }

        [TestMethod]
        public void TestOneByteToOff()
        {
            AssertSwitch(OneByte, Off);
        }

        [TestMethod]
        public void TestTwoByteToOff()
        {
            AssertSwitch(TwoByte, Off);
        }

        /// <summary>
        /// The ACK is FF F4. Read in the old mode, F4 was taken for a response with two CRC bytes to drop.
        /// That passed only by timing: the drop loop rereads the mode for each byte, and WriteCRCMode had
        /// switched to one byte before the next reply arrived, so only that reply's ACK was dropped.
        /// </summary>
        [TestMethod]
        public void TestTwoByteToOneByte()
        {
            AssertSwitch(TwoByte, OneByte);
        }

        [TestMethod]
        public void TestOneByteToTwoByte()
        {
            AssertSwitch(OneByte, TwoByte);
        }

        /// <summary>Every switch in turn on one link, as shimmer-web-sdk#176 tested on hardware.</summary>
        [TestMethod]
        public void TestEverySwitchInTurn()
        {
            var device = new CrcLinkDevice(Shimmer3R, 1, 1, 17, Off);
            device.StartReading();
            try
            {
                int ticks = 64;
                foreach (var mode in new[] { OneByte, TwoByte, OneByte, Off, TwoByte, Off })
                {
                    device.WriteCRCMode(mode);
                    ticks *= 2;
                    AssertReadsSamplingRate(device, ticks, "after switching to " + mode);
                    Assert.AreEqual(mode, device.GetCRCMode());
                }
            }
            finally
            {
                device.StopReadingAndWait();
            }
        }

        /// <summary>
        /// When no ACK arrives within WriteCRCMode's wait, the mode is still applied as it always was, and
        /// a late ACK, framed in that mode, does not stop the next reply being read.
        /// </summary>
        [TestMethod]
        public void TestALateAckStillLeavesTheNewMode()
        {
            var device = new CrcLinkDevice(Shimmer3R, 1, 1, 17, Off);
            device.StartReading();
            try
            {
                device.HoldReplies = true;
                device.WriteCRCMode(TwoByte);
                Assert.AreEqual(TwoByte, device.GetCRCMode());
                device.HoldReplies = false;
                device.ReleaseHeldReplies();
                AssertReadsSamplingRate(device, 256, "after a late ACK");
            }
            finally
            {
                device.StopReadingAndWait();
            }
        }

        /// <summary>
        /// An ACK that never arrives leaves the mode applied at the end of the wait, and the next reply's
        /// ACK is not taken for it.
        /// </summary>
        [TestMethod]
        public void TestAnAckThatNeverArrivesIsNotTakenFromTheNextReply()
        {
            var device = new CrcLinkDevice(Shimmer3R, 1, 1, 17, Off);
            device.StartReading();
            try
            {
                device.HoldReplies = true;
                device.WriteCRCMode(TwoByte);
                device.HoldReplies = false;
                Assert.AreEqual(TwoByte, device.GetCRCMode());
                AssertReadsSamplingRate(device, 256, "after an ACK that never arrived");
            }
            finally
            {
                device.StopReadingAndWait();
            }
        }

        /// <summary>
        /// A stream started straight after turning a two-byte CRC on. Read in the old mode, the ACK left
        /// 0x65 behind, which took the start command's ACK for a length and swallowed the first 255 bytes
        /// of the stream. So the API never reached the streaming state and delivered no packet, and the
        /// expansion board read at connect became "Unknown".
        /// </summary>
        [TestMethod]
        public void TestAStreamStartedStraightAfterTurningTwoByteOnIsRead()
        {
            var device = new CrcLinkDevice(Shimmer3R, 1, 1, 17, Off);
            int packets = 0;
            device.UICallback += (sender, args) =>
            {
                if (((CustomEventArgs)args).getIndicator() == (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_DATA_PACKET)
                {
                    Interlocked.Increment(ref packets);
                }
            };
            device.StartReading();
            try
            {
                device.WriteCRCMode(TwoByte);
                device.StartStreaming();
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(2 * device.ReadTimeout);
                while (packets < 10 && DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(10);
                }
                Assert.AreEqual("Streaming", device.GetStateString(), "The start command's ACK was not seen");
                Assert.IsTrue(packets >= 10, packets + " packets delivered of " + device.PacketsSent + " sent");
                Assert.AreEqual("None", device.GetExpansionBoard(), "An expansion board response was parsed");
            }
            finally
            {
                device.StopReadingAndWait();
            }
        }

        static void AssertSwitch(ShimmerBluetooth.BTCRCMode from, ShimmerBluetooth.BTCRCMode to)
        {
            var device = new CrcLinkDevice(Shimmer3R, 1, 1, 17, from);
            device.StartReading();
            try
            {
                device.WriteCRCMode(to);
                Assert.AreEqual(to, device.DeviceCRCMode);
                Assert.AreEqual(to, device.GetCRCMode());
                AssertReadsSamplingRate(device, 320, from + " to " + to);
                AssertReadsSamplingRate(device, 1280, from + " to " + to + ", second read");
            }
            finally
            {
                device.StopReadingAndWait();
            }
        }

        internal static void AssertReadsSamplingRate(CrcLinkDevice device, int ticks, string when)
        {
            double expected = (double)32768 / ticks;
            Assert.AreNotEqual(expected, device.GetSamplingRate(), "Pick a rate the API does not already have");
            device.SamplingRateTicks = ticks;
            device.ReadSamplingRate();
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(2 * device.ReadTimeout);
            while (device.GetSamplingRate() != expected && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(10);
            }
            Assert.AreEqual(expected, device.GetSamplingRate(), "The sampling rate reply was lost or misread " + when);
        }
    }

    /// <summary>
    /// A CrcFirmwareDevice that also answers, as the firmware does: it applies SET_CRC_COMMAND before it
    /// builds the ACK, and frames every reply in its own CRC mode. The API's own read loop parses the
    /// replies, and a read with nothing to read times out after ReadTimeout, as a serial port's does.
    /// It answers GET_SAMPLING_RATE_COMMAND with the sampling rate and every other command with an ACK,
    /// and after START_STREAMING_COMMAND it sends a data packet every 20 ms.
    /// </summary>
    class CrcLinkDevice : CrcFirmwareDevice
    {
        const byte Ack = (byte)PacketTypeShimmer2.ACK_COMMAND;
        const byte SetCrcCommand = 0x8B;
        const int DataPacketSize = 10;

        public BTCRCMode DeviceCRCMode;
        public int SamplingRateTicks = 640;
        /// <summary>Keeps replies back until ReleaseHeldReplies, as a slow link would.</summary>
        public bool HoldReplies;
        public int PacketsSent;

        readonly BlockingCollection<byte> received = new BlockingCollection<byte>();
        readonly List<byte> held = new List<byte>();
        volatile bool streaming;
        Thread streamThread;

        /// <summary>A link on which the device and the API are already in the given CRC mode.</summary>
        public CrcLinkDevice(int hardwareVersion, int major, int minor, int fwInternal, BTCRCMode crcMode)
            : base(hardwareVersion, major, minor, fwInternal)
        {
            DeviceCRCMode = crcMode;
            BluetoothCRCMode = crcMode;
            // As the inquiry and the expansion board read at connect would leave them
            IsFilled = true;
            PacketSize = DataPacketSize;
            ExpansionBoard = "None";
        }

        public void StartReading()
        {
            StopReading = false;
            ReadThread = new Thread(new ThreadStart(ReadData));
            ReadThread.IsBackground = true;
            ReadThread.Start();
        }

        public void StopReadingAndWait()
        {
            streaming = false;
            if (streamThread != null)
            {
                streamThread.Join();
            }
            StopReading = true;
            ReadThread.Join(2 * ReadTimeout);
        }

        public void ReleaseHeldReplies()
        {
            lock (received)
            {
                foreach (byte b in held)
                {
                    received.Add(b);
                }
                held.Clear();
            }
        }

        protected override int ReadByte()
        {
            byte b;
            if (!received.TryTake(out b, ReadTimeout))
            {
                throw new TimeoutException();
            }
            return b;
        }

        /// <summary>Only that packets arrive is under test, not what they hold.</summary>
        protected override ObjectCluster BuildMsg(List<byte> packet)
        {
            return new ObjectCluster("CrcLinkDevice", "CrcLinkDevice");
        }

        protected override void WriteBytes(byte[] b, int index, int length)
        {
            base.WriteBytes(b, index, length);
            List<byte> reply = new List<byte> { Ack };
            if (b[index] == SetCrcCommand)
            {
                DeviceCRCMode = (BTCRCMode)b[index + 1];
            }
            else if (b[index] == (byte)PacketTypeShimmer2.GET_SAMPLING_RATE_COMMAND)
            {
                reply.Add((byte)PacketTypeShimmer2.SAMPLING_RATE_RESPONSE);
                reply.Add((byte)(SamplingRateTicks & 0xFF));
                reply.Add((byte)((SamplingRateTicks >> 8) & 0xFF));
            }
            Send(reply);
            if (b[index] == (byte)PacketTypeShimmer2.START_STREAMING_COMMAND && streamThread == null)
            {
                streaming = true;
                streamThread = new Thread(new ThreadStart(SendPackets));
                streamThread.IsBackground = true;
                streamThread.Start();
            }
        }

        void SendPackets()
        {
            byte sample = 0;
            while (streaming)
            {
                List<byte> packet = new List<byte> { (byte)PacketTypeShimmer2.DATA_PACKET };
                for (int i = 0; i < DataPacketSize; i++)
                {
                    sample = (byte)(sample % 200 + 1);
                    packet.Add(sample);
                }
                Send(packet);
                Interlocked.Increment(ref PacketsSent);
                Thread.Sleep(20);
            }
        }

        /// <summary>Appends the CRC of the device's current mode, as calculateCrcAndInsert does.</summary>
        void Send(List<byte> message)
        {
            byte[] crc = ShimmerUartCrcCalc(message.ToArray(), message.Count);
            message.AddRange(crc.Take((int)DeviceCRCMode));
            lock (received)
            {
                if (HoldReplies)
                {
                    held.AddRange(message);
                }
                else
                {
                    foreach (byte x in message)
                    {
                        received.Add(x);
                    }
                }
            }
        }
    }
}
