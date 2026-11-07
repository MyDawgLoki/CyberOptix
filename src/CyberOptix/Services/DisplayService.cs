using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace CyberOptix.Services
{
    public record DisplayInfo(int Number, string Id, int X, int Y, int Width, int Height, bool IsPrimary, string DeviceName = "")
    {
        public string Resolution => $"{Width} × {Height}";
    }

    /// <summary>Lists the connected displays and sets a different wallpaper on each, through Windows' IDesktopWallpaper API.</summary>
    public static class DisplayService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        // DESKTOP_WALLPAPER_POSITION
        private enum Position { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
            uint GetMonitorDevicePathCount();
            RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
            void SetBackgroundColor(uint color);
            uint GetBackgroundColor();
            void SetPosition(Position position);
            Position GetPosition();
        }

        [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
        private class DesktopWallpaperClass { }

        private static IDesktopWallpaper Create() => (IDesktopWallpaper)new DesktopWallpaperClass();

        public static List<DisplayInfo> GetDisplays()
        {
            var result = new List<(string Id, RECT Rect)>();
            try
            {
                var wp = Create();
                uint count = wp.GetMonitorDevicePathCount();
                for (uint i = 0; i < count; i++)
                {
                    try
                    {
                        var id = wp.GetMonitorDevicePathAt(i);
                        if (string.IsNullOrEmpty(id)) continue;
                        result.Add((id, wp.GetMonitorRECT(id)));
                    }
                    catch
                    {
                        // A monitor that is listed but not active has no rectangle.
                    }
                }
            }
            catch
            {
                return new List<DisplayInfo>();
            }

            var gdi = DisplayColorService.GdiDisplays(); // Windows' own names for each display, matched by position
            return result
                .OrderBy(r => r.Rect.Left).ThenBy(r => r.Rect.Top)
                .Select((r, i) => new DisplayInfo(
                    i + 1, r.Id, r.Rect.Left, r.Rect.Top, r.Rect.Right - r.Rect.Left, r.Rect.Bottom - r.Rect.Top,
                    r.Rect.Left == 0 && r.Rect.Top == 0,
                    gdi.FirstOrDefault(g => g.X == r.Rect.Left && g.Y == r.Rect.Top)?.DeviceName ?? ""))
                .ToList();
        }

        /// <summary>The image currently on this display, or null when it uses a slideshow or solid color.</summary>
        public static string? GetWallpaper(string monitorId)
        {
            try
            {
                var path = Create().GetWallpaper(monitorId);
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Sets the wallpaper for one display, or for every display when <paramref name="monitorId"/> is null.</summary>
        public static void SetWallpaper(string? monitorId, string path) => Create().SetWallpaper(monitorId, path);

        /// <summary>Windows only has one fit mode for all displays, so this applies everywhere.</summary>
        public static void SetFit(string style)
        {
            var position = style switch
            {
                "Fit" => Position.Fit,
                "Stretch" => Position.Stretch,
                "Tile" => Position.Tile,
                "Center" => Position.Center,
                "Span" => Position.Span,
                _ => Position.Fill,
            };
            Create().SetPosition(position);
        }
    }
}
