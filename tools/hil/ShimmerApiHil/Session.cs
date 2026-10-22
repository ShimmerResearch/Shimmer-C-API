using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShimmerAPI;

namespace ShimmerApiHil
{
    /// <summary>
    /// Runs the items on the bench and keeps the results. An item's status is the worst of its runs,
    /// fail > not_covered > pass, as in the firmware HIL (log-and-stream-common Test/hil/hil_report.py)
    /// and the shimmer-logandstream HIL: a pass on one unit does not hide a failure on another. A run
    /// the bench cannot make (a transport the unit does not list, a combination the rig names as a
    /// known issue) is not_covered, with why.
    /// </summary>
    public sealed class Session
    {
        static readonly Dictionary<string, int> Rank = new Dictionary<string, int> { { "pass", 0 }, { "not_covered", 1 }, { "fail", 2 } };

        public readonly Rig Rig;
        readonly string outDir;
        readonly string commit;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly DateTime started = DateTime.UtcNow;
        readonly Dictionary<string, JObject> results = new Dictionary<string, JObject>();
        readonly Dictionary<string, string> identities = new Dictionary<string, string>();
        /// <summary>Per unit: a call on it hung in this run, so the next run checks it still answers.</summary>
        readonly Dictionary<string, string> suspect = new Dictionary<string, string>();
        /// <summary>Per unit: why it is no longer used in this session.</summary>
        readonly Dictionary<string, string> unresponsive = new Dictionary<string, string>();

        public Session(Rig rig, string outDir, string commit)
        {
            Rig = rig;
            this.outDir = outDir;
            this.commit = commit;
        }

