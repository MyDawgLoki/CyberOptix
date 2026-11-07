using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CyberOptix.Services
{
    public record ProcSample(int Pid, string Name, double CpuPct, double GpuPct, double MemMB, bool IsApp, string Title);

    public static class ProcessService
    {
        /// <summary>Windows needs these; ending them crashes or logs you out, so the app refuses.</summary>
        private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
        {
            "csrss", "wininit", "winlogon", "smss", "lsass", "services", "system", "registry",
            "memory compression", "secure system", "dwm",
        };

        private static readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> Previous = new();

        // Per-process GPU usage: Windows exposes one "GPU Engine" perf-counter instance per process per
        // engine (3D, copy, video decode, ...). Instances are kept open across polls, like Previous above,
        // because a freshly opened counter reports 0 on its first read.
        private static readonly Dictionary<string, PerformanceCounter> GpuCounters = new();
        private static readonly Regex GpuPidPattern = new(@"pid_(\d+)_", RegexOptions.Compiled);

        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);

        public static bool IsProtected(string name) => Protected.Contains(name);

        /// <summary>Processes that own a normal visible window, with that window's title. These are the "Apps"; everything else is background.</summary>
        private static Dictionary<int, string> AppWindows()
        {
            var map = new Dictionary<int, string>();
            EnumWindows((h, _) =>
            {
                if (!IsWindowVisible(h) || GetWindowTextLength(h) == 0) return true;
                if ((GetWindowLong(h, -20) & 0x80) != 0) return true; // tool windows
                int cloaked;
                if (DwmGetWindowAttribute(h, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0) return true;

                var cls = new StringBuilder(128);
                GetClassName(h, cls, cls.Capacity);
                if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;

                GetWindowThreadProcessId(h, out var pid);
                if (!map.ContainsKey((int)pid))
                {
                    var title = new StringBuilder(GetWindowTextLength(h) + 1);
                    GetWindowText(h, title, title.Capacity);
                    map[(int)pid] = title.ToString();
                }
                return true;
            }, IntPtr.Zero);
            return map;
        }

        /// <summary>GPU usage percent per PID, summed across that process's engine instances (3D, copy, video, ...) and capped at 100.</summary>
        private static Dictionary<int, double> ReadGpuUsageByPid()
        {
            var result = new Dictionary<int, double>();
            try
            {
                if (!PerformanceCounterCategory.Exists("GPU Engine")) return result;
                var instances = new PerformanceCounterCategory("GPU Engine").GetInstanceNames();
                var seen = new HashSet<string>(instances);

                foreach (var stale in GpuCounters.Keys.Where(k => !seen.Contains(k)).ToList())
                {
                    GpuCounters[stale].Dispose();
                    GpuCounters.Remove(stale);
                }

                foreach (var instance in instances)
                {
                    var m = GpuPidPattern.Match(instance);
                    if (!m.Success) continue;
                    int pid = int.Parse(m.Groups[1].Value);

                    if (!GpuCounters.TryGetValue(instance, out var counter))
                    {
                        try { counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true); }
                        catch { continue; }
                        GpuCounters[instance] = counter;
                    }

                    try
                    {
                        var value = counter.NextValue();
                        if (value <= 0) continue;
                        result[pid] = result.TryGetValue(pid, out var cur) ? cur + value : value;
                    }
                    catch { /* the process may have just exited */ }
                }
            }
            catch { /* no GPU scheduler exposing this category */ }

            foreach (var pid in result.Keys.ToList()) result[pid] = Math.Min(100, result[pid]);
            return result;
        }

        public static List<ProcSample> Sample()
        {
            var now = DateTime.UtcNow;
            var result = new List<ProcSample>();
            var alive = new HashSet<int>();
            int cores = Environment.ProcessorCount;
            var apps = AppWindows();
            var gpu = ReadGpuUsageByPid();

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == 0 || p.Id == 4) continue; // Idle and System

                    double cpu = 0;
                    try
                    {
                        var total = p.TotalProcessorTime;
                        if (Previous.TryGetValue(p.Id, out var prev))
                        {
                            var elapsed = (now - prev.At).TotalMilliseconds;
                            if (elapsed > 0)
                                cpu = Math.Clamp((total - prev.Cpu).TotalMilliseconds / elapsed / cores * 100, 0, 100);
                        }
                        Previous[p.Id] = (total, now);
                        alive.Add(p.Id);
                    }
                    catch
                    {
                        // Access denied for some system processes: show them with 0% CPU.
                    }

                    bool isApp = apps.TryGetValue(p.Id, out var title);
                    double gpuPct = gpu.TryGetValue(p.Id, out var g) ? g : 0;
                    result.Add(new ProcSample(p.Id, p.ProcessName, Math.Round(cpu, 1), Math.Round(gpuPct, 1), p.WorkingSet64 / (1024.0 * 1024.0), isApp, title ?? ""));
                }
                catch
                {
                    // The process exited while we were reading it.
                }
                finally
                {
                    p.Dispose();
                }
            }

            foreach (var gone in new List<int>(Previous.Keys))
                if (!alive.Contains(gone)) Previous.Remove(gone);

            return result;
        }

        /// <summary>Ends a process. Returns null on success, or a short reason it failed.</summary>
        public static string? Kill(int pid, bool wholeTree)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (IsProtected(p.ProcessName) || ProcessClassifier.Classify(p.ProcessName).Group == ProcessClassifier.Unkillable)
                    return $"{p.ProcessName} is needed by Windows (or your drivers or security) and can't be ended.";
                if (pid == Environment.ProcessId) return "That's CyberOptix itself.";
                p.Kill(wholeTree);
                return null;
            }
            catch (ArgumentException) { return "That process has already ended."; }
            catch (Exception ex) { return ex.Message; }
        }
    }
}
