using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerApiHil;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// The hardware suite's stream checks (tools/hil/ShimmerApiHil/StreamChecks.cs) on made-up
    /// streams, so they run in CI without hardware. The first four mirror the shimmer-logandstream
    /// HIL's hil/test_checks.py case for case: the two suites judge streams the same way.
    /// </summary>
    [TestClass]
    public class HilStreamChecksTest
    {
        const double Modulo = 1 << 24;

        static StreamChecks.Stream Stream(double rateHz, IEnumerable<double> ticks)
        {
            var s = new StreamChecks.Stream(rateHz);
            foreach (double t in ticks)
            {
                s.Arrived.Add(s.Ticks.Count * 0.01);
                s.Ticks.Add(t);
            }
            return s;
        }

        [TestMethod]
        public void TestAGapInTheTimestampsCountsTheSamplesMissing()
        {
            const int period = 128; // 256 Hz
            var ticks = Enumerable.Range(0, 100).Select(i => (double)(i * period))
                .Concat(Enumerable.Range(0, 100).Select(i => (double)((100 + 3 + i) * period)));
            StreamChecks.Figures f = StreamChecks.Measure(Stream(256.0, ticks), "shimmer3");
            Assert.AreEqual(200, f.Samples);
            Assert.AreEqual(3, f.Missing);
            Assert.AreEqual(256.0, f.MeasuredHz);
        }

        [TestMethod]
        public void TestTimestampsUnwrapPast24Bits()
        {
            double start = Modulo - 5 * 128;
            var ticks = Enumerable.Range(0, 10).Select(i => (start + i * 128) % Modulo);
            StreamChecks.Figures f = StreamChecks.Measure(Stream(256.0, ticks), "shimmer3");
            Assert.AreEqual(0, f.Missing);
            Assert.AreEqual(0, f.Repeated);
        }

        [TestMethod]
        public void TestAShimmer3RWithAnOddDividerSamplesATickEarly()
        {
            // 250 Hz: divider 131, which a Shimmer3R runs as 130
            Assert.AreEqual(StreamChecks.TicksHz / 130, StreamChecks.EffectiveRateHz("shimmer3r", 250.0));
            Assert.AreEqual(StreamChecks.TicksHz / 131, StreamChecks.EffectiveRateHz("shimmer3", 250.0));
            var ticks = Enumerable.Range(0, 50).Select(i => (double)(i * 130));
            CollectionAssert.AreEqual(new List<string>(),
                StreamChecks.Failures(StreamChecks.Measure(Stream(250.0, ticks), "shimmer3r"), accelToleranceMs2: null));
        }

        [TestMethod]
        public void TestEachCheckFailsInItsOwnWords()
        {
            var f = new StreamChecks.Figures
            {
                Samples = 90, Missing = 10, Repeated = 1, ExpectedHz = 256.0, MeasuredHz = 250.0,
                LongestSilenceS = 1.5, AccelMs2 = 12.0,
            };
            List<string> problems = StreamChecks.Failures(f);
            CollectionAssert.AreEqual(new[] { "10", "a", "the", "the", "accel" }, problems.Select(p => p.Split(' ')[0]).ToList());
            StringAssert.StartsWith(StreamChecks.Failures(f, accelToleranceMs2: null).Last(), "the stream went silent");
        }

        /// <summary>A stream across the wrap whose calibrated timeline follows its ticks.</summary>
        static StreamChecks.Stream AcrossTheWrap(int samples, double extraAfterWrapMs)
        {
            const int period = 128;
            double start = Modulo - 100 * period;
            var s = Stream(256.0, Enumerable.Range(0, samples).Select(i => (start + (double)i * period) % Modulo));
            for (int i = 0; i < samples; i++)
            {
                double ms = (start + (double)i * period) / StreamChecks.TicksHz * 1000;
                bool afterWrap = start + (double)i * period >= Modulo;
                s.CalibratedMs.Add(ms + (afterWrap ? extraAfterWrapMs : 0));
            }
            return s;
        }

        [TestMethod]
        public void TestATimelineThatFollowsItsTicksAcrossTheWrapPasses()
        {
            CollectionAssert.AreEqual(new List<string>(), StreamChecks.TimelineFailures(AcrossTheWrap(400, 0)));
        }

        [TestMethod]
        public void TestAWrapReadAsAnExtraModuloFails()
        {
            // An unwrap that adds a second modulo at the wrap puts everything after it 512 s late
            List<string> problems = StreamChecks.TimelineFailures(AcrossTheWrap(400, 512000));
            StringAssert.StartsWith(problems[0], "the calibrated timestamp stepped 512.0 s");
            Assert.IsTrue(problems.Any(p => p.StartsWith("the calibrated timeline spans")), string.Join("; ", problems));
        }

        [TestMethod]
        public void TestAWrapThatIsMissedFails()
        {
            // An unwrap that misses the wrap sends the timeline back to zero
            List<string> problems = StreamChecks.TimelineFailures(AcrossTheWrap(400, -512000));
            Assert.IsTrue(problems.Any(p => p.StartsWith("the calibrated timestamp went backwards")), string.Join("; ", problems));
        }
    }
}
