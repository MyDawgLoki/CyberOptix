using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    /// <summary>One program found on this PC that could become a dock icon.</summary>
    public record FoundSoftware(string Name, string Path);

    /// <summary>
    /// Looks through the Start menu (shortcut files, and Store apps) for the programs installed on this PC, so the user can pick which become
    /// dock icons. It only reads the Start menu: nothing is run, nothing leaves the PC, and what the user picks is saved in their own settings.
    /// </summary>
    public static class SoftwareScanner
    {
        // Start-menu entries that are not programs people launch: removers, help, licences, web links to the vendor...
        private static readonly Regex Junk = new(
            @"uninstall|remove |readme|read me|release notes|license|licence|documentation|manual|\bhelp\b|website|support|changelog|what.?s new|updater|\bupdate\b|repair|configur|setup$|eula|privacy|feedback|survey|administrative tools|magnify|narrator|on-screen keyboard|command palette|^more\.\.\.|check for updates|app recovery|faq|module docs|install additional tools|developer (command|powershell)|error reporter|diagnostics|reference$|on the web|quick assist|character map|steps recorder|wordpad|overlay host",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Start-menu folders that hold Windows' own tools, not the user's programs.
        private static readonly string[] SystemFolders =
        {
            "Accessories", "Windows Accessories", "Windows System", "Windows Tools", "Windows PowerShell", "Windows Administrative Tools",
            "Administrative Tools", "System Tools", "Windows Ease of Access", "Maintenance", "Startup", "Windows Kits",
        };

        /// <summary>The programs found, sorted by name, leaving out anything already offered as a dock icon or already added.</summary>
        public static List<FoundSoftware> Scan()
        {
            var settings = AppSettings.Current;
            var found = new Dictionary<string, FoundSoftware>(StringComparer.OrdinalIgnoreCase);

            // 1. Shortcut files in the Start menu (programs, and Steam-style .url shortcuts)
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
            {
                string programs = System.IO.Path.Combine(root, "Programs");
                if (!Directory.Exists(programs)) continue;
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(programs, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase)).ToList(); }
                catch { continue; }
                foreach (var file in files)
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(file);
                    string relative = System.IO.Path.GetRelativePath(programs, file);
                    string top = relative.Contains(System.IO.Path.DirectorySeparatorChar) ? relative.Split(System.IO.Path.DirectorySeparatorChar)[0] : "";
                    if (SystemFolders.Any(s => string.Equals(s, top, StringComparison.OrdinalIgnoreCase))) continue;
                    if (Junk.IsMatch(name)) continue;
                    found.TryAdd(name, new FoundSoftware(name, file));
                }
            }

            // 2. Store apps have no shortcut file: they come from the Start menu's app list
            foreach (var (name, id) in HardwareSoftwareService.AllStartApps())
            {
                if (!id.Contains('!') || Junk.IsMatch(name)) continue;   // a Store app id looks like "Publisher.App_hash!App"
                if (id.StartsWith("Microsoft.Windows", StringComparison.OrdinalIgnoreCase) || id.StartsWith("MicrosoftWindows", StringComparison.OrdinalIgnoreCase) ||
                    id.StartsWith("windows.", StringComparison.OrdinalIgnoreCase) || id.StartsWith("Windows", StringComparison.OrdinalIgnoreCase)) continue;
                found.TryAdd(name, new FoundSoftware(name, ShellApps.PathFor(id)));
            }

            // leave out what is already an icon: the catalog's own (brands, makers' apps, tools) and what the user already added
            var taken = new HashSet<string>(DockWidgetCatalog.All.Select(d => Normalize(d.Title)));
            foreach (var a in settings.CustomDockApps) { taken.Add(Normalize(a.Name)); taken.Add(Normalize(System.IO.Path.GetFileNameWithoutExtension(a.Path))); }
            var addedPaths = new HashSet<string>(settings.CustomDockApps.Select(a => a.Path), StringComparer.OrdinalIgnoreCase);

            return found.Values
                .Where(f => !taken.Contains(Normalize(f.Name)) && !addedPaths.Contains(f.Path))
                .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static string Normalize(string s) => Regex.Replace(s ?? "", @"[^A-Za-z0-9]", "").ToLowerInvariant();
    }
}
