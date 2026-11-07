using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CyberOptix.Models;
using CyberOptix.Widgets;

namespace CyberOptix.Services
{
    public record RunningWindow(IntPtr Handle, string Path);

    /// <summary>A display, in pixels: its whole area and the part left after the Windows taskbar.</summary>
    public record DockMonitor(IntPtr Handle, int WorkLeft, int WorkTop, int WorkRight, int WorkBottom, bool Primary);

    /// <summary>Owns the floating dock: starting and stopping it, the pinned list, and the Windows calls it needs.</summary>
    public static class DockService
    {
        private static DockWindow? _window;
        private static readonly Dictionary<IntPtr, DockWindow> Secondary = new();   // the docks on the other displays
        private static bool _displayWired;

        /// <summary>Windows that are flashing for attention right now. Filled from the shell hook on the main dock.</summary>
        public static readonly HashSet<IntPtr> Flashing = new();
        public static event Action? FlashChanged;
        public static void RaiseFlashChanged() => FlashChanged?.Invoke();
        private static readonly Dictionary<string, ImageSource?> Icons = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raised when the pinned list changes (from the dock's menu or the Dock page).</summary>
        public static event Action? PinnedChanged;

        public static bool IsRunning => _window != null;

        /// <summary>Where the main dock is on screen (pixels), for a second dock that sits next to it. Null when the dock is off.</summary>
        public static (int X, int Y, int Width, int Height)? MainDockRect() => _window?.VisibleRectPx();

        /// <summary>True while the CyberOptix window is minimized into the dock: the dock then shows a CyberOptix icon that brings it back.</summary>
        public static bool MainHiddenInDock { get; set; }

        /// <summary>Makes the main dock notice a change to the above straight away.</summary>
        public static void RefreshMain() => _window?.Reload();

        /// <summary>Raised when "use the dock as my taskbar" is switched off by the panic key, so the Dock page can update.</summary>
        public static event Action? ReplaceChanged;

        private static bool _panicWired;

        public static void Apply()
        {
            var s = AppSettings.Current;
            if (s.DockEnabled)
            {
                if (_window == null)
                {
                    var window = new DockWindow();
                    window.Closed += (_, _) => { if (_window == window) _window = null; };
                    _window = window;
                    window.Show();
                }
                _window.Reload();
            }
            else
            {
                var window = _window;
                _window = null;
                window?.Close();
            }
            SyncSecondaryDocks(s.DockEnabled && s.DockAllDisplays);
            IconGroupDockService.Apply();   // the AI / social icon docks (Individual Dock) have their own switches

            if (!_displayWired)
            {
                _displayWired = true;   // plugging a display in or out changes where docks belong
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) =>
                    Application.Current?.Dispatcher.BeginInvoke(new Action(async () => { await System.Threading.Tasks.Task.Delay(1200); Apply(); }));
            }

            // The Windows taskbar is only hidden while the dock is on AND set to replace it. Otherwise it comes straight back.
            var main = Application.Current.MainWindow;
            if (s.DockEnabled && s.DockReplaceTaskbar && main != null)
            {
                if (!_panicWired)
                {
                    _panicWired = true;
                    TaskbarTakeoverService.PanicPressed += () => Application.Current.Dispatcher.Invoke(() =>
                    {
                        AppSettings.Current.DockReplaceTaskbar = false;
                        AppSettings.Save();
                        Apply();
                        ReplaceChanged?.Invoke();
                    });
                }
                TaskbarTakeoverService.RegisterPanicKey(main);
            }
            else
            {
                TaskbarTakeoverService.UnregisterPanicKey();
                TaskbarTakeoverService.RestoreTaskbar();
            }
        }

        public static void Stop()
        {
            var window = _window;
            _window = null;
            window?.Close();
            SyncSecondaryDocks(false);
            IconGroupDockService.Stop();
            TaskbarTakeoverService.UnregisterPanicKey();
            TaskbarTakeoverService.RestoreTaskbar();
        }

        public static void Pin(string path)
        {
            path = ResolveShortcut(path);
            var pinned = AppSettings.Current.DockPinned;
            if (pinned.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) return;
            pinned.Add(path);
            Changed();
        }

        public static void Unpin(string path)
        {
            AppSettings.Current.DockPinned.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            Changed();
        }

        private static void Changed()
        {
            AppSettings.Save();
            _window?.Reload();
            foreach (var d in Secondary.Values) d.Reload();
            PinnedChanged?.Invoke();
        }

        // ---------- one dock per display ----------

        private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);

        /// <summary>Every connected display, with its usable area in pixels.</summary>
        public static List<DockMonitor> Monitors()
        {
            var list = new List<DockMonitor>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (m, _, _, _) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(m, ref info))
                    list.Add(new DockMonitor(m, info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom, (info.Flags & 1) != 0));
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static IntPtr MonitorOf(IntPtr window) => MonitorFromWindow(window, 2 /* nearest */);

        public static IntPtr PrimaryMonitor() => Monitors().FirstOrDefault(m => m.Primary)?.Handle ?? IntPtr.Zero;

