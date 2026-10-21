using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using ShimmerAPI;

namespace ShimmerApiHil
{
    /// <summary>A check that failed: the run fails with this message.</summary>
    public class HilFailure : Exception
    {
        public HilFailure(string message) : base(message) { }
    }

    /// <summary>What this run cannot test on this bench: the run is reported not_covered.</summary>
    public sealed class NotCovered : Exception
    {
        public NotCovered(string message) : base(message) { }
    }

    /// <summary>An API call that did not return within its watchdog. The run fails, and the unit is
    /// not used again in this session: its link, or the API's state for it, is stuck.</summary>
    public sealed class CallHung : HilFailure
    {
        public CallHung(string message) : base(message) { }
    }

    /// <summary>
    /// Takes over Console.Out. The API writes its own progress there ("ACK Received" and the like):
    /// each of its lines goes into the log of the run in progress, and the suite's own lines go to
    /// <see cref="Stdout"/>.
    /// </summary>
    public sealed class ApiConsole : System.IO.TextWriter
    {
        public static System.IO.TextWriter Stdout = Console.Out;
        readonly System.Text.StringBuilder line = new System.Text.StringBuilder();

        public override System.Text.Encoding Encoding
        {
            get { return System.Text.Encoding.UTF8; }
        }

        public static void Install()
        {
            Stdout = Console.Out;
            Console.SetOut(System.IO.TextWriter.Synchronized(new ApiConsole()));
        }

        public override void Write(char value)
        {
            if (value == '\n')
            {
                Case current = Case.Current;
                if (current != null)
                {
                    current.Say("api: " + line.ToString().TrimEnd('\r'));
                }
                line.Clear();
            }
            else
            {
                line.Append(value);
            }
        }
    }

    /// <summary>One run of an item on one unit over one transport.</summary>
    public sealed class Case
    {
        /// <summary>The run in progress, for <see cref="ApiConsole"/>.</summary>
        public static volatile Case Current;

        public readonly Unit Unit;
        public readonly string Transport;
        public readonly string Item;
        public readonly List<string> Notes = new List<string>();
        public readonly List<string> Log = new List<string>();
        /// <summary>Whether a link to the unit opened in this run: it still answers.</summary>
        public bool LinkOpened;
        /// <summary>Every stream the run recorded, saved as CSV beside its log.</summary>
        public readonly List<StreamChecks.Stream> Streams = new List<StreamChecks.Stream>();
        readonly Stopwatch clock;

        public Case(Unit unit, string transport, string item, Stopwatch clock)
        {
            Unit = unit;
            Transport = transport;
            Item = item;
            this.clock = clock;
        }

        public double Now
        {
            get { return clock.Elapsed.TotalSeconds; }
        }

        public void Note(string text)
        {
            Notes.Add(text);
            Say("note: " + text);
        }

        public void Say(string text)
        {
            lock (Log)
            {
                Log.Add(string.Format(CultureInfo.InvariantCulture, "{0,10:F3}  {1}", Now, text));
            }
        }
    }

