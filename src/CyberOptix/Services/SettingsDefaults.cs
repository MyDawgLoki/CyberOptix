using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    /// <summary>
    /// CyberOptix's default settings: a first run starts from them, and each page's "Reset to default" button (and Home's "Reset all settings")
    /// puts the settings it owns back to them. They are stored inside the app (Assets/DefaultSettings.json) and hold nothing personal.
    /// </summary>
    public static class SettingsDefaults
    {
        private const string Resource = "Assets/DefaultSettings.json";
        private static string? _json;

        private static string Json()
        {
            if (_json != null) return _json;
            try
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource);
                if (s != null)
                {
                    using var r = new StreamReader(s);
                    return _json = r.ReadToEnd();
                }
            }
            catch { }
            return _json = "{}";
        }

        /// <summary>A new settings object holding the defaults (every call gives its own copy).</summary>
        public static AppSettings Fresh()
        {
            try { return JsonSerializer.Deserialize<AppSettings>(Json()) ?? new AppSettings(); }
            catch { return new AppSettings(); }
        }

        /// <summary>Puts the named settings back to their defaults and saves.</summary>
        public static void Reset(IEnumerable<string> names)
        {
            var current = AppSettings.Current;
            var fresh = Fresh();
            foreach (var name in names)
            {
                var p = typeof(AppSettings).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanWrite) continue;
                try { p.SetValue(current, p.GetValue(fresh)); } catch { }
            }
            AppSettings.Save();
        }

        /// <summary>
        /// Everything except what is the user's own content (the programs and folders they added as dock icons, their games, their Apps panels) and what
        /// records changes already made to Windows (so Restore original settings keeps working).
        /// </summary>
        public static void ResetAll()
        {
            var keep = new HashSet<string> { nameof(AppSettings.CustomDockApps), nameof(AppSettings.AppPanels), nameof(AppSettings.CustomGames), nameof(AppSettings.RemovedGames),
                nameof(AppSettings.AppliedTweaks), nameof(AppSettings.Touched), nameof(AppSettings.KeepWindowsSettingsOnRelaunch), nameof(AppSettings.DockPinned),
                nameof(AppSettings.AppsOrder), nameof(AppSettings.DockOrder), nameof(AppSettings.SlideshowFolder), nameof(AppSettings.DisplayColors) };
            Reset(typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite && !keep.Contains(p.Name)).Select(p => p.Name));
        }

        // The settings each page owns (what its "Reset to default" button puts back).

        public static readonly string[] Optimizer = { nameof(AppSettings.GamingProfile), nameof(AppSettings.GeneralProfile) };
        public static readonly string[] TaskManager = { nameof(AppSettings.CleanTargets), nameof(AppSettings.NeverKill) };
        public static readonly string[] Display =
        {
            "DesktopClock", "DesktopClockTheme", "DesktopClock24h", "DesktopClockSeconds", "DesktopClockShowDate", "DesktopClockShowAmPm", "DesktopClockWeather",
            "DesktopClockScale", "DesktopClockOpacity", "DesktopClockColorMode", "DesktopClockColor", "DesktopClockGlow", "DesktopClockAnchor", "DesktopClockCustom",
            "DesktopClockX", "DesktopClockY", "DesktopClockDisplay", "WallpaperFit", "LiveEnabled", "LiveKind", "LiveSource", "LiveMute", "LivePauseFullscreen", "LivePauseMaximized",
        };
        public static readonly string[] Widgets =
        {
            "ClockWidget", "WeatherWidget", "MediaWidget", "MediaWidgetAutoShow", "City", "Fahrenheit", "WidgetsOnTop", "WidgetsCloseWhileGaming", "HideWidgetsOnBrowserOrGame",
            "ShowWidgetsWithBrowser", "MediaIcons", "WidgetOpacity", "Positions", "WidgetPlacing", "WidgetDesktopMenu", "MonitorWidgets", "DesktopTiles",
        };
        public static readonly string[] Hardware = { nameof(AppSettings.Lighting), nameof(AppSettings.Hotkeys) };
        public static readonly string[] Dock =
        {
            "DockEnabled", "DockPosition", "DockIconSize", "DockShowRunning", "DockShowRecycleBin", "DockHideFullscreen", "DockShowSystem", "DockWidgetAuto", "DockWidgetSize",
            "DockClockAuto", "DockClockSize", "DockExtraIcons", "DockGlow", "DockGlowColor", "DockMatchMedia", "DockShowBadges", "DockShowFlashing", "DockAllDisplays", "DockAppsOn", "DockLayer",
            "DockSearchAtBottom", "DockStartAtBottom", "DockFloatOverWindows", "DockReplaceTaskbar", "IndividualDocks", "IconGroups", "DockPlaceOrder", "IconOrder",
        };
        public static readonly string[] WindowManager = { nameof(AppSettings.HotkeysEnabled), nameof(AppSettings.Gap) };
        public static readonly string[] AppSettingsPage =
        {
            "AccentHex", "TintHex", "GlassTint", "WindowOpacity", "Backdrop", "FontHex", "MatchMediaAccent", "MatchMediaWindow", "MatchMediaText", "MatchMediaColor", "MatchMediaGlow", "CardHex",
            "CardOpacity", "BackgroundImage", "BackgroundVideo", "BackgroundImagePath", "BackgroundImageOpacity", "WindowGlow", "WindowGlowColor", "StartWithWindows", "StartQuietly",
            "CloseToTray", "MinimizeTo", "SyncWindowsAccent", "WinMatchMedia", "WinSystemDark", "WinAppsDark", "WinTransparency", "WinAccentOnStart", "WinAccentOnTitle",
        };
    }
}
