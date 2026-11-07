using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CyberOptix.Services
{
    /// <summary>One divider in the process list.</summary>
    public sealed record ProcGroup(string Label, string Hint, int Order);

    /// <summary>
    /// Sorts running processes into the three dividers of the Processes list:
    /// <b>Killable</b> (nothing else depends on it), <b>Bloat</b> (Windows and Microsoft extras that are not needed for anything important)
    /// and <b>Unkillable</b> (Windows, your drivers or your security software depend on it, so it can't be ended here).
    /// </summary>
    public static class ProcessClassifier
    {
        public static readonly ProcGroup Killable = new("Killable", "Safe to end. Nothing else in Windows depends on these.", 0);
        public static readonly ProcGroup Bloat = new("Bloat", "Windows and Microsoft extras you can end without losing anything important.", 1);
        public static readonly ProcGroup Unkillable = new("Unkillable", "Windows, your drivers or your security need these, so they can't be ended here.", 2);

        // Windows itself: ending any of these crashes, logs you out, or breaks the desktop, sound or other programs.
        private static readonly Dictionary<string, string> Core = new(StringComparer.OrdinalIgnoreCase)
        {
            ["system"] = "Windows core", ["system idle process"] = "Windows core", ["registry"] = "Windows core", ["memory compression"] = "Windows core",
            ["secure system"] = "Windows core", ["smss"] = "Windows core", ["csrss"] = "Windows core", ["wininit"] = "Windows core",
            ["winlogon"] = "Windows core", ["lsass"] = "Windows core", ["lsaiso"] = "Windows core", ["services"] = "Windows core",
            ["svchost"] = "Hosts Windows services", ["dwm"] = "Draws the desktop", ["fontdrvhost"] = "Fonts", ["sihost"] = "Windows shell",
            ["explorer"] = "Desktop and taskbar", ["ctfmon"] = "Keyboard input", ["textinputhost"] = "Keyboard input",
            ["runtimebroker"] = "App permissions", ["applicationframehost"] = "App windows", ["startmenuexperiencehost"] = "Start menu",
            ["shellexperiencehost"] = "Windows shell", ["logonui"] = "Sign-in screen", ["dllhost"] = "COM surrogate", ["taskhostw"] = "Windows tasks",
            ["wmiprvse"] = "Windows management", ["unsecapp"] = "Windows management", ["audiodg"] = "Windows audio", ["wudfhost"] = "Device drivers",
            ["conhost"] = "Console windows", ["csrss"] = "Windows core", ["sgrmbroker"] = "Windows security", ["smartscreen"] = "Windows security",
            ["msmpeng"] = "Windows security", ["nissrv"] = "Windows security", ["securityhealthservice"] = "Windows security",
            ["cyberoptix"] = "This app",
        };

        // Security software, graphics and audio drivers: other things stop working or you lose protection if these go.
        private static readonly Regex Protected = new(
            "^(?:(EPSecurityService|EPIntegrationService|EPUpdateService|EPProtectedService|avast.*|avg.*|mcafee.*|mcshield|mfemms|mfevtps|mcupdate|norton.*|kaspersky.*|avp|bdagent.*|mbam.*|egui|ekrn|csfalcon.*|sophos.*|cyserver|sentinel.*)" +
            "|(NVDisplay\\.Container|nvcontainer|nvidia.*|igfx.*|atieclxx|atiesrxx|amdow|radeon.*|amdrsserv|rtkaud.*|rtkngui.*|ravbg.*|rtlcpl|waves.*|dolby.*)" +
            "|(easyanticheat.*|beservice|bedaisy|vgc|vgtray|faceit.*|pnkbstr.*))$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Windows and Microsoft processes that only exist for something non-essential: telemetry, sync, ads, extras.
        private static readonly Dictionary<string, string> BloatNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["onedrive"] = "Cloud sync", ["onedrivestandaloneupdater"] = "Cloud sync updater", ["onedrivesetup"] = "Cloud sync setup",
            ["teams"] = "Chat and meetings", ["ms-teams"] = "Chat and meetings", ["msteams"] = "Chat and meetings",
            ["phoneexperiencehost"] = "Phone Link", ["yourphone"] = "Phone Link", ["crossdeviceservice"] = "Phone Link", ["crossdeviceresume"] = "Phone Link",
            ["widgets"] = "Widgets panel", ["widgetservice"] = "Widgets panel", ["widgetboard"] = "Widgets panel",
            ["copilot"] = "Copilot", ["m365copilot"] = "Copilot",
            ["gamebar"] = "Game Bar overlay", ["gamebarftserver"] = "Game Bar overlay", ["gamebarpresencewriter"] = "Game Bar overlay",
            ["xboxpcapp"] = "Xbox app", ["xboxgamebarwidgets"] = "Game Bar overlay",
            ["compattelrunner"] = "Telemetry", ["devicecensus"] = "Telemetry", ["wsqmcons"] = "Telemetry", ["aggregatorhost"] = "Telemetry",
            ["werfault"] = "Error reporting", ["werfaultsecure"] = "Error reporting",
            ["searchindexer"] = "File indexing", ["searchprotocolhost"] = "File indexing", ["searchfilterhost"] = "File indexing",
            ["microsoftedgeupdate"] = "Edge updater", ["msedgeupdate"] = "Edge updater",
            ["officeclicktorun"] = "Office updater", ["officec2rclient"] = "Office updater",
            ["mousocoreworker"] = "Update checker", ["usoclient"] = "Update checker",
            ["skypeapp"] = "Skype", ["skypebackgroundhost"] = "Skype", ["cortana"] = "Cortana", ["cortanaui"] = "Cortana",
            ["hxoutlook"] = "Mail app", ["hxtsr"] = "Mail app", ["backgroundtaskhost"] = "Background tasks", ["userOOBEBroker"] = "Setup helper",
            ["securityhealthsystray"] = "Security tray icon", ["feedbackhub"] = "Feedback app", ["gamingservicesnet"] = "Xbox services",
        };

        /// <summary>Bumped whenever the Never kill list changes, so rows re-check which divider they belong under.</summary>
        public static int Version { get; private set; }

        public static void Changed() => Version++;

        /// <summary>True when the user put this program on the Never kill list.</summary>
        public static bool IsNeverKill(string name)
        {
            foreach (var n in Models.AppSettings.Current.NeverKill)
                if (n.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static (ProcGroup Group, string Reason) Classify(string name)
        {
            if (Core.TryGetValue(name, out var core)) return (Unkillable, core);
            if (Protected.IsMatch(name)) return (Unkillable, ProtectedReason(name));
            if (BloatNames.TryGetValue(name, out var bloat)) return (Bloat, bloat);
            return (Killable, IsNeverKill(name) ? "On your Never kill list" : "");
        }

        private static string ProtectedReason(string name)
        {
            var n = name.ToLowerInvariant();
            if (n.StartsWith("nv") || n.StartsWith("igfx") || n.StartsWith("ati") || n.StartsWith("amd") || n.StartsWith("radeon")) return "Graphics driver";
            if (n.StartsWith("rtk") || n.StartsWith("rav") || n.StartsWith("waves") || n.StartsWith("dolby") || n == "rtlcpl") return "Audio driver";
            if (n.Contains("anticheat") || n is "beservice" or "bedaisy" or "vgc" or "vgtray" || n.StartsWith("faceit") || n.StartsWith("pnkbstr")) return "Anti-cheat";
            return "Security software";
        }
    }
}
