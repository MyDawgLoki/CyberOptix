using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GlassCursor;

/// <summary>
/// Glass Cursor as part of CyberOptix. CyberOptix starts it once; it runs on its own UI thread inside the same process, owns the tray icon,
/// and shares CyberOptix's protected settings file. Nothing here is a separate program any more.
/// </summary>
public static class GlassCursorHost
{
    private static Thread? _thread;
    private static TrayApp? _tray;
    private static SynchronizationContext? _context;
    private static readonly ManualResetEventSlim Ready = new(false);

    /// <summary>True while the tray and hotkeys are running.</summary>
    public static bool IsRunning => _thread is { IsAlive: true } && _tray != null;

    /// <summary>True while the lens is on the screen.</summary>
    public static bool LensOn => _tray?.IsOn == true;

    /// <summary>Starts Glass Cursor. <paramref name="settingsPath"/> is the shared settings file; the two actions are the tray's Open and Exit items.</summary>
    public static void Start(string settingsPath, Action openApp, Action exitApp, byte[]? trayIcon = null)
    {
        if (IsRunning) return;
        Settings.SettingsPath = settingsPath;
        TrayApp.TrayIconBytes = trayIcon;
        Ready.Reset();
        _thread = new Thread(() => Run(openApp, exitApp)) { IsBackground = true, Name = "GlassCursor" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        Ready.Wait(8000);
    }

    private static void Run(Action openApp, Action exitApp)
    {
        try
        {
            Application.EnableVisualStyles();
            try { Application.SetCompatibleTextRenderingDefault(false); } catch { }
            _context = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(_context);

            // A crash or a forced kill can leave the pointer hidden; always start from the real one.
            PointerHider.Restore();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => PointerHider.Restore();
            // If CyberOptix crashes, drop the mouse hook straight away. Otherwise the system-wide hook stays in place while Windows
            // writes the crash report and the mouse lags for everyone.
            AppDomain.CurrentDomain.UnhandledException += (_, _) => { _tray?.EmergencyUnhook(); PointerHider.Restore(); };

            _tray = new TrayApp(openApp, exitApp);
            Ready.Set();
            Application.Run(_tray);
        }
        catch (Exception ex)
        {
            PointerHider.Restore();
            Ready.Set();
            _ = ex;
        }
        finally
        {
            PointerHider.Restore();
            _tray = null;
        }
    }

    /// <summary>Turns the lens on or off.</summary>
    public static void SetLens(bool on)
    {
        var tray = _tray;
        _context?.Post(_ => tray?.SetOn(on), null);
    }

    /// <summary>Puts the normal Windows pointer back.</summary>
    public static void RestorePointer() => PointerHider.Restore();

    /// <summary>Stops Glass Cursor: removes the tray icon, the hotkeys and the lens, and gives the real pointer back.</summary>
    public static void Stop()
    {
        var tray = _tray;
        var thread = _thread;
        if (tray == null || thread == null) { PointerHider.Restore(); return; }
        try { _context?.Send(_ => tray.Shutdown(), null); } catch { }
        thread.Join(3000);
        PointerHider.Restore();
    }
}

internal sealed class Settings
{
    public int Diameter { get; set; } = 44;
    public double Zoom { get; set; } = 2.0;
    public bool HidePointer { get; set; } = true;
    public bool StartOn { get; set; } = true;

    // Static Centered: the lens sits in the middle of the active window as a crosshair
    // instead of following the mouse (Free Roam).
    public bool LensCentered { get; set; }

    // Static Centered and Static Right Click to Zoom use the centre of the screen, or of the
    // active window when this is set (for games played in a window).
    public bool CentreOnWindow { get; set; }

    // Custom keybinds: action name (KeyAction) -> WinForms Keys value with Ctrl/Alt/Shift; 0 means none.
    // Actions missing here use their default.
    public Dictionary<string, int> Keybinds { get; set; } = [];

    public Keys GetKeybind(string action) =>
        Keybinds.TryGetValue(action, out var keys) ? (Keys)keys : KeyAction.All.First(a => a.Action == action).Default;

    // Colours, as #RRGGBB.
    public string RingColor { get; set; } = "#000000";
    public string DotColor { get; set; } = "#111111";
    // Centre dot size in pixels (odd, so it has a middle pixel); 3 is the small plus shape.
    public int DotSize { get; set; } = 3;

    // Hold-right-click zoom ("scope"): STATIC at the centre of the active window,
    // or following the mouse cursor when ScopeFollowCursor is set.
    public bool ScopeEnabled { get; set; } = true;
    public bool ScopeFollowCursor { get; set; }
    public int ScopeDelayMs { get; set; } = 300;
    public int ScopeDiameter { get; set; } = 300;
    public double ScopeZoom { get; set; } = 2.0;
    // Static zoom clarity: Windows smooths magnified pixels by default, which looks soft;
    // crisp keeps each pixel a sharp block. Contrast boost is a percentage (0 = off).
    public bool ScopeCrisp { get; set; } = true;
    public int ScopeContrast { get; set; }
    public int CursorZoomDiameter { get; set; } = 300;
    public double CursorZoomZoom { get; set; } = 2.0;

    [System.Text.Json.Serialization.JsonIgnore]
    public string LensModeName => LensCentered ? "Static Centered (Mouse)" : "Free Roam (Mouse)";

    /// <summary>The text that means "no colour": the ring or the dot is not drawn.</summary>
    public const string NoColour = "none";

    public static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            if (string.Equals(hex, NoColour, StringComparison.OrdinalIgnoreCase)) return Color.Transparent;
            return hex is { Length: 7 } && hex[0] == '#' ? Color.FromArgb(255, ColorTranslator.FromHtml(hex)) : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static string ToHex(Color c) => c.A == 0 ? NoColour : $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Where the shared settings file lives. Set by <see cref="GlassCursorHost.Start"/>.</summary>
    internal static string? SettingsPath;

