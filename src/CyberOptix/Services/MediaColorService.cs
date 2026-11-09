using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CyberOptix.Models;
using CyberOptix.Widgets;

namespace CyberOptix.Services
{
    /// <summary>
    /// Follows what is playing (Spotify, iTunes, a browser tab...) for two things:
    ///  - "Match the Media Player Color": the accent, window, text, glass card, window glow and dock glow colors each (with a switch of its own) take the color of the album cover or thumbnail.
    ///    Nothing changes at launch: the colors in Settings stay until something actually starts playing. They go back when the media session ends.
    ///  - "Show the media player when something plays": the desktop media widget appears by itself when playback starts, until you close it.
    /// It listens to Windows' media session only while one of those is switched on.
    /// </summary>
    public static class MediaColorService
    {
        /// <summary>The color of what is playing, once something has started playing; null otherwise (before the first play, or when the session ends).</summary>
        public static Color? Current { get; private set; }

        /// <summary>The color the glass cards take, or null to use the one chosen in Settings.</summary>
        public static Color? Active => AppSettings.Current.MatchMediaColor ? Current : null;
        /// <summary>The color the glowing edge of the window and widgets takes (its own switch), or null to use the glow color chosen in Settings.</summary>
        public static Color? ForGlow => AppSettings.Current.MatchMediaGlow ? Current : null;
        /// <summary>The color the dock's border glow takes (its own switch), or null to use the dock glow color chosen on the Dock page.</summary>
        public static Color? ForDock => AppSettings.Current.DockMatchMedia ? Current : null;
        public static Color? ForAccent => AppSettings.Current.MatchMediaAccent ? Current : null;
        public static Color? ForWindow => AppSettings.Current.MatchMediaWindow ? Current : null;
        public static Color? ForText => AppSettings.Current.MatchMediaText ? Current : null;
        /// <summary>The color Windows' own accent color takes (its own switch, which only counts while the Windows color match is on).</summary>
        public static Color? ForWindows => AppSettings.Current.SyncWindowsAccent && AppSettings.Current.WinMatchMedia ? Current : null;

        private static bool _running;
        private static bool _armed;          // something has started playing since launch (or since the last session ended)
        private static string _lastKey = "";

        /// <summary>Starts or stops following the media player to match the settings. Safe to call whenever one of them changes.</summary>
        public static void Sync()
        {
            var s = AppSettings.Current;
            bool want = s.MatchMediaColor || s.MatchMediaGlow || s.DockMatchMedia || s.MatchMediaAccent || s.MatchMediaWindow || s.MatchMediaText || s.MediaWidgetAutoShow || s.DesktopClockColorMode == "Media" || (s.SyncWindowsAccent && s.WinMatchMedia);
            if (want && !_running)
            {
                _running = true;
                MediaService.Instance.Updated += OnUpdated;
                MediaService.Instance.Acquire();
            }
            else if (!want && _running)
            {
                _running = false;
                MediaService.Instance.Updated -= OnUpdated;
                MediaService.Instance.Release();
                _armed = false;
                _lastKey = "";
                Current = null;
            }
            ThemeService.Apply();
        }

        private static void OnUpdated(MediaSnapshot? s)
        {
            // Raised on a background thread. The art is frozen, so reading it here is fine; only touching windows needs the UI thread.
            bool has = s != null && !string.IsNullOrWhiteSpace(s.Title);
            if (!has)
            {
                // The session ended: the colors go back to the ones in Settings, and the next play starts it all again.
                _armed = false;
                _lastKey = "";
                SetCurrent(null);
                return;
            }

            if (s!.IsPlaying)
            {
                _armed = true;
                ShowWidgetIfWanted();
            }
            if (!_armed) return;                       // paused or idle since launch: the Settings colors stay

            Color? color = null;
            string key = "";
            if (s.Thumbnail is BitmapSource art)
            {
                key = s.Title + "|" + s.Artist + "|" + s.AlbumTitle;
                if (key == _lastKey && Current != null) return;   // same track, same cover: nothing to redo
                color = Dominant(art);
            }
            _lastKey = key;
            SetCurrent(color);
        }

        private static void SetCurrent(Color? color)
        {
            if (color == Current) return;
            Current = color;
            Application.Current?.Dispatcher.BeginInvoke(new Action(ThemeService.Apply));
            if (AppSettings.Current.SyncWindowsAccent && AppSettings.Current.WinMatchMedia)
                System.Threading.Tasks.Task.Run(WindowsAccentService.Refresh);   // registry writes and a broadcast: not on the UI thread
        }

