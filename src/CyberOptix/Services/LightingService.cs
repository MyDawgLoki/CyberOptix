using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Devices.Enumeration;
using Windows.Devices.Lights;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    public record LampDevice(string Id, string Name, string Kind, int Lamps);

    /// <summary>
    /// RGB lighting through Windows Dynamic Lighting (the HID LampArray standard). It works with any keyboard, mouse or other device that
    /// supports that standard, whatever the brand. Brand-specific lighting (Corsair iCUE, Razer, ASUS Aura, Logitech G HUB) is not reachable this way.
    /// </summary>
    public static class LightingService
    {
        public static readonly string[] Effects = { "Off", "Static", "Breathing", "Rainbow", "Color cycle", "Pulse" };

        private static readonly Dictionary<string, LampArray> Arrays = new();
        private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
        private static readonly DateTime Start = DateTime.UtcNow;
        private static bool _wired;

        public static async Task<List<LampDevice>> ListAsync()
        {
            var result = new List<LampDevice>();
            try
            {
                var infos = await DeviceInformation.FindAllAsync(LampArray.GetDeviceSelector());
                foreach (var info in infos)
                {
                    try
                    {
                        var arr = await LampArray.FromIdAsync(info.Id);
                        if (arr == null) continue;
                        Arrays[info.Id] = arr;
                        result.Add(new LampDevice(info.Id, info.Name, arr.LampArrayKind.ToString(), arr.LampCount));
                    }
                    catch { /* a device that will not open is skipped */ }
                }
            }
            catch { /* Dynamic Lighting is not available on this Windows */ }
            return result;
        }

        /// <summary>Draws the saved effect for "mouse" or "keyboard" now, and starts the animation timer if the effect moves.</summary>
        public static void Apply()
        {
            if (!_wired)
            {
                _wired = true;
                Timer.Tick += (_, _) => DrawAll();
            }
            DrawAll(); // one immediate frame is all that "Static" and "Off" need

            bool animated = AppSettings.Current.Lighting.Values.Any(l => l.Effect is "Breathing" or "Rainbow" or "Color cycle" or "Pulse");
            if (animated && !Timer.IsEnabled) Timer.Start();
            else if (!animated && Timer.IsEnabled) Timer.Stop();
        }

        /// <summary>Brings back whatever was saved. Called at launch.</summary>
        public static async Task RestoreAsync()
        {
            if (!AppSettings.Current.Lighting.Values.Any(l => l.Effect != "Off")) return;
            await ListAsync();
            Apply();
        }

        public static void Stop() => Timer.Stop();

        private static void DrawAll()
        {
            double t = (DateTime.UtcNow - Start).TotalSeconds;
            foreach (var arr in Arrays.Values)
            {
                string group = arr.LampArrayKind == LampArrayKind.Keyboard ? "keyboard" : "mouse";
                if (!AppSettings.Current.Lighting.TryGetValue(group, out var s)) continue;
                if (!ThemeService.TryParse(s.Color, out var baseColor)) baseColor = System.Windows.Media.Colors.Violet;

                try { Draw(arr, s, baseColor, t); } catch { /* the device went away or another app has control */ }
            }
        }

        private static void Draw(LampArray arr, LightSetting s, System.Windows.Media.Color baseColor, double t)
        {
            double speed = 0.3 + s.Speed * 0.25;
            double bright = Math.Clamp(s.Brightness, 0, 100) / 100.0;

            Windows.UI.Color Make(System.Windows.Media.Color c, double scale) =>
                Windows.UI.Color.FromArgb(255, (byte)(c.R * scale * bright), (byte)(c.G * scale * bright), (byte)(c.B * scale * bright));

            switch (s.Effect)
            {
                case "Off":
                    arr.SetColor(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                    break;
                case "Static":
                    arr.SetColor(Make(baseColor, 1));
                    break;
                case "Breathing":
                    arr.SetColor(Make(baseColor, 0.5 + 0.5 * Math.Sin(t * speed * 2)));
                    break;
                case "Pulse":
                    arr.SetColor(Make(baseColor, Math.Sin(t * speed * 5) > 0.4 ? 1 : 0.08));
                    break;
                case "Color cycle":
                    arr.SetColor(Make(FromHsv(t * speed * 30 % 360, 1, 1), 1));
                    break;
                case "Rainbow":
                {
                    int n = arr.LampCount;
                    var colors = new Windows.UI.Color[n];
                    var indices = new int[n];
                    for (int i = 0; i < n; i++)
                    {
                        indices[i] = i;
                        colors[i] = Make(FromHsv((i * 360.0 / Math.Max(1, n) + t * speed * 60) % 360, 1, 1), 1);
                    }
                    arr.SetColorsForIndices(colors, indices);
                    break;
                }
            }
        }

        private static System.Windows.Media.Color FromHsv(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
            (double r, double g, double b) = h switch
            {
                < 60 => (c, x, 0.0),
                < 120 => (x, c, 0.0),
                < 180 => (0.0, c, x),
                < 240 => (0.0, x, c),
                < 300 => (x, 0.0, c),
                _ => (c, 0.0, x),
            };
            return System.Windows.Media.Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }
    }
}
