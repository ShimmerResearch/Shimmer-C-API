using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
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
    /// The items. Each streams accel only, at 256 Hz unless the run gives --rate (<see cref="Link.Configure"/>), as the Android HIL
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
        /// <summary>How long link.idle_start leaves the link idle before it starts streaming.</summary>
        public const double IdleSeconds = 60;
        /// <summary>The slowest start link.idle_start accepts after the wait, as the shimmer-logandstream
        /// HIL's classic.idle_start does.</summary>
        public const double IdleStartLimitMs = 500;
        public const double InStreamSeconds = 20;
        /// <summary>How long a reply asked for while streaming may take: the Java HIL's inStream allows
        /// 3 s for one request on its own, and 5 s for requests that overlap.</summary>
        public const int ReplyTimeoutMs = 3000;
        public const int OverlapTimeoutMs = 5000;
        public const double StartOnConnectedSeconds = 10;
        /// <summary>The rates config.sampling_rate writes, as the shimmer-logandstream HIL's does.</summary>
        public static readonly double[] SamplingRatesHz = { 51.2, 256, 512 };
        public const double RateSeconds = 5;
        public const double AppThreadsSeconds = 20;
        /// <summary>How long an app thread's call, or the replies to its requests, may stall before
        /// stream.app_threads calls it hung, as the Java HIL's queueStress does.</summary>
        public const double StallSeconds = 10;

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
            new Item { Id = "link.idle_start", Run = IdleStart, BudgetSeconds = 300,
                What = "Connects, leaves the link idle 60 s, then streams 10 s" },
            new Item { Id = "stream.in_stream", Run = InStream, BudgetSeconds = 180,
                What = "Reads the battery and the SD directory while streaming, three times each on their own, then twice both at once" },
            new Item { Id = "link.start_on_connected", Run = StartOnConnected, BudgetSeconds = 240,
                What = "Starts streaming as soon as the API reports Connected, while Connect() is still running, and streams 10 s" },
            new Item { Id = "config.sampling_rate", Run = SamplingRates, BudgetSeconds = 240,
                What = "Writes 51.2, 256 and 512 Hz in turn, streams 5 s at each, then writes the suite's rate back" },
            new Item { Id = "stream.app_threads", Run = AppThreads, BudgetSeconds = 180,
                What = "Streams 20 s while three app threads read the battery, read the SD directory and query the API, in loops" },
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
                CheckCanLog(link);
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

        /// <summary>
        /// A link left idle, as an application's is between configuring a unit and starting it. The
        /// suite sends nothing for a minute. The API sends a status request on its own after every few
        /// read timeouts, and declares the connection lost after more than ten in a row with nothing
        /// read. Then the stream has to start promptly and be whole.
        /// </summary>
        static void IdleStart(Case c, Session s)
        {
            s.WithLink(c, link =>
            {
                link.Configure();
                double started = c.Now;
                int logBefore;
                lock (c.Log)
                {
                    logBefore = c.Log.Count;
                }
                while (c.Now - started < IdleSeconds)
                {
                    Thread.Sleep(200);
                    if (link.ConnectionLost)
                    {
                        throw new HilFailure(string.Format(CultureInfo.InvariantCulture, "the API reported the connection lost {0:F0} s into the idle wait", c.Now - started));
                    }
                    if (link.Device.GetState() != ShimmerBluetooth.SHIMMER_STATE_CONNECTED)
                    {
                        throw new HilFailure(string.Format(CultureInfo.InvariantCulture, "the API left Connected {0:F0} s into the idle wait (state {1})", c.Now - started, link.Device.GetStateString()));
                    }
                }
                int polls;
                lock (c.Log)
                {
                    polls = c.Log.Skip(logBefore).Count(line => line.Contains("Sending Get Status Command"));
                }
                link.StartStreaming();
                double startMs = link.LastStartMs;
                StreamChecks.Stream stream = link.Record(CrcSeconds);
                link.StopStreaming();
                var problems = new List<string>();
                StreamChecks.Figures f = StreamChecks.Measure(stream, c.Unit.Platform);
                c.Note(string.Format(CultureInfo.InvariantCulture, "idle {0:F0} s with {1} status polls by the API; streaming {2:F0} ms after the start command; {3}",
                    IdleSeconds, polls, startMs, f.Summary()));
                if (startMs > IdleStartLimitMs)
                {
                    problems.Add(string.Format(CultureInfo.InvariantCulture, "streaming started {0:F0} ms after the start command, over {1:F0} ms", startMs, IdleStartLimitMs));
                }
                problems.AddRange(StreamChecks.Failures(f, accelToleranceMs2: c.Unit.AccelToleranceMs2));
                if (problems.Count > 0)
                {
                    throw new HilFailure(string.Join("; ", problems));
                }
            });
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

        /// <summary>
        /// Replies that arrive among the packets: the battery and the SD directory, asked for while
        /// streaming, each on its own and then both at once, as the Java HIL's inStream does. The API
        /// keeps the last of each. Cleared before a request, a value afterwards can only have come from
        /// its reply. A Shimmer3 now and then skips a sample to answer (DEV-1297), which the loss limit
        /// allows.
        /// </summary>
        static void InStream(Case c, Session s)
        {
            s.WithLink(c, link =>
            {
                link.Configure();
                link.StartStreaming();
                var problems = new List<string>();
                var volts = new List<double>();
                int answered = 0, asked = 0;
                Exception requestError = null;
                var requests = new Thread(() =>
                {
                    try
                    {
                        Thread.Sleep(2000);
                        for (int round = 0; round < 3; round++)
                        {
                            foreach (bool battery in new[] { true, false })
                            {
                                asked++;
                                if (Ask(c, link, battery, !battery, ReplyTimeoutMs, problems, volts))
                                {
                                    answered++;
                                }
                                Thread.Sleep(1000);
                            }
                        }
                        for (int overlap = 0; overlap < 2; overlap++)
                        {
                            asked++;
                            if (Ask(c, link, true, true, OverlapTimeoutMs, problems, volts))
                            {
                                answered++;
                            }
                            Thread.Sleep(1000);
                        }
                    }
                    catch (Exception e)
                    {
                        requestError = e;
                    }
                });
                requests.IsBackground = true;
                requests.Start();
                StreamChecks.Stream stream = link.Record(InStreamSeconds);
                if (!requests.Join(OverlapTimeoutMs + Link.CallTimeoutMs))
                {
                    throw new CallHung("the requests made while streaming had not finished when the stream ended");
                }
                if (requestError != null)
                {
                    ExceptionDispatchInfo.Capture(requestError).Throw();
                }
                link.StopStreaming();
                StreamChecks.Figures f = StreamChecks.Measure(stream, c.Unit.Platform);
                c.Note(string.Format(CultureInfo.InvariantCulture, "{0} of {1} requests answered while streaming; battery {2}; directory \"{3}\"; {4}",
                    answered, asked, volts.Count > 0 ? string.Format(CultureInfo.InvariantCulture, "{0:F3}-{1:F3} V", volts.Min(), volts.Max()) : "never read",
                    link.Device.GetSdDir(), f.Summary()));
                problems.AddRange(StreamChecks.Failures(f, accelToleranceMs2: c.Unit.AccelToleranceMs2));
                foreach (double v in volts.Where(v => v < 3.0 || v > 4.5))
                {
                    problems.Add(string.Format(CultureInfo.InvariantCulture, "the battery read {0:F3} V while streaming, not 3.0-4.5 V", v));
                }
                if (problems.Count > 0)
                {
                    throw new HilFailure(string.Join("; ", problems));
                }
            });
        }

        /// <summary>
        /// Asks for the battery, the SD directory or both while streaming, both written before either
        /// reply when both, and waits for the replies. Records a missing reply in
        /// <paramref name="problems"/> and returns whether everything asked for arrived.
        /// </summary>
        static bool Ask(Case c, Link link, bool battery, bool directory, int timeoutMs, List<string> problems, List<double> volts)
        {
            string what = battery && directory ? "battery and directory together" : battery ? "battery" : "directory";
            if (battery)
            {
                SetBatteryVoltage(link.Device, double.NaN);
            }
            if (directory)
            {
                link.Device.SetSdDir(null);
            }
            var took = Stopwatch.StartNew();
            if (battery)
            {
                link.Call("ReadBattery", () => link.Device.ReadBattery());
            }
            if (directory)
            {
                link.Call("ReadDirectory", () => link.Device.ReadDirectory());
            }
            bool arrived = Link.WaitFor(() => (!battery || !double.IsNaN(link.Device.getBatteryVoltage()))
                && (!directory || link.Device.GetSdDir() != null), timeoutMs);
            if (!arrived)
            {
                lock (problems)
                {
                    problems.Add(string.Format(CultureInfo.InvariantCulture, "no reply to the {0} request within {1} s", what, timeoutMs / 1000));
                }
                return false;
            }
            c.Say(string.Format(CultureInfo.InvariantCulture, "{0}: answered in {1} ms", what, took.ElapsedMilliseconds));
            if (battery)
            {
                lock (volts)
                {
                    volts.Add(link.Device.getBatteryVoltage());
                }
            }
            return true;
        }

        /// <summary>
        /// The API reports Connected when it reads the unit's inquiry reply, while Connect() is still
        /// reading the rest of the configuration. An app that starts streaming on that state change, as
        /// Consensys does with the Java driver, starts in the middle of the connect sequence. The Java HIL
        /// fails a connect that reports Connected before its sequence ends (DEV-1275). This starts
        /// there: the stream has to start and be whole, Connect() has to finish, and the API's view of
        /// the configuration has to survive it.
        /// </summary>
        static void StartOnConnected(Case c, Session s)
        {
            // The configuration first, on a link of its own: the start below comes before it could be written
            s.WithLink(c, link => link.Configure());
            Thread.Sleep(TimeSpan.FromSeconds(s.Rig.SettleSeconds));
            s.WithLink(c, link =>
            {
                if (!link.WaitFirstConnected(Link.CallTimeoutMs + 1000))
                {
                    throw new CallHung("the start made on Connected had not returned");
                }
                if (link.FirstConnectedError != null)
                {
                    ExceptionDispatchInfo.Capture(link.FirstConnectedError).Throw();
                }
                int afterConnected;
                lock (c.Log)
                {
                    // This link's connect: the configuring link before it logged one too
                    int returned = c.Log.FindLastIndex(line => line.Contains("call Connect: returned"));
                    int connected = returned < 0 ? -1 : c.Log.FindLastIndex(returned, line => line.Contains("state: Connected"));
                    afterConnected = connected >= 0 && returned > connected
                        ? c.Log.Skip(connected).Take(returned - connected).Count(line => line.Contains("api: ACK Received"))
                        : 0;
                }
                if (!Link.WaitFor(() => link.Device.GetState() == ShimmerBluetooth.SHIMMER_STATE_STREAMING, 5000))
                {
                    throw new HilFailure(string.Format(CultureInfo.InvariantCulture,
                        "the API never reached Streaming after a start on Connected, {0:F0} ms before Connect() returned (state {1})",
                        (link.ConnectReturnedAt - link.ConnectedAt) * 1000, link.Device.GetStateString()));
                }
                var problems = new List<string>();
                double rate = link.Device.GetSamplingRate();
                long sensors = link.Device.GetEnabledSensors();
                if (Math.Abs(rate - Link.RateHz) > 0.5 || sensors != (long)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_A_ACCEL)
                {
                    problems.Add(string.Format(CultureInfo.InvariantCulture,
                        "the API read the configuration as {0} Hz with sensors 0x{1:X}, where the unit has {2} Hz with accel only", rate, sensors, Link.RateHz));
                }
                StreamChecks.Stream stream = link.Record(StartOnConnectedSeconds);
                link.StopStreaming();
                StreamChecks.Figures f = StreamChecks.Measure(stream, c.Unit.Platform);
                c.Note(string.Format(CultureInfo.InvariantCulture,
                    "started streaming on Connected, {0:F0} ms before Connect() returned, with {1} replies to the connect sequence still to come; {2}",
                    (link.ConnectReturnedAt - link.ConnectedAt) * 1000, afterConnected, f.Summary()));
                problems.AddRange(StreamChecks.Failures(f, accelToleranceMs2: c.Unit.AccelToleranceMs2));
                if (problems.Count > 0)
                {
                    throw new HilFailure(string.Join("; ", problems));
                }
            }, onFirstConnected: link => link.Call("StartStreaming (on Connected)", () => link.Device.StartStreaming()));
        }

        /// <summary>
        /// Several rates written in turn, each judged by the rate the unit's own timestamps give, as the
        /// shimmer-logandstream HIL's config.sampling_rate does. Then the suite's rate goes back, so the
        /// next run finds the unit as <see cref="Link.Configure"/> leaves it.
        /// </summary>
        static void SamplingRates(Case c, Session s)
        {
            s.WithLink(c, link =>
            {
                link.Configure();
                var problems = new List<string>();
                foreach (double rate in SamplingRatesHz)
                {
                    string at = rate.ToString(CultureInfo.InvariantCulture) + " Hz: ";
                    try
                    {
                        link.Call("WriteSamplingRate(" + rate.ToString(CultureInfo.InvariantCulture) + ")", () => link.Device.WriteSamplingRate(rate));
                        double now = link.Device.GetSamplingRate();
                        if (Math.Abs(now - rate) > 0.5)
                        {
                            problems.Add(at + string.Format(CultureInfo.InvariantCulture, "the API's rate is {0} Hz after the write", now));
                            continue;
                        }
                        link.StartStreaming();
                        StreamChecks.Stream stream = link.Record(RateSeconds, rateHz: rate);
                        link.StopStreaming();
                        StreamChecks.Figures f = StreamChecks.Measure(stream, c.Unit.Platform);
                        c.Note(at + f.Summary());
                        problems.AddRange(StreamChecks.Failures(f, accelToleranceMs2: c.Unit.AccelToleranceMs2).Select(p => at + p));
                    }
                    catch (HilFailure e) when (!(e is CallHung))
                    {
                        problems.Add(at + e.Message);
                        if (link.Device.GetState() != ShimmerBluetooth.SHIMMER_STATE_CONNECTED)
                        {
                            break;
                        }
                    }
                }
                if (link.Device.GetState() == ShimmerBluetooth.SHIMMER_STATE_CONNECTED)
                {
                    try
                    {
                        link.Call("WriteSamplingRate (back to the suite's)", () => link.Device.WriteSamplingRate(Link.RateHz));
                        if (Math.Abs(link.Device.GetSamplingRate() - Link.RateHz) > 0.5)
                        {
                            problems.Add(string.Format(CultureInfo.InvariantCulture, "the rate did not go back to {0} Hz", Link.RateHz));
                        }
                    }
                    catch (HilFailure e) when (!(e is CallHung))
                    {
                        problems.Add("writing the suite's rate back failed: " + e.Message);
                    }
                }
                if (problems.Count > 0)
                {
                    throw new HilFailure(string.Join("; ", problems));
                }
            });
        }

        /// <summary>
        /// Streams while app threads call into the API, as an application's UI and worker threads do. One
        /// reads the battery, one the SD directory, and one polls the API's state and configuration, each
        /// in a loop. The read thread and the app threads share the port and the parser; the Java HIL's
        /// queueStress found a lock inversion this way (DEV-1266). Fails if a call or the replies stall
        /// for 10 s, a call throws, or the stream fails the checks. Unanswered requests are counted, as
        /// queueStress counts them.
        /// </summary>
        static void AppThreads(Case c, Session s)
        {
            s.WithLink(c, link =>
            {
                link.Configure();
                link.StartStreaming();
                var stop = new ManualResetEventSlim(false);
                var problems = new List<string>();
                // Per thread: requests, replies, and when it last made progress
                var counts = new long[3, 2];
                var progress = new double[3];
                Action<int, Action> loop = (index, step) =>
                {
                    try
                    {
                        while (!stop.IsSet)
                        {
                            step();
                            Volatile.Write(ref progress[index], c.Now);
                        }
                    }
                    catch (Exception e)
                    {
                        lock (problems)
                        {
                            problems.Add("app thread " + index + " threw " + e.GetType().Name + ": " + e.Message);
                        }
                    }
                };
                Action<int, bool, int> request = (index, battery, pauseMs) =>
                {
                    if (battery)
                    {
                        SetBatteryVoltage(link.Device, double.NaN);
                        link.Device.ReadBattery();
                    }
                    else
                    {
                        link.Device.SetSdDir(null);
                        link.Device.ReadDirectory();
                    }
                    Interlocked.Increment(ref counts[index, 0]);
                    if (Link.WaitFor(() => battery ? !double.IsNaN(link.Device.getBatteryVoltage()) : link.Device.GetSdDir() != null, ReplyTimeoutMs))
                    {
                        Interlocked.Increment(ref counts[index, 1]);
                    }
                    stop.Wait(pauseMs);
                };
                var threads = new[]
                {
                    new Thread(() => loop(0, () => request(0, true, 300))),
                    new Thread(() => loop(1, () => request(1, false, 500))),
                    new Thread(() => loop(2, () =>
                    {
                        link.Device.GetState();
                        link.Device.GetSamplingRate();
                        link.Device.GetEnabledSensors();
                        link.Device.GetSdDir();
                        link.Device.getBatteryVoltage();
                        Interlocked.Increment(ref counts[2, 0]);
                        Interlocked.Increment(ref counts[2, 1]);
                        stop.Wait(20);
                    })),
                };
                double begun = c.Now;
                for (int i = 0; i < threads.Length; i++)
                {
                    progress[i] = begun;
                    threads[i].IsBackground = true;
                    threads[i].Name = "hil app thread " + i;
                    threads[i].Start();
                }
                string stalled = null;
                StreamChecks.Stream stream = link.Record(AppThreadsSeconds, second =>
                {
                    for (int i = 0; i < threads.Length && stalled == null; i++)
                    {
                        if (c.Now - Volatile.Read(ref progress[i]) > StallSeconds)
                        {
                            stalled = "app thread " + i + " made no progress for " + StallSeconds + " s";
                        }
                    }
                });
                stop.Set();
                for (int i = 0; i < threads.Length; i++)
                {
                    if (!threads[i].Join(ReplyTimeoutMs + Link.CallTimeoutMs))
                    {
                        stalled = stalled ?? "app thread " + i + " did not stop: a call it made never returned";
                    }
                }
                if (stalled != null)
                {
                    throw new CallHung(stalled);
                }
                link.StopStreaming();
                StreamChecks.Figures f = StreamChecks.Measure(stream, c.Unit.Platform);
                c.Note(string.Format(CultureInfo.InvariantCulture,
                    "while streaming: battery {0} of {1} requests answered, directory {2} of {3}, {4} queries of the API's state; {5}",
                    counts[0, 1], counts[0, 0], counts[1, 1], counts[1, 0], counts[2, 0], f.Summary()));
                problems.AddRange(StreamChecks.Failures(f, accelToleranceMs2: c.Unit.AccelToleranceMs2));
                if (counts[0, 1] == 0 || counts[1, 1] == 0)
                {
                    problems.Add("no battery or no directory request was answered while the app threads ran");
                }
                if (problems.Count > 0)
                {
                    throw new HilFailure(string.Join("; ", problems));
                }
            });
        }

        /// <summary>
        /// For an item that logs to the SD card: fails it at once, saying why, if the unit is docked or
        /// on USB, as the Android HIL's checkCanLog does. The firmware then ignores the start, and the
        /// API shows a Shimmer3R on USB logging all the same, so the item would pass without logging
        /// anything (DEV-1372).
        /// </summary>
        static void CheckCanLog(Link link)
        {
            string why = link.WhyCannotLog();
            if (why != null)
            {
                throw new HilFailure(why + ": the firmware will not start SD logging, so this item cannot run. Take the unit off the dock and USB, then run it again");
            }
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