    /// <summary>
    /// A link to one unit through the API's public classes, as an application would hold it:
    /// ShimmerLogAndStreamSystemSerialPort over the COM port Windows made for a paired unit, or
    /// ShimmerLogAndStream32FeetBLE over BLE. It records what the API delivers, and every API call
    /// goes through <see cref="Call"/>, a watchdog: a call that does not return in time fails the run
    /// rather than hanging the suite.
    /// </summary>
    public sealed class Link : IDisposable
    {
        /// <summary>The configuration every item streams with, as the Android HIL's: accel only (the
        /// low-noise accel on a Shimmer3, the LSM6DSV's on a Shimmer3R) at 256 Hz.</summary>
        public const double RateHz = 256.0;
        const int AccelOnly = (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_A_ACCEL;
        public const int CallTimeoutMs = 10000;
        const int ConnectTimeoutMs = 45000;

        public readonly ShimmerLogAndStream Device;
        readonly Case c;
        readonly object gate = new object();
        StreamChecks.Stream recording;
        int packets;
        bool connectionLost;

        public int Packets
        {
            get { return Volatile.Read(ref packets); }
        }

        public bool ConnectionLost
        {
            get { return Volatile.Read(ref connectionLost); }
        }

        Link(Case c, ShimmerLogAndStream device)
        {
            this.c = c;
            Device = device;
            Device.UICallback += HandleEvent;
        }

        /// <summary>Opens a link to the case's unit over its transport, and waits until the API says
        /// it is connected.</summary>
        public static Link Open(Case c)
        {
            ShimmerLogAndStream device;
            if (c.Transport == "classic")
            {
                string port = c.Unit.ClassicPort();
                if (port == null)
                {
                    throw new NotCovered(c.Unit.Name + " has no Bluetooth Classic COM port: pair it in Windows");
                }
                c.Say("classic: " + port);
                device = new ShimmerLogAndStreamSystemSerialPort(c.Unit.Name, port);
            }
            else
            {
                var version = c.Unit.Platform == "shimmer3r" ? ShimmerBluetooth.ShimmerVersion.SHIMMER3R : ShimmerBluetooth.ShimmerVersion.SHIMMER3;
                device = new ShimmerLogAndStream32FeetBLE(c.Unit.Name, c.Unit.Mac.ToLowerInvariant(), version);
            }
            var link = new Link(c, device);
            try
            {
                link.Call("Connect", () => device.Connect(), ConnectTimeoutMs);
                if (device.GetState() != ShimmerBluetooth.SHIMMER_STATE_CONNECTED)
                {
                    throw new HilFailure(string.Format("did not connect over {0} (the API's state is {1})", c.Transport, device.GetStateString()));
                }
            }
            catch (HilFailure)
            {
                // A port left open would refuse the next run's link
                try
                {
                    link.Call("Disconnect (after a failed connect)", () => device.Disconnect(), 15000);
                }
                catch (HilFailure e)
                {
                    c.Say("could not close the failed link: " + e.Message);
                }
                device.UICallback -= link.HandleEvent;
                throw;
            }
            c.Say(string.Format("connected: {0}, hardware {1}, CRC {2}", device.GetFirmwareVersionFullName(), device.GetShimmerVersion(), device.GetCRCMode()));
            return link;
        }

        /// <summary>The unit's firmware, for the report.</summary>
        public string Identity()
        {
            return Device.GetFirmwareVersionFullName();
        }

        /// <summary>
        /// Accel only at <see cref="RateHz"/>, written as the console example does, and only when the
        /// unit's configuration differs: a unit left that way by an earlier run is not written again.
        /// That spares its flash, and keeps a BLE run off a Shimmer3 whose firmware can stop answering
        /// after a configuration write over BLE (see the README).
        /// </summary>
        public void Configure()
        {
            double current = Device.GetSamplingRate();
            long sensors = Device.GetEnabledSensors();
            if (Math.Abs(current - RateHz) < 0.01 && sensors == AccelOnly)
            {
                c.Say("configuration is already accel only at 256 Hz: not written");
                return;
            }
            c.Say(string.Format(CultureInfo.InvariantCulture, "configuration is {0} Hz with sensors 0x{1:X}: writing accel only at {2} Hz", current, sensors, RateHz));
            Call("WriteSamplingRate", () => Device.WriteSamplingRate(RateHz));
            Call("WriteSensors", () => Device.WriteSensors(AccelOnly));
            double rate = Device.GetSamplingRate();
            if (Math.Abs(rate - RateHz) > 0.5)
            {
                throw new HilFailure(string.Format(CultureInfo.InvariantCulture, "the API's sampling rate is {0} Hz after writing {1}", rate, RateHz));
            }
        }

        /// <summary>Starts streaming, and waits until the API is streaming: the start command's ACK
        /// was read.</summary>
        public void StartStreaming(bool andLog = false)
        {
            if (andLog)
            {
                Call("StartStreamingandLog", () => Device.StartStreamingandLog());
            }
            else
            {
                Call("StartStreaming", () => Device.StartStreaming());
            }
            if (!WaitFor(() => Device.GetState() == ShimmerBluetooth.SHIMMER_STATE_STREAMING, 5000))
            {
                throw new HilFailure("the API never reached Streaming after the start command (state " + Device.GetStateString() + "): its ACK was not seen");
            }
        }

        public void StopStreaming(bool andLog = false)
        {
            if (andLog)
            {
                Call("StopStreamingandLog", () => Device.StopStreamingandLog());
            }
            else
            {
                Call("StopStreaming", () => Device.StopStreaming());
            }
            if (!WaitFor(() => Device.GetState() == ShimmerBluetooth.SHIMMER_STATE_CONNECTED, 5000))
            {
                throw new HilFailure("the API did not return to Connected after the stop command (state " + Device.GetStateString() + ")");
            }
        }

        /// <summary>Records what the API delivers for the given time, calling <paramref name="during"/>
        /// once a second with the seconds elapsed.</summary>
        public StreamChecks.Stream Record(double seconds, Action<int> during = null)
        {
            var stream = new StreamChecks.Stream(RateHz);
            c.Streams.Add(stream);
            lock (gate)
            {
                recording = stream;
            }
            int packetsBefore = Packets;
            var started = Stopwatch.StartNew();
            int second = 0;
            while (started.Elapsed.TotalSeconds < seconds)
            {
                Thread.Sleep(50);
                if (ConnectionLost)
                {
                    break;
                }
                if (started.Elapsed.TotalSeconds >= second + 1)
                {
                    second++;
                    if (during != null)
                    {
                        during(second);
                    }
                }
            }
            lock (gate)
            {
                recording = null;
            }
            c.Say(string.Format(CultureInfo.InvariantCulture, "recorded {0:F1} s: {1} samples", started.Elapsed.TotalSeconds, Packets - packetsBefore));
            if (ConnectionLost)
            {
                throw new HilFailure(string.Format(CultureInfo.InvariantCulture, "the API reported the connection lost {0:F1} s into the stream", started.Elapsed.TotalSeconds));
            }
            return stream;
        }

        /// <summary>
        /// Runs an API call on its own thread and waits for it. A call that throws fails the run with
        /// its exception; one that does not return within the timeout fails it as hung.
        /// </summary>
        public void Call(string what, Action action, int timeoutMs = CallTimeoutMs)
        {
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    error = e;
                }
            });
            thread.IsBackground = true;
            thread.Name = "hil " + what;
            c.Say("call " + what);
            var took = Stopwatch.StartNew();
            thread.Start();
            if (!thread.Join(timeoutMs))
            {
                c.Say("call " + what + ": no return in " + timeoutMs + " ms");
                throw new CallHung(string.Format("{0} did not return within {1} s", what, timeoutMs / 1000));
            }
            c.Say(string.Format("call {0}: returned in {1} ms", what, took.ElapsedMilliseconds));
            if (error != null)
            {
                throw new HilFailure(string.Format("{0} threw {1}: {2}", what, error.GetType().Name, error.Message));
            }
        }

        public static bool WaitFor(Func<bool> condition, int timeoutMs)
        {
            var started = Stopwatch.StartNew();
            while (!condition())
            {
                if (started.ElapsedMilliseconds > timeoutMs)
                {
                    return false;
                }
                Thread.Sleep(20);
            }
            return true;
        }

        void HandleEvent(object sender, EventArgs args)
        {
            var e = (CustomEventArgs)args;
            switch (e.getIndicator())
            {
                case (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_DATA_PACKET:
                    Interlocked.Increment(ref packets);
                    var oc = e.getObject() as ObjectCluster;
                    if (oc == null)
                    {
                        return;
                    }
                    lock (gate)
                    {
                        if (recording != null)
                        {
                            AddSample(recording, oc, c.Now);
                        }
                    }
                    break;
                case (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_STATE_CHANGE:
                    c.Say("state: " + Device.GetStateString());
                    break;
                case (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_NOTIFICATION_MESSAGE:
                    string message = Convert.ToString(e.getObject());
                    c.Say("notification: " + message);
                    if (message != null && message.IndexOf("Connection lost", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Volatile.Write(ref connectionLost, true);
                    }
                    break;
            }
        }

        static void AddSample(StreamChecks.Stream stream, ObjectCluster oc, double arrived)
        {
            stream.Ticks.Add(oc.RawTimeStamp);
            stream.Arrived.Add(arrived);
            SensorData cal = oc.GetData(ShimmerConfiguration.SignalNames.TIMESTAMP, ShimmerConfiguration.SignalFormats.CAL);
            stream.CalibratedMs.Add(cal != null ? cal.Data : double.NaN);
            SensorData x = oc.GetData(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_X, ShimmerConfiguration.SignalFormats.CAL);
            SensorData y = oc.GetData(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Y, ShimmerConfiguration.SignalFormats.CAL);
            SensorData z = oc.GetData(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Z, ShimmerConfiguration.SignalFormats.CAL);
            if (x != null && y != null && z != null)
            {
                stream.Accel.Add(new[] { x.Data, y.Data, z.Data });
            }
        }

        /// <summary>Stops a stream left running and disconnects. A hang here is reported like any
        /// other, so the session knows the unit is stuck.</summary>
        public void Dispose()
        {
            try
            {
                if (Device.GetState() == ShimmerBluetooth.SHIMMER_STATE_STREAMING)
                {
                    Call("StopStreaming (cleanup)", () => Device.StopStreaming());
                }
            }
            finally
            {
                if (Device.GetState() != ShimmerBluetooth.SHIMMER_STATE_NONE)
                {
                    Call("Disconnect", () => Device.Disconnect(), 15000);
                }
                Device.UICallback -= HandleEvent;
            }
        }
    }
}
