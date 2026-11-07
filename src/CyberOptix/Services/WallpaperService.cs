using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CyberOptix.Services
{
    public static class WallpaperService
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint param, string data, uint flags);

        public static readonly string[] Styles = { "Fill", "Fit", "Stretch", "Tile", "Center", "Span" };
        public static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };

        public static void Set(string path, string style)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Image not found", path);

            var (wallpaperStyle, tile) = style switch
            {
                "Center" => ("0", "0"),
                "Tile" => ("0", "1"),
                "Stretch" => ("2", "0"),
                "Fit" => ("6", "0"),
                "Span" => ("22", "0"),
                _ => ("10", "0"), // Fill
            };

            using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
            {
                k.SetValue("WallpaperStyle", wallpaperStyle, RegistryValueKind.String);
                k.SetValue("TileWallpaper", tile, RegistryValueKind.String);
            }

            // SPI_SETDESKWALLPAPER = 0x14, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE = 0x3
            if (!SystemParametersInfo(0x0014, 0, path, 0x03))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
