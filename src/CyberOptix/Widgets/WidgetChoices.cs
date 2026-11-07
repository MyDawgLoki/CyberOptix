using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>The desktop widgets you can put on a screen, and the one place that knows how a new one is placed.</summary>
    public static class WidgetChoices
    {
        public static readonly (string Key, string Name, string Glyph)[] All =
        {
            ("clock", "Clock", ""),
            ("temps", "Temperatures", ""),
            ("cpu", "CPU", ""),
            ("gpu", "GPU", ""),
            ("weather", "Weather", ""),
            ("media", "Media controls", ""),
        };

        /// <summary>About how big each widget is on screen (width, height), so the map can draw it at its real size.</summary>
        public static (double W, double H) SizeOf(string key) => key switch
        {
            "clock" => (260, 120),
            "weather" => (340, 210),
            "media" => (360, 250),
            _ => (340, 230),
        };

        public static string NameOf(string key) => All.FirstOrDefault(w => w.Key == key).Name ?? key;

        /// <summary>
        /// Puts a widget on a screen in a free-ish spot near its top-left corner (each widget already there nudges the next one along),
        /// or on the main screen when none is given. <paramref name="dpiScale"/> turns screen pixels into the units widgets are placed in.
        /// </summary>
        public static void Add(string key, DisplayInfo? screen, double dpiScale)
        {
            var displays = DisplayService.GetDisplays();
            screen ??= displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault();
            if (screen == null) { WidgetManager.SetEnabled(key, true); WidgetManager.Apply(); return; }

            int already = All.Count(w => w.Key != key && WidgetManager.IsEnabled(w.Key) && ScreenOf(w.Key, displays, dpiScale)?.Id == screen.Id);
            double px = screen.X + (40 + 36 * already) * dpiScale;
            double py = screen.Y + (80 + 36 * already) * dpiScale;
            WidgetManager.Place(key, px / dpiScale, py / dpiScale);
        }

        /// <summary>The screen a widget sits on, judged by its top-left corner (the main screen if it has no saved place).</summary>
        public static DisplayInfo? ScreenOf(string key, List<DisplayInfo> displays, double dpiScale)
        {
            var main = displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault();
            if (!CyberOptix.Models.AppSettings.Current.Positions.TryGetValue(key, out var p) || p.Length < 2) return main;
            double px = p[0] * dpiScale, py = p[1] * dpiScale;
            return displays.FirstOrDefault(d => px >= d.X && px < d.X + d.Width && py >= d.Y && py < d.Y + d.Height) ?? main;
        }
    }
}