    internal static string FilePath => SettingsPath ?? Path.Combine(AppContext.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Reads the settings file. False when it is missing or unreadable (for example while another program is mid-write).</summary>
    public static bool TryLoad(out Settings settings)
    {
        settings = new Settings();
        try
        {
            if (!File.Exists(FilePath))
            {
                return false;
            }
            var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
            if (loaded == null)
            {
                return false;
            }
            settings = loaded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static Settings Load() => TryLoad(out var settings) ? settings : new Settings();

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Keeps every number in a range the program can safely draw, whoever wrote the file.</summary>
    public void Sanitize()
    {
        Diameter = Math.Clamp(Diameter, 20, 200);
        Zoom = Math.Clamp(Zoom, 1.5, 8.0);
        ScopeZoom = Math.Clamp(ScopeZoom, 1.5, 8.0);
        CursorZoomZoom = Math.Clamp(CursorZoomZoom, 1.5, 8.0);
        ScopeDiameter = Math.Clamp(ScopeDiameter, 100, 2000);
        CursorZoomDiameter = Math.Clamp(CursorZoomDiameter, 100, 2000);
        ScopeDelayMs = Math.Clamp(ScopeDelayMs, 0, 5000);
        ScopeContrast = Math.Clamp(ScopeContrast, 0, 100);
        DotSize = Math.Clamp(DotSize, 1, 41);
        Keybinds ??= [];
    }

    /// <summary>Takes every saved value from another copy, so a window that holds this object sees the change.</summary>
    public void CopyFrom(Settings other)
    {
        foreach (var property in typeof(Settings).GetProperties())
        {
            if (!property.CanWrite || property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() != null)
            {
                continue;
            }
            var value = property.GetValue(other);
            property.SetValue(this, value is Dictionary<string, int> map ? new Dictionary<string, int>(map) : value);
        }
    }

    public void Save()
    {
        try
        {
            // Written to a temporary file and moved into place, so the app (which watches this file) never reads half of it.
            var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, ToJson());
            File.Move(temp, FilePath, overwrite: true);
        }
        catch
        {
            // Settings are a convenience; never crash over them.
        }
    }
}

internal sealed class TrayApp : ApplicationContext, ISettingsHost
{
    private static readonly int[] Sizes = [28, 36, 44, 52, 60, 72, 84];
    private static readonly double[] Zooms = [1.5, 2.0, 2.5, 3.0, 4.0, 5.0, 6.0];

    private const int HkZoomIn = 2, HkZoomInPad = 3, HkZoomOut = 4, HkZoomOutPad = 5,
        HkBigger = 6, HkSmaller = 7, HkOff = 8;
    // Custom keybinds use ids from here up, one per KeyAction.All entry.
    private const int HkCustomBase = 20;

    public const string LensOffEventName = @"Local\GlassCursor.LensOff";

    private readonly EventWaitHandle _lensOff = new(false, EventResetMode.AutoReset, LensOffEventName);
    private readonly System.Windows.Forms.Timer _lensOffPoll = new() { Interval = 200 };
    private readonly Settings _settings = Settings.Load();
    private readonly Lens _lens = new();
    private readonly Lens _scope = new(showDot: false, showEdge: false);
    private readonly Lens _cursorZoom = new(showDot: true, showEdge: false);
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10 };
    private readonly HotkeyWindow _hotkeys = new();
    private readonly ToolStripMenuItem _toggleItem, _hideItem, _sizeMenu, _zoomMenu, _scopeItem, _freeRoamItem, _centeredItem;
    private ScopeSettingsForm? _scopeForm;
    private long _rightDownSince = -1;
    private volatile bool _scoping;
    private nint _mouseHook;
    private Native.LowLevelMouseProc? _mouseProc;
    /// <summary>Every magnification change (wheel notch, hotkey, slider) moves by exactly this much.</summary>
    public const double MagStep = 0.2;
    private static double StepMag(double v, int dir) => Math.Round(Math.Clamp(Math.Round(v, 1) + MagStep * dir, 1.5, 8.0), 1);
    private bool _on;
    private bool _hotkeysSuspended;
    private readonly HashSet<string> _keybindFailed = [];

    /// <summary>CyberOptix holds this object open while one of its keybind boxes is listening, so hotkeys do not swallow the keys.</summary>
    public const string CaptureMutexName = @"Local\CyberOptix.GlassCursor.KeyCapture";
    private FileSystemWatcher? _watch;
    private int _reloadPending, _reloadRetries;
    private bool _formSuspend, _extSuspend;

    private readonly Action _openApp, _exitApp;

    /// <summary>True while the lens is showing.</summary>
    internal bool IsOn => _on;

    public TrayApp(Action openApp, Action exitApp)
    {
        _openApp = openApp;
        _exitApp = exitApp;
        _settings.Sanitize();

        _toggleItem = new ToolStripMenuItem("Cursor on", null, (_, _) => SetOn(!_on));
        _sizeMenu = new ToolStripMenuItem("Lens size");
        foreach (var size in Sizes)
        {
            _sizeMenu.DropDownItems.Add(new ToolStripMenuItem($"{size} px", null, (_, _) => SetDiameter(size)) { Tag = size });
        }
        _zoomMenu = new ToolStripMenuItem("Zoom");
        foreach (var zoom in Zooms)
        {
            _zoomMenu.DropDownItems.Add(new ToolStripMenuItem($"{zoom:0.#}x", null, (_, _) => SetZoom(zoom)) { Tag = zoom });
        }
        _hideItem = new ToolStripMenuItem("Hide the Windows pointer while the lens is on", null, (_, _) => SetHidePointer(!_settings.HidePointer));
        _scopeItem = new ToolStripMenuItem("Right Click To Zoom on", null, (_, _) => SetScopeEnabled(!_settings.ScopeEnabled));
        _freeRoamItem = new ToolStripMenuItem("Free Roam", null, (_, _) => SetLensCentered(false));
        _centeredItem = new ToolStripMenuItem("Static Center", null, (_, _) => SetLensCentered(true));

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Open CyberOptix", null, (_, _) => _openApp()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_freeRoamItem);
        menu.Items.Add(_centeredItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_sizeMenu);
        menu.Items.Add(_zoomMenu);
        menu.Items.Add(_hideItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_scopeItem);
        menu.Items.Add(new ToolStripMenuItem("Glass Cursor settings...", null, (_, _) => ShowScopeSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Shortcuts...", null, (_, _) => ShowShortcuts()));
        menu.Items.Add(new ToolStripMenuItem("Exit CyberOptix", null, (_, _) => _exitApp()));

        _tray = new NotifyIcon
        {
            Icon = LoadTrayIcon() ?? MakeTrayIcon(),
            ContextMenuStrip = menu,
            Text = "CyberOptix",
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                SetOn(!_on);
            }
        };

        _mouseProc = MouseHookProc;
        _mouseHook = Native.SetWindowsHookExW(Native.WH_MOUSE_LL, _mouseProc, Native.GetModuleHandleW(null), 0);

        if (!_lens.Create() || !_scope.Create() || !_cursorZoom.Create())
        {
            MessageBox.Show("The Windows magnifier could not be started:\n" + (_lens.Error ?? _scope.Error ?? _cursorZoom.Error), "Glass Cursor",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Quit();
            return;
        }

        _timer.Tick += (_, _) => Tick();
        _lensOffPoll.Tick += (_, _) =>
        {
            if (_lensOff.WaitOne(0))
            {
                SetOn(false);
            }
            if (Interlocked.Exchange(ref _reloadPending, 0) == 1)
            {
                ReloadExternal();
            }
            CheckExternalCapture();
        };
        _lensOffPoll.Start();
        StartWatching();
        _ = Native.timeBeginPeriod(1);
        ApplyColors();
        _hotkeys.Pressed += OnHotkey;   // the keyboard shortcuts do their job
        RegisterHotkeys(reportFailures: true);
        RefreshMenu();
        SetOn(_settings.StartOn);
        _timer.Start();
    }

    private void RegisterHotkeys(bool reportFailures)
    {
        _hotkeys.UnregisterAll();
        const uint ctrlAlt = Native.MOD_CONTROL | Native.MOD_ALT;
        var failed = new List<string>();
        void Add(int id, uint mods, uint vk, string name)
        {
            if (!_hotkeys.Register(id, mods, vk))
            {
                failed.Add(name);
            }
        }
        Add(HkZoomIn, ctrlAlt, 0xBB, "Ctrl+Alt+=");
        Add(HkZoomInPad, ctrlAlt, 0x6B, "Ctrl+Alt+Num +");
        Add(HkZoomOut, ctrlAlt, 0xBD, "Ctrl+Alt+-");
        Add(HkZoomOutPad, ctrlAlt, 0x6D, "Ctrl+Alt+Num -");
        Add(HkBigger, ctrlAlt, 0xDD, "Ctrl+Alt+]");
        Add(HkSmaller, ctrlAlt, 0xDB, "Ctrl+Alt+[");
        Add(HkOff, ctrlAlt | Native.MOD_NOREPEAT, 0x52, "Ctrl+Alt+R");

        _keybindFailed.Clear();
        for (var i = 0; i < KeyAction.All.Length; i++)
        {
            var action = KeyAction.All[i].Action;
            var keys = _settings.GetKeybind(action);
            if (keys == Keys.None)
            {
                continue;
            }
            var (mods, vk) = KeyAction.ToNative(keys);
            if (!_hotkeys.Register(HkCustomBase + i, mods, vk))
            {
                _keybindFailed.Add(action);
                failed.Add(KeyAction.Describe(keys));
            }
        }

        if (reportFailures && failed.Count > 0)
        {
            _tray.ShowBalloonTip(5000, "Glass Cursor",
                "These shortcuts are used by another app and won't work: " + string.Join(", ", failed), ToolTipIcon.Warning);
        }
    }

    private void OnHotkey(int id)
    {
        switch (id)
        {
            case HkZoomIn or HkZoomInPad: SetZoom(StepMag(_settings.Zoom, +1)); break;
            case HkZoomOut or HkZoomOutPad: SetZoom(StepMag(_settings.Zoom, -1)); break;
            case HkBigger: SetDiameter(Step(_settings.Diameter, Sizes, +1)); break;
            case HkSmaller: SetDiameter(Step(_settings.Diameter, Sizes, -1)); break;
            case HkOff: SetOn(false); break;
            case >= HkCustomBase when id - HkCustomBase < KeyAction.All.Length:
                RunAction(KeyAction.All[id - HkCustomBase].Action);
                break;
        }
    }

    /// <summary>What each custom keybind does: the same as clicking its tick box.</summary>
    private void RunAction(string action)
    {
        switch (action)
        {
            case KeyAction.CursorToggle:
                SetOn(!_on);
                break;
            case KeyAction.CursorFreeRoam or KeyAction.CursorStatic:
                var centered = action == KeyAction.CursorStatic;
                if (_on && _settings.LensCentered == centered)
                {
                    SetOn(false);
                }
                else
                {
                    _settings.LensCentered = centered;
                    SetOn(true);
                }
                break;
            case KeyAction.ZoomToggle:
                SetScopeEnabled(!_settings.ScopeEnabled);
                break;
            case KeyAction.ZoomFollow or KeyAction.ZoomStatic:
                var follow = action == KeyAction.ZoomFollow;
                if (_settings.ScopeEnabled && _settings.ScopeFollowCursor == follow)
                {
                    SetScopeEnabled(false);
                }
                else
                {
                    _settings.ScopeFollowCursor = follow;
                    SetScopeEnabled(true);
                }
                break;
        }

        var text = action is KeyAction.ZoomToggle or KeyAction.ZoomFollow or KeyAction.ZoomStatic
            ? (!_settings.ScopeEnabled ? "Right Click To Zoom: OFF"
                : _settings.ScopeFollowCursor ? "Right Click To Zoom: Follow Mouse Cursor" : "Right Click To Zoom: Static Center")
            : (!_on ? "Cursor: OFF" : _settings.LensCentered ? "Cursor: Static Center" : "Cursor: Free Roam");
        _tray.ShowBalloonTip(1500, "Glass Cursor", text, ToolTipIcon.None);
    }

    bool ISettingsHost.IsOn => _on;

    void ISettingsHost.SetOn(bool on) => SetOn(on);

    void ISettingsHost.SettingsChanged()
    {
        _settings.Save();
        ApplyColors();
        RefreshMenu();
    }

    bool ISettingsHost.SetKeybind(string action, Keys keys)
    {
        // A combination can only do one thing: take it off any other action first.
        if (keys != Keys.None)
        {
            foreach (var (other, _) in KeyAction.All)
            {
                if (other != action && _settings.GetKeybind(other) == keys)
                {
                    _settings.Keybinds[other] = 0;
                }
            }
        }
        _settings.Keybinds[action] = (int)keys;
        _settings.Save();
        if (!_hotkeysSuspended)
        {
            RegisterHotkeys(reportFailures: false);
        }
        RefreshMenu();
        return !_keybindFailed.Contains(action);
    }

    bool ISettingsHost.KeybindWorks(string action) => !_keybindFailed.Contains(action);

    void ISettingsHost.SuspendHotkeys(bool suspend)
    {
        _formSuspend = suspend;
        ApplySuspend();
    }

    private void ApplySuspend()
    {
        var want = _formSuspend || _extSuspend;
        if (want == _hotkeysSuspended)
        {
            return;
        }
        _hotkeysSuspended = want;
        if (want)
        {
            _hotkeys.UnregisterAll();
        }
        else
        {
            RegisterHotkeys(reportFailures: false);
            RefreshMenu();
        }
    }

    /// <summary>True while CyberOptix's settings page is listening for a keybind. The object disappears if CyberOptix closes or crashes.</summary>
    private void CheckExternalCapture()
    {
        var capturing = false;
        try
        {
            if (Mutex.TryOpenExisting(CaptureMutexName, out var m))
            {
                m.Dispose();
                capturing = true;
            }
        }
        catch
        {
            // Not reachable (for example a different permission level): treat as not capturing.
        }
        if (capturing != _extSuspend)
        {
            _extSuspend = capturing;
            ApplySuspend();
        }
    }

    // ---- the settings file is shared with CyberOptix's Glass Cursor page: a change made in either place shows up in both ----

    private void StartWatching()
    {
        try
        {
            _watch = new FileSystemWatcher(Path.GetDirectoryName(Settings.FilePath)!, Path.GetFileName(Settings.FilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler poke = (_, _) => Volatile.Write(ref _reloadPending, 1);
            _watch.Changed += poke;
            _watch.Created += poke;
            _watch.Renamed += (_, _) => Volatile.Write(ref _reloadPending, 1);
        }
        catch
        {
            // Without the watcher, changes made in CyberOptix show up the next time Glass Cursor starts.
        }
    }

    /// <summary>Applies a change another program made to the settings file. Our own saves match what we hold, so they are ignored.</summary>
    private void ReloadExternal()
    {
        if (!Settings.TryLoad(out var fresh))
        {
            // Probably caught mid-write: try again on the next few ticks.
            if (_reloadRetries++ < 5)
            {
                Volatile.Write(ref _reloadPending, 1);
            }
            return;
        }
        _reloadRetries = 0;
        fresh.Sanitize();
        if (fresh.ToJson() == _settings.ToJson())
        {
            return;
        }

        var bindsChanged = !fresh.Keybinds.OrderBy(k => k.Key).SequenceEqual(_settings.Keybinds.OrderBy(k => k.Key));
        var wantOn = fresh.StartOn;
        _settings.CopyFrom(fresh);
        ApplyColors();
        if (bindsChanged && !_hotkeysSuspended)
        {
            RegisterHotkeys(reportFailures: false);
        }
        if (wantOn != _on)
        {
            ApplyOn(wantOn);
        }
        RefreshMenu();
        _scopeForm?.ReloadAll();
        Tick();
    }

    private void Tick()
    {
        // While the right-click zoom is showing, the pointer lens steps aside.
        var scoping = TickScope();
        // Static Centered parks the lens in the middle of the active window as a crosshair;
        // Free Roam follows the mouse.
        if (_on && !scoping && TryGetLensPoint(out var x, out var y))
        {
            _lens.Update(x, y, _settings.Diameter, _settings.Zoom);
        }
        else
        {
            _lens.Hide();
        }

        // The Windows pointer also disappears while zooming, and comes back on release
        // unless the pointer lens is hiding it anyway. Static Centered never hides it,
        // so you can always see where the real mouse is.
        var wantHidden = scoping || (_on && _settings.HidePointer && !_settings.LensCentered);
        if (wantHidden != PointerHider.IsHidden)
        {
            if (wantHidden)
            {
                PointerHider.Hide();
            }
            else
            {
                PointerHider.Restore();
            }
        }
    }

    private bool TryGetLensPoint(out int x, out int y)
    {
        if (_settings.LensCentered)
        {
            return TryGetAimPoint(out x, out y);
        }
        var ok = Native.GetCursorPos(out var p);
        (x, y) = (p.X, p.Y);
        return ok;
    }

    private bool TickScope()
    {
        var showing = TickScopeCore();
        _scoping = showing;
        return showing;
    }

    /// <summary>
    /// Mouse wheel changes Magnification by 0.2x per notch. While a Right Click To Zoom window is showing,
    /// the plain wheel adjusts that window's magnification and the scroll is swallowed. Otherwise
    /// Ctrl+Alt+wheel adjusts the Cursor lens magnification, so normal scrolling keeps working.
    /// </summary>
    private nint MouseHookProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && wParam == Native.WM_MOUSEWHEEL)
        {
            var data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            var delta = (short)((data.mouseData >> 16) & 0xFFFF);
            var dir = delta > 0 ? 1 : delta < 0 ? -1 : 0;
            if (dir != 0)
            {
                if (_scoping)
                {
                    if (_settings.ScopeFollowCursor)
                    {
                        _settings.CursorZoomZoom = StepMag(_settings.CursorZoomZoom, dir);
                    }
                    else
                    {
                        _settings.ScopeZoom = StepMag(_settings.ScopeZoom, dir);
                    }
                    _settings.Save();
                    return 1;
                }
                var ctrl = (Native.GetAsyncKeyState(0x11) & 0x8000) != 0;
                var alt = (Native.GetAsyncKeyState(0x12) & 0x8000) != 0;
                if (_on && ctrl && alt)
                {
                    SetZoom(StepMag(_settings.Zoom, dir));
                    return 1;
                }
            }
        }
        return Native.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private bool TickScopeCore()
    {
        if (!_settings.ScopeEnabled || (Native.GetAsyncKeyState(Native.VK_RBUTTON) & 0x8000) == 0)
        {
            _rightDownSince = -1;
            _scope.Hide();
            _cursorZoom.Hide();
            return false;
        }

        var now = Environment.TickCount64;
        if (_rightDownSince < 0)
        {
            _rightDownSince = now;
        }
        if (now - _rightDownSince < _settings.ScopeDelayMs)
        {
            return false;
        }

        if (_settings.ScopeFollowCursor)
        {
            _scope.Hide();
            if (!Native.GetCursorPos(out var p))
            {
                return false;
            }
            _cursorZoom.Update(p.X, p.Y, _settings.CursorZoomDiameter, _settings.CursorZoomZoom);
            return true;
        }

        _cursorZoom.Hide();
        if (!TryGetAimPoint(out var x, out var y))
        {
            return false;
        }
        _scope.Update(x, y, _settings.ScopeDiameter, _settings.ScopeZoom);
        return true;
    }

    /// <summary>
    /// The exact centre of the screen the active window is on (where a monitor's own aim point sits),
    /// or, with CentreOnWindow, the centre of the active window itself for windowed games.
    /// </summary>
    private bool TryGetAimPoint(out int x, out int y)
    {
        var window = Native.GetForegroundWindow();
        if (_settings.CentreOnWindow && window != 0
            && Native.GetClientRect(window, out var client) && client.Right > 0 && client.Bottom > 0)
        {
            var centre = new Native.POINT { X = client.Right / 2, Y = client.Bottom / 2 };
            if (Native.ClientToScreen(window, ref centre))
            {
                (x, y) = (centre.X, centre.Y);
                return true;
            }
        }

        nint monitor;
        if (window != 0)
        {
            monitor = Native.MonitorFromWindow(window, Native.MONITOR_DEFAULTTONEAREST);
        }
        else
        {
            _ = Native.GetCursorPos(out var cursor);
            monitor = Native.MonitorFromPoint(cursor, Native.MONITOR_DEFAULTTONEAREST);
        }
        var info = new Native.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Native.MONITORINFO>() };
        if (Native.GetMonitorInfoW(monitor, ref info))
        {
            x = (info.rcMonitor.Left + info.rcMonitor.Right) / 2;
            y = (info.rcMonitor.Top + info.rcMonitor.Bottom) / 2;
            return true;
        }
        (x, y) = (0, 0);
        return false;
    }

    private void SetScopeEnabled(bool enabled)
    {
        _settings.ScopeEnabled = enabled;
        _settings.Save();
        RefreshMenu();
    }

    private void SetLensCentered(bool centered)
    {
        _settings.LensCentered = centered;
        _settings.Save();
        RefreshMenu();
        Tick();
    }

    private void ShowScopeSettings()
    {
        if (_scopeForm is { IsDisposed: false })
        {
            _scopeForm.Activate();
            return;
        }
        _scopeForm = new ScopeSettingsForm(_settings, this);
        _scopeForm.Show();
    }

    private void ApplyColors()
    {
        Lens.DotColor = Settings.ParseColor(_settings.DotColor, Color.FromArgb(17, 17, 17));
        Lens.DotSize = _settings.DotSize;
        _lens.SetRingColor(Settings.ParseColor(_settings.RingColor, Color.Black));
        _cursorZoom.RefreshDot();
        _scope.SetCrisp(_settings.ScopeCrisp);
        _scope.SetContrast(_settings.ScopeContrast / 100.0);
    }

    private void ApplyOn(bool on)
    {
        _on = on;
        if (on)
        {
            Tick();
        }
        else
        {
            _lens.Hide();
            PointerHider.Restore();
        }
    }

    internal void SetOn(bool on)
    {
        ApplyOn(on);
        _settings.StartOn = on;
        _settings.Save();
        RefreshMenu();
    }

    private void SetDiameter(int diameter)
    {
        _settings.Diameter = diameter;
        _settings.Save();
        RefreshMenu();
        Tick();
    }

    private void SetZoom(double zoom)
    {
        _settings.Zoom = zoom;
        _settings.Save();
        RefreshMenu();
        Tick();
    }

    private void SetHidePointer(bool hide)
    {
        _settings.HidePointer = hide;
        _settings.Save();
        RefreshMenu();
        Tick();
    }

    private void RefreshMenu()
    {
        _toggleItem.Checked = _on;
        _hideItem.Checked = _settings.HidePointer;
        _scopeItem.Checked = _settings.ScopeEnabled;
        foreach (ToolStripMenuItem item in _sizeMenu.DropDownItems)
        {
            item.Checked = (int)item.Tag! == _settings.Diameter;
        }
        foreach (ToolStripMenuItem item in _zoomMenu.DropDownItems)
        {
            item.Checked = Math.Abs((double)item.Tag! - _settings.Zoom) < 0.01;
        }
        string Shortcut(string action) =>
            _settings.GetKeybind(action) == Keys.None ? "" : KeyAction.Describe(_settings.GetKeybind(action)).Replace(" + ", "+");
        _toggleItem.ShortcutKeyDisplayString = Shortcut(KeyAction.CursorToggle);
        _scopeItem.ShortcutKeyDisplayString = Shortcut(KeyAction.ZoomToggle);
        _freeRoamItem.ShortcutKeyDisplayString = Shortcut(KeyAction.CursorFreeRoam);
        _centeredItem.ShortcutKeyDisplayString = Shortcut(KeyAction.CursorStatic);
        _freeRoamItem.Checked = !_settings.LensCentered;
        _centeredItem.Checked = _settings.LensCentered;
        var text = _on
            ? $"Glass Cursor: {_settings.LensModeName}, {_settings.Zoom:0.#}x, {_settings.Diameter} px"
            : "Glass Cursor: off";
        _tray.Text = text.Length > 63 ? text[..63] : text;
        _scopeForm?.SyncEnabled();
    }

    private void ShowShortcuts()
    {
        string Bind(string action) => KeyAction.Describe(_settings.GetKeybind(action));
        MessageBox.Show(
            "Your keybinds (change them in Settings...):\n" +
            $"{Bind(KeyAction.CursorFreeRoam)}\tCursor: Free Roam on / off\n" +
            $"{Bind(KeyAction.CursorStatic)}\tCursor: Static Center on / off\n" +
            $"{Bind(KeyAction.CursorToggle)}\tCursor on / off\n" +
            $"{Bind(KeyAction.ZoomFollow)}\tRight Click To Zoom: Follow Mouse Cursor on / off\n" +
            $"{Bind(KeyAction.ZoomStatic)}\tRight Click To Zoom: Static Center on / off\n" +
            $"{Bind(KeyAction.ZoomToggle)}\tRight Click To Zoom on / off\n\n" +
            "Fixed shortcuts:\n" +
            "Ctrl + Alt + R\tTurn the cursor off and bring the normal pointer back\n" +
            "Ctrl + Alt + = / -\tZoom in / out (numpad + / - work too)\n" +
            "Ctrl + Alt + ] / [\tBigger / smaller lens\n\n" +
            "Hold the right mouse button to zoom.\n" +
            "Pick the modes, keybinds, hold time, sizes, zoom and colours in \"Settings...\".\n\n" +
            "Left-click the tray icon to turn the lens on or off.\n" +
            "Clicks land on the dot in the middle of the lens.\n\n" +
            "If the pointer ever stays invisible, run the \"Restore Mouse Pointer\" shortcut.",
            "Glass Cursor shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>Removes the system-wide mouse hook at once. Used when the process is crashing.</summary>
    internal void EmergencyUnhook()
    {
        var hook = _mouseHook;
        _mouseHook = 0;
        if (hook != 0) Native.UnhookWindowsHookEx(hook);
    }

    /// <summary>Called by the host when CyberOptix is closing.</summary>
    internal void Shutdown() => Quit();

    private void Quit()
    {
        if (_mouseHook != 0)
        {
            Native.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }
        _lensOffPoll.Stop();
        _watch?.Dispose();
        _lensOff.Dispose();
        _timer.Stop();
        _lens.Hide();
        _scope.Hide();
        _cursorZoom.Hide();
        PointerHider.Restore();
        _scopeForm?.Close();
        _hotkeys.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _scope.Dispose();
        _cursorZoom.Dispose();
        _lens.Dispose();
        _ = Native.timeEndPeriod(1);
        ExitThread();
    }

    /// <summary>The next menu step above or below the value, which may sit between steps after using a slider.</summary>
    private static T Step<T>(T value, T[] steps, int direction) where T : IComparable<T>
    {
        if (direction > 0)
        {
            foreach (var s in steps)
            {
                if (s.CompareTo(value) > 0)
                {
                    return s;
                }
            }
            return steps[^1];
        }
        for (var i = steps.Length - 1; i >= 0; i--)
        {
            if (steps[i].CompareTo(value) < 0)
            {
                return steps[i];
            }
        }
        return steps[0];
    }

    /// <summary>The icon file CyberOptix hands over (its own icon). Null falls back to the little lens drawn below.</summary>
    internal static byte[]? TrayIconBytes;

    private static Icon? LoadTrayIcon()
    {
        if (TrayIconBytes == null) return null;
        try
        {
            using var stream = new MemoryStream(TrayIconBytes);
            return new Icon(stream, SystemInformation.SmallIconSize);
        }
        catch
        {
            return null;
        }
    }

    private static Icon MakeTrayIcon()
    {
        using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new LinearGradientBrush(new Rectangle(2, 2, 28, 28),
                Color.FromArgb(255, 200, 230, 255), Color.FromArgb(255, 70, 120, 190), 90f);
            g.FillEllipse(fill, 3, 3, 26, 26);
            using var rim = new Pen(Color.White, 2.5f);
            g.DrawEllipse(rim, 3, 3, 26, 26);
            using var shine = new Pen(Color.FromArgb(220, 255, 255, 255), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(shine, 8, 8, 16, 16, 200, 60);
            g.FillEllipse(Brushes.White, 14, 14, 4, 4);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}

/// <summary>
/// A round Windows-magnifier lens (bottom window) with a glass rim drawn on a
/// per-pixel-alpha window above it. Both are click-through and both are excluded
/// from the magnifier's own capture.
/// </summary>
internal sealed class Lens(bool showDot = true, bool showEdge = true) : IDisposable
{
    private const string ClassName = "GlassCursor.Window";
    private const string DotClassName = "GlassCursor.Dot";
    private const int Pad = 12;

    private static readonly Native.WndProc s_wndProc = Native.DefWindowProcW;
    private static readonly Native.WndProc s_dotProc = DotProc;
    private static bool s_classRegistered, s_dotClassRegistered;
    private static Color s_dotColor = Color.FromArgb(17, 17, 17);
    private static int s_dotSize = 3;
    private int _dotShapeSize;

    private nint _host, _magnifier, _rim, _dot;
    private bool _magInitialized, _visible;
    private int _diameter;
    private Color _ringColor = Color.Black;

    public string? Error { get; private set; }

    /// <summary>The centre dot's colour, shared by every lens; call RefreshDot on each lens after changing it.</summary>
    public static Color DotColor
    {
        get => s_dotColor;
        set => s_dotColor = value.A == 0 ? Color.Transparent : Color.FromArgb(255, value);   // transparent = no dot
    }

    public void SetRingColor(Color color)
    {
        _ringColor = color.A == 0 ? Color.Transparent : Color.FromArgb(255, color);   // transparent = no ring
        if (_diameter > 0 && _rim != 0)
        {
            using var art = DrawRim(_diameter, showEdge, _ringColor);
            PushLayeredBitmap(_rim, art);
        }
        RefreshDot();
    }

    /// <summary>Odd sizes only, so the dot always has an exact middle pixel.</summary>
    public static int DotSize
    {
        get => s_dotSize;
        set => s_dotSize = Math.Clamp(value | 1, 1, 41);
    }

    /// <summary>Turns Windows' pixel smoothing off (crisp) or on (soft) for this lens.</summary>
    public bool SetCrisp(bool crisp) => _magnifier != 0 && Native.MagSetLensUseBitmapSmoothing(_magnifier, !crisp);

    /// <summary>Raises contrast around mid-grey by the given fraction (0 = unchanged) to make detail stand out.</summary>
    public unsafe bool SetContrast(double boost)
    {
        if (_magnifier == 0)
        {
            return false;
        }
        var k = (float)(1 + Math.Clamp(boost, 0, 1));
        var shift = (1 - k) / 2;
        var effect = new Native.MAGCOLOREFFECT();
        // Row-major 5x5 matrix applied to (r, g, b, a, 1).
        for (var i = 0; i < 3; i++)
        {
            effect.Transform[i * 5 + i] = k;
            effect.Transform[4 * 5 + i] = shift;
        }
        effect.Transform[3 * 5 + 3] = 1;
        effect.Transform[4 * 5 + 4] = 1;
        return Native.MagSetColorEffect(_magnifier, ref effect);
    }

    public void RefreshDot()
    {
        if (_dot != 0)
        {
            PlaceDot();
            _ = Native.InvalidateRect(_dot, 0, true);
        }
    }

    /// <summary>Sizes and shapes the dot and puts its middle pixel on the lens's exact centre, where clicks land.</summary>
    private void PlaceDot()
    {
        if (_dot == 0 || _diameter == 0)
        {
            return;
        }
        var size = Math.Min(s_dotSize, Math.Max(1, (_diameter - 4) | 1));
        var hidden = s_dotColor.A == 0;
        var shapeKey = hidden ? 0 : size;   // 0 = no dot
        if (shapeKey != _dotShapeSize)
        {
            nint shape;
            if (hidden)
            {
                shape = Native.CreateRectRgn(0, 0, 0, 0);   // an empty region: nothing to see and nothing to hit
            }
            else if (size == 3)
            {
                // The original small plus.
                shape = Native.CreateRectRgn(1, 0, 2, 3);
                var bar = Native.CreateRectRgn(0, 1, 3, 2);
                _ = Native.CombineRgn(shape, shape, bar, Native.RGN_OR);
                _ = Native.DeleteObject(bar);
            }
            else if (size < 3)
            {
                shape = Native.CreateRectRgn(0, 0, size, size);
            }
            else
            {
                shape = Native.CreateEllipticRgn(0, 0, size + 1, size + 1);
            }
            _ = Native.SetWindowRgn(_dot, shape, true);
            _dotShapeSize = shapeKey;
        }
        var corner = _diameter / 2 - size / 2;
        _ = Native.SetWindowPos(_dot, Native.HWND_TOP, corner, corner, size, size, Native.SWP_NOACTIVATE);
    }

    // Paints the dot in the chosen colour instead of a fixed class brush.
    private static nint DotProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        const uint WM_ERASEBKGND = 0x14;
        if (msg == WM_ERASEBKGND && Native.GetClientRect(hwnd, out var rect))
        {
            var brush = Native.CreateSolidBrush((uint)(s_dotColor.R | (s_dotColor.G << 8) | (s_dotColor.B << 16)));
            _ = Native.FillRect(wParam, ref rect, brush);
            _ = Native.DeleteObject(brush);
            return 1;
        }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public bool Create()
    {
        if (!Native.MagInitialize())
        {
            Error = "MagInitialize failed.";
            return false;
        }
        _magInitialized = true;

        var instance = Native.GetModuleHandleW(null);
        if (!s_classRegistered)
        {
            var wc = new Native.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
                hInstance = instance,
                lpszClassName = ClassName,
            };
            if (Native.RegisterClassExW(ref wc) == 0)
            {
                Error = "RegisterClassEx failed: " + Marshal.GetLastWin32Error();
                return false;
            }
            s_classRegistered = true;
        }

        const uint exStyle = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
            | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
        _host = Native.CreateWindowExW(exStyle, ClassName, "Glass Cursor Lens", Native.WS_POPUP,
            0, 0, 1, 1, 0, 0, instance, 0);
        if (_host == 0)
        {
            Error = "Could not create the lens window: " + Marshal.GetLastWin32Error();
            return false;
        }
        _ = Native.SetLayeredWindowAttributes(_host, 0, 255, Native.LWA_ALPHA);

        _magnifier = Native.CreateWindowExW(0, "Magnifier", "Glass Cursor Magnifier",
            Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPSIBLINGS, 0, 0, 1, 1, _host, 0, instance, 0);
        if (_magnifier == 0)
        {
            Error = "Could not create the magnifier control: " + Marshal.GetLastWin32Error();
            return false;
        }

        // The centre dot is a child of the lens window itself, so it moves with the
        // magnified circle in the same frame and can never drift off centre.
        if (showDot)
        {
            if (!s_dotClassRegistered)
            {
                var dc = new Native.WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEXW>(),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_dotProc),
                    hInstance = instance,
                    lpszClassName = DotClassName,
                };
                if (Native.RegisterClassExW(ref dc) == 0)
                {
                    Error = "RegisterClassEx (dot) failed: " + Marshal.GetLastWin32Error();
                    return false;
                }
                s_dotClassRegistered = true;
            }
            _dot = Native.CreateWindowExW(0, DotClassName, "Glass Cursor Dot",
                Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPSIBLINGS, 0, 0, 3, 3, _host, 0, instance, 0);
            if (_dot == 0)
            {
                Error = "Could not create the centre dot: " + Marshal.GetLastWin32Error();
                return false;
            }
        }

        _rim = Native.CreateWindowExW(exStyle, ClassName, "Glass Cursor Rim", Native.WS_POPUP,
            0, 0, 1, 1, 0, 0, instance, 0);
        if (_rim == 0)
        {
            Error = "Could not create the rim window: " + Marshal.GetLastWin32Error();
            return false;
        }

        if (!Native.MagSetWindowFilterList(_magnifier, Native.MW_FILTERMODE_EXCLUDE, 2, [_host, _rim]))
        {
            Error = "MagSetWindowFilterList failed.";
            return false;
        }
        return true;
    }

    public bool Update(int x, int y, int diameter, double zoom)
    {
        diameter &= ~1;
        if (diameter != _diameter)
        {
            Resize(diameter);
        }

        var transform = new Native.MAGTRANSFORM { M00 = (float)zoom, M11 = (float)zoom, M22 = 1f };
        var ok = Native.MagSetWindowTransform(_magnifier, ref transform);

        var sourceSize = Math.Max(1, (int)Math.Round(diameter / zoom));
        var source = new Native.RECT
        {
            Left = x - sourceSize / 2,
            Top = y - sourceSize / 2,
        };
        source.Right = source.Left + sourceSize;
        source.Bottom = source.Top + sourceSize;
        ok &= Native.MagSetWindowSource(_magnifier, source);

        var flags = Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | (_visible ? 0 : Native.SWP_SHOWWINDOW);
        // Move the lens and its edge together in one screen update so the edge never lags the lens.
        var rimSize = diameter + Pad * 2;
        var batch = Native.BeginDeferWindowPos(2);
        if (batch != 0)
        {
            batch = Native.DeferWindowPos(batch, _host, Native.HWND_TOPMOST, x - diameter / 2, y - diameter / 2, 0, 0, flags);
        }
        if (batch != 0)
        {
            batch = Native.DeferWindowPos(batch, _rim, Native.HWND_TOPMOST, x - rimSize / 2, y - rimSize / 2, 0, 0, flags);
        }
        if (batch == 0 || !Native.EndDeferWindowPos(batch))
        {
            _ = Native.SetWindowPos(_host, Native.HWND_TOPMOST, x - diameter / 2, y - diameter / 2, 0, 0, flags);
            _ = Native.SetWindowPos(_rim, Native.HWND_TOPMOST, x - rimSize / 2, y - rimSize / 2, 0, 0, flags);
        }
        _ = Native.InvalidateRect(_magnifier, 0, false);
        _visible = true;
        return ok;
    }

    public void Hide()
    {
        if (!_visible)
        {
            return;
        }
        _ = Native.ShowWindow(_host, Native.SW_HIDE);
        _ = Native.ShowWindow(_rim, Native.SW_HIDE);
        _visible = false;
    }

    public void Dispose()
    {
        if (_rim != 0)
        {
            _ = Native.DestroyWindow(_rim);
            _rim = 0;
        }
        if (_host != 0)
        {
            _ = Native.DestroyWindow(_host);
            _host = 0;
            _magnifier = 0;
            _dot = 0;
        }
        if (_magInitialized)
        {
            _ = Native.MagUninitialize();
            _magInitialized = false;
        }
    }

    private void Resize(int diameter)
    {
        _diameter = diameter;
        const uint flags = Native.SWP_NOMOVE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE;
        _ = Native.SetWindowPos(_host, 0, 0, 0, diameter, diameter, flags);
        _ = Native.SetWindowPos(_magnifier, 0, 0, 0, diameter, diameter, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        _ = Native.SetWindowRgn(_host, Native.CreateEllipticRgn(0, 0, diameter + 1, diameter + 1), true);
        if (_dot != 0)
        {
            // The plus's middle pixel sits on the lens's exact centre, where clicks land.
            PlaceDot();
        }

        using var art = DrawRim(diameter, showEdge, _ringColor);
        PushLayeredBitmap(_rim, art);
    }

    private static Bitmap DrawRim(int diameter, bool showEdge, Color ringColor)
    {
        var size = diameter + Pad * 2;
        var c = size / 2f;
        var r = diameter / 2f;
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        RectangleF Circle(float radius, float dy = 0) => new(c - radius, c - radius + dy, radius * 2, radius * 2);

        // Edge: one very thin line (black unless a colour was picked) laid over the lens's own clipped edge.
        if (showEdge && ringColor.A != 0)
        {
            using var edge = new Pen(ringColor, 1.25f);
            g.DrawEllipse(edge, Circle(r - 0.5f));
        }
        return bmp;
    }

    private static unsafe void PushLayeredBitmap(nint window, Bitmap bmp)
    {
        var w = bmp.Width;
        var h = bmp.Height;
        var header = new Native.BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(Native.BITMAPINFOHEADER),
            biWidth = w,
            biHeight = -h,
            biPlanes = 1,
            biBitCount = 32,
        };
        var dib = Native.CreateDIBSection(0, ref header, 0, out var bits, 0, 0);
        if (dib == 0)
        {
            return;
        }

        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (var y = 0; y < h; y++)
            {
                Buffer.MemoryCopy((void*)(data.Scan0 + y * data.Stride), (void*)(bits + y * w * 4), w * 4, w * 4);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        var screen = Native.GetDC(0);
        var memory = Native.CreateCompatibleDC(screen);
        var old = Native.SelectObject(memory, dib);
        var size = new Native.SIZE { cx = w, cy = h };
        var origin = new Native.POINT();
        var blend = new Native.BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = Native.AC_SRC_ALPHA };
        _ = Native.UpdateLayeredWindow(window, screen, 0, ref size, memory, ref origin, 0, ref blend, Native.ULW_ALPHA);
        _ = Native.SelectObject(memory, old);
        _ = Native.DeleteObject(dib);
        _ = Native.DeleteDC(memory);
        _ = Native.ReleaseDC(0, screen);
    }
}

/// <summary>Swaps every system pointer shape for an invisible one, and back.</summary>
internal static class PointerHider
{
    private static readonly uint[] CursorIds =
        [32512, 32513, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651, 32671, 32672];

    private static bool s_hidden;

    public static void Hide()
    {
        var andMask = Enumerable.Repeat((byte)0xFF, 128).ToArray();
        var xorMask = new byte[128];
        var instance = Native.GetModuleHandleW(null);
        foreach (var id in CursorIds)
        {
            // SetSystemCursor takes ownership of the handle, so each shape needs its own copy.
            var blank = Native.CreateCursor(instance, 0, 0, 32, 32, andMask, xorMask);
            if (blank != 0)
            {
                _ = Native.SetSystemCursor(blank, id);
            }
        }
        s_hidden = true;
    }

    public static void Restore()
    {
        // Reloads the user's own pointer scheme (including Accessibility pointer settings).
        _ = Native.SystemParametersInfoW(Native.SPI_SETCURSORS, 0, 0, 0);
        s_hidden = false;
    }

    public static bool IsHidden => s_hidden;
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly List<int> _ids = [];

    public HotkeyWindow() => CreateHandle(new CreateParams());

    public event Action<int>? Pressed;

    public bool Register(int id, uint modifiers, uint key)
    {
        if (!Native.RegisterHotKey(Handle, id, modifiers, key))
        {
            return false;
        }
        _ids.Add(id);
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _ids)
        {
            _ = Native.UnregisterHotKey(Handle, id);
        }
        _ids.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
        {
            Pressed?.Invoke((int)m.WParam);
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in _ids)
        {
            _ = Native.UnregisterHotKey(Handle, id);
        }
        _ids.Clear();
        DestroyHandle();
    }
}

/// <summary>What the settings window needs from the running app.</summary>
internal interface ISettingsHost
{
    bool IsOn { get; }
    void SetOn(bool on);
    /// <summary>Saves the settings and applies them (colours, menu, hotkeys).</summary>
    void SettingsChanged();
    /// <summary>Sets an action's keybind (Keys.None clears it); false if Windows wouldn't register it.</summary>
    bool SetKeybind(string action, Keys keys);
    bool KeybindWorks(string action);
    /// <summary>Turns every Glass Cursor hotkey off while a keybind box is listening for keys.</summary>
    void SuspendHotkeys(bool suspend);
}

/// <summary>All of Glass Cursor's settings; every change applies and saves straight away.</summary>
internal sealed class ScopeSettingsForm : Form
{
    private readonly Settings _settings;
    private readonly ISettingsHost _host;
    private readonly Dictionary<string, CheckBox> _toggles = [];
    private readonly Dictionary<string, (KeybindBox Box, Label Status)> _keybinds = [];
    private bool _syncing;
    private readonly List<Action> _refreshers = [];

    public ScopeSettingsForm(Settings settings, ISettingsHost host)
    {
        _settings = settings;
        _host = host;
        Action changed = host.SettingsChanged;
        Text = "Glass Cursor settings";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        TopMost = true;

        var outer = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, AutoSize = true, Padding = new Padding(14) };
        Controls.Add(outer);
        FlowLayoutPanel Column() => new()
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 0, 24, 0),
        };
        var left = Column();
        var right = Column();
        outer.Controls.Add(left, 0, 0);
        outer.Controls.Add(right, 1, 0);

        void AddHeading(Control column, string text, int top = 14) => column.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, top, 0, 2),
        });

        void AddNote(Control column, string text) => column.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 2, 0, 4),
        });

        // One row per action: a tick box that turns it on or off, and the keybind that does the same.
        TableLayoutPanel Grid()
        {
            var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            left.Controls.Add(grid);
            return grid;
        }

        void AddRow(TableLayoutPanel grid, string action, string text, Action<bool> apply)
        {
            // A fixed minimum width lines the keybind boxes up across both sections.
            var box = new CheckBox
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(350, 0),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 4, 16, 4),
            };
            box.CheckedChanged += (_, _) =>
            {
                if (!_syncing)
                {
                    apply(box.Checked);
                    SyncEnabled();
                }
            };
            var keys = new KeybindBox { Anchor = AnchorStyles.Left, Margin = new Padding(0, 2, 8, 2) };
            var status = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.Firebrick };
            keys.Capturing += (_, listening) => host.SuspendHotkeys(listening);
            keys.KeysPicked += (_, _) =>
            {
                host.SetKeybind(action, keys.Keys);
                SyncEnabled();
            };
            grid.Controls.Add(box);
            grid.Controls.Add(keys);
            grid.Controls.Add(status);
            _toggles[action] = box;
            _keybinds[action] = (keys, status);
        }

        AddNote(left, "Tick a box to turn it on. To change a keybind, click its box and press the new keys\n" +
            "(with Ctrl, Alt or Shift).  Backspace clears it, Esc cancels.");

        AddHeading(left, "Cursor", 6);
        var cursorGrid = Grid();
        AddRow(cursorGrid, KeyAction.CursorFreeRoam, "Free Roam On/Off  -  follows your mouse", on =>
        {
            if (on)
            {
                settings.LensCentered = false;
            }
            host.SetOn(on);
        });
        AddRow(cursorGrid, KeyAction.CursorStatic, "Static Center On/Off  -  crosshair in the centre", on =>
        {
            if (on)
            {
                settings.LensCentered = true;
            }
            host.SetOn(on);
        });
        AddRow(cursorGrid, KeyAction.CursorToggle, "Cursor On/Off  -  keeps the mode you picked", host.SetOn);
        AddNote(left, "In Static Center, the normal Windows pointer stays visible so you can find it.");
        var centreOnWindow = new CheckBox
        {
            Text = "Centre on the active window instead of the screen (only for games played in a window)",
            AutoSize = true,
            Checked = settings.CentreOnWindow,
            Margin = new Padding(0, 2, 0, 2),
        };
        centreOnWindow.CheckedChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }
            settings.CentreOnWindow = centreOnWindow.Checked;
            changed();
        };
        _refreshers.Add(() => centreOnWindow.Checked = settings.CentreOnWindow);
        left.Controls.Add(centreOnWindow);

        AddHeading(left, "Right Click To Zoom");
        var zoomHeading = left.Controls[^1];
        var zoomGrid = Grid();
        AddRow(zoomGrid, KeyAction.ZoomFollow, "Follow Mouse Cursor On/Off  -  zoom follows the mouse", on =>
        {
            settings.ScopeEnabled = on;
            if (on)
            {
                settings.ScopeFollowCursor = true;
            }
            changed();
        });
        AddRow(zoomGrid, KeyAction.ZoomStatic, "Static Center On/Off  -  scope zoom in the centre", on =>
        {
            settings.ScopeEnabled = on;
            if (on)
            {
                settings.ScopeFollowCursor = false;
            }
            changed();
        });
        AddRow(zoomGrid, KeyAction.ZoomToggle, "Right Click To Zoom On/Off  -  keeps the mode you picked", on =>
        {
            settings.ScopeEnabled = on;
            changed();
        });

        void AddSlider(string title, int min, int max, int step, int value, Func<int, string> format, Action<int> apply,
            Control? column = null, int width = 360, Func<int>? read = null)
        {
            var label = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            var slider = new TrackBar
            {
                Minimum = min,
                Maximum = max,
                SmallChange = step,
                LargeChange = step * 4,
                TickFrequency = (max - min) / 10,
                Value = Math.Clamp(value, min, max),
                Width = width,
            };
            void Show(int v) => label.Text = $"{title}:  {format(v)}";
            Show(slider.Value);
            slider.ValueChanged += (_, _) =>
            {
                var snapped = slider.Value >= max ? max : min + (int)Math.Round((slider.Value - min) / (double)step, MidpointRounding.AwayFromZero) * step;
                Show(snapped);
                if (_syncing)
                {
                    return;
                }
                apply(snapped);
                changed();
            };
            if (read != null)
            {
                _refreshers.Add(() => slider.Value = Math.Clamp(read(), min, max));
            }
            (column ?? left).Controls.Add(label);
            (column ?? left).Controls.Add(slider);
        }

        // The cursor sliders sit in the Cursor section, just above Right Click To Zoom.
        var cursorSliders = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0),
        };
        left.Controls.Add(cursorSliders);
        left.Controls.SetChildIndex(cursorSliders, left.Controls.IndexOf(zoomHeading));
        AddSlider("Cursor size (diameter)", 20, 200, 2, settings.Diameter, v => $"{v} px", v => settings.Diameter = v, cursorSliders, read: () => settings.Diameter);
        AddSlider("Cursor magnification", 15, 80, 2, (int)Math.Round(settings.Zoom * 10),
            v => $"{v / 10.0:0.0}x", v => settings.Zoom = v / 10.0, cursorSliders, read: () => (int)Math.Round(settings.Zoom * 10));

        AddSlider("Hold right-click for", 0, 2000, 50, settings.ScopeDelayMs,
            v => v == 0 ? "instant" : $"{v / 1000.0:0.00} s", v => settings.ScopeDelayMs = v, read: () => settings.ScopeDelayMs);

        AddHeading(left, "Static Right Click To Zoom (Center of Screen)");
        // 1220 px fills the inside of a full-screen sniper scope on a 2560x1440 display.
        AddSlider("Zoom window size", 100, 1220, 20, Math.Min(settings.ScopeDiameter, 1220),
            v => $"{v} px", v => settings.ScopeDiameter = v, read: () => Math.Min(settings.ScopeDiameter, 1220));
        AddSlider("Magnification", 15, 80, 2, (int)Math.Round(settings.ScopeZoom * 10),
            v => $"{v / 10.0:0.0}x", v => settings.ScopeZoom = v / 10.0, read: () => (int)Math.Round(settings.ScopeZoom * 10));
        var crisp = new CheckBox
        {
            Text = "Crisp pixels  -  turns off Windows' pixel smoothing, which makes the zoom look soft",
            AutoSize = true,
            Checked = settings.ScopeCrisp,
            Margin = new Padding(0, 6, 0, 0),
        };
        crisp.CheckedChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }
            settings.ScopeCrisp = crisp.Checked;
            changed();
        };
        _refreshers.Add(() => crisp.Checked = settings.ScopeCrisp);
        left.Controls.Add(crisp);
        AddSlider("Clarity boost (contrast)", 0, 60, 5, settings.ScopeContrast,
            v => v == 0 ? "off" : $"+{v}%", v => settings.ScopeContrast = v, read: () => settings.ScopeContrast);
        AddNote(left, "Whole-number magnification (2.0x, 3.0x, 4.0x) gives the cleanest pixels.");


        void AddColorPicker(string title, string current, Color fallback, Action<Color> apply, Func<string> read)
        {
            AddHeading(right, title, right.Controls.Count == 0 ? 0 : 18);
            var wheel = new ColorWheel { Size = new Size(120, 120), Margin = new Padding(20, 4, 0, 0) };
            var brightnessLabel = new Label { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            var brightness = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 170, SmallChange = 1, LargeChange = 10 };
            var settingsNone = false;
            var swatchRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var swatch = new Panel { Size = new Size(36, 22), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 4, 8, 0) };
            var hex = new Label { AutoSize = true, Margin = new Padding(0, 7, 12, 0) };
            var reset = new Button { Text = "Reset", AutoSize = true, Margin = new Padding(0, 1, 0, 0) };
            var none = new Button { Text = "\u2715 None", AutoSize = true, Margin = new Padding(0, 1, 6, 0) };
            swatch.Paint += (_, e) =>
            {
                if (settingsNone)
                {
                    using var pen = new Pen(SystemColors.GrayText, 2f);
                    e.Graphics.DrawLine(pen, 5, 4, swatch.Width - 6, swatch.Height - 5);
                    e.Graphics.DrawLine(pen, swatch.Width - 6, 4, 5, swatch.Height - 5);
                }
            };
            swatchRow.Controls.AddRange([swatch, hex, none, reset]);

            var updating = false;
            void Show(Color c)
            {
                settingsNone = c.A == 0;
                swatch.BackColor = settingsNone ? SystemColors.Window : c;
                swatch.Invalidate();
                hex.Text = Settings.ToHex(c);
                brightnessLabel.Text = $"Brightness:  {brightness.Value}%";
            }
            void SetAll(Color c)
            {
                if (c.A != 0)
                {
                    updating = true;
                    wheel.Value = c;
                    brightness.Value = (int)Math.Round(wheel.Brightness * 100);
                    updating = false;
                }
                Show(c);
            }
            void Picked()
            {
                if (updating)
                {
                    return;
                }
                wheel.Brightness = brightness.Value / 100.0;
                var c = wheel.Value;
                Show(c);
                apply(c);
                changed();
            }
            wheel.ColorPicked += (_, _) =>
            {
                // Picking a hue on the wheel while the colour is black would show no change,
                // so lift the brightness to full.
                if (brightness.Value == 0)
                {
                    updating = true;
                    brightness.Value = 100;
                    updating = false;
                }
                Picked();
            };
            brightness.ValueChanged += (_, _) => Picked();
            reset.Click += (_, _) =>
            {
                SetAll(fallback);
                apply(fallback);
                changed();
            };
            none.Click += (_, _) =>
            {
                Show(Color.Transparent);
                apply(Color.Transparent);
                changed();
            };

            SetAll(Settings.ParseColor(current, fallback));
            _refreshers.Add(() => SetAll(Settings.ParseColor(read(), fallback)));
            right.Controls.AddRange([wheel, brightnessLabel, brightness, swatchRow]);
        }

        AddColorPicker("Outer ring colour", settings.RingColor, Color.Black, c => settings.RingColor = Settings.ToHex(c), () => settings.RingColor);
        AddColorPicker("Centre dot colour", settings.DotColor, Color.FromArgb(17, 17, 17), c => settings.DotColor = Settings.ToHex(c), () => settings.DotColor);
        AddSlider("Center dot size", 1, 21, 2, settings.DotSize,
            v => v == 3 ? "3 px (small plus)" : $"{v} px", v => settings.DotSize = v, right, 170, () => settings.DotSize);
        AddNote(right, "The ring is on the Cursor lens.\nThe dot is on the Cursor and the\nFollow Mouse Cursor zoom.");
        AddHeading(right, "Right Click To Zoom (Mouse Cursor)", 18);
        AddSlider("Zoom window size", 100, 1220, 20, settings.CursorZoomDiameter,
            v => $"{v} px", v => settings.CursorZoomDiameter = v, right, 200, () => settings.CursorZoomDiameter);
        AddSlider("Magnification", 15, 80, 2, (int)Math.Round(settings.CursorZoomZoom * 10),
            v => $"{v / 10.0:0.0}x", v => settings.CursorZoomZoom = v / 10.0, right, 200, () => (int)Math.Round(settings.CursorZoomZoom * 10));

        var close = new Button { Text = "Close", AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 12, 0, 0) };
        close.Click += (_, _) => Close();
        outer.Controls.Add(close, 1, 1);
        AcceptButton = close;
        CancelButton = close;
        SyncEnabled();
    }

    /// <summary>Refreshes every control from the settings, after a change made outside this window (CyberOptix's Glass Cursor page).</summary>
    public void ReloadAll()
    {
        _syncing = true;
        try
        {
            foreach (var refresh in _refreshers)
            {
                refresh();
            }
        }
        finally
        {
            _syncing = false;
        }
        SyncEnabled();
    }

    public void SyncEnabled()
    {
        _syncing = true;
        var on = _host.IsOn;
        _toggles[KeyAction.CursorFreeRoam].Checked = on && !_settings.LensCentered;
        _toggles[KeyAction.CursorStatic].Checked = on && _settings.LensCentered;
        _toggles[KeyAction.CursorToggle].Checked = on;
        _toggles[KeyAction.ZoomFollow].Checked = _settings.ScopeEnabled && _settings.ScopeFollowCursor;
        _toggles[KeyAction.ZoomStatic].Checked = _settings.ScopeEnabled && !_settings.ScopeFollowCursor;
        _toggles[KeyAction.ZoomToggle].Checked = _settings.ScopeEnabled;
        foreach (var (action, (box, status)) in _keybinds)
        {
            box.Display(_settings.GetKeybind(action));
            status.Text = _settings.GetKeybind(action) == Keys.None || _host.KeybindWorks(action)
                ? ""
                : "In use by another app";
        }
        _syncing = false;
    }
}

