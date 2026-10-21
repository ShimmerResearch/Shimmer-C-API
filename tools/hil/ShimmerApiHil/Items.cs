using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using ShimmerAPI;

namespace ShimmerApiHil
{
    /// <summary>One hardware item: an id, as the other HILs name theirs, and how one run of it goes.</summary>
    public sealed class Item
    {
        public string Id;
        public string What;
        /// <summary>Run once per unit, over one of its transports, rather than once per transport.</summary>
        public bool OncePerUnit;
        /// <summary>How long one run may take before it is stopped as hung.</summary>
        public double BudgetSeconds;
        public Action<Case, Session> Run;
    }

    /// <summary>
    /// The items. Each streams accel only at 256 Hz (<see cref="Link.Configure"/>), as the Android HIL
    /// does, and judges the stream with <see cref="StreamChecks"/>.
    /// </summary>
    public static class Items
    {
        public const double RestSeconds = 20;
        public const double CrcSeconds = 10;
        public const double LogSeconds = 15;
        public const int ReconnectCycles = 5;
        public const double ReconnectSeconds = 5;
        /// <summary>Past the 3-byte timestamp's wrap, 2^24 ticks = 512 s, wherever the stream starts.</summary>
        public const double RolloverSeconds = 600;

        public static readonly Item[] All =
        {
            new Item { Id = "stream.rest", Run = StreamRest, BudgetSeconds = 180,
                What = "Streams 20 s, lying still, with the link CRC off" },
            new Item { Id = "crc.stream", Run = CrcStream, BudgetSeconds = 240,
                What = "Switches the link CRC to 1 byte and then 2 bytes with WriteCRCMode, and streams 10 s in each" },
            new Item { Id = "stream.log_and_battery", Run = LogAndBattery, BudgetSeconds = 180,
                What = "Starts streaming and SD logging together, reads the battery three times while streaming, then stops both" },
            new Item { Id = "link.reconnect_loop", Run = ReconnectLoop, BudgetSeconds = 600,
                What = "Connects, streams 5 s and disconnects, five times" },
            new Item { Id = "stream.rollover", Run = Rollover, OncePerUnit = true, BudgetSeconds = 900,
                What = "Streams 10 minutes, across the 512 s wrap of the 3-byte timestamp, on one transport per unit" },
        };

        static void StreamRest(Case c, Session s)
        {
            s.WithLink(c, link =>
            {
                link.Configure();
                link.StartStreaming();
                StreamChecks.Stream stream = link.Record(RestSeconds);
                link.StopStreaming();
                Judge(c, stream, "");
            });
        }

        static void CrcStream(Case c, Session s)
        {
            s.WithLink(c, link =>
            {
                if (!link.Device.IsCRCSupported())
                {
                    throw new NotCovered("the API does not turn the link CRC on for " + link.Identity());
                }
                link.Configure();
                // Each mode is judged on its own: a failure in one is recorded, and the next still runs
                var problems = new List<string>();
                foreach (var mode in new[] { ShimmerBluetooth.BTCRCMode.ONE_BYTE, ShimmerBluetooth.BTCRCMode.TWO_BYTE })
                {
                    try
                    {
                        link.Call("WriteCRCMode(" + mode + ")", () => link.Device.WriteCRCMode(mode));
                    }
                    catch (HilFailure e) when (!(e is CallHung) && mode == ShimmerBluetooth.BTCRCMode.TWO_BYTE && e.Message.Contains("Cannot turn a 2-byte"))
                    {
                        // Firmware whose status push a 2-byte CRC overruns: the API refuses it, as it should
                        c.Note("2-byte CRC refused by the API, as designed for this firmware: " + e.Message);
                        continue;
                    }
                    catch (HilFailure e) when (!(e is CallHung))
                    {
                        problems.Add(mode + ": " + e.Message);
                        continue;
                    }
                    if (link.Device.GetCRCMode() != mode)
                    {
                        problems.Add(mode + ": the API's CRC mode is " + link.Device.GetCRCMode() + " after WriteCRCMode(" + mode + ")");
                        continue;
                    }
                    try
                    {
                        link.StartStreaming();
                        StreamChecks.Stream stream = link.Record(CrcSeconds);
                        link.StopStreaming();
                        StreamChecks.Figures f = StreamChecks.Measure(stream, c.Unit.Platform);
                        c.Note(mode + ": " + f.Summary());
                        problems.AddRange(StreamChecks.Failures(f, accelToleranceMs2: c.Unit.AccelToleranceMs2).Select(p => mode + ": " + p));
                    }
                    catch (HilFailure e) when (!(e is CallHung))
                    {
                        problems.Add(mode + ": " + e.Message);
                        if (link.Device.GetState() != ShimmerBluetooth.SHIMMER_STATE_CONNECTED)
                        {
                            break;
                        }
                    }
                }
                // Back to the CRC the API connects with. A failure here is noted, never put in place of
                // what the modes found: the link is closed next, and the next connect starts without one.
                if (link.Device.GetState() == ShimmerBluetooth.SHIMMER_STATE_CONNECTED && link.Device.GetCRCMode() != ShimmerBluetooth.BTCRCMode.OFF)
                {
                    try
                    {
                        link.Call("WriteCRCMode(OFF)", () => link.Device.WriteCRCMode(ShimmerBluetooth.BTCRCMode.OFF));
                    }
                    catch (HilFailure e) when (!(e is CallHung))
                    {
                        c.Note("switching the CRC back off failed: " + e.Message);
                    }
                }
                if (problems.Count > 0)
                {
                    throw new HilFailure(string.Join("; ", problems));
                }
            });
        }

