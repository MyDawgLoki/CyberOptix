using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>
    /// A weather icon (and the temperature) on the left of the desktop clock. The icon follows the local weather; clicking it opens the
    /// weather forecast widget, and clicking it again closes it. The clock keeps room for it on its left, so it sits against the clock wherever
    /// the clock is placed. It is a window of its own because the clock itself is click-through.
    /// </summary>
    public class ClockWeatherIconWindow : Window
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr h, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private readonly DesktopClockWindow _clock;
        private readonly TextBlock _icon = new() { HorizontalAlignment = HorizontalAlignment.Center, FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol") };
        private readonly TextBlock _temp = new() { HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold };
        private readonly StackPanel _panel = new() { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(20) };
        private WeatherInfo? _weather;
        private string _key = "";
        private bool _busy;

        public ClockWeatherIconWindow(DesktopClockWindow clock)
        {
            _clock = clock;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Topmost = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -20000; Top = -20000;

            _panel.Children.Add(_icon);
            _panel.Children.Add(_temp);
            Content = _panel;

            SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80 | 0x08000000);   // tool window, never takes focus
                Place();
            };
            SizeChanged += (_, _) => Place();
            ZOrderSettle.Run(this, Place);   // a new window starts on top: put it behind the others once it is shown
            _panel.MouseLeftButtonUp += (_, _) => Open();

            clock.Redrawn += Restyle;
            clock.Placed += Place;
            _timer.Tick += (_, _) => _ = LoadAsync(true);
            Loaded += (_, _) => { _timer.Start(); Restyle(); _ = LoadAsync(false); };
            Closed += (_, _) => { _timer.Stop(); clock.Redrawn -= Restyle; clock.Placed -= Place; };
        }

        /// <summary>Clicking opens the forecast widget; clicking again closes it.</summary>
        private static void Open()
        {
            bool on = WidgetManager.IsEnabled("weather");
            WidgetManager.SetEnabled("weather", !on);
            WidgetManager.Apply();
        }

        /// <summary>Looks the weather up again when the city or units changed, and every 20 minutes.</summary>
        public void Reload() => _ = LoadAsync(false);

        private async System.Threading.Tasks.Task LoadAsync(bool force)
        {
            var s = AppSettings.Current;
            string key = s.City + "|" + s.Fahrenheit;
            if (_busy || (!force && key == _key && _weather != null)) return;
            _busy = true;
            try
            {
                var w = await WeatherService.GetAsync(s.City, s.Fahrenheit);
                _weather = w;
                _key = key;
            }
            catch
            {
                if (key != _key) _weather = null;   // old data for another city would be wrong
            }
            finally { _busy = false; Restyle(); }
        }

        /// <summary>Draws the icon in the clock's color and size, with the current weather.</summary>
        private void Restyle()
        {
            var s = AppSettings.Current;
            var theme = DesktopClockThemes.Find(s.DesktopClockTheme);
            double scale = Math.Clamp(s.DesktopClockScale, 0.4, 3.0);
            var brush = DesktopClockWindow.ColorFor(s);
            _icon.FontSize = DesktopClockWindow.IconSize(theme, scale);
            _temp.FontSize = Math.Max(10, theme.DateSize * scale * 0.9);
            _icon.Foreground = _temp.Foreground = brush;
            Opacity = Math.Clamp(s.DesktopClockOpacity, 0.1, 1);
            _panel.Effect = s.DesktopClockGlow ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 14 * scale, ShadowDepth = 2, Opacity = 0.65 } : null;

            if (_weather is WeatherInfo w)
            {
                var (icon, text) = WeatherService.Describe(w.Code);
                _icon.Text = icon;
                _temp.Text = $"{Math.Round(w.Temp)}°";
                _panel.ToolTip = $"{w.Place}: {Math.Round(w.Temp)}{(w.Fahrenheit ? "°F" : "°C")}, {text}. Click for the forecast.";
            }
            else
            {
                _icon.Text = "🌡️";
                _temp.Text = "";
                _panel.ToolTip = "Weather. Click for the forecast.";
            }
            Place();
        }

        /// <summary>The empty space between the top of the icon's text box and the top of the icon itself, so the icon can be lined up by what you see.</summary>
        private double GlyphTop() => InkMeasure.TopOf(_icon.Text, _icon.FontFamily, _icon.FontSize, FontWeights.Normal);

        /// <summary>Sits at the left end of the clock's time line, in the room the clock keeps for it.</summary>
        public void Place()
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero || ActualWidth < 1 || ActualHeight < 1) return;
            if (_clock.IconSpot is not { } spot) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            int w = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
            int x = spot.X + (spot.Width - w) / 2;      // centered in the room that was kept for it
            int y = spot.TopY - (int)Math.Round(GlyphTop() * dpi.DpiScaleY);   // the icon's own top edge (not its text box) meets the top of the digits
            SetWindowPos(h, (IntPtr)1, x, y, 0, 0, 0x0001 | 0x0010);   // HWND_BOTTOM, NOSIZE | NOACTIVATE
        }
    }
}
