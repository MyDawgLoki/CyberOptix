using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Pages
{
    /// <summary>The Desktop Clock section of the Display page: on/off, theme, time and date options, look and position.</summary>
    public partial class DesktopClockPage : UserControl
    {
        private bool _loading = true;

        public DesktopClockPage()
        {
            InitializeComponent();
            wheelClock.ColorCommitted += hex => { if (!_loading) SetColor(hex); };   // the clock color is saved when a choice is finished
        }

        public void Initialize()
        {
            _loading = true;
            var s = AppSettings.Current;
            chkOn.IsChecked = s.DesktopClock;
            chk24.IsChecked = s.DesktopClock24h;
            chkAmPm.IsChecked = s.DesktopClockShowAmPm;
            chkSeconds.IsChecked = s.DesktopClockSeconds;
            chkDate.IsChecked = s.DesktopClockShowDate;
            chkWeather.IsChecked = s.DesktopClockWeather;
            chkGlow.IsChecked = s.DesktopClockGlow;
            sldScale.Value = Math.Clamp(s.DesktopClockScale, sldScale.Minimum, sldScale.Maximum);
            sldOpacity.Value = Math.Clamp(s.DesktopClockOpacity, sldOpacity.Minimum, sldOpacity.Maximum);

            chkSyncMedia.IsChecked = s.DesktopClockColorMode == "Media";
            wheelClock.Hex = s.DesktopClockColor;

            BuildAnchors();
            map.Moved += () =>
            {
                _loading = true;
                BuildAnchors();
                foreach (ComboBoxItem item in cmbDisplay.Items) if ((string)item.Tag == AppSettings.Current.DesktopClockDisplay) cmbDisplay.SelectedItem = item;
                _loading = false;
            };

            cmbDisplay.Items.Clear();
            var displays = DisplayService.GetDisplays();
            foreach (var d in displays)
            {
                var item = new ComboBoxItem { Content = $"Display {d.Number}: {d.Resolution}{(d.IsPrimary ? " (main)" : "")}", Tag = d.Id };
                cmbDisplay.Items.Add(item);
                if (d.Id == s.DesktopClockDisplay || (string.IsNullOrEmpty(s.DesktopClockDisplay) && d.IsPrimary)) cmbDisplay.SelectedItem = item;
            }
            if (cmbDisplay.SelectedItem == null && cmbDisplay.Items.Count > 0) cmbDisplay.SelectedIndex = 0;

            BuildThemes();
            _loading = false;
            map.Reload();
            UpdateEnabled();
            if (s.DesktopClock) DesktopClockService.Apply();   // bring the clock back if it was on last time
        }

        /// <summary>The "Place it" list: the nine places, plus "Custom spot" once the clock was dragged. Choosing a place goes back to it.</summary>
        private void BuildAnchors()
        {
            var s = AppSettings.Current;
            cmbAnchor.Items.Clear();
            foreach (var (key, name) in DesktopClockThemes.Anchors)
            {
                var item = new ComboBoxItem { Content = name, Tag = key };
                cmbAnchor.Items.Add(item);
                if (!s.DesktopClockCustom && key == s.DesktopClockAnchor) cmbAnchor.SelectedItem = item;
            }
            if (s.DesktopClockCustom)
            {
                var custom = new ComboBoxItem { Content = "Custom spot (dragged)", Tag = "Custom" };
                cmbAnchor.Items.Add(custom);
                cmbAnchor.SelectedItem = custom;
            }
            if (cmbAnchor.SelectedItem == null) cmbAnchor.SelectedIndex = 1;
        }

        private void BuildThemes()
        {
            panelThemes.Children.Clear();
            var now = DateTime.Now;
            foreach (var theme in DesktopClockThemes.All)
            {
                var (main, _, _) = DesktopClockThemes.Time(theme, now, AppSettings.Current.DesktopClock24h, false, false);
                var preview = new StackPanel { Margin = new Thickness(6, 4, 6, 4), MinWidth = 120 };
                preview.Children.Add(new TextBlock { Text = theme.Name, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
                preview.Children.Add(new TextBlock
                {
                    Text = main.Replace("\n", ":"), FontFamily = new FontFamily(theme.Font), FontSize = 24,
                    FontWeight = theme.TimeBold ? FontWeights.Bold : theme.TimeLight ? FontWeights.Light : FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
                });
                preview.Children.Add(new TextBlock
                {
                    Text = DesktopClockThemes.Date(theme, now).Split(',')[0].Trim(), FontFamily = new FontFamily(theme.Font), FontSize = 11,
                    FontStyle = theme.DateItalic ? FontStyles.Italic : FontStyles.Normal, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center,
                });
                var radio = new RadioButton
                {
                    GroupName = "ClockTheme", Style = (Style)FindResource("Segment"), Content = preview, Tag = theme.Name,
                    ToolTip = theme.Description, Margin = new Thickness(2),
                    IsChecked = string.Equals(theme.Name, AppSettings.Current.DesktopClockTheme, StringComparison.OrdinalIgnoreCase),
                };
                radio.Checked += (_, _) =>
                {
                    if (_loading) return;
                    AppSettings.Current.DesktopClockTheme = (string)radio.Tag;
                    Commit();
                };
                if (ClockThemeFiles.IsCustom(theme.Name))
                {
                    var menu = new ContextMenu();
                    var remove = new MenuItem { Header = "Remove this theme" };
                    var themeName = theme.Name;
                    remove.Click += (_, _) =>
                    {
                        ClockThemeFiles.Remove(themeName);
                        if (string.Equals(AppSettings.Current.DesktopClockTheme, themeName, StringComparison.OrdinalIgnoreCase)) AppSettings.Current.DesktopClockTheme = DesktopClockThemes.Builtin[0].Name;
                        BuildThemes();
                        Commit();
                    };
                    menu.Items.Add(remove);
                    radio.ContextMenu = menu;
                    radio.ToolTip = theme.Description + "  (your own theme: right-click to remove it)";
                }
                panelThemes.Children.Add(radio);
            }
            panelThemes.Children.Add(BuildAddTile());
        }

        /// <summary>The "+" tile: pick a theme file (JSON) to add it, or save an example file to start from.</summary>
        private UIElement BuildAddTile()
        {
            var plus = new TextBlock { Text = "+", FontSize = 34, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -4, 0, 0) };
            var label = new TextBlock { Text = "Add your own", FontSize = 11, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Center };
            var tile = new Button
            {
                Content = new StackPanel { Margin = new Thickness(6, 4, 6, 4), MinWidth = 120, VerticalAlignment = VerticalAlignment.Center, Children = { plus, label } },
                Margin = new Thickness(2), Padding = new Thickness(8), MinHeight = 82,
                ToolTip = "Add a clock theme from a .json file. It is checked and saved in CyberOptix's own clock theme folder. Right-click for an example file to start from.",
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(tile, "clock_theme_add");
            tile.Click += (_, _) => AddThemeClicked();
            var menu = new ContextMenu();
            var example = new MenuItem { Header = "Save an example theme file..." };
            example.Click += (_, _) =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Clock theme (*.json)|*.json", FileName = "My Theme.json", Title = "Save an example clock theme" };
                if (dlg.ShowDialog() != true) return;
                try { System.IO.File.WriteAllText(dlg.FileName, ClockThemeFiles.Template()); } catch (Exception ex) { MessageBox.Show(ex.Message, "Clock theme"); }
            };
            menu.Items.Add(example);
            tile.ContextMenu = menu;
            return tile;
        }

        private void AddThemeClicked()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Clock theme (*.json)|*.json", Title = "Choose a clock theme file" };
            if (dlg.ShowDialog() != true) return;
            var theme = ClockThemeFiles.Import(dlg.FileName, out var error);
            if (theme == null) { MessageBox.Show(error, "Add a clock theme", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            AppSettings.Current.DesktopClockTheme = theme.Name;   // show it straight away
            BuildThemes();
            Commit();
        }

        private void SetColor(string hex)
        {
            AppSettings.Current.DesktopClockColor = hex.ToUpperInvariant();
            if (AppSettings.Current.DesktopClockColorMode != "Media") AppSettings.Current.DesktopClockColorMode = "Custom";   // picking a color means using it
            Commit();
        }

        private void UpdateEnabled()
        {
            var s = AppSettings.Current;
            panelAll.IsEnabled = s.DesktopClock;
            panelAll.Opacity = s.DesktopClock ? 1 : 0.5;
            chkAmPm.IsEnabled = !s.DesktopClock24h;
        }

        private void Commit()
        {
            AppSettings.Save();
            UpdateEnabled();
            DesktopClockService.Apply();
            map?.Refresh();   // the block on the map follows the size, theme, screen and place
        }

        private void ReadAll()
        {
            var s = AppSettings.Current;
            s.DesktopClock = chkOn.IsChecked == true;
            s.DesktopClock24h = chk24.IsChecked == true;
            s.DesktopClockShowAmPm = chkAmPm.IsChecked == true;
            s.DesktopClockSeconds = chkSeconds.IsChecked == true;
            s.DesktopClockShowDate = chkDate.IsChecked == true;
            s.DesktopClockWeather = chkWeather.IsChecked == true;
            s.DesktopClockGlow = chkGlow.IsChecked == true;
            s.DesktopClockScale = Math.Round(sldScale.Value, 2);
            s.DesktopClockOpacity = Math.Round(sldOpacity.Value, 2);
            s.DesktopClockColorMode = chkSyncMedia.IsChecked == true ? "Media" : s.DesktopClockColorMode == "Media" ? "Custom" : s.DesktopClockColorMode;
            if (cmbAnchor.SelectedItem is ComboBoxItem a && (string)a.Tag != "Custom")
            {
                s.DesktopClockAnchor = (string)a.Tag;
                s.DesktopClockCustom = false;   // picking one of the nine places leaves the dragged spot
            }
            if (cmbDisplay.SelectedItem is ComboBoxItem d) s.DesktopClockDisplay = (string)d.Tag;
        }

        private void Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            ReadAll();
            Commit();
            if (ReferenceEquals(sender, chkSyncMedia)) MediaColorService.Sync();   // starts or stops following the media player
            if (sender == chk24) BuildThemes();
            if (sender == cmbAnchor) Dispatcher.BeginInvoke(new Action(() => { _loading = true; BuildAnchors(); _loading = false; }));   // the "Custom spot" entry goes once a place is chosen
        }

        private void SliderChanged(object sender, RoutedEventArgs e) => Changed(sender, e);
        private void SelectionChanged(object sender, SelectionChangedEventArgs e) => Changed(sender, e);
    }
}