        static void LogAndBattery(Case c, Session s)
        {
            s.WithLink(c, link =>
            {
                link.Configure();
                link.StartStreaming(andLog: true);
                // The API keeps the last battery reading. Cleared here, a reading afterwards can only
                // have come from a reply read while streaming.
                SetBatteryVoltage(link.Device, double.NaN);
                int asked = 0;
                StreamChecks.Stream stream = link.Record(LogSeconds, second =>
                {
                    if (second == 3 || second == 6 || second == 9)
                    {
                        asked++;
                        link.Call("ReadBattery", () => link.Device.ReadBattery());
                    }
                });
                bool logging = link.Device.isLogging;
                link.StopStreaming(andLog: true);
                double volts = link.Device.getBatteryVoltage();
                c.Note(string.Format(CultureInfo.InvariantCulture, "battery {0:F3} V after {1} reads while streaming; logging {2}", volts, asked, logging ? "on" : "not seen"));
                var problems = new List<string>();
                StreamChecks.Figures f = StreamChecks.Measure(stream, c.Unit.Platform);
                c.Note(f.Summary());
                problems.AddRange(StreamChecks.Failures(f, accelToleranceMs2: c.Unit.AccelToleranceMs2));
                if (double.IsNaN(volts))
                {
                    problems.Add(string.Format("no battery reply was read while streaming ({0} asked)", asked));
                }
                else if (volts < 3.0 || volts > 4.5)
                {
                    problems.Add(string.Format(CultureInfo.InvariantCulture, "the battery read {0:F3} V while streaming, not 3.0-4.5 V", volts));
                }
                if (problems.Count > 0)
                {
                    throw new HilFailure(string.Join("; ", problems));
                }
            });
        }

        static void ReconnectLoop(Case c, Session s)
        {
            for (int cycle = 1; cycle <= ReconnectCycles; cycle++)
            {
                int n = cycle;
                try
                {
                    s.WithLink(c, link =>
                    {
                        if (n == 1)
                        {
                            link.Configure();
                        }
                        link.StartStreaming();
                        StreamChecks.Stream stream = link.Record(ReconnectSeconds);
                        link.StopStreaming();
                        StreamChecks.Figures f = StreamChecks.Measure(stream, c.Unit.Platform);
                        c.Say("cycle " + n + ": " + f.Summary());
                        List<string> problems = StreamChecks.Failures(f, accelToleranceMs2: c.Unit.AccelToleranceMs2);
                        if (problems.Count > 0)
                        {
                            throw new HilFailure(string.Join("; ", problems));
                        }
                    });
                }
                catch (HilFailure e)
                {
                    throw new HilFailure("cycle " + n + " of " + ReconnectCycles + ": " + e.Message);
                }
                if (cycle < ReconnectCycles)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(s.Rig.SettleSeconds));
                }
            }
            c.Note(ReconnectCycles + " cycles connected, streamed and disconnected");
        }

        static void Rollover(Case c, Session s)
        {
            s.WithLink(c, link =>
            {
                link.Configure();
                link.StartStreaming();
                StreamChecks.Stream stream = link.Record(RolloverSeconds);
                link.StopStreaming();
                var problems = new List<string>();
                StreamChecks.Figures f = StreamChecks.Measure(stream, c.Unit.Platform);
                c.Note(f.Summary());
                problems.AddRange(StreamChecks.Failures(f, accelToleranceMs2: c.Unit.AccelToleranceMs2));
                int wraps = 0;
                for (int i = 1; i < stream.Ticks.Count; i++)
                {
                    if (stream.Ticks[i] < stream.Ticks[i - 1])
                    {
                        wraps++;
                    }
                }
                c.Note("the device's counter wrapped " + wraps + " time(s)");
                if (wraps == 0 && f.Samples > 0)
                {
                    problems.Add("the device's counter never wrapped, so the unwrap went untested");
                }
                problems.AddRange(StreamChecks.TimelineFailures(stream));
                if (problems.Count > 0)
                {
                    throw new HilFailure(string.Join("; ", problems));
                }
            });
        }

        static void Judge(Case c, StreamChecks.Stream stream, string prefix)
        {
            StreamChecks.Figures f = StreamChecks.Measure(stream, c.Unit.Platform);
            c.Note(prefix + f.Summary());
            List<string> problems = StreamChecks.Failures(f, accelToleranceMs2: c.Unit.AccelToleranceMs2);
            if (problems.Count > 0)
            {
                throw new HilFailure(prefix + string.Join("; ", problems));
            }
        }

        /// <summary>The API keeps its last battery reading in a protected field with no setter.</summary>
        static void SetBatteryVoltage(ShimmerBluetooth device, double volts)
        {
            FieldInfo field = typeof(ShimmerBluetooth).GetField("BatteryVoltage", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new HilFailure("the suite cannot clear the API's battery reading: ShimmerBluetooth.BatteryVoltage is gone");
            }
            field.SetValue(device, volts);
        }
    }
}
