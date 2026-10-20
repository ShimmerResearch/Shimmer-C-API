using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// A Shimmer3R SD header stores the low-noise accel (LSM6DSV) range in byte 11, bits 7:6, and
    /// ShimmerSDLog has to read it (DEV-1163). It used to be left at +/-2g, so GetLNAccelRange()
    /// reported +/-2g for every file, and a blank LN calibration block in the header fell back to the
    /// +/-2g default sensitivity: a +/-16g recording came out 8x too small. Each case builds a
    /// Shimmer3R file by hand with only the LN accel enabled.
    /// </summary>
    [TestClass]
    public class ShimmerSDLogLnAccelRangeTest
    {
        const int HeaderSizeS3R = 384;
        const int SampleRateDivider = 640; // 51.2 Hz
        const int IndexLnAccelCalib = 139;
        const int Range16g = 3;
        /// <summary>1g-ish at +/-16g: 2090 / 209 = 10 m/s2. At +/-2g it would read 1.25.</summary>
        static readonly int RawX = 2090;

        static readonly double[] DefaultSensitivityByRange = { 1672, 836, 418, 209 };

        /// <summary>Everything not set is zero, so the LN calibration block is blank unless one is given.</summary>
        static byte[] BuildHeader(int lnAccelRange, byte[] lnAccelCalib)
        {
            byte[] h = new byte[HeaderSizeS3R];

            // 0-1: sampling rate divider, little-endian
            h[0] = (byte)(SampleRateDivider & 0xFF);
            h[1] = (byte)((SampleRateDivider >> 8) & 0xFF);
            h[2] = 1; // buffer size

            // 3-7: enabled sensors. 0x80 in byte 3 = LN accel
            h[3] = (byte)ShimmerSDLog.SDLogHeader.ACCEL_LN;

            // 11: config setup byte 3, bits 7:6 = LN accel range
            h[11] = (byte)((lnAccelRange & 0x03) << 6);

            // 30-31: hardware version
            int hw = (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R;
            h[30] = (byte)((hw >> 8) & 0xFF);
            h[31] = (byte)(hw & 0xFF);

            // 34-39: LogAndStream v1.01.018
            h[35] = 0x03;
            h[37] = 0x01;
            h[38] = 0x01;
            h[39] = 18;

            // 139-159: LN accel calibration
            if (lnAccelCalib != null)
            {
                Array.Copy(lnAccelCalib, 0, h, IndexLnAccelCalib, lnAccelCalib.Length);
            }

            // 314: channel count, 315+: channel IDs. 0x00-0x02 LN accel X, Y, Z
            h[314] = 3;
            h[315] = 0x00;
            h[316] = 0x01;
            h[317] = 0x02;
            return h;
        }

        /// <summary>
        /// Offset and sensitivity as big-endian int16, then the alignment matrix as int8 x100. Zero
        /// offset and the Shimmer3R LN accel's default alignment.
        /// </summary>
        static byte[] CalibBlock(int sensitivity)
        {
            byte[] c = new byte[21];
            for (int axis = 0; axis < 3; axis++)
            {
                c[6 + (axis * 2)] = (byte)((sensitivity >> 8) & 0xFF);
                c[7 + (axis * 2)] = (byte)(sensitivity & 0xFF);
            }
            sbyte[] alignment = { -100, 0, 0, 0, 100, 0, 0, 0, -100 };
            for (int i = 0; i < alignment.Length; i++)
            {
                c[12 + i] = (byte)alignment[i];
            }
            return c;
        }

        /// <summary>Rows are timestamp(3) + LN accel X, Y, Z as little-endian int16.</summary>
        static ShimmerSDLog Parse(int lnAccelRange, byte[] lnAccelCalib)
        {
            // ShimmerSDLog takes the SD file number from the last three characters of the path
            string dir = Path.Combine(Path.GetTempPath(), "shimmer_sdlog_ln_accel_range_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "000");

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                byte[] header = BuildHeader(lnAccelRange, lnAccelCalib);
                fs.Write(header, 0, header.Length);
                for (int i = 0; i < 5; i++)
                {
                    int ticks = 0x1000 + (i * SampleRateDivider);
                    byte[] row = { (byte)ticks, (byte)(ticks >> 8), (byte)(ticks >> 16), (byte)RawX, (byte)(RawX >> 8), 0, 0, 0, 0 };
                    fs.Write(row, 0, row.Length);
                }
            }
            return new ShimmerSDLog(path);
        }

        static double CalibratedX(ShimmerSDLog sdLog)
        {
            ObjectCluster ojc = sdLog.ReadPacketMsg();
            Assert.IsNotNull(ojc, "a row is parsed");
            return ojc.GetData(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_X, ShimmerConfiguration.SignalFormats.CAL).Data;
        }

        [TestMethod]
        public void TestRangeIsReadFromTheHeader()
        {
            for (int range = 0; range <= Range16g; range++)
            {
                ShimmerSDLog sdLog = Parse(range, null);
                Assert.AreEqual(range, sdLog.GetLNAccelRange(), "range " + range);
                Assert.AreEqual(DefaultSensitivityByRange[range], sdLog.SensitivityMatrixAccel[0, 0],
                    "blank calibration falls back to the default for range " + range);
            }
        }

        /// <summary>The case that changed the numbers: no calibration in the header.</summary>
        [TestMethod]
        public void TestBlankCalibrationUsesTheDefaultForTheLoggedRange()
        {
            ShimmerSDLog sdLog = Parse(Range16g, null);
            Assert.AreEqual(10.0, Math.Abs(CalibratedX(sdLog)), 1e-9, "2090 counts at +/-16g, not +/-2g");
        }

        [TestMethod]
        public void TestHeaderCalibrationIsUsed()
        {
            ShimmerSDLog sdLog = Parse(Range16g, CalibBlock(200));
            Assert.AreEqual(Range16g, sdLog.GetLNAccelRange());
            Assert.AreEqual(200.0, sdLog.SensitivityMatrixAccel[0, 0]);
            Assert.AreEqual(RawX / 200.0, Math.Abs(CalibratedX(sdLog)), 1e-9);
        }
    }
}
