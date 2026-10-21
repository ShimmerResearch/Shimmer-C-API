using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace ShimmerApiHil
{
    /// <summary>One unit on the bench, as the rig file names it.</summary>
    public sealed class Unit
    {
        public string Name;
        /// <summary>shimmer3 or shimmer3r.</summary>
        public string Platform;
        /// <summary>The Bluetooth MAC, 12 hex digits, upper case, no separators.</summary>
        public string Mac;
        public string Radio = "";
        public List<string> Transports = new List<string>();
        public double AccelToleranceMs2 = 1.0;
        /// <summary>"item:transport" or "item" -> why it is not tested here: reported not_covered.</summary>
        public Dictionary<string, string> KnownIssues = new Dictionary<string, string>();

        public string KnownIssue(string item, string transport)
        {
            string why;
            if (KnownIssues.TryGetValue(item + ":" + transport, out why) || KnownIssues.TryGetValue(item, out why))
            {
                return why;
            }
            return null;
        }

        /// <summary>
        /// The COM port Windows made for this unit's Bluetooth Classic side, found by its MAC in the
        /// port's device ID, never by COM number: Windows reassigns those. Of the two ports a paired
        /// unit gets, only the outgoing one carries the MAC. Null if there is none.
        /// </summary>
        public string ClassicPort()
        {
            using (var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'BTHENUM%'"))
            {
                foreach (ManagementObject device in searcher.Get())
                {
                    string id = ((string)device["PNPDeviceID"] ?? "").ToUpperInvariant();
                    string name = (string)device["Name"] ?? "";
                    if (id.Contains("&" + Mac + "_"))
                    {
                        Match m = Regex.Match(name, @"\((COM\d+)\)");
                        if (m.Success)
                        {
                            return m.Groups[1].Value;
                        }
                    }
                }
            }
            return null;
        }
    }

    /// <summary>
    /// The bench, from the firmware HIL's rig.toml format (log-and-stream-common Test/hil), as the
    /// shimmer-logandstream HIL reads it: one bench file can serve every suite. Keys this suite does
    /// not use are ignored. Its own keys are a unit's transports, accel_tolerance_ms2 and
    /// known_issues, and the bench's settle_seconds, all as the shimmer-logandstream suite has them.
    /// </summary>
    public sealed class Rig
    {
        public const double DefaultSettleSeconds = 8.0;
        public static readonly string[] AllTransports = { "classic", "ble" };

        public string Bench = "unnamed";
        public double SettleSeconds = DefaultSettleSeconds;
        public List<Unit> Units = new List<Unit>();

        public static Rig Load(string path)
        {
            TomlTable model = Toml.ToModel(File.ReadAllText(path));
            var rig = new Rig();
            object value;
            if (model.TryGetValue("bench", out value))
            {
                rig.Bench = Convert.ToString(value);
            }
            if (model.TryGetValue("settle_seconds", out value))
            {
                rig.SettleSeconds = Convert.ToDouble(value);
            }
            if (model.TryGetValue("unit", out value))
            {
                foreach (TomlTable u in (TomlTableArray)value)
                {
                    rig.Units.Add(LoadUnit(path, u));
                }
            }
            return rig;
        }

        static Unit LoadUnit(string path, TomlTable u)
        {
            var unit = new Unit
            {
                Name = Get(u, "name", ""),
                Platform = Get(u, "platform", ""),
                Mac = Get(u, "mac", "").Replace(":", "").Replace("-", "").ToUpperInvariant(),
                Radio = Get(u, "radio", ""),
            };
            if (unit.Mac.Length != 12 || !Regex.IsMatch(unit.Mac, "^[0-9A-F]{12}$"))
            {
                throw new FormatException(path + ": unit " + unit.Name + ": mac must be 12 hex digits");
            }
            if (unit.Platform != "shimmer3" && unit.Platform != "shimmer3r")
            {
                throw new FormatException(path + ": unit " + unit.Name + ": platform must be shimmer3 or shimmer3r, not '" + unit.Platform + "'");
            }
            object value;
            if (u.TryGetValue("transports", out value))
            {
                unit.Transports = ((TomlArray)value).Select(t => Convert.ToString(t)).ToList();
            }
            else
            {
                unit.Transports = new List<string> { "classic" };
            }
            foreach (string t in unit.Transports)
            {
                if (!AllTransports.Contains(t))
                {
                    throw new FormatException(path + ": unit " + unit.Name + ": transport '" + t + "' is not classic or ble");
                }
            }
            if (u.TryGetValue("accel_tolerance_ms2", out value))
            {
                unit.AccelToleranceMs2 = Convert.ToDouble(value);
            }
            if (u.TryGetValue("known_issues", out value))
            {
                foreach (KeyValuePair<string, object> issue in (TomlTable)value)
                {
                    unit.KnownIssues[issue.Key] = Convert.ToString(issue.Value);
                }
            }
            return unit;
        }

        static string Get(TomlTable table, string key, string fallback)
        {
            object value;
            return table.TryGetValue(key, out value) ? Convert.ToString(value) : fallback;
        }
    }
}