/// <summary>The six actions that have a tick box and a custom keybind.</summary>
internal static class KeyAction
{
    public const string CursorFreeRoam = "CursorFreeRoam", CursorStatic = "CursorStatic", CursorToggle = "CursorToggle",
        ZoomFollow = "ZoomFollow", ZoomStatic = "ZoomStatic", ZoomToggle = "ZoomToggle";

    public static readonly (string Action, Keys Default)[] All =
    [
        (CursorFreeRoam, Keys.Control | Keys.Alt | Keys.F),
        (CursorStatic, Keys.Control | Keys.Alt | Keys.C),
        (CursorToggle, Keys.Control | Keys.Alt | Keys.X),
        (ZoomFollow, Keys.Control | Keys.Alt | Keys.V),
        (ZoomStatic, Keys.Control | Keys.Alt | Keys.S),
        (ZoomToggle, Keys.Control | Keys.Alt | Keys.Z),
    ];

    public static string Describe(Keys keys)
    {
        if (keys == Keys.None)
        {
            return "(none)";
        }
        var parts = new List<string>();
        if (keys.HasFlag(Keys.Control))
        {
            parts.Add("Ctrl");
        }
        if (keys.HasFlag(Keys.Alt))
        {
            parts.Add("Alt");
        }
        if (keys.HasFlag(Keys.Shift))
        {
            parts.Add("Shift");
        }
        var key = keys & Keys.KeyCode;
        parts.Add(key switch
        {
            >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
            >= Keys.NumPad0 and <= Keys.NumPad9 => "Num " + (key - Keys.NumPad0),
            Keys.Add => "Num +",
            Keys.Subtract => "Num -",
            Keys.Multiply => "Num *",
            Keys.Divide => "Num /",
            Keys.Oemplus => "=",
            Keys.OemMinus => "-",
            Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]",
            Keys.OemSemicolon => ";",
            Keys.OemQuotes => "'",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            Keys.OemPipe => "\\",
            Keys.Oemtilde => "`",
            Keys.Next => "Page Down",
            Keys.Prior => "Page Up",
            Keys.Return => "Enter",
            _ => key.ToString(),
        });
        return string.Join(" + ", parts);
    }

    /// <summary>RegisterHotKey's modifier flags and virtual key for a WinForms key combination.</summary>
    public static (uint Modifiers, uint Key) ToNative(Keys keys)
    {
        uint mods = Native.MOD_NOREPEAT;
        if (keys.HasFlag(Keys.Control))
        {
            mods |= Native.MOD_CONTROL;
        }
        if (keys.HasFlag(Keys.Alt))
        {
            mods |= Native.MOD_ALT;
        }
        if (keys.HasFlag(Keys.Shift))
        {
            mods |= Native.MOD_SHIFT;
        }
        return (mods, (uint)(keys & Keys.KeyCode));
    }
}

