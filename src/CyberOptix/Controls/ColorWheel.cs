using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using CyberOptix.Services;

namespace CyberOptix.Controls
{
    /// <summary>
    /// A color picker: a wheel for the hue (round the circle) and the strength of the color (out from the middle), a brightness slider,
    /// the chosen color with its #RRGGBB code (which can also be typed), and a Reset button.
    /// <see cref="ColorCommitted"/> is raised when a choice is finished (the mouse is let go, the code is entered, Reset), not on every pixel of a drag.
    /// </summary>
    public class ColorWheel : UserControl
    {
        private const double Size = 150;            // the wheel, in device-independent pixels
        private const int Pixels = 300;             // the wheel's picture is drawn at twice that, so it stays sharp

        private double _h, _s = 1, _v = 1;          // hue 0..360, strength 0..1, brightness 0..1
        private bool _quiet;                        // true while the controls are being set from code
        private readonly Ellipse _dim = new();
        private readonly Ellipse _thumb = new();
        private readonly Slider _slider = new() { Minimum = 0, Maximum = 100, Value = 100, Width = Size, HorizontalAlignment = HorizontalAlignment.Left, SmallChange = 1, LargeChange = 10 };
        private readonly TextBlock _brightness = new() { Margin = new Thickness(0, 10, 0, 2) };
        private readonly Border _swatch = new() { Width = 34, Height = 26, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _hex = new() { Width = 92, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(220 ) };

        /// <summary>The color this picker goes back to when Reset is pressed.</summary>
        public string DefaultHex { get; set; } = "#14B8D4";

        /// <summary>Raised while a choice is being made (dragging on the wheel or the slider), so a preview can follow.</summary>
        public event Action<string>? ColorChanged;

        /// <summary>Raised when a choice is finished.</summary>
        public event Action<string>? ColorCommitted;

        /// <summary>The chosen color as #RRGGBB. Setting it moves the wheel and the slider without raising an event.</summary>
        public string Hex
        {
            get => ToHex(Current);
            set
            {
                if (!ThemeService.TryParse(value, out var c)) return;
                ToHsv(c, out _h, out _s, out _v);
                _quiet = true;
                Refresh();
                _quiet = false;
            }
        }

        private Color Current => FromHsv(_h, _s, _v);

        public ColorWheel()
        {
            var root = new StackPanel { Width = Size + 30, HorizontalAlignment = HorizontalAlignment.Left };

            // the wheel
            var host = new Grid { Width = Size, Height = Size, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, Cursor = Cursors.Cross };
            var picture = new Image { Source = MakeWheel(), Width = Size, Height = Size, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            _dim.Width = _dim.Height = Size; _dim.Fill = Brushes.Black; _dim.IsHitTestVisible = false;   // darkens the wheel as the brightness comes down
            _thumb.Width = _thumb.Height = 14; _thumb.Stroke = Brushes.Black; _thumb.StrokeThickness = 2; _thumb.Fill = Brushes.Transparent;
            _thumb.HorizontalAlignment = HorizontalAlignment.Left; _thumb.VerticalAlignment = VerticalAlignment.Top; _thumb.IsHitTestVisible = false;
            host.Children.Add(picture); host.Children.Add(_dim); host.Children.Add(_thumb);
            host.MouseLeftButtonDown += (_, e) => { host.CaptureMouse(); FromPoint(e.GetPosition(host)); e.Handled = true; };
            host.MouseMove += (_, e) => { if (host.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) FromPoint(e.GetPosition(host)); };
            host.MouseLeftButtonUp += (_, _) => { if (!host.IsMouseCaptured) return; host.ReleaseMouseCapture(); Commit(); };
            root.Children.Add(host);

            // brightness
            _brightness.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            root.Children.Add(_brightness);
            _slider.ValueChanged += (_, _) =>
            {
                if (_quiet) return;
                _v = _slider.Value / 100.0;
                Refresh(raise: true);
                _settle.Stop(); _settle.Start();   // commit shortly after the slider stops moving
            };
            _settle.Tick += (_, _) => { _settle.Stop(); Commit(); };
            root.Children.Add(_slider);

            // the color, its code and Reset
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            _swatch.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
            row.Children.Add(_swatch);
            _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyTyped(); };
            _hex.LostFocus += (_, _) => ApplyTyped();
            row.Children.Add(_hex);
            var reset = new Button { Content = "Reset", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            reset.Click += (_, _) => { Hex = DefaultHex; Commit(); };
            row.Children.Add(reset);
            root.Children.Add(row);

            Content = root;
            Hex = DefaultHex;
        }

        private void FromPoint(Point p)
        {
            double r = Size / 2, dx = p.X - r, dy = p.Y - r;
            _s = Math.Min(1, Math.Sqrt(dx * dx + dy * dy) / r);
            _h = (Math.Atan2(-dy, dx) * 180 / Math.PI + 360) % 360;   // red at the right, going round the other way from a clock
            Refresh(raise: true);
        }

        private void ApplyTyped()
        {
            var text = _hex.Text.Trim();
            if (!text.StartsWith('#')) text = "#" + text;
            if (!ThemeService.TryParse(text, out var c) || text.Length is not (7 or 9)) { _hex.Text = ToHex(Current); return; }   // not a color: keep the one it had
            string before = ToHex(Current);
            ToHsv(c, out _h, out _s, out _v);
            _quiet = true; Refresh(); _quiet = false;
            if (ToHex(Current) != before) Commit();
        }

        private void Commit() => ColorCommitted?.Invoke(ToHex(Current));

        /// <summary>Moves the thumb, the slider, the swatch and the code to match the color.</summary>
        private void Refresh(bool raise = false)
        {
            double r = Size / 2;
            double x = r + _s * r * Math.Cos(_h * Math.PI / 180), y = r - _s * r * Math.Sin(_h * Math.PI / 180);
            _thumb.Margin = new Thickness(x - 7, y - 7, 0, 0);
            _dim.Opacity = 1 - _v;
            bool was = _quiet; _quiet = true;
            _slider.Value = Math.Round(_v * 100);
            _quiet = was;
            _brightness.Text = $"Brightness: {Math.Round(_v * 100):0}%";
            var c = Current;
            _swatch.Background = new SolidColorBrush(c);
            if (!_hex.IsKeyboardFocused) _hex.Text = ToHex(c);
            if (raise) ColorChanged?.Invoke(ToHex(c));
        }

        // ---------- colors ----------

        private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static void ToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            v = max; s = max <= 0 ? 0 : d / max;
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

        /// <summary>The wheel's picture: every hue round the circle, fading to white in the middle.</summary>
        private static BitmapSource MakeWheel()
        {
            var px = new byte[Pixels * Pixels * 4];
            double R = Pixels / 2.0;
            for (int y = 0; y < Pixels; y++)
                for (int x = 0; x < Pixels; x++)
                {
                    double dx = x + 0.5 - R, dy = y + 0.5 - R, d = Math.Sqrt(dx * dx + dy * dy);
                    double edge = Math.Clamp(R - d, 0, 1);            // a soft one-pixel edge
                    if (edge <= 0) continue;
                    double h = (Math.Atan2(-dy, dx) * 180 / Math.PI + 360) % 360;
                    var c = FromHsv(h, Math.Min(1, d / R), 1.0);
                    int i = (y * Pixels + x) * 4;
                    byte a = (byte)Math.Round(edge * 255);
                    px[i] = (byte)(c.B * a / 255); px[i + 1] = (byte)(c.G * a / 255); px[i + 2] = (byte)(c.R * a / 255); px[i + 3] = a;   // premultiplied
                }
            var bmp = BitmapSource.Create(Pixels, Pixels, 96, 96, PixelFormats.Pbgra32, null, px, Pixels * 4);
            bmp.Freeze();
            return bmp;
        }
    }
}
