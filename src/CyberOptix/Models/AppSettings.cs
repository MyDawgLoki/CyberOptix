using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CyberOptix.Models
{
    /// <summary>Persisted app state (widgets, hotkeys, slideshow). Stored in %ProgramData%\CyberOptix\settings.json, which only administrators can change.</summary>
    /// <summary>One program or shortcut the user added as a dock icon: its name, and the path it opens.</summary>
    public class CustomDockApp
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Group { get; set; } = "System controls";   // the group (and so the dock) the icon belongs to
    }

    public class AppSettings
    {
        // One saved profile per Optimizer tab. Changing one never touches the other.
        public SettingsProfile GamingProfile { get; set; } = SettingsProfile.DefaultGaming();
        public SettingsProfile GeneralProfile { get; set; } = SettingsProfile.DefaultGeneral();

        /// <summary>Ids of catalog tweaks this app has applied, so unchecking one can undo it.</summary>
        public List<string> AppliedTweaks { get; set; } = new();

        /// <summary>Which kinds of change CyberOptix has made ("profile", "mouse", "remap"), so Restore only touches what it changed.</summary>
        public List<string> Touched { get; set; } = new();

        public void Touch(string tag)
        {
            if (Touched.Contains(tag)) return;
            Touched.Add(tag);
            Save();
        }

        /// <summary>Ids of the Task Manager "Free up resources" targets that are switched on.</summary>
        public List<string> CleanTargets { get; set; } = new();
        public List<string> NeverKill { get; set; } = new(); // programs (process or service names) that Free up resources must never list or stop

        // Hardware tab
        public Dictionary<string, LightSetting> Lighting { get; set; } = new();   // "mouse" | "keyboard"
        public List<HotkeyBinding> Hotkeys { get; set; } = new();
        public List<KeyRemap> Remaps { get; set; } = new();

        /// <summary>Which Monitor pop-out widgets are on: "temps", "cpu", "gpu".</summary>
        public List<string> MonitorWidgets { get; set; } = new();

        // Dock
        public bool DockEnabled { get; set; } = true;
        public string DockPosition { get; set; } = "Top";      // Bottom | Top
        public int DockIconSize { get; set; } = 18;
        /// <summary>The system icons' size when they follow the app icon size: 18 px app icons give 15 px system icons (and a 13 px clock).</summary>
        public static int AutoWidgetPx(int appIconSize) => (int)Math.Round(appIconSize * 15 / 18.0);

        /// <summary>Do not change my Windows settings when CyberOptix starts: nothing saved is re-applied to Windows at launch (display colors and the like).</summary>
        public bool KeepWindowsSettingsOnRelaunch { get; set; }

        public bool DockIconSizeV2 { get; set; }                   // set once the old default size (26) has been moved to the smaller default
        public bool DockShowRunning { get; set; } = true;
        public bool DockShowRecycleBin { get; set; } = true;       // the Recycle Bin on the dock: click to open, drop files on it to delete them
        public bool DockHideFullscreen { get; set; } = true;
        public bool DockShowSystem { get; set; } = true;           // volume, mic, network, Bluetooth, clock
        // The sizes below are the defaults for a new install: 26 px app icons, 15 px system icons and a 13 px clock, each picked by hand
        // rather than following the app icon size. (They can be switched to follow it on the Dock page.)
        public bool DockWidgetAuto { get; set; } = true;           // true = the system icons follow the app icon size
        public int DockWidgetSize { get; set; } = 15;              // their size in pixels when they do not
        public bool DockClockAuto { get; set; } = true;            // true = the clock and date follow the system icon size
        public double DockClockSize { get; set; } = 13.0;          // the clock's text size in pixels when they do not (the date is a little smaller)
        public List<string> DockExtraIcons { get; set; } = new() { "media", "weather", "widgets" };
        public bool DockWidgetsIconOffered { get; set; }           // the Widgets icon has been added to the dock once (so removing it stays removed)
        /// <summary>The tools, settings pages and apps that have a tile on the desktop (ids from DockWidgetCatalog), added in the Widgets window.</summary>
        public List<string> DesktopTiles { get; set; } = new();  // extra system icons the user added (see DockWidgetCatalog), in catalog order
        public bool WindowGlow { get; set; } = true;               // a glowing edge on the CyberOptix window and the widget windows
        public string WindowGlowColor { get; set; } = "#14B8D4";   // ...in this color (the album cover's color while Match the Media Player Color is on)
        public bool DockGlow { get; set; }                         // a soft glow around the dock's outside border
        public string DockGlowColor { get; set; } = "#14B8D4";     // ...in this color
        public bool DockShowBadges { get; set; } = true;          // a number on an app's icon when its window title starts with one, like (3)
        public bool DockShowFlashing { get; set; } = true;         // an app's icon lights up when its window flashes for attention
        public bool DockAllDisplays { get; set; } = true;                  // a dock on every display, not just the main one
        public string DockAppsOn { get; set; } = "MainAndWindow";  // with more than one display: All | MainAndWindow | Window
        public double AppsWidth { get; set; } = 690;                      // the Apps window's size; 0 = its default
        public double AppsHeight { get; set; } = 560;
        public string DockLayer { get; set; } = "Fall";             // Top (always in front) | Fall (behind windows, hover brings it forward) | Stick (behind windows, the Windows key brings it forward)
        public bool DockSearchAtBottom { get; set; } = true;       // the same for the Windows Search menu
        public bool DockStartAtBottom { get; set; } = true;        // move the Start menu down to the very bottom of the screen (when the dock replaces the taskbar or lies behind windows)
        public bool DockFloatOverWindows { get; set; }                     // let windows use the whole screen: no strip is kept free for the dock, it floats on top of them
        public bool DockReplaceTaskbar { get; set; } = true;               // hide the Windows taskbar and reserve the dock's space
        public List<AppPanel> AppPanels { get; set; } = new();
        public List<string> DockOrder { get; set; } = new();       // the order of the icons on the dock, set by dragging them
        public List<string> AppsOrder { get; set; } = new();       // the order of the tiles in the Apps panel, set by dragging them
        public bool AppsPinned { get; set; }                       // the Apps panel is pinned to the desktop
        public List<Game> CustomGames { get; set; } = new(); // the games list: filled by Scan Steam and Add Game
        public List<string> RemovedGames { get; set; } = new(); // games the user removed; a later scan will not bring them back
        public List<string> DockPinned { get; set; } = new() { @"C:\Windows\explorer.exe" };

        // ---- the Desktop Clock (Display page): the time with the date under it, drawn straight on the wallpaper ----
        public bool DesktopClock { get; set; }
        public string DesktopClockTheme { get; set; } = "Classic";
        public bool DesktopClock24h { get; set; }
        public bool DesktopClockSeconds { get; set; }
        public bool DesktopClockShowDate { get; set; } = true;
        public bool DesktopClockShowAmPm { get; set; } = true;
        public bool DesktopClockWeather { get; set; } = true;        // a weather icon on the clock's left; click it for the forecast widget
        public double DesktopClockScale { get; set; } = 1.0;          // 0.5 - 2.5 times the theme's size
        public double DesktopClockOpacity { get; set; } = 0.95;
        public string DesktopClockColorMode { get; set; } = "Text";   // Text (the app's text color), Accent, Media (album cover), Custom
        public string DesktopClockColor { get; set; } = "#FFFFFF";    // used when the color mode is Custom
        public bool DesktopClockGlow { get; set; } = true;            // a soft shadow/glow so it reads on any wallpaper
        public string DesktopClockAnchor { get; set; } = "TopCenter"; // TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight
        public bool DesktopClockCustom { get; set; }                  // true once the clock was dragged: it then sits at the spot below instead of an anchor
        public double DesktopClockX { get; set; } = 0.5;              // where the clock's middle is on its display, 0 (left) to 1 (right)
        public double DesktopClockY { get; set; } = 0.12;             // ...and 0 (top) to 1 (bottom)
        public string DesktopClockDisplay { get; set; } = "";         // a display id from DisplayService; empty = the main display
        /// <summary>
        /// Icon groups (like "AI platforms" and "Social, streaming and chat") that sit in a dock of their own instead of in the main dock:
        /// the group's name, and the edge of the screen it sticks to (Top, Bottom, Left or Right). A group that is not listed stays in the main dock.
        /// </summary>
        public Dictionary<string, string> IndividualDocks { get; set; } = new();

        /// <summary>Icons the user dragged into a different group (and so a different dock) on the Dock page: the icon's id, then the group it now sits in.</summary>
        public Dictionary<string, string> IconGroups { get; set; } = new();

        /// <summary>The order the docks were given their places in (oldest first), so docks sharing a place line up in that order.</summary>
        public List<string> DockPlaceOrder { get; set; } = new();

        /// <summary>The order of the icons in each group (and so in each dock), set with the arrows on the Dock page: the group's name, then the icon ids in order.</summary>
        public Dictionary<string, List<string>> IconOrder { get; set; } = new();

        /// <summary>Apps and shortcuts the user added as dock icons (like a "Restart To BIOS" program), shown in the System controls group.</summary>
        public List<CustomDockApp> CustomDockApps { get; set; } = new();


        public bool ClockWidget { get; set; } = true;
        public bool WeatherWidget { get; set; } = true;
        public bool MediaWidget { get; set; }
        /// <summary>The media player widget appears on the desktop by itself when something starts playing. Closing the widget with its x switches this off.</summary>
        public bool MediaWidgetAutoShow { get; set; } = true;
        public string City { get; set; } = "New York";
        public bool Fahrenheit { get; set; } = true;
        public bool WidgetsOnTop { get; set; }
        /// <summary>Closing the window keeps CyberOptix (Glass Cursor, hotkeys, dock, widgets) running in the system tray.</summary>
        public bool StartWithWindows { get; set; } = true;          // start CyberOptix when the user signs in to Windows (a scheduled task; see AutoStartService)
        public bool StartQuietly { get; set; } = true;              // ...straight into the system tray, without opening the window
        public bool CloseToTray { get; set; } = true;
        /// <summary>Where the window goes when it is minimized: Taskbar (normal), Dock or Tray.</summary>
        public string MinimizeTo { get; set; } = "Taskbar";
        /// <summary>Close the desktop widgets while a game or other fullscreen app is in front, and bring them back afterwards.</summary>
        public bool WidgetsCloseWhileGaming { get; set; } = true;
        /// <summary>Hide the desktop widget windows while a web browser or a game window is the one in focus. Off unless the user switches it on.</summary>
        public bool HideWidgetsOnBrowserOrGame { get; set; }
        /// <summary>Keep the widget windows on screen while a web browser is the window in focus (even a fullscreen browser video). On unless the user switches it off.</summary>
        public bool ShowWidgetsWithBrowser { get; set; } = true;
        /// <summary>Which service buttons the media player shows (ids from MediaLaunchers): all five by default.</summary>
        public List<string> MediaIcons { get; set; } = new() { "youtube", "spotify", "soundcloud", "itunes", "suno" };
        public double WidgetOpacity { get; set; } = 0.62;
        public Dictionary<string, double[]> Positions { get; set; } = new();
        /// <summary>Where every widget sat when the layout was last locked (or placed from the Widgets page). "Restore Widgets Placing" goes back to it.</summary>
        public Dictionary<string, double[]> WidgetPlacing { get; set; } = new();
        /// <summary>Put "Unlock Widgets" and "Restore Widgets Placing" in the menu you get by right-clicking the desktop wallpaper.</summary>
        public bool WidgetDesktopMenu { get; set; } = true;

        public bool HotkeysEnabled { get; set; }
        public int Gap { get; set; } = 0;

        public string? SlideshowFolder { get; set; }
        public string WallpaperFit { get; set; } = "Fill"; // Fill | Fit | Stretch | Tile | Center | Span

        // App appearance (Settings page)
        public string AccentHex { get; set; } = "#14B8D4";
        public string TintHex { get; set; } = "#17171C";     // Graphite
        public double GlassTint { get; set; } = 0.65;      // 0.25 (see-through) to 0.95 (nearly solid)
        public double WindowOpacity { get; set; } = 1.0;   // 0.5 to 1.0, whole window
        public string Backdrop { get; set; } = "Mica Alt";  // Acrylic | Mica | Mica Alt | Solid
        public string FontHex { get; set; } = "#FFFFFF";          // text color
        // "Match the Media Player Color", one for each color. While something plays, that color becomes the color of the album cover.
        // Nothing changes at launch: the colors above stay until something starts playing.
        public bool MatchMediaAccent { get; set; } = true;
        public bool MatchMediaWindow { get; set; } = true;
        public bool MatchMediaText { get; set; } = true;
        public bool MatchMediaColor { get; set; } = true;         // the glass cards
        public bool MatchMediaGlow { get; set; } = true;          // the glowing edge of the window and the widget windows (on its own, apart from the cards)
        public bool DockMatchMedia { get; set; } = true;          // the dock's border glow (on its own too)
        public string CardHex { get; set; } = "#FFFFFF";         // glass cards: color
        public double CardOpacity { get; set; } = 0.25;          // glass cards: 0 (clear) to 0.6
        public bool BackgroundImage { get; set; } = true;  // the picture behind the app
        public bool BackgroundVideo { get; set; } = true;  // ...and when it is the default wallpaper, a silent looping video of it instead of the still picture
        public string? BackgroundImagePath { get; set; }   // null = the CyberOptix wallpaper
        public double BackgroundImageOpacity { get; set; } = 0.32; // 0.1 to 1

        /// <summary>Per-display color adjustments, keyed by the display's Windows monitor id.</summary>
        public Dictionary<string, DisplayColor> DisplayColors { get; set; } = new();

        // Live wallpaper
        public bool LiveEnabled { get; set; }
        public string LiveKind { get; set; } = "Video"; // Video | Gif | Web
        public string? LiveSource { get; set; }
        public bool LiveMute { get; set; } = true;
        public bool LivePauseFullscreen { get; set; } = true;
        public bool LivePauseMaximized { get; set; } = true;

        // %ProgramData%\CyberOptix\settings.json (admin-only; an older copy in %AppData% is moved across on first run)
        private static readonly string PathOnDisk = CyberOptix.Services.AppPaths.Secure("settings.json");

        public static AppSettings Current { get; } = Load();

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(PathOnDisk))
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PathOnDisk)) ?? new AppSettings();
                    if (!loaded.DockIconSizeV2) { if (loaded.DockIconSize == 26) loaded.DockIconSize = 18; loaded.DockIconSizeV2 = true; }   // one-time: the old default was larger than the other docks' icons
                    return loaded;
                }
            }
            catch { }
            return CyberOptix.Services.SettingsDefaults.Fresh();   // no saved settings yet (a first run): start from the app's defaults
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathOnDisk)!);
                File.WriteAllText(PathOnDisk, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }

    public class LightSetting
    {
        public string Effect { get; set; } = "Off";
        public string Color { get; set; } = "#B146C2";
        public int Speed { get; set; } = 5;        // 1-10
        public int Brightness { get; set; } = 100; // 0-100
    }

    public class HotkeyBinding
    {
        public string Combo { get; set; } = "";
        public uint Mods { get; set; }
        public uint Vk { get; set; }
        public string Action { get; set; } = "Program"; // Program | Website
        public string Target { get; set; } = "";
    }

    public class KeyRemap
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
    }

    /// <summary>A desktop panel of app tiles. Click a tile to open that app or jump to its window.</summary>
    public class AppPanel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Title { get; set; } = "Apps";
        public List<string> Apps { get; set; } = new();
    }
}