        /// <summary>
        /// Opens a link to the case's unit, runs the body, and closes the link whatever happens. A
        /// failure to close after the body passed fails the run; after the body failed, it is logged
        /// and the body's failure stands. <paramref name="onFirstConnected"/> is <see cref="Link.Open"/>'s.
        /// </summary>
        public void WithLink(Case c, Action<Link> body, Action<Link> onFirstConnected = null)
        {
            Link link;
            try
            {
                link = Link.Open(c, onFirstConnected);
            }
            catch (HilFailure e)
            {
                string why;
                if (suspect.TryGetValue(c.Unit.Name, out why))
                {
                    // It hung last time and does not connect now: blame that run, not this one
                    unresponsive[c.Unit.Name] = "stopped answering after " + why + "; it needs a power cycle";
                    throw new NotCovered(c.Unit.Name + " " + unresponsive[c.Unit.Name] + " (" + e.Message + ")");
                }
                throw;
            }
            suspect.Remove(c.Unit.Name);
            c.LinkOpened = true;
            identities[c.Unit.Name] = link.Identity();
            bool passed = false;
            try
            {
                body(link);
                passed = true;
            }
            finally
            {
                if (passed)
                {
                    link.Dispose();
                }
                else
                {
                    try
                    {
                        link.Dispose();
                    }
                    catch (Exception e)
                    {
                        c.Say("closing the link after the failure failed too: " + e.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Plans the runs, then makes each in a process of its own (<see cref="RunChild"/>). A run
        /// leaves behind whatever the API leaves running, such as a read thread that never stops or a
        /// call that never returned, and those would slow or break every later run in a shared
        /// process. The process's exit takes them with it, so each run starts as an application would.
        /// Returns whether nothing failed.
        /// </summary>
        public bool RunAll(IList<Item> items, ICollection<string> onlyUnits, ICollection<string> onlyTransports, string rigPath)
        {
            var cases = new List<KeyValuePair<Item, KeyValuePair<Unit, string>>>();
            foreach (Item item in items)
            {
                foreach (Unit unit in Rig.Units.Where(u => onlyUnits.Count == 0 || onlyUnits.Contains(u.Name)))
                {
                    IEnumerable<string> transports;
                    if (item.OncePerUnit)
                    {
                        string pick = new[] { "classic", "ble" }.FirstOrDefault(t => unit.Transports.Contains(t)
                            && (onlyTransports.Count == 0 || onlyTransports.Contains(t)) && unit.KnownIssue(item.Id, t) == null);
                        transports = pick != null ? new[] { pick } : new string[0];
                        if (pick == null)
                        {
                            Record(item.Id, "not_covered", unit.Name + ": no transport left to run it over");
                        }
                    }
                    else
                    {
                        transports = Rig.AllTransports.Where(t => onlyTransports.Count == 0 || onlyTransports.Contains(t));
                    }
                    foreach (string t in transports)
                    {
                        cases.Add(new KeyValuePair<Item, KeyValuePair<Unit, string>>(item, new KeyValuePair<Unit, string>(unit, t)));
                    }
                }
            }

            for (int i = 0; i < cases.Count; i++)
            {
                Item item = cases[i].Key;
                Unit unit = cases[i].Value.Key;
                string transport = cases[i].Value.Value;
                bool usedTheLink = RunPlanned(item, unit, transport, rigPath);
                if (usedTheLink && i < cases.Count - 1)
                {
                    // Windows can hold a closed link for a few seconds, and a Shimmer takes one at a time
                    Thread.Sleep(TimeSpan.FromSeconds(Rig.SettleSeconds));
                }
            }
            return !results.Values.Any(r => (string)r["status"] == "fail");
        }

        /// <summary>Makes one planned run, unless the bench cannot, and records it. Returns whether it
        /// touched the unit.</summary>
        bool RunPlanned(Item item, Unit unit, string transport, string rigPath)
        {
            string where = unit.Name + " " + transport;
            string why;
            if (unresponsive.TryGetValue(unit.Name, out why))
            {
                Record(item.Id, "not_covered", where + ": " + unit.Name + " " + why);
                return false;
            }
            if (!unit.Transports.Contains(transport))
            {
                Record(item.Id, "not_covered", where + ": the rig lists no " + transport + " link for " + unit.Name);
                return false;
            }
            string known = unit.KnownIssue(item.Id, transport);
            if (known != null)
            {
                Record(item.Id, "not_covered", where + ": " + known);
                return false;
            }

            ApiConsole.Stdout.WriteLine("{0,-24} {1}", item.Id, where);
            JObject r = RunChild(item, unit, transport, rigPath);
            string status = (string)r["status"];
            string note = (string)r["note"];
            Record(item.Id, status, note);
            if (r.Value<string>("identity") != null)
            {
                identities[unit.Name] = (string)r["identity"];
            }
            if (r.Value<string>("unresponsive") != null)
            {
                unresponsive[unit.Name] = (string)r["unresponsive"];
            }
            if (r.Value<string>("hung") != null)
            {
                // The next run on this unit checks it still answers, and blames this one if not
                suspect[unit.Name] = item.Id + " over " + transport + " (" + (string)r["hung"] + ")";
            }
            else if (r.Value<bool>("link_opened"))
            {
                suspect.Remove(unit.Name);
            }
            ApiConsole.Stdout.WriteLine("  {0}: {1}", status.ToUpperInvariant(), note);
            return true;
        }

        /// <summary>Runs one case in a child process of this executable and reads back its result.</summary>
        JObject RunChild(Item item, Unit unit, string transport, string rigPath)
        {
            string where = unit.Name + " " + transport;
            string resultFile = ResultFile(outDir, item.Id, unit.Name, transport);
            var args = new StringBuilder();
            args.Append("--run-case ").Append(Quote(item.Id)).Append(" --unit ").Append(Quote(unit.Name))
                .Append(" --transport ").Append(transport).Append(" --rig ").Append(Quote(rigPath))
                .Append(" --out ").Append(Quote(outDir))
                .Append(" --rate ").Append(Link.RateHz.ToString(System.Globalization.CultureInfo.InvariantCulture));
            string why;
            if (suspect.TryGetValue(unit.Name, out why))
            {
                args.Append(" --suspect ").Append(Quote(why));
            }
            var info = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, args.ToString())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            int exitCode;
            using (Process child = Process.Start(info))
            {
                // Drained so a full pipe never stalls the child; it logs to its own file
                child.OutputDataReceived += (s, e) => { };
                child.ErrorDataReceived += (s, e) => { };
                child.BeginOutputReadLine();
                child.BeginErrorReadLine();
                int budgetMs = (int)(item.BudgetSeconds * 1000);
                if (!child.WaitForExit(budgetMs))
                {
                    try
                    {
                        child.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    return new JObject
                    {
                        ["status"] = "fail",
                        ["note"] = where + ": the run did not finish within " + item.BudgetSeconds + " s and was stopped",
                        ["hung"] = "the run did not finish",
                    };
                }
                exitCode = child.ExitCode;
                if (!File.Exists(resultFile))
                {
                    return new JObject
                    {
                        ["status"] = "fail",
                        ["note"] = where + ": the run's process ended (exit " + exitCode + ") without a result" + Crash(item, unit, transport),
                    };
                }
            }
            JObject result = JObject.Parse(File.ReadAllText(resultFile));
            if (exitCode != 0)
            {
                // The case had finished, and then something the API left running ended the process.
                // An application would have gone down with it
                result["status"] = "fail";
                result["note"] = (string)result["note"] + "; then the run's process ended (exit " + exitCode + ")" + Crash(item, unit, transport);
            }
            return result;
        }

        /// <summary>Where a child run leaves the exception that ended its process, if one did.</summary>
        public static string CrashFile(string outDir, string item, string unit, string transport)
        {
            return Path.Combine(outDir, "logs", item + "-" + unit + "-" + transport + ".crash.txt");
        }

        /// <summary>The first line of the exception that ended a child run's process, for its note.</summary>
        string Crash(Item item, Unit unit, string transport)
        {
            string file = CrashFile(outDir, item.Id, unit.Name, transport);
            if (!File.Exists(file))
            {
                return "";
            }
            string first = File.ReadLines(file).FirstOrDefault() ?? "";
            return ": " + first.Trim() + " (" + Path.GetFileName(file) + ")";
        }

        static string Quote(string arg)
        {
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }

        static string ResultFile(string outDir, string item, string unit, string transport)
        {
            return Path.Combine(outDir, "cases", item + "-" + unit + "-" + transport + ".json");
        }

        /// <summary>
        /// The child's side of <see cref="RunChild"/>: runs one case in this process, writes its log and
        /// samples, and leaves its result for the parent.
        /// </summary>
        /// <param name="suspectWhy">why the parent suspects the unit (a call hung on it last run), or null</param>
        public void RunOneHere(Item item, Unit unit, string transport, string suspectWhy)
        {
            if (suspectWhy != null)
            {
                suspect[unit.Name] = suspectWhy;
            }
            string where = unit.Name + " " + transport;
            var c = new Case(unit, transport, item.Id, clock);
            c.Say("start " + item.Id + " on " + where);
            Case.Current = c;
            string status;
            string note;
            string hung = null;
            try
            {
                item.Run(c, this);
                status = "pass";
                note = where + ": " + string.Join("; ", c.Notes);
            }
            catch (NotCovered e)
            {
                status = "not_covered";
                note = where + ": " + e.Message;
            }
            catch (CallHung e)
            {
                hung = e.Message;
                status = "fail";
                note = where + ": " + e.Message + Notes(c);
            }
            catch (HilFailure e)
            {
                status = "fail";
                note = where + ": " + e.Message + Notes(c);
            }
            catch (Exception e)
            {
                status = "fail";
                note = where + ": " + e.GetType().Name + ": " + e.Message + Notes(c);
                c.Say(e.ToString());
            }
            c.Say("end: " + status);
            Case.Current = null;

            Directory.CreateDirectory(Path.Combine(outDir, "logs"));
            string stem = Path.Combine(outDir, "logs", item.Id + "-" + unit.Name + "-" + transport);
            File.WriteAllLines(stem + ".log", c.Log, Encoding.UTF8);
            for (int n = 0; n < c.Streams.Count; n++)
            {
                WriteSamples(c.Streams[n], stem + (c.Streams.Count > 1 ? "-" + (n + 1) : "") + ".csv");
            }
            string identity;
            string gone;
            var result = new JObject
            {
                ["status"] = status,
                ["note"] = note,
                ["link_opened"] = c.LinkOpened,
                ["identity"] = identities.TryGetValue(unit.Name, out identity) ? identity : null,
                ["unresponsive"] = unresponsive.TryGetValue(unit.Name, out gone) ? gone : null,
                ["hung"] = hung,
            };
            string file = ResultFile(outDir, item.Id, unit.Name, transport);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, result.ToString(Formatting.Indented), new UTF8Encoding(false));
        }

        /// <summary>One row per sample: what the checks judged, to look at a failure afterwards.</summary>
        static void WriteSamples(StreamChecks.Stream s, string path)
        {
            var sb = new StringBuilder("raw_ticks,arrived_s,calibrated_ms,accel_x,accel_y,accel_z\n");
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < s.Ticks.Count; i++)
            {
                // Accel is only added for a sample that carries all three axes, so its rows can run short
                double[] a = s.Accel.Count == s.Ticks.Count ? s.Accel[i] : null;
                sb.Append(s.Ticks[i].ToString("F0", inv)).Append(',').Append(s.Arrived[i].ToString("F4", inv)).Append(',')
                  .Append(i < s.CalibratedMs.Count ? s.CalibratedMs[i].ToString("F3", inv) : "").Append(',')
                  .Append(a != null ? string.Join(",", a.Select(v => v.ToString("F4", inv))) : ",,").Append('\n');
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        static string Notes(Case c)
        {
            return c.Notes.Count > 0 ? "; " + string.Join("; ", c.Notes) : "";
        }

        void Record(string item, string status, string note)
        {
            JObject r;
            if (!results.TryGetValue(item, out r))
            {
                r = new JObject { ["id"] = item, ["status"] = "pass", ["runs"] = 0, ["notes"] = new JArray() };
                results[item] = r;
            }
            r["runs"] = (int)r["runs"] + 1;
            if (Rank[status] > Rank[(string)r["status"]])
            {
                r["status"] = status;
            }
            ((JArray)r["notes"]).Add(note);
        }

        public IEnumerable<KeyValuePair<string, string>> Statuses()
        {
            return results.Values.OrderBy(r => (string)r["id"]).Select(r => new KeyValuePair<string, string>((string)r["id"], (string)r["status"]));
        }

        /// <summary>The report, in the shimmer-logandstream HIL's structure (its conftest.py), so the
        /// suites' reports read alike.</summary>
        public string WriteReport()
        {
            Assembly api = typeof(ShimmerBluetooth).Assembly;
            var informational = api.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var report = new JObject
            {
                ["schema"] = "shimmer-c-api-hil-report/1",
                ["kind"] = "automated",
                ["subject"] = "Shimmer-C-API",
                ["commit"] = commit,
                ["api"] = new JObject
                {
                    ["version"] = informational != null ? informational.InformationalVersion : api.GetName().Version.ToString(),
                    ["path"] = api.Location,
                },
                ["bench"] = Rig.Bench,
                ["rate_hz"] = Link.RateHz,
                ["operator"] = Environment.UserName,
                ["created"] = started.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                ["duration_s"] = (int)Math.Round(clock.Elapsed.TotalSeconds),
                ["units"] = new JArray(Rig.Units.Select(u => new JObject
                {
                    ["name"] = u.Name,
                    ["platform"] = u.Platform,
                    ["mac"] = u.Mac,
                    ["radio"] = u.Radio,
                    ["transports"] = new JArray(u.Transports),
                    ["identity"] = identities.ContainsKey(u.Name) ? identities[u.Name] : null,
                })),
                ["results"] = new JArray(results.Values.OrderBy(r => (string)r["id"]).Select(r =>
                {
                    string note = string.Join("; ", ((JArray)r["notes"]).Select(n => (string)n));
                    return new JObject
                    {
                        ["id"] = r["id"],
                        ["status"] = r["status"],
                        ["runs"] = r["runs"],
                        ["note"] = note.Length > 2000 ? note.Substring(0, 2000) : note,
                    };
                })),
            };
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "report.json");
            File.WriteAllText(path, report.ToString(Formatting.Indented) + "\n", new UTF8Encoding(false));
            return path;
        }
    }
}
