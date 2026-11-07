using System;
using System.Runtime.InteropServices;

namespace CyberOptix.Services
{
    /// <summary>Read-only questions about the screen: where the pointer is, where a window is, how big its screen is. Nothing here moves anything.</summary>
    public static class ScreenProbe
    {
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int Size; public RECT Monitor; public RECT Work; public uint Flags; }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        /// <summary>The pointer's position in screen pixels.</summary>
        public static bool Pointer(out int x, out int y)
        {
            x = y = 0;
            if (!GetCursorPos(out var p)) return false;
            x = p.X; y = p.Y;
            return true;
        }

        /// <summary>True while the primary (or, if buttons are swapped, the physical left) mouse button is held down. Only reads the state.</summary>
        public static bool ButtonDown() => (GetAsyncKeyState(0x01 /* VK_LBUTTON */) & 0x8000) != 0 || (GetAsyncKeyState(0x02 /* VK_RBUTTON */) & 0x8000) != 0;

        public static bool WindowRect(IntPtr window, out int left, out int top, out int right, out int bottom)
        {
            left = top = right = bottom = 0;
            if (window == IntPtr.Zero || !GetWindowRect(window, out var r)) return false;
            left = r.Left; top = r.Top; right = r.Right; bottom = r.Bottom;
            return true;
        }

        /// <summary>The whole screen (not just the part Windows leaves free) that a window is mostly on.</summary>
        public static bool MonitorRect(IntPtr window, out int left, out int top, out int right, out int bottom)
        {
            left = top = right = bottom = 0;
            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (window == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(window, 2 /* the nearest screen */), ref info)) return false;
            left = info.Monitor.Left; top = info.Monitor.Top; right = info.Monitor.Right; bottom = info.Monitor.Bottom;
            return true;
        }
    }
}
