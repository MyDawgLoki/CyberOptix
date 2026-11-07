using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CyberOptix.Models;
using CyberOptix.Widgets;

namespace CyberOptix.Pages
{
    public partial class WidgetsPage : UserControl
    {
        private bool _loading = true;

        public WidgetsPage() => InitializeComponent();

        public void Initialize()
        {
            _loading = true;
            var s = AppSettings.Current;
            chkClock.IsChecked = s.ClockWidget;
            chkWeather.IsChecked = s.WeatherWidget;
            chkMedia.IsChecked = s.MediaWidget;
            chkMediaAuto.IsChecked = s.MediaWidgetAutoShow;
            txtCity.Text = s.City;
            cmbUnits.SelectedIndex = s.Fahrenheit ? 0 : 1;
            sldOpacity.Value = s.WidgetOpacity;
            chkOnTop.IsChecked = s.WidgetsOnTop;
            chkCloseGaming.IsChecked = s.WidgetsCloseWhileGaming;
            chkHideFocus.IsChecked = s.HideWidgetsOnBrowserOrGame;
            chkShowBrowser.IsChecked = s.ShowWidgetsWithBrowser;
            BuildMediaIcons();
            _loading = false;
        }

        // Shared by CheckBox.Click, ComboBox.SelectionChanged and Slider.ValueChanged.
        private void Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var s = AppSettings.Current;
            s.ClockWidget = chkClock.IsChecked == true;
            s.WeatherWidget = chkWeather.IsChecked == true;
            // Turning the media player on by hand puts "show it when something plays" back too.
            if (chkMedia.IsChecked == true && !s.MediaWidget) { s.MediaWidgetAutoShow = true; chkMediaAuto.IsChecked = true; }
            s.MediaWidgetAutoShow = chkMediaAuto.IsChecked == true;
            s.MediaWidget = chkMedia.IsChecked == true;
            s.Fahrenheit = cmbUnits.SelectedIndex == 0;
            s.WidgetOpacity = sldOpacity.Value;
            s.WidgetsOnTop = chkOnTop.IsChecked == true;
            s.WidgetsCloseWhileGaming = chkCloseGaming.IsChecked == true;
            s.HideWidgetsOnBrowserOrGame = chkHideFocus.IsChecked == true;
            s.ShowWidgetsWithBrowser = chkShowBrowser.IsChecked == true;
            WidgetManager.Apply();
            WidgetManager.RefreshWeather();
            CyberOptix.Services.MediaColorService.Sync();   // starts or stops listening for playback
        }

        // ---------- media player icons ----------

        private void BuildMediaIcons()
        {
            panelMediaIcons.Children.Clear();
            var chosen = new System.Collections.Generic.HashSet<string>(AppSettings.Current.MediaIcons);
            bool first = true;
            foreach (var l in CyberOptix.Services.MediaLaunchers.All)
            {
                if (!first) panelMediaIcons.Children.Add(new Border { Style = (Style)FindResource("Divider") });
                first = false;
                var launcher = l;
                var box = new CheckBox { Content = launcher.Name, IsChecked = chosen.Contains(launcher.Id) };
                System.Windows.Automation.AutomationProperties.SetAutomationId(box, "mediaicon_" + launcher.Id);
                box.Click += (_, _) => MediaIconChanged();
                panelMediaIcons.Children.Add(box);
            }
        }

        private void MediaIconChanged()
        {
            if (_loading) return;
            var on = panelMediaIcons.Children.OfType<CheckBox>().Where(b => b.IsChecked == true).Select(b => b.Content as string).ToList();
            AppSettings.Current.MediaIcons = CyberOptix.Services.MediaLaunchers.All.Where(l => on.Contains(l.Name)).Select(l => l.Id).ToList();
            AppSettings.Save();
            CyberOptix.Services.MediaLaunchers.RaiseSelectionChanged();
        }

        private void SetCityClicked(object sender, RoutedEventArgs e)
        {
            var city = txtCity.Text.Trim();
            if (city.Length == 0) return;
            AppSettings.Current.City = city;
            AppSettings.Save();
            WidgetManager.RefreshWeather();
            txtStatus.Text = AppSettings.Current.WeatherWidget
                ? $"Weather location set to {city}."
                : $"Weather location set to {city}. Turn on the weather widget to see it.";
        }

        private void CityKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) SetCityClicked(sender, e);
        }

        private void ResetClicked(object sender, RoutedEventArgs e)
        {
            WidgetManager.ResetPositions();
            txtStatus.Text = "Widget positions reset.";
        }
    }
}
