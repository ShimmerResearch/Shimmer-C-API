using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;
using ShimmerAPI.Utilities;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// What ReadData does with a stream that is not perfect: a packet stamped a few ticks off the
    /// grid, and a misframed packet it has to throw away. Runs the API's own read loop and its own
    /// timestamp unwrap against <see cref="TimelineDevice"/>, plus <see cref="LinkByteBuffer"/>, the
    /// byte buffer the BLE link now reads from.
    /// </summary>
    [TestClass]
    public class ShimmerStreamReadTest
    {
        const int Period = 128; // 256 Hz
        const int FirstTick = 0x010101;

        // --- LinkByteBuffer (DEV-1270) -------------------------------------------------------------

        [TestMethod]
        public void TestALinkBufferReadsBytesInTheOrderTheyCame()
        {
            var buffer = new LinkByteBuffer();
            buffer.Add(new byte[] { 1, 2 });
            buffer.Add(new byte[] { 3 });
            Assert.AreEqual(1, buffer.Read(100));
            Assert.AreEqual(2, buffer.Read(100));
            Assert.AreEqual(3, buffer.Read(100));
        }

        /// <summary>
        /// With nothing to read, a read waits and then times out, as a serial port's does. The BLE
        /// link's read used to spin instead: a whole core while connected, and for ever after
        /// Disconnect.
        /// </summary>
        [TestMethod]
        public void TestALinkBufferReadTimesOutWithoutSpinning()
        {
            var buffer = new LinkByteBuffer();
            TimeSpan cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
            var took = Stopwatch.StartNew();
            bool timedOut = false;
            try
            {
                buffer.Read(1000);
            }
            catch (TimeoutException)
            {
                timedOut = true;
            }
            took.Stop();
            Assert.IsTrue(timedOut, "the read returned a byte that was never added");
            TimeSpan cpu = Process.GetCurrentProcess().TotalProcessorTime - cpuBefore;
            Assert.IsTrue(took.ElapsedMilliseconds >= 900, "returned after " + took.ElapsedMilliseconds + " ms");
            Assert.IsTrue(cpu.TotalMilliseconds < 300, "the wait used " + cpu.TotalMilliseconds + " ms of CPU");
        }

        /// <summary>Closing the buffer, as closing the link does, wakes a read that is waiting. The read
        /// throws IOException, as a serial port's does when it is closed: a timeout would have ReadData
        /// poll the link that is going.</summary>
        [TestMethod]
        public void TestClosingALinkBufferReleasesAWaitingRead()
        {
            var buffer = new LinkByteBuffer();
            Exception thrown = null;
            var reader = new Thread(() =>
            {
                try
                {
                    buffer.Read(10000);
                }
                catch (Exception e)
                {
                    thrown = e;
                }
            });
            reader.Start();
            Thread.Sleep(200);
            var took = Stopwatch.StartNew();
            buffer.Close();
            Assert.IsTrue(reader.Join(1000), "the read was still waiting after the buffer closed");
            Assert.IsTrue(took.ElapsedMilliseconds < 500, "the read took " + took.ElapsedMilliseconds + " ms to see the close");
            Assert.IsInstanceOfType(thrown, typeof(IOException));
            buffer.Add(new byte[] { 1 });
            Assert.AreEqual(0, buffer.Count, "a closed buffer takes no more bytes");
        }

        // --- The alignment check's window (DEV-1273) ---------------------------------------------

        [TestMethod]
        public void TestTheAlignmentCheckPassesJitterAndGaps()
        {
            var device = new TimelineDevice(new List<byte>());
            Assert.IsTrue(device.packetTimeStampChecker(1000 + Period, 1000), "one period");
            Assert.IsTrue(device.packetTimeStampChecker(1000 + 137, 1000), "a Shimmer3R's off-grid stamp");
            Assert.IsTrue(device.packetTimeStampChecker(1000 + 5 * Period, 1000), "four packets lost");
            Assert.IsTrue(device.packetTimeStampChecker(Period - 50, (1 << 24) - 50), "across the counter's wrap");
            Assert.IsTrue(device.packetTimeStampChecker(1000 + 32768, 1000), "a second");
        }

        [TestMethod]
        public void TestTheAlignmentCheckFailsWhatAMisframedPacketCarries()
        {
            var device = new TimelineDevice(new List<byte>());
            Assert.IsFalse(device.packetTimeStampChecker(1000, 1000), "the same timestamp again");
            Assert.IsFalse(device.packetTimeStampChecker(950, 1000), "a step backwards");
            Assert.IsFalse(device.packetTimeStampChecker(1000 + 32769, 1000), "over a second ahead");
            Assert.IsFalse(device.packetTimeStampChecker(0x5A5A5A, 1000), "random bytes");
        }

        [TestMethod]
        public void TestTheAlignmentCheckPassesAnythingBeforeTheRateIsKnown()
        {
            var device = new TimelineDevice(new List<byte>());
            device.SetPeriod(0);
            Assert.IsTrue(device.packetTimeStampChecker(1000, 1000));
        }

        // --- Through ReadData ------------------------------------------------------------------------

        /// <summary>
        /// A Shimmer3R stamps the odd packet a few ticks off the grid. Every packet arrives, apart from
        /// the last, which the read loop holds back to check against the next. The check used to throw
        /// the off-grid packet and its predecessor away, then read a byte away and misalign the stream.
        /// </summary>
        [TestMethod]
        public void TestAPacketStampedOffTheGridIsDelivered()
        {
            var ticks = Enumerable.Range(0, 200).Select(i => FirstTick + i * Period).ToList();
            ticks[100] += 9;
            List<Delivered> delivered = Stream(Packets(ticks));
            Assert.AreEqual(199, delivered.Count, "packets delivered of 200");
            CollectionAssert.AreEqual(ticks.Take(199).ToList(), delivered.Select(d => d.RawTicks).ToList());
        }

        /// <summary>
        /// A misframed packet, whose timestamp is whatever bytes fell there: here an earlier one, which the
        /// unwrap reads as the counter's wrap. The check throws it away, with the packet held before it.
        /// The unwrap is put back as it was, so the packets after it carry on the timeline rather than
        /// 512 s later.
        /// </summary>
        [TestMethod]
        public void TestAMisframedPacketThrownAwayLeavesTheTimelineAlone()
        {
            var ticks = Enumerable.Range(0, 200).Select(i => FirstTick + i * Period).ToList();
            List<byte> bytes = Packets(ticks.Take(100));
            bytes.AddRange(Packet(5)); // an earlier timestamp: read as a wrap
            bytes.AddRange(Packets(ticks.Skip(100)));
            List<Delivered> delivered = Stream(bytes);

            Assert.IsFalse(delivered.Any(d => d.RawTicks == 5), "the misframed packet was delivered");
            Assert.IsTrue(delivered.Count >= 190, delivered.Count + " packets delivered");
            for (int i = 1; i < delivered.Count; i++)
            {
                double stepMs = delivered[i].CalibratedMs - delivered[i - 1].CalibratedMs;
                double ticksMs = (delivered[i].RawTicks - delivered[i - 1].RawTicks) / 32.768;
                Assert.AreEqual(ticksMs, stepMs, 0.001, "the calibrated timeline stepped " + stepMs + " ms where the ticks stepped " + ticksMs + " ms, at packet " + i);
            }
        }

        /// <summary>
        /// Disconnecting closes the link's buffer, which wakes the read thread. The closed buffer used to
        /// read as a timeout, at once and every time, so in the moments before Disconnect stopped the
        /// thread it counted five and wrote a status poll to the link being torn down. Over BLE that
        /// write failed where nothing could catch it, and ended the process.
        /// </summary>
        [TestMethod]
        public void TestDisconnectingAnIdleLinkWritesNothingToIt()
        {
            var device = new BufferedLinkDevice();
            var notifications = new List<string>();
            device.UICallback += (sender, args) =>
            {
                var e = (CustomEventArgs)args;
                if (e.getIndicator() == (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_NOTIFICATION_MESSAGE)
                {
                    lock (notifications)
                    {
                        notifications.Add(e.getObject() as string);
                    }
                }
            };
            Thread reader = device.StartReading();
            Thread.Sleep(200); // the read thread is waiting for a byte
            device.Disconnect();
            Assert.IsTrue(reader.Join(2000), "the read thread was still running after Disconnect");
            Assert.AreEqual(0, device.Writes, "writes to the link during Disconnect");
            CollectionAssert.DoesNotContain(notifications, "Connection lost");
        }

        // --- Helpers -------------------------------------------------------------------------------

        sealed class Delivered
        {
            public int RawTicks;
            public double CalibratedMs;
        }

        sealed class TimedObjectCluster : ObjectCluster
        {
            public double CalibratedMs;

            public TimedObjectCluster() : base("TimelineDevice", "TimelineDevice")
            {
            }
        }

        /// <summary>One data packet: the header, the 3-byte timestamp, and six data bytes with no zero
        /// among them, so a misaligned reader only finds a packet start where one is.</summary>
        static List<byte> Packet(int ticks)
        {
            return new List<byte>
            {
                (byte)ShimmerBluetooth.PacketTypeShimmer2.DATA_PACKET,
                (byte)(ticks & 0xFF), (byte)((ticks >> 8) & 0xFF), (byte)((ticks >> 16) & 0xFF),
                0x11, 0x12, 0x13, 0x14, 0x15, 0x16,
            };
        }

        static List<byte> Packets(IEnumerable<int> ticks)
        {
            return ticks.SelectMany(Packet).ToList();
        }

        static List<Delivered> Stream(List<byte> bytes)
        {
            var device = new TimelineDevice(bytes);
            var delivered = new List<Delivered>();
            device.UICallback += (sender, args) =>
            {
                var e = (CustomEventArgs)args;
                if (e.getIndicator() == (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_DATA_PACKET)
                {
                    var oc = (TimedObjectCluster)e.getObject();
                    lock (delivered)
                    {
                        delivered.Add(new Delivered { RawTicks = oc.RawTimeStamp, CalibratedMs = oc.CalibratedMs });
                    }
                }
            };
            device.Run();
            return delivered;
        }

        /// <summary>
        /// A Shimmer3R streaming 3-byte timestamps at 256 Hz, with the alignment check on, as the API
        /// sets it up for one. It reads the given bytes once, then stops the read loop. Its BuildMsg
        /// decodes only the timestamp, through the API's own CalibrateTimeStamp.
        /// </summary>
        sealed class TimelineDevice : ShimmerBluetooth
        {
            readonly List<byte> bytes;
            int next;

            public TimelineDevice(List<byte> bytes) : base("TimelineDevice")
            {
                this.bytes = bytes;
                HardwareVersion = (int)ShimmerVersion.SHIMMER3R;
                TimeStampPacketRawMaxValue = 1 << 24;
                SamplingRate = 32768.0 / Period;
                ADCRawSamplingRateValue = Period;
                mEnableTimeStampAlignmentCheck = true;
                IsFilled = true;
                PacketSize = 9;
            }

            public void SetPeriod(int ticks)
            {
                ADCRawSamplingRateValue = ticks;
            }

            /// <summary>Runs ReadData over the bytes and returns when it has read them all.</summary>
            public void Run()
            {
                StopReading = false;
                SetState(SHIMMER_STATE_STREAMING);
                var reader = new Thread(ReadData);
                reader.IsBackground = true;
                reader.Start();
                Assert.IsTrue(reader.Join(10000), "ReadData did not finish the stream");
            }

            protected override int ReadByte()
            {
                if (next >= bytes.Count)
                {
                    StopReading = true;
                    throw new TimeoutException();
                }
                return bytes[next++];
            }

            protected override ObjectCluster BuildMsg(List<byte> packet)
            {
                int ticks = packet[0] | (packet[1] << 8) | (packet[2] << 16);
                return new TimedObjectCluster { RawTimeStamp = ticks, CalibratedMs = CalibrateTimeStamp(ticks) };
            }

            public override string GetShimmerAddress()
            {
                return "TimelineDevice";
            }

            public override void SetShimmerAddress(string address)
            {
            }

            protected override void CloseConnection()
            {
            }

            protected override void FlushConnection()
            {
            }

            protected override void FlushInputConnection()
            {
            }

            protected override bool IsConnectionOpen()
            {
                return true;
            }

            protected override void OpenConnection()
            {
            }

            protected override void WriteBytes(byte[] b, int index, int length)
            {
            }
        }

        /// <summary>
        /// A LogAndStream unit, connected and idle, on a link that reads from a <see cref="LinkByteBuffer"/>
        /// as the BLE link does. Closing the link closes the buffer and then takes a while, as tearing a
        /// GATT connection down can: the read thread is awake for all of it.
        /// </summary>
        sealed class BufferedLinkDevice : ShimmerBluetooth
        {
            readonly LinkByteBuffer link = new LinkByteBuffer();
            volatile bool open = true;
            public int Writes;

            public BufferedLinkDevice() : base("BufferedLinkDevice")
            {
                HardwareVersion = (int)ShimmerVersion.SHIMMER3R;
                FirmwareIdentifier = FW_IDENTIFIER_LOGANDSTREAM;
            }

            /// <summary>Starts ReadData on the idle link, as Connect does once the unit is set up.</summary>
            public Thread StartReading()
            {
                StopReading = false;
                SetState(SHIMMER_STATE_CONNECTED);
                var reader = new Thread(ReadData);
                reader.IsBackground = true;
                reader.Start();
                return reader;
            }

            protected override int ReadByte()
            {
                return link.Read(ReadTimeout);
            }

            public override string GetShimmerAddress()
            {
                return "BufferedLinkDevice";
            }

            public override void SetShimmerAddress(string address)
            {
            }

            protected override void CloseConnection()
            {
                link.Close();
                Thread.Sleep(50);
                open = false;
            }

            protected override void FlushConnection()
            {
            }

            protected override void FlushInputConnection()
            {
            }

            protected override bool IsConnectionOpen()
            {
                return open;
            }

            protected override void OpenConnection()
            {
            }

            protected override void WriteBytes(byte[] b, int index, int length)
            {
                Interlocked.Increment(ref Writes);
            }
        }
    }
}
