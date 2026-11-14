using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    /// <summary>Tells whether the window in front is a web browser or a game. Read only: it looks at the window and its program, nothing else.</summary>
    public static class ForegroundApp
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "iexplore", "waterfox", "librewolf", "zen", "arc",
            "chromium", "thorium", "duckduckgo", "floorp", "palemoon", "maxthon", "yandex", "browser",
        };

        // Where games live, whichever launcher put them there.
        private static readonly string[] GameFolderMarkers =
        {
            @"\steamapps\common\", @"\epic games\", @"\riot games\", @"\gog galaxy\games\", @"\gog games\", @"\ubisoft game launcher\games\",
            @"\ea games\", @"\xboxgames\", @"\battle.net\games\", @"\rockstar games\",
        };

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr h, uint flags, System.Text.StringBuilder name, ref int size);

        private static string? ImagePath(int pid)
        {
            var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
            }
            finally { CloseHandle(h); }
        }

        /// <summary>Reads the program in front once: is it a web browser, and is it a game?</summary>
        public static void Read(out bool browser, out bool game)
        {
            browser = game = false;
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return;
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0 || pid == Environment.ProcessId) return;
                // Only the program's path is read, through a limited-rights handle (no memory access), which is all a protected game allows
                // anyway and keeps the check from looking like an attempt to inspect the game.
                string? path = ImagePath((int)pid);
                if (path == null) return;
                (browser, game) = Kind(System.IO.Path.GetFileNameWithoutExtension(path), path, AppSettings.Current.CustomGames.Select(g => g.Path));
            }
            catch { }
        }

        /// <summary>The decision, split in two: a browser by its program name, a game by where it is installed.</summary>
        public static (bool Browser, bool Game) Kind(string processName, string? exePath, IEnumerable<string?> gameFolders)
        {
            if (string.Equals(processName, "CyberOptix", StringComparison.OrdinalIgnoreCase)) return (false, false);
            if (Browsers.Contains(processName)) return (true, false);
            return (false, InGameFolder(exePath, gameFolders));
        }

        /// <summary>The decision itself: a browser by its program name, a game by where it is installed.</summary>
        public static bool Classify(string processName, string? exePath, IEnumerable<string?> gameFolders)
        {
            var (browser, game) = Kind(processName, exePath, gameFolders);
            return browser || game;
        }

        private static bool InGameFolder(string? exePath, IEnumerable<string?> gameFolders)
        {
            if (string.IsNullOrEmpty(exePath)) return false;

            var path = exePath.Replace('/', '\\');
            if (GameFolderMarkers.Any(m => path.Contains(m, StringComparison.OrdinalIgnoreCase))) return true;
            foreach (var folder in gameFolders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                var f = folder.Replace('/', '\\').TrimEnd('\\') + "\\";
                if (path.StartsWith(f, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
