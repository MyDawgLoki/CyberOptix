using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    public class WeatherWidget : DesktopWidget
    {
        private readonly TextBlock _place = Text(13, "Loading weather...", 0.7);
        private readonly TextBlock _icon = Text(48);
        private readonly TextBlock _temp = Text(40, weight: FontWeights.SemiBold);
        private readonly TextBlock _desc = Text(14, opacity: 0.8);
        private readonly TextBlock _extra = Text(12, opacity: 0.6);
        private readonly StackPanel _days = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(15) };

        public WeatherWidget() : base("weather", 190)
        {
            var temps = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            temps.Children.Add(_temp);
            temps.Children.Add(_desc);

            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
            top.Children.Add(_icon);
            top.Children.Add(temps);

            var root = new StackPanel { MinWidth = 280 };
            root.Children.Add(_place);
            root.Children.Add(top);
            root.Children.Add(_extra);
            root.Children.Add(_days);
            Card.Child = root;

            _timer.Tick += (_, _) => Refresh();
            _timer.Start();
            Loaded += (_, _) => Refresh();
            Closed += (_, _) => _timer.Stop();
        }

        public void Refresh() => _ = RefreshAsync();

        private async Task RefreshAsync()
        {
            var s = AppSettings.Current;
            try
            {
                Render(await WeatherService.GetAsync(s.City, s.Fahrenheit));
            }
            catch (Exception ex)
            {
                _place.Text = ex.Message;
            }
        }

        private void Render(WeatherInfo w)
        {
            var unit = w.Fahrenheit ? "°F" : "°C";
            var (icon, text) = WeatherService.Describe(w.Code);
            _place.Text = w.Place;
            _icon.Text = icon;
            _temp.Text = $"{Math.Round(w.Temp)}{unit}";
            _desc.Text = text;
            _extra.Text = $"Feels like {Math.Round(w.Feels)}{unit}  ·  Humidity {w.Humidity}%  ·  Wind {Math.Round(w.Wind)} {(w.Fahrenheit ? "mph" : "km/h")}";

            _days.Children.Clear();
            foreach (var d in w.Days)
            {
                var col = new StackPanel { Width = 54, HorizontalAlignment = HorizontalAlignment.Left };
                col.Children.Add(Center(Text(12, d.Date.ToString("ddd"), 0.7)));
                col.Children.Add(Center(Text(20, WeatherService.Describe(d.Code).Icon)));
                col.Children.Add(Center(Text(12, $"{Math.Round(d.High)}° / {Math.Round(d.Low)}°")));
                _days.Children.Add(col);
            }
        }

        private static TextBlock Center(TextBlock t)
        {
            t.HorizontalAlignment = HorizontalAlignment.Center;
            return t;
        }
    }
}
