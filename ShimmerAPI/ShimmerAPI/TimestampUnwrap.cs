using System;

namespace ShimmerAPI
{
    /// <summary>
    /// Turns a Shimmer's wrapping packet tick counter into a monotonic one.
    /// <para>
    /// The counter is 3 bytes at 32768 Hz on current firmware, so it returns to zero
    /// every 512 seconds exactly, and a host has to add a whole modulo back each time
    /// it does. The obvious rule - if this sample reads lower than the last one, a
    /// wrap happened - is what every Shimmer host API implemented, and it is wrong
    /// three times over: on an out-of-order packet, on a record the firmware never
    /// stamped, and on a packet arriving late from before a wrap boundary.
    /// </para>
    /// <para>
    /// Each sample is classified by its <em>modular forward distance</em> from the
    /// previous one, with forward motion as the default:
    /// </para>
    /// <list type="number">
    /// <item><b>Duplicate</b> - the same raw value again. The timeline holds.</item>
    /// <item><b>Reordered</b> - no further back than
    /// <see cref="ReorderWindowTicks"/>. Placed where it was actually taken, which is
    /// below its predecessor. The output is deliberately not monotonic: a late packet
    /// belongs at the time it was sampled, not at the time it arrived.</item>
    /// <item><b>Invalid</b> - the 3-byte counter, a raw value of exactly zero, and a
    /// predecessor more than <see cref="WrapWindowTicks"/> below the top of the range.
    /// Firmware stamps a packet when the sample tick starts it and does not publish a
    /// packet it never stamped, so <c>0x000000</c> means the record is invalid, not
    /// that the counter reached its origin. LogAndStream v1.00.x-v1.01.003 could emit
    /// one under SD write back-pressure. Read as a wrap, a single such record makes
    /// every later sample in the recording 512 seconds late - which is how a 9 minute
    /// 30 second trial came back as 43 minutes 38. The timeline holds, the wrap count
    /// is left alone and <see cref="Result.Rejected"/> is set.</item>
    /// <item><b>Forward</b> - everything else, which is a wrap when the raw value
    /// fell. This is the <em>default</em>, and that matters: a wrap preceded by a long
    /// dropout is still a wrap, however much was lost before it.</item>
    /// </list>
    /// <para>
    /// Two details are easy to get wrong and are load-bearing here.
    /// </para>
    /// <para>
    /// <b>The comparison is on modular distance, not on unwrapped values.</b> Asking
    /// whether the new unwrapped candidate is below the last one misses a packet that
    /// arrives late from <em>before</em> a wrap boundary: its candidate sits nearly a
    /// whole modulo ahead, so it is accepted, and the next real sample is then read as
    /// a second wrap. The sequence 16777206, 5, 16777206, 70 is the smallest case, and
    /// it costs 512 seconds twice over.
    /// </para>
    /// <para>
    /// <b>The reorder window is sized in sample periods</b>, not as a fraction of the
    /// modulo - see <see cref="ReorderWindowTicks"/>.
    /// </para>
    /// <para>
    /// This mirrors TimestampUnwrap in the Java driver deliberately, and both are
    /// checked against the same conformance vectors. The rule is specified in
    /// log-and-stream-common, docs/SHIMMER3_STREAMING_DATA_FORMAT.md section 2.1; the
    /// vectors are run against this class by TimestampUnwrapVectorsTest.
    /// </para>
    /// </summary>
    public static class TimestampUnwrap
    {
        /// <summary>Maximum value of the 3-byte tick counter, exclusive: 2^24 ticks = 512 s.</summary>
        public const int TicksMax3Byte = 1 << 24;

        /// <summary>
        /// How close to the top of the range the previous sample must have been for a
        /// drop to exactly zero to be believed as a wrap. One second at 32768 Hz.
        /// </summary>
        public const int WrapWindowTicks = 32768;

        /// <summary>Sample periods a packet may lag its predecessor and still be a reorder.</summary>
        public const int ReorderPeriods = 8;

        /// <summary>
        /// The clock the packet tick counter runs on. Not the sampling clock, which is
        /// 312500 or 255765.625 Hz on a TCXO board - see <see cref="ReorderWindowTicks"/>.
        /// </summary>
        public const double RtcTicksPerSecond = 32768.0;

        /// <summary>The reorder window is never allowed past this fraction of the modulo.</summary>
        public const int MaxWindowDivisor = 8;

        /// <summary>Outcome of unwrapping one sample.</summary>
        public struct Result
        {
            /// <summary>The unwrapped tick count to use. On a rejected sample, the previous one.</summary>
            public double Unwrapped;
            /// <summary>
            /// Wrap count after this sample: floor(Unwrapped / maxTicks). Unchanged when
            /// the sample was rejected, and one lower than its predecessor's for a packet
            /// that arrived late from before a wrap boundary.
            /// </summary>
            public double Cycle;
            /// <summary>True when the raw value was an invalid zero rather than a real wrap.</summary>
            public bool Rejected;
        }

