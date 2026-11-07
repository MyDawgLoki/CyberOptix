using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Pages
{
    /// <summary>Lighting effect, color, speed and brightness for the mouse or keyboard.</summary>
    public partial class LightingCard : UserControl
    {
        private static readonly string[] Palette =
            { "#FF3B3B", "#FF8A1F", "#FFD21F", "#2EE66B", "#16D9D9", "#3B82F6", "#8B5CF6", "#B146C2", "#FF4FA3", "#FFFFFF" };

        private bool _loading = true, _loaded;

        /// <summary>"mouse" or "keyboard".</summary>
        public string Group { get; set; } = "mouse";

        public LightingCard()
        {
            InitializeComponent();
            cmbEffect.ItemsSource = LightingService.Effects;

            foreach (var hex in Palette)
            {
                ThemeService.TryParse(hex, out var color);
                var swatch = new RadioButton
                {
                    GroupName = "Lamp" + GetHashCode(),
                    Style = (Style)FindResource("Swatch"),
                    Background = new SolidColorBrush(color),
                    Tag = hex,
                    Margin = new Thickness(0, 0, 6, 0),
                };
                swatch.Checked += SwatchChecked;
                panelSwatches.Children.Add(swatch);
            }
        }

        /// <summary>Looks for devices (once) and shows the saved settings.</summary>
        public async void Initialize()
        {
            titleLighting.Text = Group == "keyboard" ? "Keyboard lighting" : "Mouse lighting";
            if (_loaded) return;
            _loaded = true;
            await RefreshDevices();
        }

        private async System.Threading.Tasks.Task RefreshDevices()
        {
            var all = await LightingService.ListAsync();
            var mine = all.Where(d => (d.Kind == "Keyboard") == (Group == "keyboard")).ToList();

            if (mine.Count == 0)
            {
                panelNone.Visibility = Visibility.Visible;
                panelControls.Visibility = Visibility.Collapsed;
                txtNone.Text = Group == "keyboard"
                    ? "No keyboard that supports Windows Dynamic Lighting was found. Brand-specific lighting (Corsair iCUE, Razer, Logitech G HUB, ASUS Aura) is controlled in that brand's own software."
                    : "No mouse that supports Windows Dynamic Lighting was found. Brand-specific lighting (Corsair iCUE, Razer, Logitech G HUB, ASUS Aura) is controlled in that brand's own software.";
                return;
            }

            panelNone.Visibility = Visibility.Collapsed;
            panelControls.Visibility = Visibility.Visible;
            txtDevices.Text = string.Join(", ", mine.Select(d => $"{d.Name} ({d.Lamps} lights)"));
            LoadSaved();
        }

        private void LoadSaved()
        {
            _loading = true;
            var s = AppSettings.Current.Lighting.TryGetValue(Group, out var v) ? v : new LightSetting();
            cmbEffect.SelectedItem = LightingService.Effects.Contains(s.Effect) ? s.Effect : "Off";
            sldSpeed.Value = s.Speed;
            sldBrightness.Value = s.Brightness;
            txtHex.Text = s.Color;
            foreach (RadioButton r in panelSwatches.Children)
                r.IsChecked = string.Equals((string)r.Tag, s.Color, StringComparison.OrdinalIgnoreCase);
            UpdateColorRow(s.Effect);
            _loading = false;
        }

        private void UpdateColorRow(string effect)
        {
            bool uses = effect is "Static" or "Breathing" or "Pulse";
            rowColor.Opacity = uses ? 1 : 0.45;
            rowColor.IsEnabled = uses;
        }

        private void Save()
        {
            if (_loading) return;
            var s = new LightSetting
            {
                Effect = cmbEffect.SelectedItem as string ?? "Off",
                Color = txtHex.Text,
                Speed = (int)sldSpeed.Value,
                Brightness = (int)sldBrightness.Value,
            };
            if (!ThemeService.TryParse(s.Color, out _)) s.Color = "#B146C2";
            AppSettings.Current.Lighting[Group] = s;
            AppSettings.Save();
            UpdateColorRow(s.Effect);
            LightingService.Apply();
        }

        private void Changed(object sender, RoutedEventArgs e) => Save();

        private void SwatchChecked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            txtHex.Text = (string)((RadioButton)sender).Tag;
            Save();
        }

        private void HexCommitted(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (!ThemeService.TryParse(txtHex.Text, out _)) { txtHex.Text = AppSettings.Current.Lighting.TryGetValue(Group, out var v) ? v.Color : "#B146C2"; return; }
            foreach (RadioButton r in panelSwatches.Children) r.IsChecked = false;
            Save();
        }

        private async void RefreshClicked(object sender, RoutedEventArgs e) => await RefreshDevices();

        private void OpenSettingsClicked(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:personalization-lighting") { UseShellExecute = true }); } catch { }
        }
    }
}
