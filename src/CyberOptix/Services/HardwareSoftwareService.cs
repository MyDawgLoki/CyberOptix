using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace CyberOptix.Services
{
    /// <summary>A maker's own app for hardware settings (lighting, fans, performance), and how to recognize it on this PC.</summary>
    public record VendorApp(string Id, string Brand, string Name, string NamePattern, string[] Processes, string Covers, string Group = "Hardware software");

    /// <summary>One vendor app that was found on this PC.</summary>
    public record FoundVendorApp(VendorApp App, string StartAppId, bool Running);

    /// <summary>
    /// Finds the hardware makers' own apps installed on this PC (MSI, ASUS, Corsair, Razer, Logitech...) and opens them. Their lighting, fan and
    /// performance settings live in those apps; CyberOptix only lists and opens what is installed, it never changes their settings itself.
    /// The list of known apps is generic: it matches by the name an app shows in the Start menu, so it finds whatever you happen to have.
    /// </summary>
    public static class HardwareSoftwareService
    {
        public static readonly VendorApp[] Known =
        {
            new("msi-center", "MSI", "MSI Center", @"^MSI Center$", new[] { "MSI Center", "MSI.CentralServer", "MSI_Central_Service" },
                "Mystic Light lighting effects, fan and performance profiles, Super Charger, True Color and more for MSI hardware."),
            new("msi-dragon", "MSI", "MSI Dragon Center", @"^(MSI )?Dragon Center$", new[] { "DragonCenter", "Dragon Center" },
                "The older MSI app for lighting, fans and performance."),
            new("mystic-light", "MSI", "Mystic Light", @"^(MSI )?Mystic Light$", new[] { "MysticLight", "Mystic Light" },
                "MSI's stand-alone lighting app."),
            new("msi-afterburner", "MSI", "MSI Afterburner", @"^MSI Afterburner$", new[] { "MSIAfterburner" },
                "Graphics card overclocking, fan curves and the on-screen display."),
            new("armoury-crate", "ASUS", "Armoury Crate", @"^Armoury Crate$", new[] { "ArmouryCrate", "ArmouryCrate.UserSessionHelper" },
                "Aura Sync lighting, fans, performance modes and game library for ASUS hardware."),
            new("aura-creator", "ASUS", "AURA Creator", @"^(ASUS )?AURA Creator$", new[] { "AuraCreator" },
                "Custom Aura lighting effects."),
            new("icue", "Corsair", "iCUE", @"^iCUE$", new[] { "iCUE" },
                "Lighting, fans, pumps and settings for Corsair devices."),
            new("razer-synapse", "Razer", "Razer Synapse", @"^Razer Synapse", new[] { "RazerCentral", "Razer Synapse 3", "Razer Synapse" },
                "Chroma lighting and settings for Razer devices."),
            new("wallpaper-engine", "Steam", "Wallpaper Engine", @"^Wallpaper Engine$", new[] { "wallpaper32", "wallpaper64", "wallpaperengine" },
                "Opens Wallpaper Engine (through Steam) to pick or change your animated wallpapers.", "System controls"),
            new("thx-spatial", "Razer", "THX Spatial Audio", @"^THX Spatial Audio", new[] { "THXSpatialAudio", "THX Spatial Audio" },
                "Surround sound for headphones, with the THX Spatial Audio settings."),
            new("logitech-ghub", "Logitech", "Logitech G HUB", @"^(Logitech )?G HUB$", new[] { "lghub", "lghub_agent" },
                "Lighting, DPI and button settings for Logitech G devices."),
            new("glorious-core", "Glorious", "Glorious Core", @"^Glorious CORE$", new[] { "Glorious Core" },
                "Lighting, DPI and button settings for Glorious mice and keyboards."),
            new("steelseries-gg", "SteelSeries", "SteelSeries GG", @"^SteelSeries GG$", new[] { "SteelSeriesGG", "SteelSeriesEngine" },
                "Lighting and settings for SteelSeries devices."),
            new("nzxt-cam", "NZXT", "NZXT CAM", @"^(NZXT )?CAM$", new[] { "NZXT CAM" },
                "Lighting, fans and cooler settings for NZXT hardware."),
            new("hyperx-ngenuity", "HyperX", "HyperX NGENUITY", @"^HyperX NGENUITY", new[] { "HyperX NGENUITY" },
                "Lighting and settings for HyperX devices."),
            new("gigabyte-control", "Gigabyte", "Gigabyte Control Center", @"^(GIGABYTE )?Control Center$", new[] { "GigabyteControlCenter", "GCC" },
                "Lighting, fans and performance for Gigabyte hardware."),
            new("rgb-fusion", "Gigabyte", "RGB Fusion", @"^RGB Fusion", new[] { "RGBFusion" },
                "Gigabyte's lighting app."),
            new("nvidia-app", "NVIDIA", "NVIDIA app", @"^NVIDIA app$", new[] { "NVIDIA app" },
                "Graphics driver, game settings and recording for NVIDIA graphics cards."),
        };

        private static List<(string Name, string Id)>? _apps;
        private static readonly object Gate = new();

        /// <summary>Every app in the Start menu (name and app id), read once. Win32 programs and Store apps both show up here.</summary>
        private static List<(string Name, string Id)> StartApps(bool refresh)
        {
            lock (Gate)
            {
                if (_apps != null && !refresh) return _apps;
                var list = new List<(string, string)>();
                try
                {
                    var type = Type.GetTypeFromProgID("Shell.Application");
                    if (type != null)
                    {
                        dynamic shell = Activator.CreateInstance(type)!;
                        dynamic folder = shell.NameSpace("shell:AppsFolder");
                        foreach (dynamic item in folder.Items())
                        {
                            string name = item.Name, id = item.Path;
                            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id)) list.Add((name, id));
                        }
                    }
                }
                catch { }
                return _apps = list;
            }
        }

        /// <summary>Every app in the Start menu as (name, app id). Slow the first time, so call it off the UI thread.</summary>
        public static List<(string Name, string Id)> AllStartApps() => new(StartApps(false));

        /// <summary>Which of the known apps are installed here. Slow the first time (it reads the Start menu), so call it off the UI thread.</summary>
        public static List<FoundVendorApp> Scan(bool refresh = false)
        {
            var apps = StartApps(refresh);
            var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { foreach (var p in Process.GetProcesses()) { running.Add(p.ProcessName); p.Dispose(); } } catch { }

            var found = new List<FoundVendorApp>();
            foreach (var v in Known)
            {
                var hit = apps.FirstOrDefault(a => Regex.IsMatch(a.Name, v.NamePattern, RegexOptions.IgnoreCase));
                if (hit.Id == null) continue;
                found.Add(new FoundVendorApp(v, hit.Id, v.Processes.Any(running.Contains)));
            }
            return found;
        }

        /// <summary>The shell address of a Start-menu app (a program or a Store app), which opens it and gives it its icon.</summary>
        public static string Target(string startAppId) =>
            startAppId.Contains("://") ? startAppId : ShellApps.PathFor(startAppId);   // a Steam game is its own web-style address, not a Store app id

        /// <summary>The Start-menu id of a known vendor app on this PC, or null when it is not installed.</summary>
        public static string? StartAppIdOf(string vendorId)
        {
            var v = Known.FirstOrDefault(k => k.Id == vendorId);
            if (v == null) return null;
            var hit = StartApps(false).FirstOrDefault(a => Regex.IsMatch(a.Name, v.NamePattern, RegexOptions.IgnoreCase));
            return hit.Id;
        }

        /// <summary>
        /// The Start-menu shortcut file (.lnk or .url) of a known app, for apps that are a web-style address (a Steam game) and so have no Store
        /// icon of their own: the shortcut carries the icon.
        /// </summary>
        public static string? ShortcutFileFor(string vendorId)
        {
            var v = Known.FirstOrDefault(k => k.Id == vendorId);
            if (v == null) return null;
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
            {
                try
                {
                    var programs = System.IO.Path.Combine(root, "Programs");
                    if (!System.IO.Directory.Exists(programs)) continue;
                    foreach (var file in System.IO.Directory.EnumerateFiles(programs, "*.*", System.IO.SearchOption.AllDirectories))
                    {
                        var ext = System.IO.Path.GetExtension(file);
                        if (!ext.Equals(".url", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) continue;
                        if (Regex.IsMatch(System.IO.Path.GetFileNameWithoutExtension(file), v.NamePattern, RegexOptions.IgnoreCase)) return file;
                    }
                }
                catch { }
            }
            return null;
        }

        public static bool IsInstalled(string vendorId) => StartAppIdOf(vendorId) != null;

        /// <summary>Opens the app the way the Start menu does. It goes through Explorer, so it starts as you and not as administrator.</summary>
        public static void OpenById(string vendorId)
        {
            var id = StartAppIdOf(vendorId);
            if (id != null) DockService.Launch(Target(id));
        }

        /// <summary>Reads the Start menu in the background so the first look at the icons does not wait for it.</summary>
        public static void Warm() => System.Threading.Tasks.Task.Run(() => StartApps(false));
    }
}
