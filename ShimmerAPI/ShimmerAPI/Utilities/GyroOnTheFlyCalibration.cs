using System;
using System.Collections.Generic;

namespace ShimmerAPI.Utilities
{
    /// <summary>
    /// Gyro on-the-fly offset calibration, as the Java driver runs it
    /// (com.shimmerresearch.algorithms.gyroOnTheFlyCal.GyroOnTheFlyCalModule and
    /// OnTheFlyGyroOffsetCal), and so as Consensys does.
    /// <para>
    /// While the sensor lies still it re-estimates the gyro offset. Each sample's calibrated
    /// and raw triples go into a sliding window of round(fs) samples. Once the window is full,
    /// if every calibrated axis has a standard deviation under 1.2 deg/s, the mean of the raw
    /// window becomes the gyro offset, from the next sample on.
    /// </para>
    /// <para>
    /// The arithmetic is Apache Commons Math 2.2's: <c>Mean.evaluate</c> with its correction
    /// pass, and the bias-corrected <c>Variance</c> with its correction term. That is what makes
    /// the result bit-exact with Java. The port follows shimmer-host-core
    /// (<c>src/calibration.rs</c>: <c>mean</c>, <c>standard_deviation</c>, <c>GyroOnTheFly</c>),
    /// which is held to Java's output to the last bit; the unit tests replay its traces.
    /// </para>
    /// </summary>
    public class GyroOnTheFlyCalibration
    {
        /// <summary>Derived-sensor bit 29, DerivedSensorsBitMask.GYRO_ON_THE_FLY_CAL in the Java driver.</summary>
        public const long DERIVED_SENSOR_GYRO_ON_THE_FLY_CAL = 1L << 29;

        /// <summary>OnTheFlyGyroOffsetCal.DEFAULT_THRESHOLD, in deg/s.</summary>
        public const double DEFAULT_THRESHOLD = 1.2;

        private readonly List<double>[] mCal = { new List<double>(), new List<double>(), new List<double>() };
        private readonly List<double>[] mRaw = { new List<double>(), new List<double>(), new List<double>() };

        /// <summary>Samples in the window; 0 until a window is set, and then nothing is estimated.</summary>
        public int WindowSize { get; private set; }

        /// <summary>The per-axis standard deviation, in deg/s, under which the window counts as still.</summary>
        public double Threshold { get; set; } = DEFAULT_THRESHOLD;

        /// <summary>Whether a derived-sensors bitmap has bit 29 set.</summary>
        public static bool IsEnabledInDerivedSensors(long derivedSensors)
        {
            return (derivedSensors & DERIVED_SENSOR_GYRO_ON_THE_FLY_CAL) != 0;
        }

        /// <summary>
        /// The window for a sampling rate: Java's (int)Math.round(fs), so halves round up
        /// (OnTheFlyGyroOffsetCal.setBufferSizeFromSamplingRate). 0 for a rate that is not known.
        /// </summary>
        public static int WindowSizeFromSamplingRate(double samplingRate)
        {
            if (double.IsNaN(samplingRate) || double.IsInfinity(samplingRate) || samplingRate <= 0)
            {
                return 0;
            }
            return (int)Math.Floor(samplingRate + 0.5);
        }

        /// <summary>
        /// Sets the window length. Values already in it are kept, oldest first to go if it
        /// shrinks, as DescriptiveStatistics.setWindowSize does. Java does this when streaming
        /// starts.
        /// </summary>
        public void SetWindowSize(int windowSize)
        {
            WindowSize = Math.Max(0, windowSize);
            for (int axis = 0; axis < 3; axis++)
            {
                Trim(mCal[axis]);
                Trim(mRaw[axis]);
            }
        }

        /// <summary>Empties the window, as a new SD file does.</summary>
        public void Reset()
        {
            for (int axis = 0; axis < 3; axis++)
            {
                mCal[axis].Clear();
                mRaw[axis].Clear();
            }
        }

        /// <summary>
        /// Feeds one sample (updateGyroOnTheFlyGyroOVCal): its calibrated triple, in deg/s, and
        /// the raw triple it was calibrated from. Returns the new offset, in raw counts, when the
        /// window is full and still; otherwise null.
        /// </summary>
        public double[] Update(double[] calibrated, double[] raw)
        {
            // A window of 0 is not a window: Java builds no buffers for it.
            if (WindowSize < 1)
            {
                return null;
            }
            for (int axis = 0; axis < 3; axis++)
            {
                if (mCal[axis].Count == WindowSize)
                {
                    mCal[axis].RemoveAt(0);
                    mRaw[axis].RemoveAt(0);
                }
                mCal[axis].Add(calibrated[axis]);
                mRaw[axis].Add(raw[axis]);
            }
            if (mCal[1].Count != WindowSize)
            {
                return null;
            }
            for (int axis = 0; axis < 3; axis++)
            {
                if (!(StandardDeviation(mCal[axis]) < Threshold))
                {
                    return null;
                }
            }
            return new double[] { Mean(mRaw[0]), Mean(mRaw[1]), Mean(mRaw[2]) };
        }

        /// <summary>Commons Math 2.2 Mean.evaluate: the sum's mean, then a correction pass.</summary>
        public static double Mean(IList<double> values)
        {
            double n = values.Count;
            double total = 0.0;
            foreach (double v in values)
            {
                total += v;
            }
            double xbar = total / n;
            double correction = 0.0;
            foreach (double v in values)
            {
                correction += v - xbar;
            }
            return xbar + (correction / n);
        }

        /// <summary>
        /// Commons Math 2.2 DescriptiveStatistics.getStandardDeviation: the square root of the
        /// bias-corrected Variance, with its correction term. 0 for one value.
        /// </summary>
        public static double StandardDeviation(IList<double> values)
        {
            int count = values.Count;
            if (count == 1)
            {
                return 0.0;
            }
            double m = Mean(values);
            double accum = 0.0;
            double accum2 = 0.0;
            foreach (double v in values)
            {
                double dev = v - m;
                accum += dev * dev;
                accum2 += dev;
            }
            double n = count;
            return Math.Sqrt((accum - (accum2 * accum2 / n)) / (n - 1.0));
        }

        private void Trim(List<double> values)
        {
            if (values.Count > WindowSize)
            {
                values.RemoveRange(0, values.Count - WindowSize);
            }
        }
    }
}
