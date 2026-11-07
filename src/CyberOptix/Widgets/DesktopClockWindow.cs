using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>
    /// The Desktop Clock: the time with the date under it, drawn straight on the wallpaper (no card, no frame). It is click-through, so it never
    /// gets in the way of the desktop, and sits behind every window. Its look comes from the Display page.
    /// </summary>
    public class DesktopClockWindow : Window
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr h, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private readonly TextBlock _time = new() { TextWrapping = TextWrapping.NoWrap };
        private readonly TextBlock _date = new() { TextWrapping = TextWrapping.NoWrap };
        private readonly StackPanel _panel = new();
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private string _shown = "";
        private double _iconRoom;

        /// <summary>Raised after the clock was redrawn or moved, so the weather icon beside it can follow.</summary>
        public event Action? Redrawn;
        public event Action? Placed;

        /// <summary>Where the weather icon goes, in screen pixels: the room kept on the clock's left, and the top of the clock's digits. Null when there is none.</summary>
        public (int X, int Width, int TopY)? IconSpot { get; private set; }

        public DesktopClockWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Topmost = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -20000; Top = -20000;   // placed properly once its size is known
            _panel.Children.Add(_time);
            _panel.Children.Add(_date);
            _panel.Margin = new Thickness(24);   // the left side grows when the weather icon is on (see Refresh)
            Content = _panel;

            SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                // tool window (not in Alt+Tab), no-activate, and transparent to the mouse so every click goes to the desktop under it
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80 | 0x08000000 | 0x20);
                Place();
            };
            SizeChanged += (_, _) => Place();
            DpiChanged += (_, _) => Place();
            ZOrderSettle.Run(this, Place);   // a new window starts on top: put it behind the others once it is shown
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaysChanged;
            Closed += (_, _) => { _timer.Stop(); Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaysChanged; };

            _timer.Tick += (_, _) => Refresh(false);
            _timer.Start();
            Refresh(true);
        }

        private void DisplaysChanged(object? s, EventArgs e) => Dispatcher.BeginInvoke(new Action(Place));

        /// <summary>Re-reads every option and redraws. Called when a setting changes and about twice a second for the time.</summary>
        public void Refresh(bool force)
        {
            var s = AppSettings.Current;
            var theme = DesktopClockThemes.Find(s.DesktopClockTheme);
            var now = Now();
            var (main, sec, ampm) = DesktopClockThemes.Time(theme, now, s.DesktopClock24h, s.DesktopClockSeconds, s.DesktopClockShowAmPm);
            string date = s.DesktopClockShowDate ? DesktopClockThemes.Date(theme, now) : "";

            var brush = ColorBrush(s);
            string look = $"{theme.Name}|{s.DesktopClockScale}|{s.DesktopClockAnchor}|{s.DesktopClockGlow}|{s.DesktopClockOpacity}|{s.DesktopClockDisplay}|{s.DesktopClock24h}|{s.DesktopClockSeconds}|{s.DesktopClockShowAmPm}|{s.DesktopClockShowDate}|{s.DesktopClockWeather}";
            string key = look + "|" + main + sec + ampm + date + "|" + (brush as SolidColorBrush)?.Color;
            if (!force && key == _shown) return;
            bool relayout = !_shown.StartsWith(look + "|");
            _shown = key;

            double scale = Math.Clamp(s.DesktopClockScale, 0.4, 3.0);
            double iconRoom = s.DesktopClockWeather ? IconSize(theme, scale) * 1.15 + 10 : 0;   // free space on the left, where the weather icon sits
            _panel.Margin = new Thickness(24 + iconRoom, 24, 24, 24);
            _iconRoom = iconRoom;
            var family = new FontFamily(theme.Font);
            var align = s.DesktopClockAnchor.EndsWith("Left") ? TextAlignment.Left
                      : s.DesktopClockAnchor.EndsWith("Right") ? TextAlignment.Right : TextAlignment.Center;
            var hAlign = align == TextAlignment.Left ? HorizontalAlignment.Left : align == TextAlignment.Right ? HorizontalAlignment.Right : HorizontalAlignment.Center;

            _time.Inlines.Clear();
            _time.FontFamily = family;
            _time.FontSize = theme.TimeSize * scale;
            _time.FontWeight = theme.TimeBold ? FontWeights.Bold : theme.TimeLight ? FontWeights.Light : FontWeights.Normal;
            if (theme.Name == "Bold") _time.FontWeight = FontWeights.Black;
            _time.TextAlignment = align;
            _time.LineHeight = theme.Stacked ? theme.TimeSize * scale * 0.9 : double.NaN;
            _time.LineStackingStrategy = theme.Stacked ? LineStackingStrategy.BlockLineHeight : LineStackingStrategy.MaxHeight;
            _time.Inlines.Add(new Run(main));
            if (sec.Length > 0) AddSeconds(theme, scale, family, sec, brush);
            if (ampm.Length > 0)
                _time.Inlines.Add(new Run(" " + ampm) { FontSize = theme.TimeSize * scale * 0.3, FontWeight = FontWeights.SemiBold });
            _time.Foreground = brush;
            _time.HorizontalAlignment = hAlign;

            _date.Text = date;
            _date.Visibility = date.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            _date.FontFamily = family;
            _date.FontSize = theme.DateSize * scale;
            _date.FontStyle = theme.DateItalic ? FontStyles.Italic : FontStyles.Normal;
            _date.FontWeight = FontWeights.Normal;
            _date.TextAlignment = align;
            _date.HorizontalAlignment = hAlign;
            _date.Foreground = brush;
            _date.Opacity = 0.82;
            _date.Margin = new Thickness(0, theme.Stacked ? 8 * scale : -2 * scale, 0, 0);

            Opacity = Math.Clamp(s.DesktopClockOpacity, 0.1, 1);
            if (s.DesktopClockGlow)
            {
                bool neon = theme.GlowStrength > 1.5;
                var color = neon && brush is SolidColorBrush sb ? sb.Color : Colors.Black;
                _panel.Effect = new DropShadowEffect
                {
                    Color = color,
                    BlurRadius = (neon ? 26 : 14) * scale * Math.Max(0.6, theme.GlowStrength / 1.5 + 0.4),
                    ShadowDepth = neon ? 0 : 2,
                    Opacity = neon ? 0.95 : 0.65,
                };
            }
            else _panel.Effect = null;

            if (relayout) { UpdateLayout(); Place(); }
            Redrawn?.Invoke();
        }

        /// <summary>The time the clock shows (the real time; tests set their own).</summary>
        private static Func<DateTime> Now = () => DateTime.Now;

        // ---------- seconds that never move the clock ----------

        private static readonly System.Collections.Generic.Dictionary<string, (double Max, double Min)> SecondWidths = new();

        /// <summary>The widest and the narrowest the two seconds digits (00 to 59) get in a font, with or without equal-width digits.</summary>
        private static (double Max, double Min) MeasureSeconds(FontFamily family, double size, FontWeight weight, bool tabular)
        {
            string key = $"{family.Source}|{Math.Round(size, 1)}|{weight}|{tabular}";
            if (SecondWidths.TryGetValue(key, out var known)) return known;
            double max = 0, min = double.MaxValue;
            for (int n = 0; n < 60; n++)
            {
                var box = new TextBlock { Text = n.ToString("00"), FontFamily = family, FontSize = size, FontWeight = weight };
                if (tabular) System.Windows.Documents.Typography.SetNumeralAlignment(box, FontNumeralAlignment.Tabular);
                box.Measure(new Size(10000, 10000));
                max = Math.Max(max, box.DesiredSize.Width);
                min = Math.Min(min, box.DesiredSize.Width);
            }
            return SecondWidths[key] = (max, min);
        }

        /// <summary>
        /// Adds the seconds so that they always take the same room: the clock then only changes size when the hour or the minute does, never as
        /// the seconds tick. Equal-width digits do it where the font has them; otherwise the two digits sit in a box as wide as the widest pair.
        /// </summary>
        private void AddSeconds(ClockTheme theme, double scale, FontFamily family, string sec, Brush brush)
        {
            double size = theme.TimeSize * scale * (theme.Digital ? 1 : 0.42);
            string digits = sec.Substring(sec.Length - 2), lead = (theme.Stacked ? "\n" : theme.Digital ? "" : " ") + sec.Substring(0, sec.Length - 2);
            if (lead.Length > 0) _time.Inlines.Add(new Run(lead) { FontSize = size, Foreground = brush, BaselineAlignment = BaselineAlignment.Baseline });

            var weight = _time.FontWeight;
            var tab = MeasureSeconds(family, size, weight, true);
            if (tab.Max - tab.Min < 0.6)
            {
                var run = new Run(digits) { FontSize = size, Foreground = brush, BaselineAlignment = BaselineAlignment.Baseline };
                System.Windows.Documents.Typography.SetNumeralAlignment(run, FontNumeralAlignment.Tabular);
                _time.Inlines.Add(run);
                return;
            }

            // proportional digits (a serif font, say): a fixed-width box, lowered so its text sits on the same line as the rest
            var plain = MeasureSeconds(family, size, weight, false);
            double descent = Math.Max(0, (family.LineSpacing - family.Baseline) * size);
            var text = new TextBlock
            {
                Text = digits, FontFamily = family, FontSize = size, FontWeight = weight, Foreground = brush,
                Width = Math.Ceiling(plain.Max) + 1, TextAlignment = TextAlignment.Left, Margin = new Thickness(0, 0, 0, -descent),
            };
            _time.Inlines.Add(new InlineUIContainer(text) { BaselineAlignment = BaselineAlignment.Baseline });
        }

        /// <summary>How far below the top of the time's text box the tops of its digits are (device-independent units), measured from the font in use.</summary>
        private double DigitTop() =>
            InkMeasure.TopOf("0", _time.FontFamily, _time.FontSize, _time.FontWeight, double.IsNaN(_time.LineHeight) ? double.NaN : _time.LineHeight);

        /// <summary>The size of the weather icon beside the clock, from the theme and size settings.</summary>
        public static double IconSize(ClockTheme theme, double scale) => Math.Max(18, theme.TimeSize * scale * 0.62);

        /// <summary>The color the clock is drawn in (also used by the weather icon beside it).</summary>
        public static Brush ColorFor(AppSettings s) => ColorBrush(s);

        private static Brush ColorBrush(AppSettings s)
        {
            Color? c = null;
            switch (s.DesktopClockColorMode)
            {
                case "Accent":
                    if (Application.Current?.TryFindResource("AccentBrush") is SolidColorBrush a) c = a.Color;
                    break;
                case "Media":
                    c = MediaColorService.Current;
                    if (c == null && Application.Current?.TryFindResource("AccentBrush") is SolidColorBrush a2) c = a2.Color;
                    break;
                case "Custom":
                    if (ThemeService.TryParse(s.DesktopClockColor, out var custom)) c = custom;
                    break;
                default:
                    if (Application.Current?.TryFindResource("TextBrush") is SolidColorBrush t) c = t.Color;
                    break;
            }
            var brush = new SolidColorBrush(c ?? Colors.White);
            brush.Freeze();
            return brush;
        }

        /// <summary>Puts the clock at its anchor on the chosen display, in screen pixels (so mixed-DPI setups land right).</summary>
        public void Place()
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            var displays = DisplayService.GetDisplays();
            if (displays.Count == 0) return;
            var s = AppSettings.Current;
            var d = displays.FirstOrDefault(x => x.Id == s.DesktopClockDisplay) ?? displays.FirstOrDefault(x => x.IsPrimary) ?? displays[0];

            var dpi = VisualTreeHelper.GetDpi(this);
            int w = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), ht = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
            if (w <= 0 || ht <= 0) return;
            int margin = (int)(40 * dpi.DpiScaleX);
            string a = s.DesktopClockAnchor;
            int x = a.EndsWith("Left") ? d.X + margin : a.EndsWith("Right") ? d.X + d.Width - w - margin : d.X + (d.Width - w) / 2;
            int y = a.StartsWith("Top") ? d.Y + margin + (int)(40 * dpi.DpiScaleY) : a.StartsWith("Bottom") ? d.Y + d.Height - ht - margin : d.Y + (d.Height - ht) / 2;
            if (s.DesktopClockCustom)
            {
                // A spot picked by dragging the clock on the Display page: its middle sits there, kept fully on the screen.
                x = (int)Math.Clamp(d.X + s.DesktopClockX * d.Width - w / 2.0, d.X, Math.Max(d.X, d.X + d.Width - w));
                y = (int)Math.Clamp(d.Y + s.DesktopClockY * d.Height - ht / 2.0, d.Y, Math.Max(d.Y, d.Y + d.Height - ht));
            }
            // HWND_BOTTOM (behind every window), NOSIZE | NOACTIVATE
            SetWindowPos(h, (IntPtr)1, x, y, 0, 0, 0x0001 | 0x0010);

            if (_iconRoom > 0)
            {
                // The room on the left is at the clock's left margin; the icon lines up with the top of the digits.
                int room = (int)Math.Round(_iconRoom * dpi.DpiScaleX);
                int topY = y + (int)Math.Round((24 + DigitTop()) * dpi.DpiScaleY);
                IconSpot = (x + (int)Math.Round(24 * dpi.DpiScaleX), room, topY);
            }
            else IconSpot = null;
            Placed?.Invoke();
        }
    }
}
