using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CyberOptix.Services
{
    /// <summary>Attaches a window to the desktop (behind the icons) and reports what the user is looking at.</summary>
    public static class DesktopHost
    {
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
        private delegate bool EnumProc(IntPtr h, IntPtr l);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
        [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, UIntPtr w, IntPtr l, uint flags, uint timeout, out UIntPtr result);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);

        /// <summary>Re-parents <paramref name="hwnd"/> under the desktop and sizes it to the primary monitor. False if the desktop could not be found.</summary>
        public static bool Attach(IntPtr hwnd)
        {
            var progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return false;

            // Ask the shell to create the WorkerW layer that sits behind the icons.
            SendMessageTimeout(progman, 0x052C, (UIntPtr)0xD, (IntPtr)1, 0, 1000, out _);

            IntPtr parent = progman;
            IntPtr insertAfter = IntPtr.Zero; // 0 = top of the parent's children
            var defViewInProgman = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (defViewInProgman != IntPtr.Zero)
            {
                // Windows 11 24H2 and later: icons and WorkerW are both children of Progman.
                // Sit directly beneath the icon layer and above the wallpaper layer.
                insertAfter = defViewInProgman;
            }
            else
            {
                // Earlier builds: the icons live in a top-level WorkerW; the layer behind it is our host.
                IntPtr workerw = IntPtr.Zero;
                EnumWindows((top, _) =>
                {
                    if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                        workerw = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                    return true;
                }, IntPtr.Zero);
                if (workerw != IntPtr.Zero) parent = workerw;
            }

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(MonitorFromPoint(new POINT(), 1), ref mi); // primary monitor
            int w = mi.rcMonitor.Right - mi.rcMonitor.Left, h = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
            var origin = new POINT { X = mi.rcMonitor.Left, Y = mi.rcMonitor.Top };

            SetParent(hwnd, parent);
            int style = GetWindowLong(hwnd, -16);
            style = (style & ~unchecked((int)0x80000000)) | 0x40000000; // WS_POPUP off, WS_CHILD on
            SetWindowLong(hwnd, -16, style);

            ScreenToClient(parent, ref origin);
            // SWP_NOACTIVATE | SWP_SHOWWINDOW
            return SetWindowPos(hwnd, insertAfter, origin.X, origin.Y, w, h, 0x0010 | 0x0040);
        }

        /// <summary>What the foreground window is doing to the desktop.</summary>
        /// <summary>
        /// What the user is looking at. <paramref name="ignore"/> is the caller's own window. When <paramref name="sameMonitorOnly"/> is true (the default)
        /// a window only counts if it is on the same monitor as that window, so a game or a maximized editor on another screen does not hide a dock
        /// that sits on this one.
        /// </summary>
        public static void Query(IntPtr ignore, out bool coversScreen, out bool maximized, bool sameMonitorOnly = true)
        {
            coversScreen = false;
            maximized = false;

            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == ignore) return;

            var cls = new StringBuilder(128);
            GetClassName(fg, cls, cls.Capacity);
            if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "SHELLDLL_DefView")
                return; // the desktop itself is in front

            if (sameMonitorOnly && ignore != IntPtr.Zero && MonitorFromWindow(fg, 2) != MonitorFromWindow(ignore, 2))
                return; // it is on a different screen from the caller's window

            maximized = IsZoomed(fg);

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetWindowRect(fg, out var r) && GetMonitorInfo(MonitorFromWindow(fg, 2), ref mi))
            {
                coversScreen = r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top &&
                               r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
            }
        }
    }
}
