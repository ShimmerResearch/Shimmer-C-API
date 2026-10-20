using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// With the Bluetooth CRC on, the firmware frames every reply as the ACK, then any response, then one
    /// CRC over both (log-and-stream-common Comms/shimmer_bt_uart.c ShimBt_sendRsp). So a SET command's
    /// reply is a bare ACK followed at once by its CRC, which is always F4 65, or F4 in one-byte mode.
    /// While connected, ReadData drops a reply's CRC after its response, and never after an ACK, so the
    /// CRC of a bare ACK was left in the stream. F4 was then read as a response code, and the CRC drop
    /// after it took the first byte of whatever came next, if it came within ReadTimeout. When that was
    /// the ACK to START_STREAMING_COMMAND, the API never saw it: it stayed connected and delivered no
    /// data while the device streamed. Each case runs the API's own read loop against CrcLinkDevice.
    /// </summary>
    [TestClass]
    public class ShimmerBareAckCrcTest
    {
        const int Shimmer3R = (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R;
        static readonly ShimmerBluetooth.BTCRCMode Off = ShimmerBluetooth.BTCRCMode.OFF;
        static readonly ShimmerBluetooth.BTCRCMode OneByte = ShimmerBluetooth.BTCRCMode.ONE_BYTE;
        static readonly ShimmerBluetooth.BTCRCMode TwoByte = ShimmerBluetooth.BTCRCMode.TWO_BYTE;

        /// <summary>WriteAccelRange waits 250 ms for its ACK, so the stream starts well within ReadTimeout of it.</summary>
        [TestMethod]
        public void TestAStreamStartedAfterASetCommandIsReadWithAOneByteCRC()
        {
            AssertAStreamStartedAfterASetCommandIsRead(OneByte);
        }

        [TestMethod]
        public void TestAStreamStartedAfterASetCommandIsReadWithATwoByteCRC()
        {
            AssertAStreamStartedAfterASetCommandIsRead(TwoByte);
        }

        [TestMethod]
        public void TestAStreamStartedAfterASetCommandIsReadWithNoCRC()
        {
            AssertAStreamStartedAfterASetCommandIsRead(Off);
        }

        /// <summary>
        /// The connect sequence sends one SET command after another. Every bare ACK's CRC has to go, or
        /// each one takes the next reply's ACK, and the last one the start command's.
        /// </summary>
        [TestMethod]
        public void TestAStreamStartedAfterSeveralSetCommandsIsRead()
        {
            foreach (var mode in new[] { OneByte, TwoByte })
            {
                var device = new CrcLinkDevice(Shimmer3R, 1, 1, 17, mode);
                AssertStreams(device, () =>
                {
                    device.WriteAccelRange(1);
                    device.WriteGSRRange(2);
                    device.WriteGyroRange(1);
                }, mode + " after three SET commands");
            }
        }

        /// <summary>
        /// The byte after an ACK is read to tell a bare ACK from a response, so a response that follows a
        /// bare ACK must still arrive whole, in either CRC mode.
        /// </summary>
        [TestMethod]
        public void TestAReplyAfterASetCommandIsRead()
        {
            foreach (var mode in new[] { OneByte, TwoByte })
            {
                var device = new CrcLinkDevice(Shimmer3R, 1, 1, 17, mode);
                device.StartReading();
                try
                {
                    int ticks = 64;
                    for (int n = 0; n < 3; n++)
                    {
                        device.WriteAccelRange(n % 2);
                        ticks *= 2;
                        ShimmerCrcSwitchTest.AssertReadsSamplingRate(device, ticks, mode + " after a SET command, round " + n);
                    }
                    Assert.AreEqual("None", device.GetExpansionBoard(), "A bare ACK's CRC was parsed as an expansion board response");
                }
                finally
                {
                    device.StopReadingAndWait();
                }
            }
        }

        static void AssertAStreamStartedAfterASetCommandIsRead(ShimmerBluetooth.BTCRCMode mode)
        {
            var device = new CrcLinkDevice(Shimmer3R, 1, 1, 17, mode);
            AssertStreams(device, () => device.WriteAccelRange(1), mode + " after WriteAccelRange");
        }

        static void AssertStreams(CrcLinkDevice device, Action before, string when)
        {
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
                before();
                device.StartStreaming();
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(2 * device.ReadTimeout);
                while (packets < 10 && DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(10);
                }
                Assert.AreEqual("Streaming", device.GetStateString(), "The start command's ACK was not seen " + when);
                Assert.IsTrue(packets >= 10, packets + " packets delivered of " + device.PacketsSent + " sent " + when);
            }
            finally
            {
                device.StopReadingAndWait();
            }
        }
    }
}
