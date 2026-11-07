using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    /// <summary>Pointer speed, precision, scrolling and cursor themes. These are Windows settings; they do not change a mouse's own hardware DPI.</summary>
    public static class PointerService
    {
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool Spi(uint action, uint param, ref int value, uint flags);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SpiSet(uint action, uint param, IntPtr value, uint flags);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SpiMouse(uint action, uint param, int[] value, uint flags);

        private const uint Update = 0x3; // SPIF_UPDATEINIFILE | SPIF_SENDCHANGE

        public static int GetSpeed() { int v = 10; Spi(0x70, 0, ref v, 0); return v; }
        public static void SetSpeed(int v) { AppSettings.Current.Touch("pointer"); SpiSet(0x71, 0, (IntPtr)Math.Clamp(v, 1, 20), Update); }

        public static bool GetPrecision()
        {
            var m = new int[3];
            SpiMouse(0x3, 0, m, 0);
            return m[2] != 0;
        }

        public static void SetPrecision(bool on)
        {
            AppSettings.Current.Touch("pointer");
            var m = new int[3];
            SpiMouse(0x3, 0, m, 0);
            if (on) { m[0] = 6; m[1] = 10; m[2] = 1; } else { m[0] = 0; m[1] = 0; m[2] = 0; }
            SpiMouse(0x4, 0, m, Update);
        }

        public static int GetScrollLines() { int v = 3; Spi(0x68, 0, ref v, 0); return v; }
        public static void SetScrollLines(int v) { AppSettings.Current.Touch("scroll"); SpiSet(0x69, (uint)Math.Clamp(v, 1, 20), IntPtr.Zero, Update); }

        // ---------- cursor schemes ----------

        private static readonly string[] Roles =
        {
            "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No", "SizeNS", "SizeWE",
            "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Pin", "Person",
        };

        private const string UserSchemes = @"Control Panel\Cursors\Schemes";
        private const string SystemSchemes = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cursors\Schemes";

        /// <summary>Name and source (1 = yours, 2 = Windows) of every cursor theme installed.</summary>
        public static List<(string Name, int Source)> Schemes()
        {
            var list = new List<(string, int)>();
            try
            {
                using var u = Registry.CurrentUser.OpenSubKey(UserSchemes);
                if (u != null) list.AddRange(u.GetValueNames().Where(n => n.Length > 0).Select(n => (n, 1)));
                using var s = Registry.LocalMachine.OpenSubKey(SystemSchemes);
                if (s != null) list.AddRange(s.GetValueNames().Where(n => n.Length > 0 && !list.Any(x => x.Item1 == n)).Select(n => (n, 2)));
            }
            catch { }
            list.Add((DefaultName, 3));
            return list.OrderBy(x => x.Item1).ToList();
        }

        public static string CurrentScheme()
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
            return k?.GetValue("") as string ?? "";
        }

        public const string DefaultName = "Windows Default";

        // Windows' own default pointers. This theme is not stored in the schemes list, so it is spelled out here.
        private const string DefaultPaths =
            @"%SystemRoot%\cursors\aero_arrow.cur,%SystemRoot%\cursors\aero_helpsel.cur,%SystemRoot%\cursors\aero_working.ani,%SystemRoot%\cursors\aero_busy.ani," +
            @",,%SystemRoot%\cursors\aero_pen.cur,%SystemRoot%\cursors\aero_unavail.cur,%SystemRoot%\cursors\aero_ns.cur,%SystemRoot%\cursors\aero_ew.cur," +
            @"%SystemRoot%\cursors\aero_nwse.cur,%SystemRoot%\cursors\aero_nesw.cur,%SystemRoot%\cursors\aero_move.cur,%SystemRoot%\cursors\aero_up.cur," +
            @"%SystemRoot%\cursors\aero_link.cur,%SystemRoot%\cursors\aero_pin.cur,%SystemRoot%\cursors\aero_person.cur";

        public static string? ApplyScheme(string name, int source)
        {
            AppSettings.Current.Touch("cursors");
            try
            {
                string raw;
                if (source == 3) raw = DefaultPaths;
                else
                {
                    using var src = (source == 1 ? Registry.CurrentUser : Registry.LocalMachine).OpenSubKey(source == 1 ? UserSchemes : SystemSchemes);
                    raw = src?.GetValue(name) as string ?? "";
                }
                var paths = raw.Split(',');
                if (paths.Length < Roles.Length) return "That theme is not complete.";

                using var cur = Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors");
                for (int i = 0; i < Roles.Length; i++)
                    cur.SetValue(Roles[i], paths[i].Trim(), RegistryValueKind.ExpandString);
                cur.SetValue("", name);
                cur.SetValue("Scheme Source", source == 3 ? 2 : source, RegistryValueKind.DWord);
                SpiSet(0x57, 0, IntPtr.Zero, Update); // SPI_SETCURSORS: reload now
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>Cursor size from 1 (smallest) to 15, the same scale as Windows Settings.</summary>
        public static int GetSize()
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility");
            int px = k?.GetValue("CursorSize") is int s ? s : 1;
            return Math.Clamp(px, 1, 15);
        }

        public static string? SetSize(int size)
        {
            AppSettings.Current.Touch("cursors");
            try
            {
                size = Math.Clamp(size, 1, 15);
                using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Accessibility");
                k.SetValue("CursorSize", size, RegistryValueKind.DWord);
                k.SetValue("CursorBaseSize", 32 + (size - 1) * 16, RegistryValueKind.DWord);
                SpiSet(0x57, 0, IntPtr.Zero, Update);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }

    /// <summary>Remaps keys through Windows' Scancode Map. Takes effect after restarting your PC.</summary>
    public static class KeyRemapService
    {
        /// <summary>Keys offered in the remap lists, with their Windows scan codes.</summary>
        public static readonly (string Name, ushort Code)[] Keys =
        {
            ("Caps Lock", 0x3A), ("Left Ctrl", 0x1D), ("Right Ctrl", 0xE01D), ("Left Alt", 0x38), ("Right Alt", 0xE038),
            ("Left Shift", 0x2A), ("Right Shift", 0x36), ("Left Windows", 0xE05B), ("Right Windows", 0xE05C), ("Menu", 0xE05D),
            ("Escape", 0x01), ("Tab", 0x0F), ("Backspace", 0x0E), ("Enter", 0x1C), ("Space", 0x39),
            ("Insert", 0xE052), ("Delete", 0xE053), ("Home", 0xE047), ("End", 0xE04F), ("Page Up", 0xE049), ("Page Down", 0xE051),
            ("Scroll Lock", 0x46), ("Print Screen", 0xE037), ("Num Lock", 0x45),
            ("F1", 0x3B), ("F2", 0x3C), ("F3", 0x3D), ("F4", 0x3E), ("F5", 0x3F), ("F6", 0x40),
            ("F7", 0x41), ("F8", 0x42), ("F9", 0x43), ("F10", 0x44), ("F11", 0x57), ("F12", 0x58),
        };

        public const string Disabled = "Disabled (does nothing)";

        private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Keyboard Layout";

        private static ushort Code(string name) => name == Disabled ? (ushort)0 : Keys.First(k => k.Name == name).Code;

        /// <summary>Writes the remaps, or removes the Scancode Map when the list is empty. Returns null on success.</summary>
        public static string? Write(IReadOnlyList<KeyRemap> remaps)
        {
            AppSettings.Current.Touch("remap");
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
                if (k == null) return "Windows would not open the keyboard settings.";
                if (remaps.Count == 0)
                {
                    k.DeleteValue("Scancode Map", false);
                    return null;
                }

                using var ms = new MemoryStream();
                using var w = new BinaryWriter(ms);
                w.Write(0); w.Write(0);                 // header
                w.Write(remaps.Count + 1);              // entries + terminator
                foreach (var r in remaps)
                {
                    w.Write(Code(r.To));                // new key
                    w.Write(Code(r.From));              // key being replaced
                }
                w.Write(0);                              // terminator
                k.SetValue("Scancode Map", ms.ToArray(), RegistryValueKind.Binary);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }

    /// <summary>Global hot-keys that open a program, file or website. Press a key combination anywhere and the action runs once.</summary>
    public static class HotkeyService
    {
        private const int WM_HOTKEY = 0x0312;
        private const int FirstId = 7000;

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);

        private static HwndSource? _source;
        private static int _count;
        private static bool _hooked;

        /// <summary>Registers every saved hot-key. Returns the combos another app already owns.</summary>
        public static List<string> Apply(Window owner)
        {
            Unregister();
            var failed = new List<string>();
            _source = HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle);
            if (_source == null) return failed;
            if (!_hooked) { _source.AddHook(Hook); _hooked = true; }

            var list = AppSettings.Current.Hotkeys;
            for (int i = 0; i < list.Count; i++)
            {
                var h = list[i];
                if (h.Vk == 0 || string.IsNullOrWhiteSpace(h.Target)) continue;
                if (RegisterHotKey(_source.Handle, FirstId + i, h.Mods | 0x4000, h.Vk)) _count = Math.Max(_count, i + 1);
                else failed.Add(h.Combo);
            }
            return failed;
        }

        private static void Unregister()
        {
            if (_source == null) return;
            for (int i = 0; i < Math.Max(_count, AppSettings.Current.Hotkeys.Count); i++) UnregisterHotKey(_source.Handle, FirstId + i);
            _count = 0;
        }

        public static void Stop() => Unregister();

        private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_HOTKEY) return IntPtr.Zero;
            int index = wParam.ToInt32() - FirstId;
            var list = AppSettings.Current.Hotkeys;
            if (index < 0 || index >= list.Count) return IntPtr.Zero;

            var h = list[index];
            handled = true;
            try
            {
                // CyberOptix runs as administrator, but what a hot-key opens should not: it goes through Explorer, as the normal user.
                var target = h.Target.Trim();
                if (h.Action == "Website")
                {
                    if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                        return IntPtr.Zero;
                    target = uri.AbsoluteUri;
                }
                DockService.Launch(target);
            }
            catch { }
            return IntPtr.Zero;
        }
    }

    public record ConnectedDevice(string Name, string Type, string Connection, string Status);

    /// <summary>Lists the mice, keyboards and Bluetooth devices Windows can currently see.</summary>
    public static class DeviceService
    {
        public static async Task<List<ConnectedDevice>> ListAsync() => await Task.Run(() =>
        {
            var result = new List<ConnectedDevice>();
            try
            {
                const string script =
                    "Get-PnpDevice -PresentOnly -Class Mouse,Keyboard,Bluetooth,HIDClass,Image,Printer,Camera -ErrorAction SilentlyContinue | " +
                    "Select-Object FriendlyName,Class,Status,InstanceId | ConvertTo-Json -Compress";
                var psi = new ProcessStartInfo(SystemTools.PowerShell, $"-NoProfile -Command \"{script}\"")
                {
                    RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
                };
                using var p = Process.Start(psi)!;
                var json = p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
                if (string.IsNullOrWhiteSpace(json)) return result;

                using var doc = JsonDocument.Parse(json);
                var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
                var seen = new HashSet<string>();
                foreach (var e in items)
                {
                    string name = e.TryGetProperty("FriendlyName", out var n) ? n.GetString() ?? "" : "";
                    string cls = e.TryGetProperty("Class", out var c) ? c.GetString() ?? "" : "";
                    string id = e.TryGetProperty("InstanceId", out var i) ? i.GetString() ?? "" : "";
                    string status = e.TryGetProperty("Status", out var s) ? s.GetString() ?? "" : "";
                    if (name.Length == 0 || id.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase)) continue;
                    if (cls == "HIDClass" && !name.Contains("game", StringComparison.OrdinalIgnoreCase) && !name.Contains("controller", StringComparison.OrdinalIgnoreCase)) continue;
                    // Bluetooth lists many protocol and service entries per device; only the device itself (DEV_...) is shown.
                    if (cls == "Bluetooth" && !(id.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase) || id.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase))) continue;

                    string conn = id.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) ? "Bluetooth"
                        : id.StartsWith("USB", StringComparison.OrdinalIgnoreCase) || id.Contains("VID_", StringComparison.OrdinalIgnoreCase) ? "Wired (USB)"
                        : "Built in";
                    string type = cls == "HIDClass" ? "Game controller" : cls == "Bluetooth" ? "Device" : cls;
                    if (seen.Add(name + "|" + conn)) result.Add(new ConnectedDevice(name, type, conn, status));
                }
            }
            catch { }
            return result.OrderBy(d => d.Connection).ThenBy(d => d.Type).ThenBy(d => d.Name).ToList();
        });
    }

    /// <summary>
    /// Glass Cursor, part of CyberOptix. It runs inside the CyberOptix process (its own UI thread and tray icon) and keeps its settings in
    /// CyberOptix's protected folder. "On" and "off" here mean the lens; the tray icon and hotkeys stay for as long as CyberOptix runs.
    /// </summary>
    public static class GlassCursorService
    {
        /// <summary>The settings file shared by the Hardware page and the tray menu.</summary>
        public static string SettingsFile => AppPaths.Secure(@"GlassCursor\settings.json");

        /// <summary>True while the lens is on the screen.</summary>
        public static bool IsRunning => GlassCursor.GlassCursorHost.LensOn;

        /// <summary>Starts Glass Cursor inside CyberOptix. The two actions are the tray's "Open CyberOptix" and "Exit CyberOptix".</summary>
        public static void Begin(Action open, Action exit)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
                if (!File.Exists(SettingsFile))
                {
                    using var stream = typeof(GlassCursorService).Assembly.GetManifestResourceStream("GlassCursor/settings.json");
                    if (stream != null)
                    {
                        using var output = File.Create(SettingsFile);
                        stream.CopyTo(output);
                    }
                }
                byte[]? icon = null;
                try
                {
                    // The tray shows CyberOptix's own icon.
                    var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/CyberOptix.ico"));
                    if (info != null)
                    {
                        using var ms = new MemoryStream();
                        info.Stream.CopyTo(ms);
                        icon = ms.ToArray();
                    }
                }
                catch { }
                GlassCursor.GlassCursorHost.Start(SettingsFile, open, exit, icon);
            }
            catch { }
        }

        /// <summary>Removes the tray icon, the hotkeys and the lens, and gives the real pointer back. Called when CyberOptix exits.</summary>
        public static void End()
        {
            try { GlassCursor.GlassCursorHost.Stop(); } catch { }
        }

        public static string? Start()
        {
            if (!GlassCursor.GlassCursorHost.IsRunning) return "Glass Cursor is not running.";
            GlassCursor.GlassCursorHost.SetLens(true);
            return null;
        }

        /// <summary>Turns the lens off and brings the normal pointer back.</summary>
        public static string? Stop()
        {
            GlassCursor.GlassCursorHost.SetLens(false);
            GlassCursor.GlassCursorHost.RestorePointer();
            return null;
        }

        public static string? RestorePointer()
        {
            GlassCursor.GlassCursorHost.RestorePointer();
            return null;
        }
    }
}
