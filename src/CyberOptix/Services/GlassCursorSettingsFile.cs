using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace CyberOptix.Services
{
    /// <summary>Glass Cursor's settings as CyberOptix sees them. The property names match Glass Cursor's own settings.json exactly.</summary>
    public sealed class GlassCursorSettings
    {
        public int Diameter { get; set; } = 44;
        public double Zoom { get; set; } = 2.0;
        public bool HidePointer { get; set; } = true;
        public bool StartOn { get; set; } = true;
        public bool LensCentered { get; set; }
        public bool CentreOnWindow { get; set; }
        public Dictionary<string, int> Keybinds { get; set; } = new();
        public string RingColor { get; set; } = "#000000";
        public string DotColor { get; set; } = "#111111";
        public int DotSize { get; set; } = 3;
        public bool ScopeEnabled { get; set; } = true;
        public bool ScopeFollowCursor { get; set; }
        public int ScopeDelayMs { get; set; } = 300;
        public int ScopeDiameter { get; set; } = 300;
        public double ScopeZoom { get; set; } = 2.0;
        public bool ScopeCrisp { get; set; } = true;
        public int ScopeContrast { get; set; }
        public int CursorZoomDiameter { get; set; } = 300;
        public double CursorZoomZoom { get; set; } = 2.0;

        /// <summary>The key combination for an action: the saved one, or its default when none is saved.</summary>
        public int KeybindFor(string action) =>
            Keybinds.TryGetValue(action, out var keys) ? keys : GlassCursorKeys.DefaultFor(action);
    }

    /// <summary>
    /// The one settings file that both CyberOptix's Glass Cursor page and Glass Cursor's own tray menu edit. Each side watches it, so a change
    /// made in either place shows up in the other. A change is written by merging into what is on disk (so nothing Glass Cursor saved in the
    /// meantime is lost), and through a temporary file that is moved into place (so the other side never reads half a file).
    /// </summary>
    public sealed class GlassCursorSettingsFile : IDisposable
    {
        private static GlassCursorSettingsFile? _shared;

        /// <summary>The real file, in CyberOptix's protected folder.</summary>
        public static GlassCursorSettingsFile Shared => _shared ??= new GlassCursorSettingsFile(GlassCursorService.SettingsFile);

        private readonly string _path;
        private readonly object _gate = new();
        private JsonObject? _lastSeen;
        private FileSystemWatcher? _watcher;
        private Timer? _debounce;
        private int _retries;

        public GlassCursorSettingsFile(string path) => _path = path;

        public string Path => _path;

        /// <summary>Raised (on a background thread) when the file changes for a reason other than our own save.</summary>
        public event Action? ChangedExternally;

        public GlassCursorSettings Load()
        {
            var obj = TryRead();
            if (obj == null) return new GlassCursorSettings();
            try { return obj.Deserialize<GlassCursorSettings>() ?? new GlassCursorSettings(); }
            catch { return new GlassCursorSettings(); }
        }

        /// <summary>Changes the file: reads the current content, applies <paramref name="edit"/>, and saves atomically.</summary>
        public void Update(Action<JsonObject> edit)
        {
            lock (_gate)
            {
                var obj = TryRead() ?? SeedDefaults();
                edit(obj);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, _path, overwrite: true);
                _lastSeen = (JsonObject)obj.DeepClone(); // our own save: the watcher must not report it back to us
            }
        }

        /// <summary>Sets one action's key combination, taking the same combination off any other action first (one combination, one job).</summary>
        public void SetKeybind(string action, int keys)
        {
            Update(obj =>
            {
                var binds = obj["Keybinds"] as JsonObject ?? new JsonObject();
                obj["Keybinds"] = binds;
                int Current(string a) => binds[a] is JsonValue v && v.TryGetValue<int>(out var n) ? n : GlassCursorKeys.DefaultFor(a);
                if (keys != 0)
                    foreach (var other in GlassCursorKeys.Actions.Select(a => a.Action).Where(a => a != action))
                        if (Current(other) == keys) binds[other] = 0;
                binds[action] = keys;
            });
        }

        /// <summary>Starts watching the file. Safe to call more than once.</summary>
        public void StartWatching()
        {
            lock (_gate)
            {
                if (_watcher != null) return;
                try
                {
                    var dir = System.IO.Path.GetDirectoryName(_path)!;
                    Directory.CreateDirectory(dir);
                    _lastSeen = TryRead();
                    _debounce = new Timer(_ => CheckForChange(), null, Timeout.Infinite, Timeout.Infinite);
                    _watcher = new FileSystemWatcher(dir, System.IO.Path.GetFileName(_path))
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                        EnableRaisingEvents = true,
                    };
                    FileSystemEventHandler poke = (_, _) => { _retries = 0; _debounce?.Change(150, Timeout.Infinite); };
                    _watcher.Changed += poke;
                    _watcher.Created += poke;
                    _watcher.Renamed += (_, _) => { _retries = 0; _debounce?.Change(150, Timeout.Infinite); };
                }
                catch
                {
                    // Without the watcher the page still works; it just will not notice tray changes until it is reopened.
                }
            }
        }

        private void CheckForChange()
        {
            JsonObject? now;
            lock (_gate)
            {
                now = TryRead();
                if (now == null)
                {
                    // Caught mid-write, or the file is gone: look again shortly.
                    if (_retries++ < 5) _debounce?.Change(150, Timeout.Infinite);
                    return;
                }
                if (_lastSeen != null && JsonNode.DeepEquals(now, _lastSeen)) return;
                _lastSeen = (JsonObject)now.DeepClone();
            }
            try { ChangedExternally?.Invoke(); } catch { }
        }

        private JsonObject? TryRead()
        {
            try
            {
                if (!File.Exists(_path)) return null;
                return JsonNode.Parse(File.ReadAllText(_path)) as JsonObject;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The settings Glass Cursor ships with, used the first time the file is written before Glass Cursor has ever run.</summary>
        private static JsonObject SeedDefaults()
        {
            try
            {
                var asm = typeof(GlassCursorSettingsFile).Assembly;
                using var stream = asm.GetManifestResourceStream("GlassCursor/settings.json");
                if (stream != null && JsonNode.Parse(stream) is JsonObject shipped) return shipped;
            }
            catch { }
            return (JsonObject)JsonSerializer.SerializeToNode(new GlassCursorSettings())!;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _watcher?.Dispose();
                _watcher = null;
                _debounce?.Dispose();
                _debounce = null;
            }
        }
    }

    /// <summary>Glass Cursor's six keybind actions, and how its key numbers (WinForms Keys values) read as text.</summary>
    public static class GlassCursorKeys
    {
        public const int Control = 0x20000, Shift = 0x10000, Alt = 0x40000, KeyCode = 0xFFFF;

        public static readonly (string Action, string Title, string Description, int Default)[] Actions =
        {
            ("CursorFreeRoam", "Cursor: Free Roam on / off", "The lens follows your mouse.", Control | Alt | 0x46),
            ("CursorStatic", "Cursor: Static Center on / off", "A crosshair in the middle of the screen.", Control | Alt | 0x43),
            ("CursorToggle", "Cursor on / off", "Keeps whichever mode you picked.", Control | Alt | 0x58),
            ("ZoomFollow", "Right Click To Zoom: Follow Mouse on / off", "The zoom window follows the mouse.", Control | Alt | 0x56),
            ("ZoomStatic", "Right Click To Zoom: Static Center on / off", "The zoom window sits in the centre.", Control | Alt | 0x53),
            ("ZoomToggle", "Right Click To Zoom on / off", "Keeps whichever mode you picked.", Control | Alt | 0x5A),
        };

        public static int DefaultFor(string action) => Actions.FirstOrDefault(a => a.Action == action).Default;

        public static string Describe(int keys)
        {
            if (keys == 0) return "(none)";
            var parts = new List<string>();
            if ((keys & Control) != 0) parts.Add("Ctrl");
            if ((keys & Alt) != 0) parts.Add("Alt");
            if ((keys & Shift) != 0) parts.Add("Shift");
            parts.Add(KeyName(keys & KeyCode));
            return string.Join(" + ", parts);
        }

        private static string KeyName(int vk) => vk switch
        {
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x60 and <= 0x69 => "Num " + (vk - 0x60),
            >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
            0x6B => "Num +",
            0x6D => "Num -",
            0x6A => "Num *",
            0x6F => "Num /",
            0x6E => "Num .",
            0xBB => "=",
            0xBD => "-",
            0xDB => "[",
            0xDD => "]",
            0xBA => ";",
            0xDE => "'",
            0xBC => ",",
            0xBE => ".",
            0xBF => "/",
            0xDC => "\\",
            0xC0 => "`",
            0x22 => "Page Down",
            0x21 => "Page Up",
            0x0D => "Enter",
            0x08 => "Backspace",
            0x09 => "Tab",
            0x20 => "Space",
            0x1B => "Esc",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "Print Screen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x13 => "Pause",
            0x90 => "Num Lock",
            0x91 => "Scroll Lock",
            _ => "Key 0x" + vk.ToString("X2"),
        };
    }

    /// <summary>
    /// While one of CyberOptix's keybind boxes is listening, Glass Cursor's own hotkeys would swallow the keys before this window ever saw them.
    /// Holding this named object tells a running Glass Cursor to step its hotkeys aside; it disappears on its own if CyberOptix closes or crashes.
    /// </summary>
    public static class GlassCursorKeyCapture
    {
        public const string Name = @"Local\CyberOptix.GlassCursor.KeyCapture";
        private static Mutex? _held;

        public static void Begin()
        {
            try { _held ??= new Mutex(false, Name); } catch { }
        }

        public static void End()
        {
            _held?.Dispose();
            _held = null;
        }
    }
}
