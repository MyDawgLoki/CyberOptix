using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    /// <summary>
    /// The glowing edge of the CyberOptix window and the widget windows. It is on or off, takes a color from App Settings, and while
    /// "Match the Media Player Color" is on and something is playing it takes the album cover's color instead (the same rule as the dock glow).
    /// Windows ask for the current color with <see cref="Attach"/> and are told whenever it changes.
    /// </summary>
    public static class WindowGlow
    {
        public static event Action? Changed;

        private static readonly DispatcherTimer Watch = new() { Interval = TimeSpan.FromSeconds(1) };
        private static string _last = "";
        private static int _users;

        /// <summary>The color the glow has right now, or null when the glow is off.</summary>
        public static Color? Current
        {
            get
            {
                var s = AppSettings.Current;
                if (!s.WindowGlow) return null;
                if (MediaColorService.ForGlow is Color m) return Color.FromRgb(m.R, m.G, m.B);
                return ThemeService.TryParse(s.WindowGlowColor, out var c) ? Color.FromRgb(c.R, c.G, c.B) : Color.FromRgb(0x14, 0xB8, 0xD4);
            }
        }

        /// <summary>Call after the glow settings change: every window redraws its edge.</summary>
        public static void Refresh()
        {
            _last = Key();
            Changed?.Invoke();
        }

        private static string Key() => Current is Color c ? $"{c.R:X2}{c.G:X2}{c.B:X2}" : "off";

        /// <summary>
        /// Calls <paramref name="apply"/> now and again every time the glow changes, until the window is closed. The media color changes by
        /// itself, so a once-a-second check looks for that while any window is listening.
        /// </summary>
        public static void Attach(Window window, Action<Color?> apply)
        {
            void Handler() => apply(Current);
            Changed += Handler;
            if (_users++ == 0) { _last = Key(); Watch.Tick += OnTick; Watch.Start(); }
            window.Closed += (_, _) =>
            {
                Changed -= Handler;
                if (--_users <= 0) { _users = 0; Watch.Stop(); Watch.Tick -= OnTick; }
            };
            Handler();
        }

        private static void OnTick(object? s, EventArgs e)
        {
            var key = Key();
            if (key == _last) return;
            _last = key;
            Changed?.Invoke();
        }

        /// <summary>
        /// Puts the glow round a card that floats in a transparent window (it needs free space round it for the glow to fall on). With no color it
        /// gives the card back its normal edge: <paramref name="original"/> is the effect it had (a plain shadow, or none).
        /// </summary>
        public static void ApplyOuter(Border card, Color? color, Effect? original, string strokeKey)
        {
            if (color is Color c)
            {
                card.BorderBrush = new SolidColorBrush(c);
                card.Effect = new DropShadowEffect { Color = c, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.9 };
            }
            else
            {
                card.SetResourceReference(Border.BorderBrushProperty, strokeKey);
                card.Effect = original;
            }
        }

        /// <summary>
        /// A glowing ring just inside the edge of a window whose frame is the window itself (the main CyberOptix window). The ring's own shadow
        /// is cut off at the window's edge, so only the glow falling inwards shows.
        /// </summary>
        public static void ApplyInner(Border ring, Color? color)
        {
            if (color is Color c)
            {
                // A thick band mostly outside the window (the clip cuts it off) gives the shadow enough to spill inwards.
                ring.Margin = new Thickness(-5);
                ring.BorderThickness = new Thickness(8);
                ring.BorderBrush = new SolidColorBrush(c);
                ring.Effect = new DropShadowEffect { Color = c, BlurRadius = 26, ShadowDepth = 0, Opacity = 1 };
                ring.Visibility = Visibility.Visible;
            }
            else ring.Visibility = Visibility.Collapsed;
        }
    }
}
