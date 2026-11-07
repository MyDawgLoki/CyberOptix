using System;
using System.Windows;
using System.Windows.Media;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    /// <summary>
    /// Applies the user's appearance choices. The XAML refers to these brushes with DynamicResource, so
    /// replacing a brush in Application.Resources recolors every control that uses it, live.
    /// </summary>
    public static class ThemeService
    {
        public static readonly (string Name, string Hex)[] Accents =
        {
            ("Violet", "#B146C2"), ("Indigo", "#6C63FF"), ("Blue", "#3B82F6"), ("Turquoise", "#14B8D4"),
            ("Green", "#22C55E"), ("Amber", "#F59E0B"), ("Rose", "#F43F5E"), ("Pink", "#EC4899"),
        };

        public static readonly (string Name, string Hex)[] Tints =
        {
            ("Midnight violet", "#130E1F"), ("Graphite", "#17171C"), ("Deep blue", "#0B1426"),
            ("Forest", "#0C1A14"), ("Wine", "#1F0E16"), ("Black", "#000000"),
        };

        /// <summary>The blur styles offered. Windows 10 has no Mica or Acrylic backdrop, so there it is just Solid.</summary>
        public static readonly string[] Backdrops = OsInfo.SupportsBackdrops ? new[] { "Acrylic", "Mica", "Mica Alt", "Solid" } : new[] { "Solid" };

        private static int _lastBackdrop = -1;
        private static bool _backdropOk = true;

        public static bool TryParse(string? hex, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            try
            {
                color = (Color)ColorConverter.ConvertFromString(hex.Trim());
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Apply()
        {
            var s = AppSettings.Current;
            if (!TryParse(s.AccentHex, out var accent)) TryParse("#14B8D4", out accent);
            if (!TryParse(s.TintHex, out var tint)) TryParse("#17171C", out tint);

            // "Match the Media Player Color": while something is playing, these take the color of its album cover.
            if (MediaColorService.ForAccent is Color mediaAccent) accent = mediaAccent;
            if (MediaColorService.ForWindow is Color mediaWindow) tint = MediaColorService.WindowShade(mediaWindow);

            SetBrush("AccentBrush", accent);
            SetBrush("AccentHoverBrush", Shift(accent, +0.07));
            SetBrush("AccentPressedBrush", Shift(accent, -0.08));
            SetBrush("AccentSoftBrush", Color.FromArgb(0x40, accent.R, accent.G, accent.B));
            SetBrush("AccentBorderBrush", Color.FromArgb(0x80, accent.R, accent.G, accent.B));

            // Text on accent surfaces: dark on light accents (amber, green, cyan), white on dark ones.
            SetBrush("OnAccentBrush", Luminance(accent) > 0.18 ? Color.FromRgb(0x14, 0x0E, 0x1C) : Colors.White);

            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            gradient.GradientStops.Add(new GradientStop(Shift(accent, +0.08), 0));
            gradient.GradientStops.Add(new GradientStop(Shift(accent, -0.09), 1));
            Application.Current.Resources["AccentGradient"] = gradient;

            bool solid = s.Backdrop == "Solid";
            byte glass = (byte)Math.Round(Math.Clamp(s.GlassTint, 0.25, 0.95) * 255);
            SetBrush("Tint", Color.FromArgb(solid ? (byte)0xFF : glass, tint.R, tint.G, tint.B));

            if (!TryParse(s.FontHex, out var font)) TryParse("#FFFFFF", out font);
            if (MediaColorService.ForText is Color mediaText) font = MediaColorService.TextShade(mediaText);
            SetBrush("TextBrush", Color.FromRgb(font.R, font.G, font.B));
            SetBrush("TextDimBrush", Color.FromArgb(0xB8, font.R, font.G, font.B));
            SetBrush("TextFaintBrush", Color.FromArgb(0x96, font.R, font.G, font.B));

            if (!TryParse(s.CardHex, out var card)) card = Colors.White;
            double cardOpacity = Math.Clamp(s.CardOpacity, 0, 0.6);
            if (MediaColorService.Active is Color media)   // "Match the Media Player Color": the cover's color, never fainter than a tint you can see
            {
                card = media;
                cardOpacity = Math.Max(cardOpacity, 0.14);
            }
            SetBrush("CardBrush", Color.FromArgb((byte)Math.Round(cardOpacity * 255), card.R, card.G, card.B));

            var popup = Shift(tint, +0.04);
            SetBrush("PopupBrush", Color.FromArgb(0xF2, popup.R, popup.G, popup.B));

            if (Application.Current.MainWindow is MainWindow main)
            {
                ApplyBackgroundImage(main, s);
                main.Root.Opacity = Math.Clamp(s.WindowOpacity, 0.5, 1.0);

                int type = s.Backdrop switch { "Mica" => 2, "Mica Alt" => 4, "Solid" => 1, _ => 3 };
                if (type != _lastBackdrop) // only talk to the window manager when the style actually changed
                {
                    _backdropOk = WindowEffects.TryEnableBackdrop(main, type);
                    _lastBackdrop = type;
                }

                if (!_backdropOk && !solid)
                    main.Root.Background = new SolidColorBrush(Color.FromRgb(tint.R, tint.G, tint.B)); // pre-Windows 11 fallback
                else
                    main.Root.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Tint");
            }
        }

        private static string? _loadedImageKey;

        /// <summary>Shows the chosen background picture (or the CyberOptix wallpaper) behind the app, or nothing when it is off.</summary>
        private static bool _videoFailed;
        private static int _videoRetries;

        /// <summary>Called when the window is shown again: a video that failed earlier gets another chance (a few times at most).</summary>
        public static void RetryVideo()
        {
            if (!_videoFailed || _videoRetries >= 5) return;
            _videoRetries++;
            _videoFailed = false;
            Apply();
        }
        private static bool _videoHooked;

        private static void ApplyBackgroundImage(MainWindow main, AppSettings s)
        {
            double opacity = Math.Clamp(s.BackgroundImageOpacity, 0.1, 1.0);
            main.imgBackdrop.Opacity = opacity;
            main.bgVideo.Opacity = opacity;

            // The default wallpaper is a silent looping video. A picture you chose yourself, or a PC that cannot play the video, gets a still picture.
            if (!_videoHooked)
            {
                _videoHooked = true;
                main.bgVideo.Failed += () => { _videoFailed = true; BackgroundVideo.Log("falling back to the still picture"); Apply(); };
            }
            string? video = s.BackgroundImage && s.BackgroundVideo && string.IsNullOrWhiteSpace(s.BackgroundImagePath) && !_videoFailed
                ? BackgroundVideo.EnsureFile() : null;
            if (video != null)
            {
                main.imgBackdrop.Visibility = Visibility.Collapsed;
                main.bgVideo.Visibility = Visibility.Visible;
                main.bgVideo.Load(video);
                main.UpdateBackgroundVideo();
                return;
            }
            main.bgVideo.SetActive(false);
            main.bgVideo.Visibility = Visibility.Collapsed;

            main.imgBackdrop.Visibility = s.BackgroundImage ? Visibility.Visible : Visibility.Collapsed;
            if (!s.BackgroundImage) return;

            string key = string.IsNullOrWhiteSpace(s.BackgroundImagePath) ? "default" : s.BackgroundImagePath!;
            if (key == _loadedImageKey) return; // only decode again when the picture changed

            try
            {
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 1920;
                image.UriSource = key == "default"
                    ? new Uri("pack://application:,,,/Assets/CyberOptixWallpaper.png")
                    : new Uri(key);
                image.EndInit();
                image.Freeze();
                main.imgBackdrop.Source = image;
                _loadedImageKey = key;
            }
            catch
            {
                // A picture that was moved or deleted: fall back to the built-in wallpaper next time.
                main.imgBackdrop.Source = null;
                _loadedImageKey = null;
            }
        }

        public static void Reset()
        {
            var s = AppSettings.Current;
            s.AccentHex = "#14B8D4";
            s.TintHex = "#0B1426";
            s.GlassTint = 0.65;
            s.WindowOpacity = 1.0;
            s.Backdrop = "Mica";
            s.FontHex = "#F4EFF8";
            s.CardHex = "#FFFFFF";
            s.CardOpacity = 0.08;
            s.BackgroundImage = true;
            s.BackgroundImagePath = null;
            s.BackgroundImageOpacity = 0.34;
            AppSettings.Save();
            Apply();
        }

        /// <summary>WCAG relative luminance, 0 (black) to 1 (white).</summary>
        private static double Luminance(Color c)
        {
            static double Lin(byte v)
            {
                double x = v / 255.0;
                return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        }

        private static void SetBrush(string key, Color color) =>
            Application.Current.Resources[key] = new SolidColorBrush(color);

        /// <summary>Lightens (positive) or darkens (negative) a color by changing its HSL lightness.</summary>
        private static Color Shift(Color c, double delta)
        {
            RgbToHsl(c.R / 255.0, c.G / 255.0, c.B / 255.0, out var h, out var s, out var l);
            l = Math.Clamp(l + delta, 0, 1);
            HslToRgb(h, s, l, out var r, out var g, out var b);
            return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        }

        private static void RgbToHsl(double r, double g, double b, out double h, out double s, out double l)
        {
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2;
            if (max == min) { h = s = 0; return; }
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h /= 6;
        }

        private static void HslToRgb(double h, double s, double l, out double r, out double g, out double b)
        {
            if (s == 0) { r = g = b = l; return; }
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            r = Hue(p, q, h + 1.0 / 3);
            g = Hue(p, q, h);
            b = Hue(p, q, h - 1.0 / 3);
        }

        private static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }
}
