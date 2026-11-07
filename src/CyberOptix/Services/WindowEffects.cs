using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CyberOptix.Services
{
    /// <summary>Windows 11 system backdrop (the real blur-behind used by Mica/Acrylic).</summary>
    public static class WindowEffects
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr h, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        /// <summary>
        /// With the glass frame extended, Windows 11 draws its own min/max/close buttons under our custom ones,
        /// so every icon looks double-struck. Removing WS_SYSMENU removes the native buttons; resizing and snapping still work.
        /// </summary>
        public static void RemoveNativeCaptionButtons(Window window)
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            const int GWL_STYLE = -16, WS_SYSMENU = 0x00080000;
            SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_SYSMENU);
            // SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
        }

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        /// <summary>backdrop: 2 = Mica, 3 = Acrylic, 4 = Mica Alt. Returns false where unsupported (before Windows 11 22H2).</summary>
        public static bool TryEnableBackdrop(Window window, int backdrop = 3)
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget != null)
                source.CompositionTarget.BackgroundColor = Colors.Transparent;

            int dark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0;
        }
    }
}
