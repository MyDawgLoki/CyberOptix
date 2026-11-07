using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace CyberOptix.Services
{
    /// <summary>Closes CyberOptix and starts it again a moment later (the new copy waits until this one has let go of the "one at a time" lock).</summary>
    public static class AppRestart
    {
        public static bool Now()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                Process.Start(new ProcessStartInfo(cmd, "/c ping -n 3 127.0.0.1 >nul & start \"\" \"" + exe + "\"") { UseShellExecute = false, CreateNoWindow = true });
                Application.Current.Shutdown();
                return true;
            }
            catch { return false; }
        }
    }
}
