using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// Builds a Shimmer3 LogAndStream SD file by hand and reads it back through
    /// <see cref="ShimmerSDLog"/>.
    /// <para>
    /// Covers three things that were each independently broken, and which only show
    /// up together on a real recording:
    /// </para>
    /// <list type="number">
    /// <item>the 3-byte timestamp was unwrapped against a 2-byte modulo, so every
    /// roll-over added 65536 instead of 2^24 and a recording read as a negative
    /// duration;</item>
    /// <item>sync-when-logging blocks were not handled, so the 9-byte offset record
    /// at the head of each write buffer was consumed as sample data and the record
    /// stream lost alignment from the first block onwards;</item>
    /// <item>records the firmware never stamped were read as roll-overs, adding
    /// 512 seconds each.</item>
    /// </list>
    /// <para>
    /// Mirrors ADV_API_00031_ShimmerSDLogZeroTimestampTest in the Java driver. Both
    /// read the same files, so they have to agree.
    /// </para>
    /// </summary>
    [TestClass]
    public class ShimmerSDLogParseTest
    {
        private const int HeaderSize = 256;
        /// <summary>3-byte timestamp + gyro(6) + wide-range accel(6) + mag(6).</summary>
        private const int RowBytes = 21;
        /// <summary>32768 / 65 = 504.123 Hz, the rate the field evidence is in.</summary>
        private const int PeriodTicks = 65;
        private const int SampleRateDivider = 65;
        private const int SyncHeaderBytes = 9;
        private const int SdWriteBufSize = 512;
        private const double TicksPerSecond = 32768.0;

        private const int RowCount = 3000;
        /// <summary>Near the top of the 24-bit range, so the file contains a real wrap.</summary>
        private const long FirstTick = 0xFFF000L;

        private static byte[] BuildHeader(bool syncWhenLogging)
        {
            byte[] h = new byte[HeaderSize];

            // 0-1: sampling rate divider, little-endian
            h[0] = (byte)(SampleRateDivider & 0xFF);
            h[1] = (byte)((SampleRateDivider >> 8) & 0xFF);
            h[2] = 1; // buffer size

            // 3-7: enabled sensors. 0x60 = gyro | mag, 0x10 (byte 4) = wide-range accel
            h[3] = 0x60;
            h[4] = 0x10;

            // 16: trial config 0, bit 2 = sync when logging
            h[16] = (byte)(syncWhenLogging ? 0x04 : 0x00);
            h[17] = 0;
            h[18] = 54; // broadcast interval

            // 24-29: MAC
            h[24] = 0x00; h[25] = 0x06; h[26] = 0x66;
            h[27] = 0x80; h[28] = 0xE0; h[29] = 0xE1;

            // 30-31: hardware version - Shimmer3
            h[30] = 0x00; h[31] = 0x03;
            h[32] = 1; // trial id
            h[33] = 1; // number of shimmers

            // 34-39: LogAndStream v1.01.003
            h[34] = 0x00; h[35] = 0x03;
            h[36] = 0x00; h[37] = 0x01;
            h[38] = 0x01;
            h[39] = 0x03;

            // 214-216: expansion board, SR31-6-0
            h[214] = 31; h[215] = 6; h[216] = 0;

            // 251, 252-255: initial timestamp, split as the firmware writes it
            long initialTs = FirstTick;
            h[251] = (byte)((initialTs >> 32) & 0xFF);
            h[252] = (byte)(initialTs & 0xFF);
            h[253] = (byte)((initialTs >> 8) & 0xFF);
            h[254] = (byte)((initialTs >> 16) & 0xFF);
            h[255] = (byte)((initialTs >> 24) & 0xFF);

            return h;
        }

        private static void WriteRow(Stream outStream, long ticks, int seq)
        {
            byte[] row = new byte[RowBytes];
            row[0] = (byte)(ticks & 0xFF);
            row[1] = (byte)((ticks >> 8) & 0xFF);
            row[2] = (byte)((ticks >> 16) & 0xFF);
            // The sensor payload is irrelevant here, but make it non-constant so a
            // parser reading the wrong offset would not look plausible.
            for (int i = 3; i < RowBytes; i++)
            {
                row[i] = (byte)((seq + i) & 0xFF);
            }
            outStream.Write(row, 0, row.Length);
        }

        /// <param name="badRowIndexes">rows whose timestamp field is written as 00 00 00</param>
        private static string BuildFile(bool syncWhenLogging, int[] badRowIndexes)
        {
            // The reader takes the last three characters of the path as the SD file
            // number, so give it a name shaped like one the firmware writes.
            string dir = Path.Combine(Path.GetTempPath(), "shimmer_sdlog_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "000");

            int rowsPerBlock = (SdWriteBufSize - (syncWhenLogging ? SyncHeaderBytes : 0)) / RowBytes;

            using (var outStream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                byte[] header = BuildHeader(syncWhenLogging);
                outStream.Write(header, 0, header.Length);

                for (int i = 0; i < RowCount; i++)
                {
                    if (syncWhenLogging && (i % rowsPerBlock) == 0)
                    {
                        // Each write buffer opens with the node's sync offset. 0xFF
                        // means "no offset recorded", which is what an unsynced node
                        // writes.
                        byte[] syncHeader = new byte[SyncHeaderBytes];
                        for (int j = 0; j < SyncHeaderBytes; j++)
                        {
                            syncHeader[j] = 0xFF;
                        }
                        outStream.Write(syncHeader, 0, syncHeader.Length);
                    }

                    bool bad = Array.IndexOf(badRowIndexes, i) >= 0;
                    long ticks = (FirstTick + ((long)i * PeriodTicks)) & 0xFFFFFFL;
                    WriteRow(outStream, bad ? 0L : ticks, i);
                }
            }

            return path;
        }

        private static void DeleteFile(string path)
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(path), true);
            }
            catch (IOException)
            {
                // A temp file left behind must not fail a test.
            }
        }

        /// <summary>Calibrated timestamps, in milliseconds, for every record returned.</summary>
        private static List<double> ReadAllTimestamps(ShimmerSDLog sdLog)
        {
            var timestamps = new List<double>();
            while (!sdLog.EndOfFile)
            {
                ObjectCluster ojc = sdLog.ReadPacketMsg();
                if (ojc == null)
                {
                    break;
                }
                SensorData ts = ojc.GetData(
                    ShimmerConfiguration.SignalNames.TIMESTAMP,
                    ShimmerConfiguration.SignalFormats.CAL);
                Assert.IsNotNull(ts, "every record carries a calibrated timestamp");
                timestamps.Add(ts.Data);
            }
            return timestamps;
        }

        private static void AssertParsesCleanly(bool syncWhenLogging)
        {
            int[] bad = { 500, 1500 };
            string path = BuildFile(syncWhenLogging, bad);
            try
            {
                var sdLog = new ShimmerSDLog(path);

                Assert.AreEqual(syncWhenLogging, sdLog.IsSyncWhenLogging(),
                    "the sync flag is read out of the header");
                Assert.AreEqual(RowBytes, sdLog.SampleRecordSize(),
                    "gyro + wide-range accel + mag at 3-byte timestamps is a 21-byte record");
                if (syncWhenLogging)
                {
                    Assert.AreEqual((SdWriteBufSize - SyncHeaderBytes) / RowBytes, sdLog.SamplesPerBlock(),
                        "23 records fit between one offset record and the next");
                }
                else
                {
                    Assert.AreEqual(0, sdLog.SamplesPerBlock(), "no blocks without sync");
                }

                List<double> timestamps = ReadAllTimestamps(sdLog);

                Assert.AreEqual(RowCount - bad.Length, timestamps.Count,
                    "every record except the bad ones is returned");
                Assert.AreEqual(bad.Length, sdLog.RejectedTimestampRowCount,
                    "and the drops are reported");

                double periodMs = PeriodTicks / TicksPerSecond * 1000.0;
                double maxStep = 0;
                for (int i = 1; i < timestamps.Count; i++)
                {
                    double step = timestamps[i] - timestamps[i - 1];
                    Assert.IsTrue(step > 0, "timestamps are strictly increasing at record " + i);
                    maxStep = Math.Max(maxStep, step);
                }

                // A missed wrap, or a wrap unwrapped against the wrong modulo, shows
                // up here as a step of thousands of milliseconds.
                Assert.IsTrue(maxStep < 4 * periodMs,
                    "no step anywhere near a modulo (largest was " + maxStep + " ms)");

                // First to last: RowCount samples one period apart, and the two that
                // were dropped were in the middle, so the span is unchanged.
                double expectedSpanMs = (RowCount - 1) * periodMs;
                Assert.AreEqual(expectedSpanMs, timestamps[timestamps.Count - 1] - timestamps[0], 0.001,
                    "the recording spans exactly the samples it took");
            }
            finally
            {
                DeleteFile(path);
            }
        }

        [TestMethod]
        public void TestZeroTimestampRowsAreDropped()
        {
            AssertParsesCleanly(false);
        }

        /// <summary>Sync-when-logging puts a 9-byte offset record at the head of each block.</summary>
        [TestMethod]
        public void TestZeroTimestampRowsAreDroppedWithSyncBlocks()
        {
            AssertParsesCleanly(true);
        }

        /// <summary>A file with no bad records must be completely unaffected.</summary>
        [TestMethod]
        public void TestCleanFileIsUnchanged()
        {
            string path = BuildFile(false, new int[0]);
            try
            {
                var sdLog = new ShimmerSDLog(path);
                List<double> timestamps = ReadAllTimestamps(sdLog);

                Assert.AreEqual(RowCount, timestamps.Count, "every record is returned");
                Assert.AreEqual(0, sdLog.RejectedTimestampRowCount, "nothing was dropped");

                double periodMs = PeriodTicks / TicksPerSecond * 1000.0;
                for (int i = 1; i < timestamps.Count; i++)
                {
                    Assert.AreEqual(periodMs, timestamps[i] - timestamps[i - 1], 0.001,
                        "one sample period between consecutive records");
                }
            }
            finally
            {
                DeleteFile(path);
            }
        }

        /// <summary>
        /// The defect this file was built to catch: a 3-byte counter unwrapped against
        /// a 2-byte modulo. The file crosses 0xFFFFFF once, so a wrong modulo shows up
        /// as a duration that runs backwards.
        /// </summary>
        [TestMethod]
        public void TestRecordingDurationIsPositiveAcrossARollOver()
        {
            string path = BuildFile(false, new int[0]);
            try
            {
                var sdLog = new ShimmerSDLog(path);
                List<double> timestamps = ReadAllTimestamps(sdLog);

                double spanMs = timestamps[timestamps.Count - 1] - timestamps[0];
                Assert.IsTrue(spanMs > 0, "a recording cannot have a negative duration");
                Assert.AreEqual(RowCount * PeriodTicks / TicksPerSecond, spanMs / 1000.0, 0.01,
                    "and it lasts as long as the samples it holds");
            }
            finally
            {
                DeleteFile(path);
            }
        }

        /// <summary>
        /// The offset record is read out of the stream rather than decoded as sample
        /// data, and is available to a caller that wants it.
        /// </summary>
        [TestMethod]
        public void TestSyncOffsetRecordIsReadNotDecoded()
        {
            string path = BuildFile(true, new int[0]);
            try
            {
                var sdLog = new ShimmerSDLog(path);
                Assert.IsNull(sdLog.LastSyncOffset(), "nothing read yet");

                sdLog.ReadPacketMsg();
                byte[] offset = sdLog.LastSyncOffset();

                Assert.IsNotNull(offset, "the first record of a block is preceded by one");
                Assert.AreEqual(SyncHeaderBytes, offset.Length);
                foreach (byte b in offset)
                {
                    Assert.AreEqual(0xFF, b, "this file records no offset, so all bytes are 0xFF");
                }
            }
            finally
            {
                DeleteFile(path);
            }
        }
    }
}
