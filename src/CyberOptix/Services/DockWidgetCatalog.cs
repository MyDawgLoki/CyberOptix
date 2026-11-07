using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CyberOptix.Services
{
    /// <summary>One optional icon for the system end of the dock. Clicking it opens a Windows settings page or runs a small tool.</summary>
    public record DockWidgetDef(string Id, string Group, string Title, string Description, string Glyph, Action Run, string? BrandId = null, string? VendorId = null, string? AppPath = null);

    /// <summary>
    /// The icons you can add to the dock next to volume, network and the clock. They only open something, so they change nothing on their own.
    /// The glyphs are from Segoe Fluent Icons (Segoe MDL2 Assets on Windows 10), which share these codes.
    /// </summary>
    public static class DockWidgetCatalog
    {
        public const string Live = "Live information";
        public const string Settings = "Windows settings";
        public const string Tools = "Tools";
        public const string Social = "Social, streaming and chat";
        public const string AI = "AI platforms";
        public const string Hardware = "Hardware software";
        public const string SystemControls = "System controls";
        public const string Software = "Software";

        public static readonly string[] Groups = { Live, SystemControls, Software, Social, AI, Hardware, Settings, Tools };

        /// <summary>The groups that can have a dock of their own (the Individual Dock switch on the Dock page).</summary>
        public static readonly string[] DockableGroups = { SystemControls, Software, Social, AI, Hardware };

        /// <summary>True when this icon belongs to a group that has been given its own dock, so the main dock leaves it out.</summary>
        public static bool IsInOwnDock(string id)
        {
            var def = Find(id);
            if (def == null) return false;
            var s = CyberOptix.Models.AppSettings.Current;
            return s.IndividualDocks.ContainsKey(def.Group);   // in a dock of its own
        }

        /// <summary>The id of the weather icon. It is drawn by the dock itself (temperature and a details popup), so it has no action of its own.</summary>
        public const string WeatherId = "weather";

        /// <summary>The id of the music note. The dock opens the "Now playing" panel from it, so it has no action of its own either.</summary>
        public const string MediaId = "media";

        /// <summary>The id of the widgets icon. The dock opens a list of desktop widgets from it, so it has no action of its own.</summary>
        public const string WidgetsId = "widgets";
        public const string TaskManagerId = "taskmgr";
        /// <summary>The power icon: the dock opens a small Restart / Shut down menu above it, so it has no action of its own.</summary>
        public const string PowerId = "power";

        private static void Start(string file, string? args = null)
        {
            try { Process.Start(new ProcessStartInfo(SystemTools.Resolve(file), args ?? "") { UseShellExecute = true }); } catch { }
        }

        private static DockWidgetDef Page(string id, string title, string description, string glyph, string uri) =>
            new(id, Settings, title, description, glyph, () => Start(uri));

        private static DockWidgetDef Tool(string id, string title, string description, string glyph, Action run) =>
            new(id, Tools, title, description, glyph, run);

        /// <summary>Shuts the PC down or restarts it at once. Asks first, because everything open is closed.</summary>
        public static void PowerAction(bool restart)
        {
            string what = restart ? "Restart" : "Shut down";
            var answer = System.Windows.MessageBox.Show(what + " the PC now?" + Environment.NewLine + Environment.NewLine + "Save your work first: everything open will be closed.",
                what, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question, System.Windows.MessageBoxResult.No);
            if (answer == System.Windows.MessageBoxResult.Yes) Start("shutdown.exe", restart ? "/r /t 0" : "/s /t 0");
        }

        /// <summary>Restarts into the firmware (BIOS/UEFI) setup. It asks first, because it restarts the PC at once.</summary>
        private static void RestartToBios()
        {
            var answer = System.Windows.MessageBox.Show("Restart the PC now and open its BIOS / UEFI setup?" + Environment.NewLine + Environment.NewLine + "Save your work first: everything open will be closed.",
                "Restart to BIOS", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question, System.Windows.MessageBoxResult.No);
            if (answer == System.Windows.MessageBoxResult.Yes) Start("shutdown.exe", "/r /fw /t 1");
        }

        private static void ShowDesktop()
        {
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type == null) return;
                var shell = Activator.CreateInstance(type);
                type.InvokeMember("ToggleDesktop", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
            }
            catch { }
        }

        /// <summary>
        /// Which group a brand icon belongs to. RULE: social media, streaming, video and voice-chat apps are one group, AI platforms are another,
        /// and anything new gets sorted into the right one (or a new group) instead of joining a catch-all list.
        /// </summary>
        private static string GroupOf(string brandId) => brandId switch
        {
            "chatgpt" or "gemini" or "grok" or "claude" or "vscode" or "ollama" or "opencode" or "codex" or "github" or "pinokio" or "vercel" or "cursor" or "perplexity" or "multiset" or "claudeduet" or BrandLaunchers.GrokBotId => AI,
            _ => Social,
        };

        /// <summary>The icons for apps and websites (ChatGPT, Discord, Claude, VS Code...). Each draws the service's logo and opens its app, or its website when there is no app.</summary>
        private static IEnumerable<DockWidgetDef> Brands() => BrandLaunchers.All.Select(l =>
        {
            var id = l.Id;
            string how = l.Url == null
                ? $"Opens {l.Name} if it is installed on this PC."
                : $"Opens the {l.Name} app if you have it, otherwise its website in your default browser.";
            return new DockWidgetDef("app_" + id, GroupOf(id), l.Name, how, "", () => BrandLaunchers.Open(id), id);
        });

        /// <summary>
        /// The hardware makers' own apps (MSI Center, Armoury Crate, iCUE...). Each icon opens its app; they only show for apps that are installed
        /// on this PC (see <see cref="Available"/>). Their lighting, fan and performance settings stay in those apps.
        /// </summary>
        private static IEnumerable<DockWidgetDef> Vendors() => HardwareSoftwareService.Known.Select(v =>
        {
            var id = v.Id;
            return new DockWidgetDef("hw_" + id, v.Group, v.Name, $"Opens {v.Name} ({v.Brand}): {v.Covers}", "", () => HardwareSoftwareService.OpenById(id), null, id);
        });

        /// <summary>False for a hardware app icon whose app is not installed here, so the settings only list what you can really open.</summary>
        public static bool Available(DockWidgetDef def) => def.VendorId == null || HardwareSoftwareService.IsInstalled(def.VendorId);

        private static readonly List<DockWidgetDef> BuiltIn = Core().Concat(Brands()).Concat(Vendors()).ToList();

        /// <summary>The icons the user added themselves (a program or shortcut each), in the System controls group.</summary>
        private static IEnumerable<DockWidgetDef> Customs() => CyberOptix.Models.AppSettings.Current.CustomDockApps
            .Where(a => !string.IsNullOrWhiteSpace(a.Id) && !string.IsNullOrWhiteSpace(a.Path))
            .Select(a =>
            {
                var path = a.Path;
                return new DockWidgetDef("custom_" + a.Id, string.IsNullOrWhiteSpace(a.Group) ? SystemControls : a.Group, string.IsNullOrWhiteSpace(a.Name) ? System.IO.Path.GetFileNameWithoutExtension(path) : a.Name,
                    (System.IO.Directory.Exists(path) ? "Opens the folder " : "Opens ") + System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar)) + ", which you added.", "", () => DockService.Launch(path), null, null, path);
            });

        /// <summary>The icons of one group in the order the user arranged them (anything not arranged yet follows, in the usual order).</summary>
        public static List<DockWidgetDef> InGroup(string group)
        {
            var defs = All.Where(d => d.Group == group).ToList();
            if (!CyberOptix.Models.AppSettings.Current.IconOrder.TryGetValue(group, out var order) || order.Count == 0) return defs;
            return defs.OrderBy(d => { int i = order.IndexOf(d.Id); return i < 0 ? int.MaxValue : i; }).ToList();
        }

        /// <summary>Every icon in the order they are shown: the groups in their usual order, and each group's icons as arranged.</summary>
        public static List<DockWidgetDef> Ordered() => Groups.SelectMany(InGroup).ToList();

        /// <summary>
        /// Drops an icon into a group (a dock), just before another icon of that group or at the end, and remembers it. The icons that are drawn by
        /// the main dock itself (media, weather, widgets) stay where they are.
        /// </summary>
        public static bool MoveTo(string id, string group, string? beforeId)
        {
            var s = CyberOptix.Models.AppSettings.Current;
            var def = Find(id);
            if (def == null || id == beforeId || def.Group == Live || group == Live || !Groups.Contains(group)) return false;
            string baseGroup = Raw().First(d => d.Id == id).Group;
            if (group == baseGroup) s.IconGroups.Remove(id); else s.IconGroups[id] = group;
            var ids = InGroup(group).Select(d => d.Id).Where(x => x != id).ToList();
            int at = beforeId == null ? -1 : ids.IndexOf(beforeId);
            if (at < 0) ids.Add(id); else ids.Insert(at, id);
            s.IconOrder[group] = ids;
            CyberOptix.Models.AppSettings.Save();
            return true;
        }

        private static List<DockWidgetDef> Raw() => BuiltIn.Concat(Customs()).ToList();

        /// <summary>Every icon: the built-in ones and the programs the user added.</summary>
        public static IReadOnlyList<DockWidgetDef> All
        {
            get
            {
                var moved = CyberOptix.Models.AppSettings.Current.IconGroups;
                return Raw().Select(d => moved.TryGetValue(d.Id, out var g) && Groups.Contains(g) ? d with { Group = g } : d).ToList();
            }
        }

        /// <summary>Adds a program or shortcut as a dock icon (in System controls) and ticks it so it shows straight away.</summary>
        public static DockWidgetDef AddCustom(string path, string name, string group = SystemControls)
        {
            var app = new CyberOptix.Models.CustomDockApp { Id = Guid.NewGuid().ToString("N").Substring(0, 10), Name = name, Path = path, Group = group };
            var s = CyberOptix.Models.AppSettings.Current;
            s.CustomDockApps.Add(app);
            s.DockExtraIcons.Add("custom_" + app.Id);
            CyberOptix.Models.AppSettings.Save();
            return Find("custom_" + app.Id)!;
        }

        public static void RemoveCustom(string id)
        {
            var s = CyberOptix.Models.AppSettings.Current;
            s.CustomDockApps.RemoveAll(a => "custom_" + a.Id == id);
            s.DockExtraIcons.Remove(id);
            CyberOptix.Models.AppSettings.Save();
        }

        private static List<DockWidgetDef> Core() => new List<DockWidgetDef>
        {
            // ---------- live information ----------
            new(MediaId, Live, "Media controls", "A music note that opens the Media Player widget on your desktop (album art, the track, previous / play / next, and buttons for YouTube, Spotify, SoundCloud, iTunes and Suno) if it is not open already.",
                "", () => { }),
            new("cyberoptix", Software, "CyberOptix", "Opens the CyberOptix window.", "",
                () => { try { (System.Windows.Application.Current.MainWindow as MainWindow)?.ShowFromTray(); } catch { } }, null, null, Environment.ProcessPath),
            new(WeatherId, Live, "Weather", "The weather icon and temperature. Click it for feels like, humidity, wind, UV index and the next days. The city and units are the ones on the Widgets page. It looks up the weather on the internet (Open-Meteo) every 20 minutes, and only while this is ticked.",
                "", () => { }),

            new(WidgetsId, Live, "Widgets", "Opens the Widgets window: put desktop widgets (clock, weather, media player, temperatures, CPU, GPU) and a desktop icon for any tool, Windows settings page, app or website on your desktop, or open Windows' own Widgets. Click one to add it, click it again to take it off. Widgets stay locked in place: to move them, Right Click the desktop -> Show More Options -> Lock/Unlock Widgets.",
                "", () => { }),

            // ---------- Windows settings ----------
            Page("display", "Display", "Resolution, brightness, HDR and multiple displays.", "", "ms-settings:display"),
            Page("nightlight", "Night light", "Warmer screen colors in the evening.", "", "ms-settings:nightlight"),
            Page("graphics", "Graphics settings", "Choose which graphics card each game or app uses.", "", "ms-settings:display-advancedgraphics"),
            Page("gamemode", "Game Mode", "Windows Game Mode and the other gaming settings.", "", "ms-settings:gaming-gamemode"),
            Page("vpn", "VPN", "Your VPN connections.", "", "ms-settings:network-vpn"),
            Page("airplane", "Airplane mode", "Turn all wireless on or off.", "", "ms-settings:network-airplanemode"),
            Page("hotspot", "Mobile hotspot", "Share this PC's internet connection.", "", "ms-settings:network-mobilehotspot"),
            Page("focus", "Do not disturb", "Quiet the notifications while you play or work.", "",
                 OsInfo.IsWindows11 ? "ms-settings:quietmomentshome" : "ms-settings:quiethours"),
            Page("notifications", "Notifications", "Which apps may show notifications.", "", "ms-settings:notifications"),
            Page("power", "Power and battery", "Power mode, sleep and battery use.", "", "ms-settings:powersleep"),
            Page("mouse", "Mouse", "Pointer speed, buttons and scrolling.", "", "ms-settings:mousetouchpad"),
            Page("camera", "Camera", "Your cameras and who may use them.", "", "ms-settings:privacy-webcam"),
            Page("printers", "Printers and scanners", "Add or manage printers.", "", "ms-settings:printers"),
            Page("clipboard", "Clipboard", "Clipboard history and sync.", "", "ms-settings:clipboard"),
            Page("personalization", "Personalization", "Background, colors, themes and the taskbar.", "", "ms-settings:personalization"),
            Page("datetime", "Date and time", "Time zone and clock settings.", "", "ms-settings:dateandtime"),
            Page("accessibility", "Accessibility", "Text size, magnifier, narrator and more.", "", "ms-settings:easeofaccess"),
            Page("update", "Windows Update", "Check for and install updates.", "", "ms-settings:windowsupdate"),
            Page("security", "Windows Security", "Virus and threat protection, firewall.", "", "windowsdefender:"),

            // ---------- Tools ----------
            new(TaskManagerId, SystemControls, "Task Manager", "Windows Task Manager.", "", () => Start("taskmgr.exe")),
            Tool("resmon", "Resource Monitor", "Live CPU, memory, disk and network use per program.", "", () => Start("resmon.exe")),
            Tool("devmgr", "Device Manager", "Drivers and hardware.", "", () => Start("devmgmt.msc")),
            Tool("snip", "Screenshot", "Opens the Windows screen snip, so you can capture part of the screen.", "", () => Start("ms-screenclip:")),
            Tool("gamebar", "Game Bar", "Opens the Xbox Game Bar overlay (capture, performance, audio).", "", () => Start("ms-gamebar:")),
            Tool("calc", "Calculator", "Windows Calculator.", "", () => Start("calc.exe")),
            Tool("desktop", "Show desktop", "Minimizes every window, and brings them back on the second click.", "", ShowDesktop),
            new(PowerId, SystemControls, "Power", "Opens a small menu above the icon with Restart and Shut down. Each one asks before it does anything.", "", () => { }),
            new("biosrestart", SystemControls, "Restart to BIOS", "Restarts the PC straight into its BIOS / UEFI setup screen (after you confirm). Save your work first.", "", RestartToBios),
            new("lock", SystemControls, "Lock this PC", "Locks Windows straight away. You sign back in with your usual sign-in.", "", () => Start("rundll32.exe", "user32.dll,LockWorkStation")),
        };

        public static DockWidgetDef? Find(string id) => All.FirstOrDefault(d => d.Id == id);
    }
}