/// <summary>A box that records the next key combination pressed while it has focus.</summary>
internal sealed class KeybindBox : TextBox
{
    public KeybindBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        TabStop = false;
        Width = 150;
        TextAlign = HorizontalAlignment.Center;
        Cursor = Cursors.Hand;
        BackColor = SystemColors.Window;
    }

    public Keys Keys { get; private set; }

    /// <summary>True when the box starts listening for keys, false when it stops.</summary>
    public event EventHandler<bool>? Capturing;
    public event EventHandler? KeysPicked;

    public void Display(Keys keys)
    {
        Keys = keys;
        if (!Focused)
        {
            Text = KeyAction.Describe(keys);
        }
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        Text = "Press keys...";
        BackColor = Color.LightYellow;
        Capturing?.Invoke(this, true);
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        Text = KeyAction.Describe(Keys);
        BackColor = SystemColors.Window;
        Capturing?.Invoke(this, false);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var mods = keyData & Keys.Modifiers;
        switch (key)
        {
            case Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LWin or Keys.RWin:
                Text = mods == Keys.None ? "Press keys..." : KeyAction.Describe(mods).Replace(" + None", "") + " + ...";
                return true;
            case Keys.Escape when mods == Keys.None:
                Done();
                return true;
            case Keys.Back or Keys.Delete when mods == Keys.None:
                Keys = Keys.None;
                Done(picked: true);
                return true;
        }
        if (mods == Keys.None && key is not (>= Keys.F1 and <= Keys.F24))
        {
            // A bare letter would stop that key working everywhere else.
            Text = "Add Ctrl, Alt or Shift";
            return true;
        }
        Keys = key | mods;
        Done(picked: true);
        return true;
    }

    private void Done(bool picked = false)
    {
        // Moving focus away ends listening and shows the result.
        FindForm()!.ActiveControl = null;
        if (picked)
        {
            KeysPicked?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>A hue/saturation colour wheel; brightness is set separately.</summary>
internal sealed class ColorWheel : Control
{
    private Bitmap? _wheel;
    private double _hue, _saturation, _brightness = 1;

    public event EventHandler? ColorPicked;

    public ColorWheel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Cursor = Cursors.Cross;
    }

    public double Brightness
    {
        get => _brightness;
        set
        {
            _brightness = Math.Clamp(value, 0, 1);
            Invalidate();
        }
    }

    public Color Value
    {
        get => FromHsv(_hue, _saturation, _brightness);
        set
        {
            ToHsv(value, out _hue, out _saturation, out _brightness);
            Invalidate();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _wheel?.Dispose();
        _wheel = null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        _wheel ??= BuildWheel(Math.Min(Width, Height));
        e.Graphics.DrawImage(_wheel, 0, 0);

        var r = _wheel.Width / 2.0;
        var angle = _hue * Math.PI / 180;
        var mx = (float)(r + Math.Cos(angle) * _saturation * (r - 1));
        var my = (float)(r - Math.Sin(angle) * _saturation * (r - 1));
        var m = Math.Max(5f, Width / 30f);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var outerPen = new Pen(Color.Black, 2f);
        using var innerPen = new Pen(Color.White, 1.5f);
        e.Graphics.DrawEllipse(outerPen, mx - m, my - m, m * 2, m * 2);
        e.Graphics.DrawEllipse(innerPen, mx - m + 2, my - m + 2, m * 2 - 4, m * 2 - 4);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            Capture = true;
            Pick(e.Location);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Capture && e.Button == MouseButtons.Left)
        {
            Pick(e.Location);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Capture = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _wheel?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void Pick(Point p)
    {
        var r = Math.Min(Width, Height) / 2.0;
        var dx = p.X - r;
        var dy = r - p.Y;
        _hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        _saturation = Math.Min(1, Math.Sqrt(dx * dx + dy * dy) / (r - 1));
        Invalidate();
        ColorPicked?.Invoke(this, EventArgs.Empty);
    }

    private static Bitmap BuildWheel(int size)
    {
        size = Math.Max(size, 8);
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var r = size / 2.0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x + 0.5 - r;
                var dy = r - (y + 0.5);
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (d > r)
                {
                    continue;
                }
                var hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                var c = FromHsv(hue, Math.Min(1, d / (r - 1)), 1);
                var alpha = (int)Math.Round(Math.Clamp(r - d, 0, 1) * 255);
                bmp.SetPixel(x, y, Color.FromArgb(alpha, c));
            }
        }
        return bmp;
    }

    public static Color FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = (h / 60) switch
        {
            < 1 => (c, x, 0d),
            < 2 => (x, c, 0d),
            < 3 => (0d, c, x),
            < 4 => (0d, x, c),
            < 5 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        int To255(double value) => (int)Math.Round(Math.Clamp(value + m, 0, 1) * 255);
        return Color.FromArgb(255, To255(r), To255(g), To255(b));
    }

    public static void ToHsv(Color color, out double h, out double s, out double v)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        v = max;
        s = max == 0 ? 0 : delta / max;
        if (delta == 0)
        {
            h = 0;
        }
        else if (max == r)
        {
            h = 60 * ((g - b) / delta % 6);
        }
        else if (max == g)
        {
            h = 60 * ((b - r) / delta + 2);
        }
        else
        {
            h = 60 * ((r - g) / delta + 4);
        }
        if (h < 0)
        {
            h += 360;
        }
    }
}