        /// <summary>
        /// How far back a sample may be from its predecessor and still be treated as a
        /// reordered packet rather than a wrap.
        /// <para>
        /// Sized in sample periods, because that is what distinguishes the two cases: a
        /// reorder swaps packets that are adjacent in time - a handful of periods -
        /// whereas a dropout that spans the counter's wrap point is most of a modulo.
        /// Sizing the window as a fraction of the modulo confuses them. At
        /// <c>modulo / 8</c> on the 2-byte counter, every dropout between 1.75 and 2.0
        /// seconds reads as a reorder and the wrap is silently lost - and a 1.75 second
        /// Bluetooth gap is ordinary. Eight periods shrinks the band in which that can
        /// happen to about 16 milliseconds.
        /// </para>
        /// <para>
        /// The rate must be in the <see cref="RtcTicksPerSecond"/> domain. On a TCXO
        /// board the sampling clock is 312500 or 255765.625 Hz, and deriving the window
        /// from that would widen it by roughly nine and a half times.
        /// </para>
        /// <para>
        /// <b>Returns zero - the branch disabled - when the rate is not known.</b> Never
        /// guess: <c>32768 / 0</c> is <see cref="double.PositiveInfinity"/> in C#, and an
        /// infinite window classifies every backward step as a reorder and loses every
        /// wrap, which is worse than the naive rule this replaces. Rejecting an unstamped
        /// record needs no rate, so that still works with the window at zero.
        /// </para>
        /// </summary>
        /// <param name="samplingRateHz">the configured rate, in Hz; zero, negative, NaN
        /// or infinite all mean "not known"</param>
        /// <param name="maxTicks">the counter's modulo</param>
        /// <returns>the window in ticks, clamped so that it can never reach the modulo
        /// and leave no backward step large enough to be read as a wrap</returns>
        public static double ReorderWindowTicks(double samplingRateHz, int maxTicks)
        {
            if (double.IsNaN(samplingRateHz) || double.IsInfinity(samplingRateHz) || samplingRateHz <= 0.0)
            {
                return 0.0;
            }
            double window = ReorderPeriods * RtcTicksPerSecond / samplingRateHz;
            return Math.Min(window, (double)maxTicks / MaxWindowDivisor);
        }

        /// <summary>
        /// Unwraps one sample with the reorder branch disabled. Equivalent to passing a
        /// window of zero; kept so that a caller with no rate to hand still compiles.
        /// </summary>
        public static Result Unwrap(double rawTicks, double lastUnwrapped, double cycle, int maxTicks)
        {
            return Unwrap(rawTicks, lastUnwrapped, cycle, maxTicks, 0.0);
        }

        /// <param name="rawTicks">the packet's raw tick value</param>
        /// <param name="lastUnwrapped">the unwrapped value returned for the previous sample</param>
        /// <param name="cycle">how many wraps have been counted so far</param>
        /// <param name="maxTicks">the counter's modulo - 2^24, or 2^16 on old firmware</param>
        /// <param name="reorderWindowTicks">from <see cref="ReorderWindowTicks"/>; zero
        /// disables reorder detection</param>
        public static Result Unwrap(double rawTicks, double lastUnwrapped, double cycle, int maxTicks,
            double reorderWindowTicks)
        {
            // (0, 0) is the reset state AND a state the rule can reach, so this
            // overload cannot always tell them apart - see the six-argument form.
            // Kept for callers written before the distinction existed.
            return Unwrap(rawTicks, lastUnwrapped, cycle, maxTicks, reorderWindowTicks,
                !(lastUnwrapped == 0.0 && cycle == 0.0));
        }

        /// <summary>
        /// As above, but told outright whether a previous sample exists.
        ///
        /// The five-argument form infers it from the state being (0, 0). That is the
        /// reset state, and it is also a state the rule can produce: a reorder that
        /// lands exactly on the counter's origin leaves lastUnwrapped = 0 and
        /// cycle = 0 in the middle of a stream. The next packet is then read as a
        /// first sample and passed through, so one arriving from just before the
        /// origin is placed a whole modulo late rather than a few ticks behind. The
        /// conformance vector reorder-onto-origin-then-earlier-packet-24bit is
        /// exactly that sequence.
        ///
        /// Hosts that keep the previous RAW value instead of a cycle count - the web
        /// SDK and pyshimmer - never had the ambiguity.
        /// </summary>
        /// <param name="hasPreviousSample">
        /// False only before the first sample of a stream.
        /// </param>
        public static Result Unwrap(double rawTicks, double lastUnwrapped, double cycle, int maxTicks,
            double reorderWindowTicks, bool hasPreviousSample)
        {
            if (!hasPreviousSample)
            {
                // Nothing has been unwrapped yet, so there is no predecessor to measure
                // against. Taking the reset state as a real sample at zero would let a
                // first raw value near the top of the range read as a packet reordered
                // across a boundary, placing a whole recording one modulo early.
                return new Result { Unwrapped = rawTicks, Cycle = 0.0, Rejected = false };
            }

            double lastRaw = lastUnwrapped - (maxTicks * cycle);
            double forward = rawTicks - lastRaw;
            if (forward < 0)
            {
                forward += maxTicks;
            }
            double backwards = maxTicks - forward;

            double candidate;
            if (forward == 0.0)
            {
                // The same value again: a duplicate. Hold the timeline where it is.
                candidate = lastUnwrapped;
            }
            else if (backwards <= reorderWindowTicks)
            {
                // Reordered, on either side of a wrap boundary.
                candidate = lastUnwrapped - backwards;
            }
            else if (maxTicks == TicksMax3Byte
                && rawTicks == 0.0
                && lastRaw < (maxTicks - WrapWindowTicks))
            {
                // Mid-range and then exactly zero: a record the firmware never stamped.
                // Hold the timeline where it was and say so. Nothing about this sample
                // moves the state, so the next real one is an ordinary step forward and
                // the rejection cannot cascade.
                return new Result { Unwrapped = lastUnwrapped, Cycle = cycle, Rejected = true };
            }
            else
            {
                // Forward motion, which is a wrap when the raw value fell.
                candidate = lastUnwrapped + forward;
            }

            return new Result
            {
                Unwrapped = candidate,
                Cycle = Math.Floor(candidate / maxTicks),
                Rejected = false
            };
        }
    }
}
