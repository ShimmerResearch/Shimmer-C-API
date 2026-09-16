using System;

namespace ShimmerAPI
{
    /// <summary>
    /// Turns a Shimmer's wrapping packet tick counter into a monotonic one.
    /// <para>
    /// The counter is 3 bytes at 32768 Hz on current firmware, so it returns to zero
    /// every 512 seconds exactly, and a host has to add a whole modulo back each time
    /// it does. The obvious rule - if this sample reads lower than the last one, a
    /// wrap happened - is what every Shimmer host API has implemented, and it is
    /// wrong for one input: a record whose timestamp field is exactly zero.
    /// </para>
    /// <para>
    /// Firmware stamps a packet when the sample tick starts it and does not publish a
    /// packet it never stamped, so 0x000000 in that field means the record is invalid,
    /// not that the counter reached its origin. LogAndStream v1.00.x-v1.01.003 could
    /// emit one under SD write back-pressure. Read as a wrap, a single such record
    /// makes every later sample in the recording 512 seconds late - which is how a
    /// 9 minute 30 second trial came back as 43 minutes 38.
    /// </para>
    /// <para>
    /// So: a backward step is a roll-over unless the counter is the 3-byte one, the
    /// new value is exactly zero, and the previous value was more than
    /// <see cref="WrapWindowTicks"/> below the top of the range. In that case the
    /// sample is rejected - the caller is handed the previous timestamp back and the
    /// wrap count is left alone.
    /// </para>
    /// <para>
    /// The exemption is deliberately narrow. A genuine wrap that happens to land on
    /// zero still has a predecessor near the top of the range, so it is accepted. A
    /// 2-byte counter (older firmware, 2 second range, where a stall really can
    /// exceed a second) is never rejected. And rejection cannot cascade: the following
    /// sample reads above the retained previous value, so it is accepted normally and
    /// the timeline carries on at its true spacing.
    /// </para>
    /// <para>
    /// This mirrors TimestampUnwrap in the Java driver deliberately. The two must
    /// agree: the same recording is read by both.
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

        /// <summary>Outcome of unwrapping one sample.</summary>
        public struct Result
        {
            /// <summary>The unwrapped tick count to use. On a rejected sample, the previous one.</summary>
            public double Unwrapped;
            /// <summary>Wrap count after this sample. Unchanged when the sample was rejected.</summary>
            public double Cycle;
            /// <summary>True when the raw value was an invalid zero rather than a real wrap.</summary>
            public bool Rejected;
        }

        /// <param name="rawTicks">the packet's raw tick value</param>
        /// <param name="lastUnwrapped">the unwrapped value returned for the previous sample</param>
        /// <param name="cycle">how many wraps have been counted so far</param>
        /// <param name="maxTicks">the counter's modulo - 2^24, or 2^16 on old firmware</param>
        public static Result Unwrap(double rawTicks, double lastUnwrapped, double cycle, int maxTicks)
        {
            double candidate = rawTicks + (maxTicks * cycle);

            if (lastUnwrapped > candidate)
            {
                // The counter went backwards. Either it wrapped, or this record is bad.
                double lastRaw = lastUnwrapped - (maxTicks * cycle);

                if (maxTicks == TicksMax3Byte
                    && rawTicks == 0.0
                    && lastRaw < (maxTicks - WrapWindowTicks))
                {
                    // Mid-range and then exactly zero: a record the firmware never
                    // stamped. Hold the timeline where it was and say so.
                    return new Result { Unwrapped = lastUnwrapped, Cycle = cycle, Rejected = true };
                }

                cycle += 1;
                candidate = rawTicks + (maxTicks * cycle);
            }

            return new Result { Unwrapped = candidate, Cycle = cycle, Rejected = false };
        }
    }
}
