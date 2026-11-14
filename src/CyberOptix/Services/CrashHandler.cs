using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace CyberOptix.Services
{
    /// <summary>
    /// Catches errors nobody handled. A single error on the screen's thread is written to <c>crash.log</c> and CyberOptix carries on; a burst of
    /// them, or an error that cannot be recovered from, is logged, the Windows taskbar is put back, and CyberOptix starts itself again (at most
    /// once every five minutes, so a bug at start-up cannot loop). The watchdog covers the desktop items.
    /// The log is in the administrator-only data folder, holds the error and its call stack (profile and install folders are replaced by
    /// placeholders), is kept under about 30 KB, and is never sent anywhere.
    /// </summary>
    public static class CrashHandler
    {
        private const string LogName = "crash.log";
        private const string RestartStamp = "crash-restart.txt";
        private static readonly TimeSpan RestartGap = TimeSpan.FromMinutes(5);
        private static readonly Queue<DateTime> Recent = new();
        private static readonly object Gate = new();
        private static int _dying;

        public static void Install(Application app)
        {
            app.DispatcherUnhandledException += OnUiException;
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Fatal(e.ExceptionObject as Exception, "background thread", exitNow: false);
            TaskScheduler.UnobservedTaskException += (_, e) => { Log(e.Exception, "background task (ignored)"); e.SetObserved(); };
        }

        private static void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            bool storm;
            lock (Gate)
            {
                var now = DateTime.UtcNow;
                Recent.Enqueue(now);
                while (Recent.Count > 0 && now - Recent.Peek() > TimeSpan.FromSeconds(10)) Recent.Dequeue();
                storm = Recent.Count > 5;   // an error that keeps coming back is not one to carry on through
            }
            if (!storm)
            {
                Log(e.Exception, "screen thread (carried on)");
                e.Handled = true;
                return;
            }
            e.Handled = true;
            Fatal(e.Exception, "screen thread (repeating)", exitNow: true);
        }

        private static void Fatal(Exception? ex, string where, bool exitNow)
        {
            if (System.Threading.Interlocked.Exchange(ref _dying, 1) == 1) return;
            Log(ex, where + " (fatal)");
            try { TaskbarTakeoverService.RestoreTaskbar(); } catch { }
            TryRestart();
            // Ending without telling the watchdog is what makes it put the desktop items back.
            if (exitNow) Environment.Exit(1);
        }

        private static void TryRestart()
        {
            try
            {
                var stamp = AppPaths.Secure(RestartStamp);
                if (File.Exists(stamp) && long.TryParse(File.ReadAllText(stamp).Trim(), out var ticks)
                    && DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) < RestartGap) return;   // it already restarted a moment ago
                File.WriteAllText(stamp, DateTime.UtcNow.Ticks.ToString());

                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                // The new copy waits for this one to let go of the "one at a time" lock, and starts quietly in the tray.
                Process.Start(new ProcessStartInfo(cmd, "/c ping -n 6 127.0.0.1 >nul & start \"\" \"" + exe + "\" --startup") { UseShellExecute = false, CreateNoWindow = true });
            }
            catch { }
        }

        private static string Clean(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            try
            {
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(profile)) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
                var app = AppContext.BaseDirectory.TrimEnd('\\');
                if (!string.IsNullOrEmpty(app)) text = text.Replace(app, "<app>", StringComparison.OrdinalIgnoreCase);
                var machine = Environment.UserName;
                if (!string.IsNullOrEmpty(machine) && machine.Length > 2) text = text.Replace(machine, "<user>", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
            return text;
        }

        private static void Log(Exception? ex, string where)
        {
            try
            {
                var lines = new List<string> { $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {where}  v{typeof(CrashHandler).Assembly.GetName().Version}" };
                for (var e = ex; e != null; e = e.InnerException)
                {
                    lines.Add("  " + e.GetType().FullName + ": " + Clean(e.Message).Replace("\r", " ").Replace("\n", " "));
                    foreach (var l in Clean(e.StackTrace).Split('\n').Take(12)) lines.Add("    " + l.TrimEnd('\r'));
                    if (lines.Count > 40) break;
                }
                lines.Add("");
                lock (Gate)
                {
                    var path = AppPaths.Secure(LogName);
                    var old = File.Exists(path) ? File.ReadAllText(path) : "";
                    var text = old + string.Join(Environment.NewLine, lines) + Environment.NewLine;
                    if (text.Length > 30_000) text = text.Substring(text.Length - 24_000);   // keep the newest
                    File.WriteAllText(path, text);
                }
            }
            catch { }
        }
    }
}
