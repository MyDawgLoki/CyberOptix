using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media;
using Microsoft.Win32;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    /// <summary>
    /// Sets Windows' own accent color to the CyberOptix color, so the Start menu, Search, the taskbar, window title bars
    /// and window borders (including the Windows Settings window) are tinted with it. Windows only lets those take the accent color;
    /// the inside of the Settings app can only be light or dark. The colors Windows had are saved first and put back when this is switched off.
    /// </summary>
    public static class WindowsAccentService
    {
        private const string Accent = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
        private const string Dwm = @"Software\Microsoft\Windows\DWM";
        private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public class Saved
        {
            public string Sub { get; set; } = "";
            public string Name { get; set; } = "";
            public bool Exists { get; set; }
            public string Kind { get; set; } = "";
            public string? Value { get; set; }   // numbers as text, binary as base64
        }

        private static string BackupPath => AppPaths.Secure("windows-accent-original.json");
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

        private static string? _lastHex;

        public static bool HasBackup => File.Exists(BackupPath);

        private static readonly (string Sub, string Name)[] Touched =
        {
            (Accent, "AccentColorMenu"), (Accent, "StartColorMenu"), (Accent, "AccentPalette"),
            (Dwm, "AccentColor"), (Dwm, "ColorizationColor"), (Dwm, "ColorizationAfterglow"), (Dwm, "ColorPrevalence"),
            (Personalize, "ColorPrevalence"), (Personalize, "AppsUseLightTheme"), (Personalize, "SystemUsesLightTheme"), (Personalize, "EnableTransparency"),
        };

        private static List<Saved> Read()
        {
            var list = new List<Saved>();
            foreach (var (sub, name) in Touched)
            {
                var s = new Saved { Sub = sub, Name = name };
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(sub);
                    if (k != null && k.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        s.Exists = true;
                        s.Kind = k.GetValueKind(name).ToString();
                        var v = k.GetValue(name);
                        s.Value = v is byte[] b ? Convert.ToBase64String(b) : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                catch { }
                list.Add(s);
            }
            return list;
        }

        private static void Write(string sub, string name, object value, RegistryValueKind kind)
        {
            using var k = Registry.CurrentUser.CreateSubKey(sub);
            k.SetValue(name, value, kind);
        }

        private static int Dword(uint v) => unchecked((int)v);

        private static Color Shade(Color c, double f)   // f > 0 toward white, f < 0 toward black
        {
            byte M(byte x) => (byte)Math.Clamp(f >= 0 ? x + (255 - x) * f : x * (1 + f), 0, 255);
            return Color.FromRgb(M(c.R), M(c.G), M(c.B));
        }

        /// <summary>Makes Windows' accent color (and the places it is shown) this color. Keeps a copy of the old values the first time.</summary>
        private static readonly object Gate = new();

        public static void Apply(string hex)
        {
            lock (Gate) ApplyLocked(hex);
        }

        private static void ApplyLocked(string hex)
        {
            try
            {
                if (!ThemeService.TryParse(hex, out var c)) return;
                var s0 = AppSettings.Current;
                string key = hex + s0.WinSystemDark + s0.WinAppsDark + s0.WinTransparency + s0.WinAccentOnStart + s0.WinAccentOnTitle;
                if (key == _lastHex && HasBackup) return;   // already exactly this
                _lastHex = key;
                if (!HasBackup) File.WriteAllText(BackupPath, JsonSerializer.Serialize(Read(), Json));
                else
                {
                    // a copy made by an earlier version may lack the newer values: add them as they are now, before they change
                    var have = JsonSerializer.Deserialize<List<Saved>>(File.ReadAllText(BackupPath)) ?? new();
                    var extra = Read().Where(n => !have.Any(h => h.Sub == n.Sub && h.Name == n.Name)).ToList();
                    if (extra.Count > 0) { have.AddRange(extra); File.WriteAllText(BackupPath, JsonSerializer.Serialize(have, Json)); }
                }

                uint abgr = 0xFF000000u | (uint)(c.B << 16) | (uint)(c.G << 8) | c.R;
                uint argb = 0xC4000000u | (uint)(c.R << 16) | (uint)(c.G << 8) | c.B;

                var palette = new List<byte>();
                foreach (var f in new[] { 0.6, 0.4, 0.2, 0.0, -0.2, -0.4, -0.6, -0.75 })
                {
                    var p = Shade(c, f);
                    palette.AddRange(new byte[] { p.R, p.G, p.B, 0xFF });
                }

                Write(Accent, "AccentColorMenu", Dword(abgr), RegistryValueKind.DWord);
                Write(Accent, "StartColorMenu", Dword(abgr), RegistryValueKind.DWord);
                Write(Accent, "AccentPalette", palette.ToArray(), RegistryValueKind.Binary);
                Write(Dwm, "AccentColor", Dword(abgr), RegistryValueKind.DWord);
                Write(Dwm, "ColorizationColor", Dword(argb), RegistryValueKind.DWord);
                Write(Dwm, "ColorizationAfterglow", Dword(argb), RegistryValueKind.DWord);
                var s = AppSettings.Current;
                Write(Dwm, "ColorPrevalence", s.WinAccentOnTitle ? 1 : 0, RegistryValueKind.DWord);        // title bars and window borders
                Write(Personalize, "ColorPrevalence", s.WinAccentOnStart ? 1 : 0, RegistryValueKind.DWord); // Start and the taskbar
                Write(Personalize, "AppsUseLightTheme", s.WinAppsDark ? 0 : 1, RegistryValueKind.DWord);     // File Explorer, Settings and other apps
                Write(Personalize, "SystemUsesLightTheme", s.WinSystemDark ? 0 : 1, RegistryValueKind.DWord); // Start and the taskbar show the accent color only in dark mode
                Write(Personalize, "EnableTransparency", s.WinTransparency ? 1 : 0, RegistryValueKind.DWord);
                Broadcast();
            }
            catch { }
        }

        /// <summary>Puts back the colors Windows had before <see cref="Apply"/>.</summary>
        public static void Restore()
        {
            try
            {
                _lastHex = null;
                if (!HasBackup) return;
                var list = JsonSerializer.Deserialize<List<Saved>>(File.ReadAllText(BackupPath)) ?? new();
                foreach (var s in list)
                {
                    // the copy is treated as data: only the values CyberOptix itself changes are put back, nothing else it might name
                    if (!Touched.Any(t => t.Sub == s.Sub && t.Name == s.Name)) continue;
                    if (s.Exists && s.Kind != nameof(RegistryValueKind.Binary) && s.Kind != nameof(RegistryValueKind.DWord)) continue;
                    try
                    {
                        using var k = Registry.CurrentUser.CreateSubKey(s.Sub);
                        if (!s.Exists) { k.DeleteValue(s.Name, false); continue; }
                        if (s.Kind == nameof(RegistryValueKind.Binary)) k.SetValue(s.Name, Convert.FromBase64String(s.Value ?? ""), RegistryValueKind.Binary);
                        else k.SetValue(s.Name, int.TryParse(s.Value, out var n) ? n : unchecked((int)uint.Parse(s.Value ?? "0")), RegistryValueKind.DWord);
                    }
                    catch { }
                }
                File.Delete(BackupPath);
                Broadcast();
            }
            catch { }
        }

        /// <summary>Tells Windows the colors changed, so Start, the taskbar and open windows repaint.</summary>
        private static void Broadcast()
        {
            try
            {
                SendMessageTimeout((IntPtr)0xFFFF, 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, "ImmersiveColorSet", 0x2 /* abort if hung */, 1500, out _);
                SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, "WindowsThemeElement", 0x2, 1500, out _);
            }
            catch { }
        }

        /// <summary>The color to use now: the album cover's while matching the media player and something plays, otherwise the color picked for Windows, or the CyberOptix accent.</summary>
        private static string CurrentHex() =>
            MediaColorService.ForWindows is Color m ? $"#{m.R:X2}{m.G:X2}{m.B:X2}" : AppSettings.Current.WinOwnColor ? AppSettings.Current.WinColorHex : AppSettings.Current.AccentHex;

        /// <summary>Applies the current color when the match is on (and does nothing when it is already applied).</summary>
        public static void Refresh()
        {
            if (AppSettings.Current.SyncWindowsAccent) Apply(CurrentHex());
        }

        /// <summary>Applies or removes the color to match the setting.</summary>
        public static void Sync()
        {
            if (AppSettings.Current.SyncWindowsAccent) Apply(CurrentHex()); else Restore();
        }
    }
}
