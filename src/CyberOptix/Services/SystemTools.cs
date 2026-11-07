using System;
using System.IO;

namespace CyberOptix.Services
{
    /// <summary>
    /// Full paths for the Windows tools CyberOptix starts. The app runs as administrator, and a program started by bare name (powershell.exe,
    /// schtasks.exe...) is looked for in the app's own folder first, so a planted file there would run with administrator rights. Starting
    /// them by their real path in the Windows folders closes that door.
    /// </summary>
    internal static class SystemTools
    {
        /// <summary>The full path of an in-box tool, or the name unchanged when it is not found in the Windows folders (or already has a path).</summary>
        public static string Resolve(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file)) return file;
            string system = Environment.SystemDirectory;
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (var dir in new[] { system, Path.Combine(system, "WindowsPowerShell", "v1.0"), windows })
            {
                if (string.IsNullOrEmpty(dir)) continue;
                var candidate = Path.Combine(dir, file);
                if (File.Exists(candidate)) return candidate;
            }
            return file;
        }

        public static string PowerShell => Resolve("powershell.exe");
        public static string Schtasks => Resolve("schtasks.exe");
        public static string Explorer => Resolve("explorer.exe");

        /// <summary>winget lives in the user's WindowsApps folder (an app execution alias): use that exact file when it is there.</summary>
        public static string Winget
        {
            get
            {
                var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
                return File.Exists(alias) ? alias : "winget.exe";
            }
        }
    }
}