        /// <summary>Puts the media player on the desktop when playback starts, unless the user closed it (that switches this off) or it is already there.</summary>
        private static void ShowWidgetIfWanted()
        {
            var s = AppSettings.Current;
            if (!s.MediaWidgetAutoShow || WidgetManager.IsEnabled("media")) return;
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!AppSettings.Current.MediaWidgetAutoShow || WidgetManager.IsEnabled("media")) return;
                WidgetManager.SetEnabled("media", true);
                WidgetManager.Apply();
            }));
        }

        /// <summary>A dark shade of the color for the window background: its hue, deep and a little tinted, so the text on it stays readable.</summary>
        public static Color WindowShade(Color c)
        {
            Hsv(c.R / 255.0, c.G / 255.0, c.B / 255.0, out double h, out double s, out _);
            return FromHsv(h, Math.Clamp(s, 0.25, 0.6), 0.16);
        }

        /// <summary>A pale shade of the color for text: nearly white, with just a tint of its hue.</summary>
        public static Color TextShade(Color c)
        {
            Hsv(c.R / 255.0, c.G / 255.0, c.B / 255.0, out double h, out double s, out _);
            return FromHsv(h, Math.Min(s, 0.14), 1.0);
        }

        /// <summary>
        /// The main color of a picture: the most colorful hue (weighted by how vivid and bright its pixels are), lifted to a clear, bright shade
        /// so it reads well on dark glass. A picture with no color in it (black and white art) gives its average shade, or null if that is nearly black.
        /// </summary>
        public static Color? Dominant(BitmapSource source)
        {
            try
            {
                const int size = 32;
                var scaled = new TransformedBitmap(source, new ScaleTransform(size / (double)source.PixelWidth, size / (double)source.PixelHeight));
                var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
                int w = bgra.PixelWidth, h = bgra.PixelHeight;
                var px = new byte[w * h * 4];
                bgra.CopyPixels(px, w * 4, 0);

                const int bins = 12;
                var weight = new double[bins];
                var sr = new double[bins]; var sg = new double[bins]; var sb = new double[bins];
                double ar = 0, ag = 0, ab = 0; int count = 0;

                for (int i = 0; i < px.Length; i += 4)
                {
                    if (px[i + 3] < 128) continue;
                    double b = px[i] / 255.0, g = px[i + 1] / 255.0, r = px[i + 2] / 255.0;
                    ar += r; ag += g; ab += b; count++;
                    Hsv(r, g, b, out double hue, out double sat, out double val);
                    if (val < 0.2 || sat < 0.25) continue;      // dark and gray pixels have no color to give
                    int bin = Math.Min(bins - 1, (int)(hue / (360.0 / bins)));
                    double wt = sat * val;
                    weight[bin] += wt; sr[bin] += r * wt; sg[bin] += g * wt; sb[bin] += b * wt;
                }
                if (count == 0) return null;

                int best = 0;
                for (int i = 1; i < bins; i++) if (weight[i] > weight[best]) best = i;

                double rr, gg, bb; bool vivid = weight[best] >= count * 0.02;
                if (vivid) { rr = sr[best] / weight[best]; gg = sg[best] / weight[best]; bb = sb[best] / weight[best]; }
                else { rr = ar / count; gg = ag / count; bb = ab / count; }

                Hsv(rr, gg, bb, out double hh, out double ss, out double vv);
                if (!vivid && vv < 0.12) return null;            // an all-black cover: leave the colors alone
                if (vivid) { ss = Math.Clamp(ss, 0.55, 0.9); vv = Math.Clamp(vv, 0.72, 1.0); }
                else { ss = Math.Min(ss, 0.2); vv = Math.Clamp(vv, 0.58, 0.9); }
                return FromHsv(hh, ss, vv);
            }
            catch { return null; }
        }

        private static void Hsv(double r, double g, double b, out double h, out double s, out double v)
        {
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            v = max;
            s = max <= 0 ? 0 : d / max;
            if (d <= 0) h = 0;
            else if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }

        private static Color FromHsv(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
            (double r, double g, double b) = h switch
            {
                < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x),
                < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x),
            };
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }
    }
}
