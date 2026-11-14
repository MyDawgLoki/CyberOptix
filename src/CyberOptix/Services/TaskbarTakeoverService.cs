using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    /// <summary>
    /// Lets the dock stand in for the Windows taskbar: hides the native one, reserves the dock's screen space, and always keeps a way back.
    /// The way back is the panic key (Ctrl + Alt + T), the "Bring back the Windows taskbar" button, and closing CyberOptix.
    /// </summary>
    public static class TaskbarTakeoverService
    {
        private const int WM_HOTKEY = 0x0312;
        private const int PanicId = 7999;

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam;
        }

        [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref APPBARDATA data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);

        [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int Size; public RECT Monitor; public RECT Work; public uint Flags; }
        private delegate bool MonitorProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorProc cb, IntPtr data);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, ref RECT rect, uint flags);
        private const uint SPI_SETWORKAREA = 0x002F, SPIF_SENDCHANGE = 0x0002;

        private const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_GETSTATE = 4, ABM_SETSTATE = 10;
        private const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;

        private static bool _hidden;
        private static HwndSource? _source;
        private static bool _hooked;

        public static bool IsHidden => _hidden;

        // ---------- hiding and restoring the native taskbar ----------

        private static IntPtr MainTaskbar => FindWindow("Shell_TrayWnd", null);

        private static IntPtr[] AllTaskbars()
        {
            var list = new System.Collections.Generic.List<IntPtr> { MainTaskbar };
            IntPtr h = IntPtr.Zero;
            while ((h = FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) list.Add(h);
            return list.Where(x => x != IntPtr.Zero).ToArray();
        }

        public static void Hide()
        {
            if (!_hidden)
            {
                // The taskbar's own auto-hide setting is left alone. Asking Windows to turn it on made Explorer answer "A toolbar is already
                // hidden on this side of your screen" whenever another screen (a virtual or remote display, say) came or went, and the setting
                // stayed switched on in the registry. The taskbar windows are hidden and their strip handed back instead.
                _hidden = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreTaskbar();
                AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreTaskbar();
            }
            KeepHidden();
            _workCheckedAt = DateTime.MinValue;
            KeepWorkArea();
        }

        // ---------- giving the taskbar's strip back ----------
        // Hiding the taskbar's window is not enough: while Windows still counts it as a taskbar that stays on screen (auto-hide refused, for example
        // with "a toolbar is already hidden on this side"), it keeps its strip free, and maximized windows stop short of the screen's bottom edge.
        // So the strip is handed back by setting the work area to the whole screen, and the old work areas are kept to put back later.

        private static readonly System.Collections.Generic.Dictionary<(int, int), RECT> _savedWork = new();
        private static DateTime _workCheckedAt = DateTime.MinValue;

        /// <summary>Makes the work area of every screen reach the bottom edge while the taskbar is hidden. Cheap; checked every couple of seconds at most.</summary>
        public static void KeepWorkArea()
        {
            if (!_hidden) return;
            if (_reservedFor != IntPtr.Zero) return;   // the dock holds its own strip as an app bar: giving the whole screen back would undo it every 2 seconds
            var now = DateTime.UtcNow;
            if (now - _workCheckedAt < TimeSpan.FromSeconds(2)) return;
            _workCheckedAt = now;
            try
            {
                var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = MainTaskbar };
                if (((int)(ulong)SHAppBarMessage(ABM_GETSTATE, ref data) & ABS_AUTOHIDE) != 0) return;   // Windows really auto-hides it: it frees the strip itself
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (m, _, _, _) =>
                {
                    var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
                    if (!GetMonitorInfo(m, ref info)) return true;
                    int gap = info.Monitor.Bottom - info.Work.Bottom;
                    if (gap <= 0 || gap > 200) return true;   // already full height, or not a taskbar-sized strip
                    if (_savedWork.TryAdd((info.Work.Left, info.Monitor.Top), info.Work)) SaveWork();
                    var full = new RECT { Left = info.Work.Left, Top = info.Work.Top, Right = info.Work.Right, Bottom = info.Monitor.Bottom };
                    SystemParametersInfo(SPI_SETWORKAREA, 0, ref full, SPIF_SENDCHANGE);
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
        }

        private static void GiveWorkAreaBack()
        {
            try
            {
                foreach (var saved in _savedWork.Values.ToArray())
                {
                    var r = saved;
                    SystemParametersInfo(SPI_SETWORKAREA, 0, ref r, SPIF_SENDCHANGE);
                }
                _savedWork.Clear();
                DeleteWorkFile();
            }
            catch { }
        }

        // The old work areas are also kept in a small file, so that when CyberOptix is killed (Task Manager, a crash) the watchdog process
        // can still give the screens their strip back.
        private static string WorkFile => AppPaths.Secure("taskbar-workarea.txt");

        private static void SaveWork()
        {
            try
            {
                System.IO.File.WriteAllLines(WorkFile, _savedWork.Values.Select(r => $"{r.Left} {r.Top} {r.Right} {r.Bottom}"));
            }
            catch { }
        }

        private static void DeleteWorkFile() { try { System.IO.File.Delete(WorkFile); } catch { } }

        /// <summary>Puts back the work areas a dead copy of CyberOptix had changed (from its file), if any.</summary>
        private static void RestoreWorkFromFile()
        {
            try
            {
                if (!System.IO.File.Exists(WorkFile)) return;
                foreach (var line in System.IO.File.ReadAllLines(WorkFile))
                {
                    var n = line.Split(' ');
                    if (n.Length != 4 || !n.All(x => int.TryParse(x, out _))) continue;
                    var r = new RECT { Left = int.Parse(n[0]), Top = int.Parse(n[1]), Right = int.Parse(n[2]), Bottom = int.Parse(n[3]) };
                    if (r.Right <= r.Left || r.Bottom <= r.Top || r.Right - r.Left > 20000 || r.Bottom - r.Top > 20000) continue;
                    SystemParametersInfo(SPI_SETWORKAREA, 0, ref r, SPIF_SENDCHANGE);
                }
            }
            catch { }
            DeleteWorkFile();
        }

        /// <summary>Run by the watchdog process after CyberOptix was killed: the Windows taskbar and the screens' strips come back.</summary>
        public static void RestoreAfterKill()
        {
            RestoreWorkFromFile();
            foreach (var h in AllTaskbars()) ShowWindow(h, 5 /* SW_SHOW */);
        }

        /// <summary>Hides the taskbar windows again if Windows brought them back (it does that now and then).</summary>
        public static void KeepHidden()
        {
            if (!_hidden || DateTime.UtcNow < _holdUntil) return;
            foreach (var h in AllTaskbars())
                if (IsWindowVisible(h)) ShowWindow(h, 0 /* SW_HIDE */);
        }

        /// <summary>Puts the Windows taskbar back exactly as it was.</summary>
        public static void RestoreTaskbar()
        {
            if (!_hidden) return;
            _hidden = false;
            try
            {
                GiveWorkAreaBack();
                foreach (var h in AllTaskbars()) ShowWindow(h, 5 /* SW_SHOW */);

            }
            catch { }
        }

        /// <summary>At launch: if an earlier run died with the taskbar hidden, bring it back before doing anything else.</summary>
        public static void RecoverIfStranded()
        {
            if (AppSettings.Current.DockReplaceTaskbar) return; // it will be hidden again on purpose
            RestoreWorkFromFile();
            foreach (var h in AllTaskbars())
                if (!IsWindowVisible(h)) ShowWindow(h, 5);
        }

        // ---------- reserving the dock's screen space ----------

        private static IntPtr _reservedFor = IntPtr.Zero;

        /// <summary>Reserves a strip along the bottom or top of the primary display so maximized windows stop at the dock. Sizes are in pixels.</summary>
        public static (int Left, int Top) Reserve(IntPtr hwnd, bool top, int heightPx, int screenWidthPx, int screenHeightPx)
        {
            var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd, uCallbackMessage = 0x8001 };
            if (_reservedFor != hwnd)
            {
                SHAppBarMessage(ABM_NEW, ref data);
                _reservedFor = hwnd;
            }
            data.uEdge = top ? 1u : 3u; // ABE_TOP / ABE_BOTTOM
            data.rc = new RECT { Left = 0, Right = screenWidthPx, Top = top ? 0 : screenHeightPx - heightPx, Bottom = top ? heightPx : screenHeightPx };
            SHAppBarMessage(ABM_QUERYPOS, ref data);
            if (top) data.rc.Bottom = data.rc.Top + heightPx; else data.rc.Top = data.rc.Bottom - heightPx;
            SHAppBarMessage(ABM_SETPOS, ref data);
            return (data.rc.Left, data.rc.Top);
        }

        public static void Unreserve(IntPtr hwnd)
        {
            if (_reservedFor != hwnd || hwnd == IntPtr.Zero) return;
            var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
            SHAppBarMessage(ABM_REMOVE, ref data);
            _reservedFor = IntPtr.Zero;
        }

        // ---------- panic key ----------

        /// <summary>Registers Ctrl + Alt + T, which brings the Windows taskbar back no matter what.</summary>
        public static bool RegisterPanicKey(Window owner)
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle);
            if (_source == null) return false;
            if (!_hooked) { _source.AddHook(Hook); _hooked = true; }
            UnregisterHotKey(_source.Handle, PanicId);
            return RegisterHotKey(_source.Handle, PanicId, 0x1 | 0x2 | 0x4000, 0x54); // Alt + Ctrl + T
        }

        public static void UnregisterPanicKey()
        {
            if (_source != null) UnregisterHotKey(_source.Handle, PanicId);
        }

        /// <summary>Raised when the panic key is pressed, so the app can switch the setting off and tell the user.</summary>
        public static event Action? PanicPressed;

        private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == PanicId)
            {
                handled = true;
                RestoreTaskbar();
                PanicPressed?.Invoke();
            }
            return IntPtr.Zero;
        }

        // ---------- Start button and hidden-icons flyout ----------

        /// <summary>Opens the Windows Start menu by pressing the Windows key once.</summary>
        public static void OpenStart()
        {
            keybd_event(0x5B, 0, 0, UIntPtr.Zero);
            keybd_event(0x5B, 0, 2, UIntPtr.Zero);
        }

        private static DateTime _holdUntil = DateTime.MinValue;

        // Learnt at run time: if hiding the taskbar ever closes the hidden-icons panel, the taskbar has to stay up while the panel is open.
        private static bool _overflowNeedsTaskbar;

        /// <summary>
        /// The panel is a window of its own, so the taskbar that was only shown to open it can go away again at once. If that closes the panel
        /// (checked a moment later), the taskbar is brought back and from then on stays while the panel is open, the way it used to.
        /// </summary>
        private static void HideTaskbarBehindPanel(IntPtr tray, IntPtr flyout)
        {
            System.Threading.Thread.Sleep(150);
            _holdUntil = DateTime.MinValue;
            KeepHidden();
            System.Threading.Thread.Sleep(300);
            if (!IsWindowVisible(flyout))
            {
                _overflowNeedsTaskbar = true;
                _holdUntil = DateTime.UtcNow.AddSeconds(10);
                ShowWindow(tray, 4 /* SW_SHOWNOACTIVATE */);
            }
        }

        private static void Tap(byte vk)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, 2, UIntPtr.Zero);
        }

        /// <summary>
        /// Opens Windows' own hidden-icons panel and puts it next to the dock. It presses Windows + B (which focuses the tray) and then Enter
        /// on the chevron, because the taskbar does not let other programs click that button directly. Returns false if the panel did not appear.
        /// This borrows Explorer's panel, so a big Windows update could need an adjustment here.
        /// </summary>
        public static bool ShowHiddenIcons(int anchorXPx, int anchorYPx, bool dockOnTop)
        {
            try
            {
                var tray = MainTaskbar;
                if (tray == IntPtr.Zero) return false;

                // Windows + B needs the taskbar on screen for the moment it takes to open the panel. It goes away again as soon as the panel is up
                // (see below); this hold is only the longest it may stay if something goes wrong.
                _holdUntil = DateTime.UtcNow.AddSeconds(_overflowNeedsTaskbar ? 10 : 4);
                if (!IsWindowVisible(tray)) ShowWindow(tray, 4 /* SW_SHOWNOACTIVATE */);

                keybd_event(0x5B, 0, 0, UIntPtr.Zero);   // Windows
                Tap(0x42);                                // B
                keybd_event(0x5B, 0, 2, UIntPtr.Zero);
                System.Threading.Thread.Sleep(500);
                Tap(0x0D);                                // Enter on the chevron

                for (int i = 0; i < 25; i++)
                {
                    System.Threading.Thread.Sleep(60);
                    var flyout = FindWindow("TopLevelWindowForOverflowXamlIsland", null);
                    if (flyout == IntPtr.Zero || !IsWindowVisible(flyout)) continue;

                    GetWindowRect(flyout, out var r);
                    int w = r.Right - r.Left, h = r.Bottom - r.Top;
                    int x = Math.Max(0, anchorXPx - w / 2);
                    int y = dockOnTop ? anchorYPx : anchorYPx - h;
                    SetWindowPos(flyout, (IntPtr)(-1), x, y, 0, 0, 0x1 | 0x10); // HWND_TOPMOST, keep size, no activate
                    if (!_overflowNeedsTaskbar) HideTaskbarBehindPanel(tray, flyout);
                    return true;
                }
                _holdUntil = DateTime.UtcNow.AddSeconds(1);   // it did not open: do not leave the taskbar up
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
