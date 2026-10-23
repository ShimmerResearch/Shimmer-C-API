using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;
using ShimmerAPI.Utilities;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// Gyro on-the-fly offset calibration (derived-sensor bit 29), held bit-exact with the Java
    /// driver.
    /// <para>
    /// The reference is <see cref="GyroOnTheFlyVectors"/>: raw gyro and shimmer-host-core's
    /// calibrated gyro, as IEEE-754 bits, from three of its conformance traces (a Shimmer3R and a
    /// Shimmer3 Bluetooth session, and a Shimmer3R SD file). host-core is held to the Java
    /// driver's output to the last bit, so matching it is matching Consensys. The TypeScript web
    /// SDK runs the same cases. Each is long enough for the window to fill and the offset to be
    /// re-estimated many times; the Shimmer3 one starts from a stored offset of zero against a
    /// real offset of hundreds of counts, where the Commons Math correction terms matter most.
    /// </para>
    /// <para>
    /// Before this, the C# estimator was opt-in only, used a fixed 100-sample window, checked one
    /// sample after the window filled, and used a population standard deviation and a plain
    /// mean, so its offsets, and every calibrated gyro value after them, differed from Java's.
    /// </para>
    /// </summary>
    [TestClass]
    public class GyroOnTheFlyCalibrationTest
    {
        private static ulong BitsOf(double v)
        {
            return (ulong)BitConverter.DoubleToInt64Bits(v);
        }

        private static double[,] Diagonal(double[] d)
        {
            return new double[3, 3] { { d[0], 0, 0 }, { 0, d[1], 0 }, { 0, 0, d[2] } };
        }

        private static double[,] Matrix(double[] rowMajor)
        {
            double[,] m = new double[3, 3];
            for (int i = 0; i < 9; i++)
            {
                m[i / 3, i % 3] = rowMajor[i];
            }
            return m;
        }

        private static double[,] Column(double[] v)
        {
            return new double[3, 1] { { v[0] }, { v[1] }, { v[2] } };
        }

        /// <summary>Index of the first sample whose bits differ from host-core's, or -1.</summary>
        private static int FirstMismatch(GyroOnTheFlyVectors.Case c, List<double[]> output)
        {
            for (int i = 0; i < output.Count; i++)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    if (BitsOf(output[i][axis]) != c.ExpectedCalBits[i, axis])
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        private static double[] RawAt(GyroOnTheFlyVectors.Case c, int i)
        {
            return new double[] { c.Raw[i, 0], c.Raw[i, 1], c.Raw[i, 2] };
        }

        /// <summary>The estimator alone, calibrating each sample as BuildMsg does.</summary>
        private static List<double[]> Replay(GyroOnTheFlyVectors.Case c, bool onTheFly, out int updates)
        {
            double[,] alignment = Matrix(c.Alignment);
            double[,] sensitivity = Diagonal(c.Sensitivity);
            double[,] offset = Column(c.Offset);
            GyroOnTheFlyCalibration otf = new GyroOnTheFlyCalibration();
            otf.SetWindowSize(GyroOnTheFlyCalibration.WindowSizeFromSamplingRate(c.SamplingRate));
            updates = 0;
            List<double[]> output = new List<double[]>();
            for (int i = 0; i < c.Raw.GetLength(0); i++)
            {
                double[] raw = RawAt(c, i);
                double[] cal = UtilCalibration.CalibrateInertialSensorData((double[])raw.Clone(), alignment, sensitivity, offset);
                output.Add(cal);
                if (onTheFly)
                {
                    double[] estimate = otf.Update(cal, raw);
                    if (estimate != null)
                    {
                        offset = Column(estimate);
                        updates++;
                    }
                }
            }
            return output;
        }

        [TestMethod]
        public void BitExactWithHostCore()
        {
            foreach (GyroOnTheFlyVectors.Case c in GyroOnTheFlyVectors.Cases)
            {
                int updates;
                List<double[]> output = Replay(c, true, out updates);
                Assert.AreEqual(-1, FirstMismatch(c, output), c.Name);
                // The cases only test the estimator if it actually fired.
                Assert.IsTrue(updates > 0, c.Name);
            }
        }

        [TestMethod]
        public void WithoutItTheOutputPartsFromHostCoreButNeverInsideTheFirstWindow()
        {
            foreach (GyroOnTheFlyVectors.Case c in GyroOnTheFlyVectors.Cases)
            {
                int updates;
                int mismatch = FirstMismatch(c, Replay(c, false, out updates));
                // No estimate can exist before the window is full; after that, the first still
                // window moves the offset.
                Assert.IsTrue(mismatch >= GyroOnTheFlyCalibration.WindowSizeFromSamplingRate(c.SamplingRate),
                    c.Name + ": first mismatch at " + mismatch);
            }
        }

        [TestMethod]
        public void BitExactThroughBuildMsgWhenDerivedBit29IsSet()
        {
            foreach (GyroOnTheFlyVectors.Case c in GyroOnTheFlyVectors.Cases)
            {
                int hardware = c.Name.StartsWith("shimmer3r")
                    ? (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R
                    : (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3;
                GyroDecoder decoder = new GyroDecoder(hardware, c.SamplingRate);
                decoder.AlignmentMatrixGyro = Matrix(c.Alignment);
                decoder.SensitivityMatrixGyro = Diagonal(c.Sensitivity);
                decoder.OffsetVectorGyro = Column(c.Offset);
                decoder.ApplyDerivedSensors(GyroOnTheFlyCalibration.DERIVED_SENSOR_GYRO_ON_THE_FLY_CAL);
                Assert.IsTrue(decoder.IsGyroOnTheFlyCalEnabled(), c.Name);

                List<double[]> output = new List<double[]>();
                for (int i = 0; i < c.Raw.GetLength(0); i++)
                {
                    output.Add(decoder.Decode(c.Raw[i, 0], c.Raw[i, 1], c.Raw[i, 2]));
                }
                Assert.AreEqual(-1, FirstMismatch(c, output), c.Name);
            }
        }

        [TestMethod]
        public void FirstEstimateComesAsSoonAsTheWindowIsFull()
        {
            GyroOnTheFlyCalibration otf = new GyroOnTheFlyCalibration();
            otf.SetWindowSize(5);
            double[] still = { 0.1, -0.2, 0.3 };
            double[] raw = { 10, 20, 30 };
            for (int i = 0; i < 4; i++)
            {
                Assert.IsNull(otf.Update(still, raw), "sample " + i);
            }
            CollectionAssert.AreEqual(raw, otf.Update(still, raw));
        }

        [TestMethod]
        public void NoEstimateWhenAnyAxisMoves()
        {
            GyroOnTheFlyCalibration otf = new GyroOnTheFlyCalibration();
            otf.SetWindowSize(4);
            double[] raw = { 10, 20, 30 };
            double[] estimate = null;
            for (int i = 0; i < 4; i++)
            {
                // Z alternates by 3 deg/s: a sample standard deviation of 1.73, over the threshold.
                estimate = otf.Update(new double[] { 0, 0, i % 2 == 0 ? 0 : 3 }, raw);
            }
            Assert.IsNull(estimate);
        }

        [TestMethod]
        public void NoWindowNoEstimate()
        {
            GyroOnTheFlyCalibration otf = new GyroOnTheFlyCalibration();
            Assert.IsNull(otf.Update(new double[3], new double[3]));
        }

        [TestMethod]
        public void StandardDeviationIsTheBiasCorrectedSampleOne()
        {
            // Population SD of 1..4 is 1.118; the sample SD Java uses is sqrt(5/3).
            Assert.AreEqual(Math.Sqrt(5.0 / 3.0), GyroOnTheFlyCalibration.StandardDeviation(new double[] { 1, 2, 3, 4 }), 1e-15);
            Assert.AreEqual(0.0, GyroOnTheFlyCalibration.StandardDeviation(new double[] { 42 }));
            Assert.AreEqual(2.5, GyroOnTheFlyCalibration.Mean(new double[] { 1, 2, 3, 4 }));
        }

        [TestMethod]
        public void WindowIsJavaMathRoundOfTheSamplingRate()
        {
            Assert.AreEqual(51, GyroOnTheFlyCalibration.WindowSizeFromSamplingRate(51.2));
            Assert.AreEqual(51, GyroOnTheFlyCalibration.WindowSizeFromSamplingRate(50.5));
            Assert.AreEqual(504, GyroOnTheFlyCalibration.WindowSizeFromSamplingRate(32768.0 / 65));
            Assert.AreEqual(0, GyroOnTheFlyCalibration.WindowSizeFromSamplingRate(0));
            Assert.AreEqual(0, GyroOnTheFlyCalibration.WindowSizeFromSamplingRate(double.NaN));
        }

        [TestMethod]
        public void ShrinkingTheWindowDropsTheOldestSamples()
        {
            GyroOnTheFlyCalibration otf = new GyroOnTheFlyCalibration();
            otf.SetWindowSize(4);
            double[] still = new double[3];
            for (int i = 0; i < 3; i++)
            {
                otf.Update(still, new double[] { i, i, i });
            }
            otf.SetWindowSize(2);
            // Kept: samples 1 and 2. Sample 3 pushes 1 out, leaving 2 and 3, mean 2.5.
            CollectionAssert.AreEqual(new double[] { 2.5, 2.5, 2.5 }, otf.Update(still, new double[] { 3, 3, 3 }));
        }

        [TestMethod]
        public void DerivedBit29TurnsItOnAndOff()
        {
            GyroDecoder decoder = new GyroDecoder((int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R, 51.2);
            Assert.IsFalse(decoder.IsGyroOnTheFlyCalEnabled());

            decoder.ApplyDerivedSensors(1L << 29);
            Assert.IsTrue(decoder.IsGyroOnTheFlyCalEnabled());
            Assert.AreEqual(51, decoder.OnTheFlyWindowSize);
            Assert.AreEqual(1L << 29, decoder.GetDerivedSensors());

            // Other derived bits leave it off.
            decoder.ApplyDerivedSensors(~(1L << 29));
            Assert.IsFalse(decoder.IsGyroOnTheFlyCalEnabled());

            // The caller can still turn it on, with Java's window, or with one of its own.
            decoder.SetGyroOnTheFlyCalibration(true);
            Assert.IsTrue(decoder.IsGyroOnTheFlyCalEnabled());
            Assert.AreEqual(51, decoder.OnTheFlyWindowSize);
            decoder.SetGyroOnTheFlyCalibration(true, 100, 1.2);
            Assert.AreEqual(100, decoder.OnTheFlyWindowSize);
        }
    }

    /// <summary>
    /// A ShimmerBluetooth with no connection behind it, streaming only the gyro, so that gyro
    /// packets can be put through BuildMsg directly.
    /// </summary>
    class GyroDecoder : ShimmerBluetooth
    {
        public GyroDecoder(int hardwareVersion, double samplingRate) : base("GyroDecoder")
        {
            HardwareVersion = hardwareVersion;
            SamplingRate = samplingRate;
            InterpretDataPacketFormat(3, new byte[] { 0x0A, 0x0B, 0x0C });
        }

        public int OnTheFlyWindowSize
        {
            get { return GyroOnTheFlyCal.WindowSize; }
        }

        public void ApplyDerivedSensors(long derivedSensors)
        {
            SetDerivedSensors(derivedSensors);
        }

        /// <summary>
        /// Decode one packet: a 16-bit timestamp, then gyro X, Y and Z, big-endian on a Shimmer3
        /// and little-endian on a Shimmer3R.
        /// </summary>
        public double[] Decode(int x, int y, int z)
        {
            List<byte> packet = new List<byte> { 0, 0 };
            foreach (int v in new int[] { x, y, z })
            {
                byte lo = (byte)(v & 0xFF);
                byte hi = (byte)((v >> 8) & 0xFF);
                if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                {
                    packet.Add(hi);
                    packet.Add(lo);
                }
                else
                {
                    packet.Add(lo);
                    packet.Add(hi);
                }
            }
            ObjectCluster ojc = BuildMsg(packet);
            string units = DefaultGyroParams
                ? ShimmerConfiguration.SignalUnits.DegreePerSecond_DefaultCal
                : ShimmerConfiguration.SignalUnits.DegreePerSecond;
            return new double[]
            {
                ojc.GetData(Shimmer3Configuration.SignalNames.GYROSCOPE_X, ShimmerConfiguration.SignalFormats.CAL, units).Data,
                ojc.GetData(Shimmer3Configuration.SignalNames.GYROSCOPE_Y, ShimmerConfiguration.SignalFormats.CAL, units).Data,
                ojc.GetData(Shimmer3Configuration.SignalNames.GYROSCOPE_Z, ShimmerConfiguration.SignalFormats.CAL, units).Data,
            };
        }

        public override string GetShimmerAddress()
        {
            return "GyroDecoder";
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
            return false;
        }

        protected override void OpenConnection()
        {
        }

        protected override int ReadByte()
        {
            throw new NotSupportedException();
        }

        protected override void WriteBytes(byte[] b, int index, int length)
        {
        }
    }
}
