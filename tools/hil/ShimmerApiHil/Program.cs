using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ShimmerApiHil
{
    /// <summary>
    /// Hardware-in-the-loop tests of the C# API on real Shimmer3 and Shimmer3R units. Usually started
    /// by tools/hil/run-csharp-hil.ps1, which builds this and can post the result for the release
    /// gate. See tools/hil/README.md.
    /// </summary>
    static class Program
    {
        const string Usage = @"ShimmerApiHil --rig <rig.toml> [options]

  --rig <file>        the bench file, in the firmware HIL's rig.toml format (default $HIL_RIG)
  --item <id>         only this item (repeatable)
  --unit <name>       only this unit (repeatable)
  --transport <t>     only classic or ble (repeatable)
  --out <dir>         where report.json and the logs go (default .\hil-out\<time>)
  --commit <sha>      the commit under test, for the report
  --rate <Hz>         the sampling rate every item streams at (default 256)
  --list              list the items and exit
  --describe          print the rig's bench and units as JSON and exit (for the runner's checks)

Exit code: 0 when nothing failed, 1 when an item failed, 2 on a usage or bench error.";

        static int Main(string[] args)
        {
            ApiConsole.Install();
            string rigPath = Environment.GetEnvironmentVariable("HIL_RIG");
            string outDir = null;
            string commit = "unknown";
            var onlyItems = new List<string>();
            var onlyUnits = new List<string>();
            var onlyTransports = new List<string>();
            bool describe = false;
            string runCase = null;
            string suspectWhy = null;
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    Func<string> next = () =>
                    {
                        if (i + 1 >= args.Length)
                        {
                            throw new ArgumentException(a + " needs a value");
                        }
                        return args[++i];
                    };
                    switch (a)
                    {
                        case "--rig": rigPath = next(); break;
                        case "--item": onlyItems.Add(next()); break;
                        case "--unit": onlyUnits.Add(next()); break;
                        case "--transport": onlyTransports.Add(next()); break;
                        case "--out": outDir = next(); break;
                        case "--commit": commit = next(); break;
                        case "--rate":
                            double rate;
                            if (!double.TryParse(next(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rate)
                                || rate < 1 || rate > 2048)
                            {
                                throw new ArgumentException("--rate needs a sampling rate in Hz, from 1 to 2048");
                            }
                            Link.RateHz = rate;
                            break;
                        case "--describe": describe = true; break;
                        // The parent starts one of these per run (Session.RunChild)
                        case "--run-case": runCase = next(); break;
                        case "--suspect": suspectWhy = next(); break;
                        case "--list":
                            foreach (Item item in Items.All)
                            {
                                ApiConsole.Stdout.WriteLine("{0,-24} {1}", item.Id, item.What);
                            }
                            return 0;
                        case "-h":
                        case "--help":
                            ApiConsole.Stdout.WriteLine(Usage);
                            return 0;
                        default:
                            throw new ArgumentException("unknown option " + a);
                    }
                }
                foreach (string t in onlyTransports)
                {
                    if (!Rig.AllTransports.Contains(t))
                    {
                        throw new ArgumentException("--transport " + t + ": not classic or ble");
                    }
                }
                foreach (string id in onlyItems)
                {
                    if (!Items.All.Any(item => item.Id == id))
                    {
                        throw new ArgumentException("--item " + id + ": no such item (see --list)");
                    }
                }
                if (string.IsNullOrEmpty(rigPath))
                {
                    throw new ArgumentException("no bench file: give --rig or set HIL_RIG");
                }
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine(Usage);
                return 2;
            }

            Rig rig;
            try
            {
                rig = Rig.Load(rigPath);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Cannot read the bench file " + rigPath + ": " + e.Message);
                return 2;
            }
            if (describe)
            {
                ApiConsole.Stdout.WriteLine(new Newtonsoft.Json.Linq.JObject
                {
                    ["bench"] = rig.Bench,
                    ["units"] = new Newtonsoft.Json.Linq.JArray(rig.Units.Select(u => new Newtonsoft.Json.Linq.JObject
                    {
                        ["name"] = u.Name,
                        ["platform"] = u.Platform,
                        ["transports"] = new Newtonsoft.Json.Linq.JArray(u.Transports),
                    })),
                }.ToString(Newtonsoft.Json.Formatting.None));
                return 0;
            }
            foreach (string name in onlyUnits)
            {
                if (!rig.Units.Any(u => u.Name == name))
                {
                    Console.Error.WriteLine("--unit " + name + ": the rig has no such unit (" + string.Join(", ", rig.Units.Select(u => u.Name)) + ")");
                    return 2;
                }
            }
            if (rig.Units.Count == 0)
            {
                Console.Error.WriteLine("The bench file lists no [[unit]]: nothing to test.");
                return 2;
            }
            outDir = Path.GetFullPath(outDir ?? Path.Combine("hil-out", DateTime.Now.ToString("yyyyMMdd-HHmmss")));

            if (runCase != null)
            {
                Item item = Items.All.FirstOrDefault(x => x.Id == runCase);
                if (item == null || onlyUnits.Count != 1 || onlyTransports.Count != 1)
                {
                    Console.Error.WriteLine("--run-case needs a known item, one --unit and one --transport");
                    return 2;
                }
                string crashFile = Session.CrashFile(outDir, item.Id, onlyUnits[0], onlyTransports[0]);
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    // What is ending the process, for the parent's note
                    Directory.CreateDirectory(Path.GetDirectoryName(crashFile));
                    File.WriteAllText(crashFile, e.ExceptionObject.ToString());
                };
                new Session(rig, outDir, commit).RunOneHere(item, rig.Units.First(u => u.Name == onlyUnits[0]), onlyTransports[0], suspectWhy);
                // Whatever the API left running goes with the process
                Environment.Exit(0);
            }

            ApiConsole.Stdout.WriteLine("C# API HIL on bench {0}: {1}", rig.Bench, string.Join(", ", rig.Units.Select(u => u.Name + " (" + u.Platform + ", " + string.Join("+", u.Transports) + ")")));
            var session = new Session(rig, outDir, commit);
            List<Item> items = Items.All.Where(item => onlyItems.Count == 0 || onlyItems.Contains(item.Id)).ToList();
            bool passed = session.RunAll(items, onlyUnits, onlyTransports, Path.GetFullPath(rigPath));
            string report = session.WriteReport();

            ApiConsole.Stdout.WriteLine();
            ApiConsole.Stdout.WriteLine("==================== C# API HIL ====================");
            foreach (KeyValuePair<string, string> r in session.Statuses())
            {
                ApiConsole.Stdout.WriteLine("{0,-12} {1}", r.Value.ToUpperInvariant(), r.Key);
            }
            ApiConsole.Stdout.WriteLine("report: " + Path.GetFullPath(report));
            // A call that hung leaves its thread blocked inside the API: do not wait for it
            Environment.Exit(passed ? 0 : 1);
            return passed ? 0 : 1;
        }
    }
}
