using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CyberOptix.Services
{
    /// <summary>
    /// If CyberOptix is force-quit (Task Manager, End process tree, a crash), nothing inside it gets to run, so the Windows taskbar stays hidden
    /// and the desktop items stay in the panel. To cover that, each CyberOptix starts a small watchdog: another copy of this program, started with
    /// --watchdog=&lt;process id&gt;, that does nothing but wait. When CyberOptix ends normally (closed, restarted, Windows shutting down) it tells
    /// the watchdog to stand down. When it ends any other way, the watchdog puts the taskbar back and moves every desktop item back to the desktop.
    /// It is started through cmd, so it is not a child of CyberOptix and survives "End process tree".
    /// </summary>
    public static class CrashGuard
    {
        public const string Argument = "--watchdog=";
        private static EventWaitHandle? _standDown;
        private static Mutex? _marker;

        private static string StandDownName(int pid) => @"Local\CyberOptix.StandDown." + pid;
        private static string MarkerName(int pid) => @"Local\CyberOptix.Watchdog." + pid;

        /// <summary>True when that process is a watchdog (so the "only one CyberOptix" check does not count it).</summary>
        public static bool IsWatchdog(int pid)
        {
            try
            {
                if (!Mutex.TryOpenExisting(MarkerName(pid), out var m)) return false;
                m.Dispose();
                return true;
            }
            catch { return false; }
        }

        /// <summary>Called by the real CyberOptix once it is running: starts its watchdog.</summary>
        public static void Start()
        {
            try
            {
                int pid = Environment.ProcessId;
                _standDown = new EventWaitHandle(false, EventResetMode.ManualReset, StandDownName(pid));
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                // "start" hands the watchdog to Windows and cmd exits, so the watchdog has no live parent to be taken down with.
                Process.Start(new ProcessStartInfo(cmd, "/c start \"\" \"" + exe + "\" " + Argument + pid) { UseShellExecute = false, CreateNoWindow = true });
            }
            catch { }
        }

        /// <summary>Called when CyberOptix ends on purpose (closed, restarted, Windows signing out or shutting down): the watchdog stays out of it.</summary>
        public static void StandDown()
        {
            try { _standDown?.Set(); } catch { }
        }

        /// <summary>The watchdog itself. Returns when there is nothing left to do.</summary>
        public static void RunWatchdog(int parentPid)
        {
            try
            {
                _marker = new Mutex(false, MarkerName(Environment.ProcessId));
                if (!EventWaitHandle.TryOpenExisting(StandDownName(parentPid), out var standDown)) return;   // not a CyberOptix of ours
                Process parent;
                try { parent = Process.GetProcessById(parentPid); }
                catch { return; }
                using var parentHandle = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(parent.Handle, false) };
                int which = WaitHandle.WaitAny(new WaitHandle[] { parentHandle, standDown });
                if (which == 1) return;   // it ended on purpose

                Thread.Sleep(1500);   // a moment for Windows to settle
                if (standDown.WaitOne(0)) return;   // it was a shutdown after all
                try { TaskbarTakeoverService.RestoreAfterKill(); } catch { }
                try { DesktopItemsService.MoveAllBack(); } catch { }
            }
            catch { }
        }
    }
}
