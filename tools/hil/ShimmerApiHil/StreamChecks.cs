using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ShimmerApiHil
{
    /// <summary>
    /// The checks on a stream, as the Android HIL and the shimmer-logandstream HIL make them (its
    /// hil/checks.py, which this mirrors): loss and rate by the unit's own timestamps, so that a loss
    /// is a gap in its sequence rather than the PC's scheduling; silence on the link; and the accel of
    /// a unit lying still. No hardware here, so StreamChecksTest runs it in CI.
    /// </summary>
    public static class StreamChecks
    {
        public const double TicksHz = 32768.0;
        public const int TimestampBits = 24;
        public const double GravityMs2 = 9.81;

        /// <summary>What a stream delivered: each sample's raw device timestamp (ticks, as sent, before
        /// any unwrap), when it arrived (seconds on a monotonic clock), and the calibrated accel.</summary>
        public sealed class Stream
        {
            public double RateHz;
            public readonly List<double> Ticks = new List<double>();
            public readonly List<double> Arrived = new List<double>();
            public readonly List<double[]> Accel = new List<double[]>();
            /// <summary>The API's calibrated timestamp of each sample, in ms: its unwrapped timeline.</summary>
            public readonly List<double> CalibratedMs = new List<double>();

            public Stream(double rateHz)
            {
                RateHz = rateHz;
            }
        }

        public sealed class Figures
        {
            public int Samples;
            public int Missing;
            public int Repeated;
            public double ExpectedHz;
            public double MeasuredHz;
            public double LongestSilenceS;
            public double? AccelMs2;

            public double LossPercent
            {
                get
                {
                    int total = Samples + Missing;
                    return total > 0 ? 100.0 * Missing / total : 0.0;
                }
            }

            public string Summary()
            {
                string accel = AccelMs2.HasValue ? string.Format(CultureInfo.InvariantCulture, ", accel {0:F2} m/s^2", AccelMs2.Value) : "";
                return string.Format(CultureInfo.InvariantCulture,
                    "{0} samples, {1} missing ({2:F2} %), {3:F3} Hz measured against {4:F3}, longest silence {5:F0} ms{6}",
                    Samples, Missing, LossPercent, MeasuredHz, ExpectedHz, LongestSilenceS * 1000, accel);
            }
        }

        /// <summary>The rate a unit samples at: on a Shimmer3R an odd divider samples one tick early,
        /// every 2 x floor(divider / 2) ticks (log-and-stream-common SHIMMER3_STREAMING_DATA_FORMAT.md
        /// section 2.1).</summary>
        public static double EffectiveRateHz(string platform, double rateHz)
        {
            int divider = (int)Math.Round(TicksHz / rateHz);
            if (platform == "shimmer3r" && divider % 2 == 1)
            {
                return TicksHz / (2 * (divider / 2));
            }
            return TicksHz / divider;
        }

        public static Figures Measure(Stream stream, string platform)
        {
            double expected = EffectiveRateHz(platform, stream.RateHz);
            double period = TicksHz / expected;
            double modulo = 1 << TimestampBits;
            var steps = new List<double>();
            for (int i = 1; i < stream.Ticks.Count; i++)
            {
                double d = stream.Ticks[i] - stream.Ticks[i - 1];
                d = ((d % modulo) + modulo) % modulo;
                steps.Add(d);
            }
            int missing = steps.Where(d => d > 1.5 * period).Sum(d => (int)Math.Round(d / period) - 1);
            int repeated = steps.Count(d => d == 0);
            double measured = steps.Count > 0 ? TicksHz / Median(steps) : 0.0;
            double silence = 0.0;
            for (int i = 1; i < stream.Arrived.Count; i++)
            {
                silence = Math.Max(silence, stream.Arrived[i] - stream.Arrived[i - 1]);
            }
            double? accel = null;
            if (stream.Accel.Count > 0)
            {
                accel = stream.Accel.Average(a => Math.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]));
            }
            return new Figures
            {
                Samples = stream.Ticks.Count,
                Missing = missing,
                Repeated = repeated,
                ExpectedHz = expected,
                MeasuredHz = measured,
                LongestSilenceS = silence,
                AccelMs2 = accel,
            };
        }

        /// <summary>What the figures fail, in words; empty if nothing. A null accel tolerance skips the
        /// accel check.</summary>
        public static List<string> Failures(Figures f, double maxLossPercent = 1.0, double rateTolerance = 0.01,
            double maxSilenceS = 1.0, double? accelToleranceMs2 = 1.0)
        {
            var c = CultureInfo.InvariantCulture;
            var output = new List<string>();
            if (f.Samples < 2)
            {
                output.Add(string.Format(c, "only {0} sample(s) arrived", f.Samples));
                return output;
            }
            if (f.LossPercent > maxLossPercent)
            {
                output.Add(string.Format(c, "{0} samples missing ({1:F2} %, over {2:G} %)", f.Missing, f.LossPercent, maxLossPercent));
            }
            if (f.Repeated > 0)
            {
                output.Add(string.Format(c, "a timestamp repeated {0} time(s)", f.Repeated));
            }
            if (Math.Abs(f.MeasuredHz - f.ExpectedHz) > rateTolerance * f.ExpectedHz)
            {
                output.Add(string.Format(c, "the rate is {0:F3} Hz, not {1:F3}", f.MeasuredHz, f.ExpectedHz));
            }
            if (f.LongestSilenceS > maxSilenceS)
            {
                output.Add(string.Format(c, "the stream went silent for {0:F1} s", f.LongestSilenceS));
            }
            if (accelToleranceMs2.HasValue && f.AccelMs2.HasValue && Math.Abs(f.AccelMs2.Value - GravityMs2) > accelToleranceMs2.Value)
            {
                output.Add(string.Format(c, "accel at rest is {0:F2} m/s^2, more than {1:G} from 9.81", f.AccelMs2.Value, accelToleranceMs2.Value));
            }
            return output;
        }

        /// <summary>
        /// The API's own timeline, for a stream long enough to cross the 3-byte counter's wrap (512 s):
        /// each calibrated timestamp should follow the last by about the gap in the device's ticks. A
        /// step of more than a second either way, or a span that does not match the ticks, is the unwrap
        /// adding or losing a modulo. Empty if nothing is wrong.
        /// </summary>
        public static List<string> TimelineFailures(Stream stream, double maxStepS = 1.0)
        {
            var c = CultureInfo.InvariantCulture;
            var output = new List<string>();
            var cal = stream.CalibratedMs;
            if (cal.Count < 2 || cal.Count != stream.Ticks.Count)
            {
                output.Add(string.Format(c, "the API gave {0} calibrated timestamps for {1} samples", cal.Count, stream.Ticks.Count));
                return output;
            }
            double modulo = 1 << TimestampBits;
            double ticksSpan = 0.0;
            int backwards = 0;
            int jumps = 0;
            for (int i = 1; i < cal.Count; i++)
            {
                double d = stream.Ticks[i] - stream.Ticks[i - 1];
                d = ((d % modulo) + modulo) % modulo;
                ticksSpan += d;
                double step = cal[i] - cal[i - 1];
                if (step < 0)
                {
                    backwards++;
                }
                if (Math.Abs(step) > maxStepS * 1000)
                {
                    if (jumps == 0)
                    {
                        output.Add(string.Format(c, "the calibrated timestamp stepped {0:F1} s at sample {1}", step / 1000, i));
                    }
                    jumps++;
                }
            }
            if (jumps > 1)
            {
                output.Add(string.Format(c, "{0} steps of over {1:G} s in all", jumps, maxStepS));
            }
            if (backwards > 0)
            {
                output.Add(string.Format(c, "the calibrated timestamp went backwards {0} time(s)", backwards));
            }
            double calSpanS = (cal[cal.Count - 1] - cal[0]) / 1000;
            double tickSpanS = ticksSpan / TicksHz;
            if (Math.Abs(calSpanS - tickSpanS) > 1.0)
            {
                output.Add(string.Format(c, "the calibrated timeline spans {0:F1} s where the ticks span {1:F1} s", calSpanS, tickSpanS));
            }
            return output;
        }

        static double Median(List<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            int n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }
    }
}
