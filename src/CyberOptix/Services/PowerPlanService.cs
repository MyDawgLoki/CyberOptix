using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace CyberOptix.Services
{
    /// <summary>
    /// Windows power plans, through powercfg. "Ultimate Performance" is made once and then reused: copying the built-in plan again on every
    /// Apply left a new identical plan in Windows each time.
    /// </summary>
    public static class PowerPlanService
    {
        public const string UltimateName = "Ultimate Performance";
        private const string UltimateTemplate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        private const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        private static readonly Regex Line = new(@"Power Scheme GUID:\s*([0-9a-fA-F\-]{36})\s*\((.*?)\)(\s*\*)?", RegexOptions.Compiled);

        public record Plan(string Id, string Name, bool Active);

        private static string Run(string args)
        {
            try
            {
                var psi = new ProcessStartInfo(SystemTools.Resolve("powercfg.exe"), args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using var p = Process.Start(psi);
                if (p == null) return "";
                var text = p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
                return text;
            }
            catch { return ""; }
        }

        public static List<Plan> List() =>
            Line.Matches(Run("/list")).Select(m => new Plan(m.Groups[1].Value, m.Groups[2].Value.Trim(), m.Groups[3].Success)).ToList();

        /// <summary>Switches to the Ultimate Performance plan, making it the one time if there is none. Falls back to High performance.</summary>
        public static void UseUltimate()
        {
            var existing = List().FirstOrDefault(p => p.Name == UltimateName);
            string? id = existing?.Id;
            if (id == null)
            {
                var m = Line.Match(Run("/duplicatescheme " + UltimateTemplate));   // prints the new plan's id
                if (m.Success) id = m.Groups[1].Value;
            }
            Run("/setactive " + (id ?? HighPerformance));
        }

        public static void UseBalanced() => Run("/setactive " + Balanced);

        /// <summary>
        /// Deletes the extra "Ultimate Performance" copies, keeping one (the active one if it is a copy). Nothing else is touched:
        /// not the active plan, not Balanced, High performance, Power saver or a plan with another name. Returns how many were removed.
        /// </summary>
        public static int RemoveDuplicateUltimate()
        {
            var copies = List().Where(p => p.Name == UltimateName).ToList();
            if (copies.Count < 2) return 0;
            var keep = copies.FirstOrDefault(p => p.Active) ?? copies[0];
            int removed = 0;
            foreach (var p in copies.Where(p => p.Id != keep.Id && !p.Active))
            {
                Run("/delete " + p.Id);
                removed++;
            }
            return removed;
        }
    }
}
