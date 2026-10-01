using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// Covers <see cref="TimestampUnwrap"/>, and in particular the one input that made
    /// a customer's 9 minute 30 second recording import as 43 minutes 38: a record
    /// whose timestamp field is exactly zero, read as a 24-bit roll-over.
    /// <para>
    /// Mirrors API_00009_TimestampUnwrapTest in the Java driver. The two
    /// implementations read the same recordings, so they have to agree.
    /// </para>
    /// </summary>
    [TestClass]
    public class TimestampUnwrapTest
    {
        private const int Max3Byte = TimestampUnwrap.TicksMax3Byte; // 2^24
        private const int Max2Byte = 65536;
        private const int PeriodTicks = 65; // 504.123 Hz, the rate the field evidence is in

        /// <summary>Feeds a series of raw values through the unwrapper, as a caller would.</summary>
        private class Unwrapper
        {
            public double LastUnwrapped;
            public double Cycle;
            public bool LastRejected;

            public double Feed(double rawTicks, int maxTicks)
            {
                TimestampUnwrap.Result r = TimestampUnwrap.Unwrap(rawTicks, LastUnwrapped, Cycle, maxTicks);
                LastRejected = r.Rejected;
                Cycle = r.Cycle;
                LastUnwrapped = r.Unwrapped;
                return r.Unwrapped;
            }
        }

        [TestMethod]
        public void TestMonotonicSamplesAreUnchanged()
        {
            var u = new Unwrapper();
            Assert.AreEqual(1000, u.Feed(1000, Max3Byte));
            Assert.AreEqual(1065, u.Feed(1065, Max3Byte));
            Assert.AreEqual(1130, u.Feed(1130, Max3Byte));
            Assert.IsFalse(u.LastRejected, "nothing here is invalid");
            Assert.AreEqual(0.0, u.Cycle, "no wrap was counted");
        }

        [TestMethod]
        public void TestGenuineWrapIsCounted()
        {
            var u = new Unwrapper();
            u.Feed(Max3Byte - 16, Max3Byte);
            double afterWrap = u.Feed(16, Max3Byte);

            Assert.IsFalse(u.LastRejected, "a real wrap is not a rejection");
            Assert.AreEqual(1.0, u.Cycle, "one wrap counted");
            Assert.AreEqual(Max3Byte + 16, afterWrap, "advanced by 32 ticks, not by a modulo");
        }

        /// <summary>
        /// A wrap can legitimately land on zero - the counter simply reached its last
        /// tick. What distinguishes it from a corrupt record is where it came from.
        /// </summary>
        [TestMethod]
        public void TestWrapLandingExactlyOnZeroIsAccepted()
        {
            var u = new Unwrapper();
            u.Feed(Max3Byte - 100, Max3Byte);
            double afterWrap = u.Feed(0, Max3Byte);

            Assert.IsFalse(u.LastRejected, "predecessor was at the top of the range, so this is a wrap");
            Assert.AreEqual(1.0, u.Cycle, "one wrap counted");
            Assert.AreEqual(Max3Byte, afterWrap);
        }

        /// <summary>The defect: mid-range, then exactly zero.</summary>
        [TestMethod]
        public void TestIsolatedZeroMidRangeIsRejected()
        {
            var u = new Unwrapper();
            u.Feed(7406506, Max3Byte);
            double afterZero = u.Feed(0, Max3Byte);

            Assert.IsTrue(u.LastRejected, "an exact zero from mid-range is an invalid record");
            Assert.AreEqual(0.0, u.Cycle, "no wrap may be counted for it");
            Assert.AreEqual(7406506, afterZero, "the timeline holds where it was");
        }

        [TestMethod]
        public void TestRejectionDoesNotCascade()
        {
            var u = new Unwrapper();
            u.Feed(7406506, Max3Byte);
            u.Feed(0, Max3Byte);
            double next = u.Feed(7406571, Max3Byte);

            Assert.IsFalse(u.LastRejected, "the following sample is ordinary");
            Assert.AreEqual(7406571, next, "and lands one sample period on");
            Assert.AreEqual(0.0, u.Cycle, "still no wrap counted");
        }

        /// <summary>
        /// The exact sequence recovered from the customer's file, either side of one of
        /// its four bad records. Before the fix this spanned 512 seconds.
        /// </summary>
        [TestMethod]
        public void TestCustomerSignatureSpansFourSamplePeriods()
        {
            double[] raw = { 7406116, 7406506, 0, 7406571 };
            var u = new Unwrapper();
            double first = u.Feed(raw[0], Max3Byte);
            double last = 0;
            int rejected = 0;
            for (int i = 1; i < raw.Length; i++)
            {
                last = u.Feed(raw[i], Max3Byte);
                if (u.LastRejected)
                {
                    rejected++;
                }
            }

            Assert.AreEqual(1, rejected, "exactly one record was invalid");
            Assert.AreEqual(0.0, u.Cycle, "no modulo was added");
            Assert.AreEqual(455, last - first, "the four records span 455 ticks");
        }

        [TestMethod]
        public void TestFirstSampleZeroIsAccepted()
        {
            var u = new Unwrapper();
            double first = u.Feed(0, Max3Byte);

            Assert.IsFalse(u.LastRejected, "nothing precedes it, so there is nothing to contradict");
            Assert.AreEqual(0.0, first);
            Assert.AreEqual(0.0, u.Cycle);
        }

        /// <summary>
        /// Old firmware uses a 2-byte counter whose whole range is 2 seconds, so a
        /// stall really can cross it. The exemption must not apply there.
        /// </summary>
        [TestMethod]
        public void TestTwoByteCounterZeroIsStillAWrap()
        {
            var u = new Unwrapper();
            u.Feed(30000, Max2Byte);
            double afterZero = u.Feed(0, Max2Byte);

            Assert.IsFalse(u.LastRejected, "2-byte counters keep their existing behaviour");
            Assert.AreEqual(1.0, u.Cycle, "one wrap counted");
            Assert.AreEqual(Max2Byte, afterZero);
        }

        [TestMethod]
        public void TestNonZeroBackwardStepIsStillAWrap()
        {
            var u = new Unwrapper();
            u.Feed(7406506, Max3Byte);
            double after = u.Feed(1, Max3Byte);

            Assert.IsFalse(u.LastRejected, "a value of 1 is not the invalid marker");
            Assert.AreEqual(1.0, u.Cycle, "so it is read as a wrap, as before");
            Assert.AreEqual(Max3Byte + 1, after);
        }

        /// <summary>
        /// Four bad records is what the customer's 9m30s file contained; read as wraps
        /// they added 2048 seconds and it imported as 43m38s.
        /// </summary>
        [TestMethod]
        public void TestBadRecordsNoLongerInflateARecording()
        {
            var u = new Unwrapper();
            double startTicks = 1000000;
            double t = startTicks;
            int samples = 0;

            for (int i = 0; i < 800; i++)
            {
                if (i > 0 && i % 200 == 0)
                {
                    u.Feed(0, Max3Byte);
                    Assert.IsTrue(u.LastRejected, "planted record is rejected");
                }
                u.Feed(t, Max3Byte);
                samples++;
                t += PeriodTicks;
            }

            Assert.AreEqual(0.0, u.Cycle, "no wraps counted across the whole recording");
            Assert.AreEqual((samples - 1) * PeriodTicks, u.LastUnwrapped - startTicks,
                "span is exactly the samples that were taken");
        }
    }
}
