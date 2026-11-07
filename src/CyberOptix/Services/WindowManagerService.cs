using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    /// <summary>Global snapping hotkeys (Ctrl+Alt+...) and a tile-all-windows helper.</summary>
    public static class WindowManagerService
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT rect, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);

        private enum Snap { Left = 1, Right, Top, Bottom, Maximize, Center, TopLeft, TopRight, BottomLeft, BottomRight }

        // Ctrl+Alt + key
        private static readonly (Snap Action, uint Vk, string Label)[] Keys =
        {
            (Snap.Left, 0x25, "Left"), (Snap.Right, 0x27, "Right"), (Snap.Top, 0x26, "Up"), (Snap.Bottom, 0x28, "Down"),
            (Snap.Maximize, 0x0D, "Enter"), (Snap.Center, 0x43, "C"),
            (Snap.TopLeft, 0x31, "1"), (Snap.TopRight, 0x32, "2"), (Snap.BottomLeft, 0x33, "3"), (Snap.BottomRight, 0x34, "4"),
        };

        private static HwndSource? _source;
        private static IntPtr _hwnd;

        /// <summary>Registers the hotkeys. Returns the labels of any that another app already owns.</summary>
        public static List<string> Enable(Window owner)
        {
            Disable();
            var failed = new List<string>();
            _hwnd = new WindowInteropHelper(owner).EnsureHandle();
            _source = HwndSource.FromHwnd(_hwnd);
            _source?.AddHook(Hook);
            foreach (var (action, vk, label) in Keys)
            {
                if (!RegisterHotKey(_hwnd, (int)action, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, vk))
                    failed.Add(label);
            }
            return failed;
        }

        public static void Disable()
        {
            if (_hwnd == IntPtr.Zero) return;
            foreach (var (action, _, _) in Keys) UnregisterHotKey(_hwnd, (int)action);
            _source?.RemoveHook(Hook);
            _source = null;
            _hwnd = IntPtr.Zero;
        }

        private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                handled = true;
                var target = GetForegroundWindow();
                if (IsManageable(target)) Apply(target, (Snap)wParam.ToInt32());
            }
            return IntPtr.Zero;
        }

        private static (int X, int Y, int W, int H) WorkArea(IntPtr monitor)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(monitor, ref mi);
            return (mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right - mi.rcWork.Left, mi.rcWork.Bottom - mi.rcWork.Top);
        }

        private static void Apply(IntPtr hwnd, Snap action)
        {
            if (action == Snap.Maximize) { ShowWindow(hwnd, 3); return; } // SW_MAXIMIZE

            var (ax, ay, aw, ah) = WorkArea(MonitorFromWindow(hwnd, 2)); // MONITOR_DEFAULTTONEAREST
            int gap = AppSettings.Current.Gap;
            int x = ax + gap, y = ay + gap, w = aw - 2 * gap, h = ah - 2 * gap;
            int hw = (w - gap) / 2, hh = (h - gap) / 2;
            int rx = x + hw + gap, by = y + hh + gap;

            switch (action)
            {
                case Snap.Left: Place(hwnd, x, y, hw, h); break;
                case Snap.Right: Place(hwnd, rx, y, hw, h); break;
                case Snap.Top: Place(hwnd, x, y, w, hh); break;
                case Snap.Bottom: Place(hwnd, x, by, w, hh); break;
                case Snap.TopLeft: Place(hwnd, x, y, hw, hh); break;
                case Snap.TopRight: Place(hwnd, rx, y, hw, hh); break;
                case Snap.BottomLeft: Place(hwnd, x, by, hw, hh); break;
                case Snap.BottomRight: Place(hwnd, rx, by, hw, hh); break;
                case Snap.Center:
                    int cw = w * 6 / 10, ch = h * 6 / 10;
                    Place(hwnd, x + (w - cw) / 2, y + (h - ch) / 2, cw, ch);
                    break;
            }
        }

        /// <summary>Moves a window so its *visible* frame fills the rectangle (compensates for the invisible resize border).</summary>
        private static void Place(IntPtr hwnd, int x, int y, int w, int h)
        {
            if (IsZoomed(hwnd)) ShowWindow(hwnd, 9); // SW_RESTORE
            int l = 0, t = 0, r = 0, b = 0;
            RECT frame;
            if (GetWindowRect(hwnd, out var win) &&
                DwmGetWindowAttribute(hwnd, 9, out frame, Marshal.SizeOf<RECT>()) == 0) // DWMWA_EXTENDED_FRAME_BOUNDS
            {
                l = frame.Left - win.Left; t = frame.Top - win.Top;
                r = win.Right - frame.Right; b = win.Bottom - frame.Bottom;
            }
            // SWP_NOZORDER | SWP_NOACTIVATE
            SetWindowPos(hwnd, IntPtr.Zero, x - l, y - t, w + l + r, h + t + b, 0x0004 | 0x0010);
        }

        private static bool IsManageable(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || hwnd == _hwnd || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
            if (GetWindowTextLength(hwnd) == 0) return false;
            if ((GetWindowLong(hwnd, -20) & 0x80) != 0) return false; // WS_EX_TOOLWINDOW
            int cloaked;
            if (DwmGetWindowAttribute(hwnd, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0) return false; // DWMWA_CLOAKED
            var cls = new StringBuilder(256);
            GetClassName(hwnd, cls, cls.Capacity);
            return cls.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
        }

        /// <summary>Arranges every normal visible window in a grid on the primary monitor. Returns how many were tiled.</summary>
        public static int TileAll()
        {
            var windows = new List<IntPtr>();
            EnumWindows((h, _) => { if (IsManageable(h)) windows.Add(h); return true; }, IntPtr.Zero);
            if (windows.Count == 0) return 0;

            var (ax, ay, aw, ah) = WorkArea(MonitorFromPoint(new POINT(), 1)); // MONITOR_DEFAULTTOPRIMARY
            int gap = AppSettings.Current.Gap;
            int cols = (int)Math.Ceiling(Math.Sqrt(windows.Count));
            int rows = (int)Math.Ceiling(windows.Count / (double)cols);
            int cw = (aw - gap * (cols + 1)) / cols, ch = (ah - gap * (rows + 1)) / rows;

            for (int i = 0; i < windows.Count; i++)
            {
                int c = i % cols, r = i / cols;
                Place(windows[i], ax + gap + c * (cw + gap), ay + gap + r * (ch + gap), cw, ch);
            }
            return windows.Count;
        }
    }
}