        /// <summary>Makes sure there is exactly one extra dock on every display except the main one, or none.</summary>
        private static void SyncSecondaryDocks(bool want)
        {
            var wanted = want ? Monitors().Where(m => !m.Primary).ToList() : new List<DockMonitor>();

            foreach (var gone in Secondary.Keys.Where(k => wanted.All(m => m.Handle != k)).ToList())
            {
                var w = Secondary[gone];
                Secondary.Remove(gone);
                w.Close();
            }
            foreach (var m in wanted)
            {
                if (Secondary.TryGetValue(m.Handle, out var existing))
                {
                    existing.UpdateMonitor(m);
                    existing.Reload();
                    continue;
                }
                var dock = new DockWindow(m);
                var key = m.Handle;
                dock.Closed += (_, _) => { if (Secondary.TryGetValue(key, out var cur) && cur == dock) Secondary.Remove(key); };
                Secondary[key] = dock;
                dock.Show();
            }
        }

        // ---------- badges ----------

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        private static readonly Regex TitleCount = new(@"^\s*[\(\[](\d{1,3})\+?[\)\]]", RegexOptions.Compiled);

        /// <summary>
        /// The number an app shows in its window title, like "(3) Inbox" or "[2] chat". Windows keeps its own taskbar badges to itself, so this is how
        /// the dock can tell an app has something waiting: many apps put the count at the start of the title.
        /// </summary>
        public static int BadgeFor(IntPtr hwnd)
        {
            try
            {
                var sb = new StringBuilder(256);
                GetWindowText(hwnd, sb, sb.Capacity);
                var m = TitleCount.Match(sb.ToString());
                return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
            }
            catch { return 0; }
        }

        // ---------- names and icons ----------

        public static string DisplayName(string path)
        {
            if (ShellApps.IsShellApp(path))
            {
                var name = ShellApps.NameOf(path);
                if (!string.IsNullOrWhiteSpace(name)) return name;
                return path.Substring(path.LastIndexOf('\\') + 1);
            }
            try
            {
                var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
            }
            catch { }
            return Path.GetFileNameWithoutExtension(path);
        }