internal static class Native
{
    public const int WH_MOUSE_LL = 14;
    public const nint WM_MOUSEWHEEL = 0x020A;
    public delegate nint LowLevelMouseProc(int code, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public nint dwExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] public static extern nint SetWindowsHookExW(int idHook, LowLevelMouseProc fn, nint mod, uint threadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    public const uint WS_POPUP = 0x80000000, WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000;
    public const int RGN_OR = 2;
    public static readonly nint HWND_TOP = 0;
    public const uint WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
        WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    public const uint LWA_ALPHA = 0x2, ULW_ALPHA = 0x2;
    public const byte AC_SRC_ALPHA = 0x1;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public const int SW_HIDE = 0;
    public const uint MW_FILTERMODE_EXCLUDE = 0;
    public const uint SPI_SETCURSORS = 0x57;
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
    public const int VK_RBUTTON = 0x02;
    public const uint MONITOR_DEFAULTTONEAREST = 0x2;
    public static readonly nint HWND_TOPMOST = -1;

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    public delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MAGCOLOREFFECT
    {
        public fixed float Transform[25];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MAGTRANSFORM
    {
        public float M00, M01, M02, M10, M11, M12, M20, M21, M22;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx, cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public nint lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [DllImport("Magnification.dll")] public static extern bool MagInitialize();
    [DllImport("Magnification.dll")] public static extern bool MagUninitialize();
    [DllImport("Magnification.dll")] public static extern bool MagSetWindowSource(nint hwnd, RECT rect);
    [DllImport("Magnification.dll")] public static extern bool MagSetWindowTransform(nint hwnd, ref MAGTRANSFORM transform);
    [DllImport("Magnification.dll")] public static extern bool MagSetLensUseBitmapSmoothing(nint hwnd, bool useSmoothing);
    [DllImport("Magnification.dll")] public static extern bool MagSetColorEffect(nint hwnd, ref MAGCOLOREFFECT effect);
    [DllImport("Magnification.dll")] public static extern bool MagSetWindowFilterList(nint hwnd, uint mode, int count, nint[] list);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] public static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("user32.dll")] public static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(nint hwnd, nint dstDc, nint dstPoint, ref SIZE size,
        nint srcDc, ref POINT srcPoint, uint key, ref BLENDFUNCTION blend, uint flags);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetClientRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern int FillRect(nint dc, ref RECT rect, nint brush);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(nint hwnd, ref POINT point);
    [DllImport("user32.dll")] public static extern nint MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] public static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] public static extern nint CreateCursor(nint instance, int hotX, int hotY, int width, int height, byte[] andMask, byte[] xorMask);
    [DllImport("user32.dll")] public static extern bool SetSystemCursor(nint cursor, uint id);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfoW(uint action, uint param, nint data, uint winIni);

    [DllImport("gdi32.dll")] public static extern nint CreateEllipticRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] public static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] public static extern int CombineRgn(nint dest, nint src1, nint src2, int mode);
    [DllImport("gdi32.dll")] public static extern nint CreateSolidBrush(uint colorRef);
    [DllImport("user32.dll")] public static extern nint BeginDeferWindowPos(int count);
    [DllImport("user32.dll")] public static extern nint DeferWindowPos(nint info, nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool EndDeferWindowPos(nint info);
    [DllImport("gdi32.dll")] public static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] public static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] public static extern nint CreateDIBSection(nint dc, ref BITMAPINFOHEADER header, uint usage,
        out nint bits, nint section, uint offset);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandleW(string? name);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint period);
}
