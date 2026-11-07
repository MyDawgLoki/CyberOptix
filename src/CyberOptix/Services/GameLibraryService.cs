using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    /// <summary>Finds installed Steam games, and keeps the games you add yourself.</summary>
    public static class GameLibraryService
    {
        // Steam installs tools next to games; these are not games.
        private static readonly Regex NotGames = new(
            @"^(Steamworks Common Redistributables|Proton|Steam Linux Runtime|Steam Controller Configs|SteamVR)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The Steam folder, from the registry, or null if Steam is not installed in the usual way.</summary>
        public static string? FindSteam()
        {
            string?[] candidates =
            {
                Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string,
                Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string,
                @"C:\Program Files (x86)\Steam",
            };
            return candidates
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c!.Replace('/', '\\'))
                .FirstOrDefault(c => Directory.Exists(Path.Combine(c, "steamapps")));
        }

        /// <summary>Reads every Steam library folder and returns the games installed in them.</summary>
        public static List<Game> ScanSteam(string steamRoot)
        {
            var libraries = new List<string> { steamRoot };
            var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    var lib = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (!libraries.Contains(lib, StringComparer.OrdinalIgnoreCase)) libraries.Add(lib);
                }
            }

            var games = new List<Game>();
            foreach (var lib in libraries)
            {
                var apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;

                foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    try
                    {
                        var text = File.ReadAllText(manifest);
                        var name = Regex.Match(text, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                        var dir = Regex.Match(text, "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;
                        if (name.Length == 0 || NotGames.IsMatch(name)) continue;

                        var common = Path.GetFullPath(Path.Combine(apps, "common"));
                        var path = Path.GetFullPath(Path.Combine(common, dir));
                        // An install folder must live inside steamapps\common; anything that points elsewhere is ignored.
                        if (!path.StartsWith(common + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                        games.Add(new Game { Name = name, Path = path, Source = "Steam" });
                    }
                    catch
                    {
                        // A manifest that is mid-update or unreadable is skipped.
                    }
                }
            }
            return games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Adds games to the saved list, skipping any already there. A scan also skips games the user removed; adding one by hand brings it back.
        /// Returns the games that were actually new.
        /// </summary>
        public static List<Game> AddCustom(IEnumerable<Game> games, bool fromScan)
        {
            var saved = AppSettings.Current.CustomGames;
            var removed = AppSettings.Current.RemovedGames;
            var added = new List<Game>();
            foreach (var g in games)
            {
                if (saved.Any(s => s.Name.Equals(g.Name, StringComparison.OrdinalIgnoreCase))) continue;
                if (fromScan && removed.Any(r => r.Equals(g.Name, StringComparison.OrdinalIgnoreCase))) continue;
                if (!fromScan) removed.RemoveAll(r => r.Equals(g.Name, StringComparison.OrdinalIgnoreCase));
                saved.Add(g);
                added.Add(g);
            }
            AppSettings.Save();
            return added;
        }

        public static void RemoveCustom(string name)
        {
            // Only the saved entry is removed. The game itself is never touched. It is remembered so a later scan does not list it again.
            AppSettings.Current.CustomGames.RemoveAll(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (!AppSettings.Current.RemovedGames.Any(r => r.Equals(name, StringComparison.OrdinalIgnoreCase)))
                AppSettings.Current.RemovedGames.Add(name);
            AppSettings.Save();
        }
    }
}
