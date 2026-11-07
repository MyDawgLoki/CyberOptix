using System;
using System.Runtime.InteropServices;

namespace CyberOptix.Services
{
    /// <summary>Windows' own Widgets board (Windows 11). Windows does not let other programs place its widgets on the desktop, so this only opens the board.</summary>
    public static class StockWidgets
    {
        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        public static bool IsAvailable => OsInfo.IsWindows11;

        /// <summary>Opens (or closes) the Widgets board by pressing Windows + W once, the same shortcut you would press yourself.</summary>
        public static void Toggle()
        {
            keybd_event(0x5B, 0, 0, UIntPtr.Zero);   // Windows down
            keybd_event(0x57, 0, 0, UIntPtr.Zero);   // W down
            keybd_event(0x57, 0, 2, UIntPtr.Zero);   // W up
            keybd_event(0x5B, 0, 2, UIntPtr.Zero);   // Windows up
        }
    }
}