        public static ImageSource? IconFor(string path)
        {
            if (Icons.TryGetValue(path, out var cached)) return cached;
            ImageSource? image = null;
            if (ShellApps.IsShellApp(path)) return Icons[path] = ShellApps.IconOf(path);
            // A web-link shortcut (Steam and Epic games) names its own icon file; Windows hands back a generic globe for it.
            if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            {
                var own = UrlIcon(path);
                if (own != null) return Icons[path] = own;
            }
            var key = path;
            path = RealProgram(path);
            if (Icons.TryGetValue(path, out cached)) return cached;
            try
            {
                var info = new SHFILEINFO();
                // SHGFI_ICON | SHGFI_LARGEICON
                if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100) != IntPtr.Zero && info.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        image = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        image.Freeze();
                    }
                    finally
                    {
                        DestroyIcon(info.hIcon);
                    }
                }
            }
            catch { }
            return Icons[key] = image;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[]? ids, uint count, uint flags);

        /// <summary>One icon from a file (.ico, .exe, .dll) at an index, at 64 px. Null if it has none.</summary>
        public static ImageSource? IconAt(string file, int index)
        {
            try
            {
                var handles = new IntPtr[1];
                if (PrivateExtractIcons(file, index, 64, 64, handles, null, 1, 0) == 0 || handles[0] == IntPtr.Zero) return null;
                try
                {
                    var image = Imaging.CreateBitmapSourceFromHIcon(handles[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    image.Freeze();
                    return image;
                }
                finally { DestroyIcon(handles[0]); }
            }
            catch { return null; }
        }

        /// <summary>
        /// The icon to show for a shortcut: the one the shortcut itself names (a game launcher's own picture, Restart To BIOS), else its
        /// program's, else the shortcut's. Never the little arrow when the real icon can be found.
        /// </summary>
        public static ImageSource? ShortcutIcon(string lnk)
        {
            var key = "lnk:" + lnk;
            if (Icons.TryGetValue(key, out var cached)) return cached;
            ImageSource? image = null;
            try
            {
                if (lnk.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType != null)
                    {
                        dynamic shell = Activator.CreateInstance(shellType)!;
                        var link = shell.CreateShortcut(lnk);
                        string target = link.TargetPath ?? "";
                        string location = link.IconLocation ?? "";
                        int comma = location.LastIndexOf(',');
                        string iconFile = Environment.ExpandEnvironmentVariables(comma > 0 ? location.Substring(0, comma) : location).Trim();
                        int.TryParse(comma > 0 ? location.Substring(comma + 1) : "0", out int index);
                        if (iconFile.Length > 0 && File.Exists(iconFile)) image = IconAt(iconFile, index);
                        if (image == null && target.Length > 0)
                        {
                            target = DesktopItemsService.Remap(target);
                            if (File.Exists(target) || Directory.Exists(target)) image = IconFor(target);
                        }
                    }
                }
            }
            catch { }
            image ??= IconFor(lnk);
            return Icons[key] = image;
        }

        /// <summary>The picture a .url file names with IconFile=, read straight from its .ico (or from the program it points at). Null if there is none.</summary>
        private static ImageSource? UrlIcon(string urlFile)
        {
            try
            {
                string? file = null;
                foreach (var line in File.ReadLines(urlFile))
                    if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase)) { file = line.Substring(9).Trim().Trim('"'); break; }
                if (string.IsNullOrEmpty(file)) return null;
                file = file.Replace('/', '\\');
                if (!File.Exists(file)) return null;
                if (file.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var best = decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
                    if (best == null) return null;
                    best.Freeze();
                    return best;
                }
                return IconFor(file);
            }
            catch { return null; }
        }

        /// <summary>Some launchers (GitHub Desktop) are a tiny stub with no icon, the real program sitting in an app-1.2.3 folder beside it. Gives that one.</summary>
        private static string RealProgram(string path)
        {
            try
            {
                if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return path;
                var folder = Path.GetDirectoryName(path);
                if (folder == null || !Directory.Exists(folder)) return path;
                var newest = Directory.GetDirectories(folder, "app-*")
                    .Select(d => Path.Combine(d, Path.GetFileName(path)))
                    .Where(File.Exists)
                    .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                return newest ?? path;
            }
            catch { return path; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        /// <summary>Turns a .lnk shortcut into the program it launches, so pinned and running apps match up.</summary>
        public static string ResolveShortcut(string path)
        {
            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return path;
                dynamic shell = Activator.CreateInstance(shellType)!;
                string target = shell.CreateShortcut(path).TargetPath;
                return string.IsNullOrWhiteSpace(target) ? path : target;
            }
            catch
            {
                return path;
            }
        }

        // ---------- windows ----------

        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        /// <summary>Normal visible top-level windows and the program each belongs to.</summary>
        public static List<RunningWindow> RunningWindows(IntPtr ignore)
        {
            var list = new List<RunningWindow>();
            EnumWindows((h, _) =>
            {
                if (h == ignore || !IsWindowVisible(h) || GetWindowTextLength(h) == 0) return true;
                if ((GetWindowLong(h, -20) & 0x80) != 0) return true; // tool windows
                int cloaked;
                if (DwmGetWindowAttribute(h, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0) return true;

                var cls = new StringBuilder(128);
                GetClassName(h, cls, cls.Capacity);
                if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;

                var path = ImagePath(h);
                if (path == null) return true;

                // Store-style apps (Settings, Calculator, Photos...) are all drawn by one shared host program, and Control Panel runs inside
                // Explorer. Windows gives each of these its own taskbar button by their AppUserModelID, so the dock does the same.
                bool store = path.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
                bool explorer = path.EndsWith(@"\explorer.exe", StringComparison.OrdinalIgnoreCase);
                if (store || explorer)
                {
                    var appId = ShellApps.AppIdOf(h);
                    if (store)
                    {
                        if (string.IsNullOrEmpty(appId)) return true;   // no way to tell which app it is
                        path = ShellApps.PathFor(appId);
                    }
                    else if (!string.IsNullOrEmpty(appId) && !appId.Equals("Microsoft.Windows.Explorer", StringComparison.OrdinalIgnoreCase))
                    {
                        path = ShellApps.PathFor(appId);   // Control Panel and the like, not an ordinary folder window
                    }
                }

                list.Add(new RunningWindow(h, path));
                return true;
            }, IntPtr.Zero);
            return list;
        }

        private static string? ImagePath(IntPtr hwnd)
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            return ImagePathOfProcess(pid);
        }

        private static string? ImagePathOfProcess(uint pid)
        {
            var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(process, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally
            {
                CloseHandle(process);
            }
        }

        /// <summary>Brings a window to the front, restoring it first if it was minimized.</summary>
        public static void Activate(IntPtr hwnd)
        {
            if (IsIconic(hwnd)) ShowWindow(hwnd, 9); // SW_RESTORE
            SetForegroundWindow(hwnd);
        }

        public static bool IsForeground(IntPtr hwnd) => GetForegroundWindow() == hwnd;

        public static void CloseWindow(IntPtr hwnd) => PostMessage(hwnd, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);

        /// <summary>
        /// Opens an app, folder or file. CyberOptix runs as administrator, but everything it opens should run as the normal user (an
        /// elevated game or app cannot take drag and drop from normal windows, among other problems), so this goes through Explorer.
        /// </summary>
        public static void Launch(string path)
        {
            try
            {
                var psi = new ProcessStartInfo(SystemTools.Explorer) { UseShellExecute = false };
                psi.ArgumentList.Add(path);
                Process.Start(psi);
            }
            catch
            {
                // The program may have moved or been uninstalled; there is nothing useful to do from a click.
            }
        }

        public static void ShowInFolder(string path)
        {
            try
            {
                var psi = new ProcessStartInfo(SystemTools.Explorer) { UseShellExecute = false };
                psi.ArgumentList.Add("/select," + path);
                Process.Start(psi);
            }
            catch { }
        }
    }
}
