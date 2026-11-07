using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Pages
{
    public partial class HardwarePage : UserControl
    {
        private bool _loading = true, _devicesLoaded;
        private List<(string Name, int Source)> _schemes = new();

        // The hot-key box currently waiting for a key combination.
        private HotkeyBinding? _recording;
        private Button? _recordingButton;

        public HardwarePage() => InitializeComponent();

        /// <summary>Called once when the app starts: restores saved lighting and hot-keys, and fills in the current Windows values.</summary>
        public void Initialize()
        {
            _loading = true;
            sldSpeed.Value = PointerService.GetSpeed();
            chkPrecision.IsChecked = PointerService.GetPrecision();
            sldScroll.Value = PointerService.GetScrollLines();

            _schemes = PointerService.Schemes();
            cmbScheme.ItemsSource = _schemes.Select(s => s.Name).ToList();
            cmbScheme.SelectedItem = PointerService.CurrentScheme();
            sldSize.Value = PointerService.GetSize();

            cmbFrom.ItemsSource = KeyRemapService.Keys.Select(k => k.Name).ToList();
            cmbTo.ItemsSource = new[] { KeyRemapService.Disabled }.Concat(KeyRemapService.Keys.Select(k => k.Name)).ToList();
            cmbFrom.SelectedItem = "Caps Lock";
            cmbTo.SelectedItem = "Left Ctrl";
            _loading = false;

            RefreshGlass();
            BuildHotkeys();
            BuildRemaps();

            var failed = HotkeyService.Apply(Window.GetWindow(this));
            if (failed.Count > 0) txtHotkeyStatus.Text = "Another app already uses: " + string.Join(", ", failed);

            _ = LightingService.RestoreAsync();
            lightMouse.Initialize();
            lightKeyboard.Initialize();
        }

        private void TabChecked(object sender, RoutedEventArgs e)
        {
            if (tabMouse == null) return;
            tabMouse.Visibility = rbMouse.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            tabKeyboard.Visibility = rbKeyboard.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            tabDevices.Visibility = rbDevices.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (rbDevices.IsChecked == true && !_devicesLoaded) LoadDevices();
            if (rbMouse.IsChecked == true) RefreshGlass();
        }

        // ---------- pointer ----------

        private void SpeedChanged(object sender, RoutedEventArgs e) { if (!_loading) PointerService.SetSpeed((int)sldSpeed.Value); }
        private void ScrollChanged(object sender, RoutedEventArgs e) { if (!_loading) PointerService.SetScrollLines((int)sldScroll.Value); }
        private void PrecisionClicked(object sender, RoutedEventArgs e) => PointerService.SetPrecision(chkPrecision.IsChecked == true);

        private void ApplyCursorClicked(object sender, RoutedEventArgs e)
        {
            string? error = null;
            if (cmbScheme.SelectedItem is string name)
            {
                var scheme = _schemes.FirstOrDefault(s => s.Name == name);
                error = PointerService.ApplyScheme(scheme.Name, scheme.Source);
            }
            error ??= PointerService.SetSize((int)sldSize.Value);
            txtCursorStatus.Text = error ?? "Applied. If a program still shows the old cursor, close and reopen it or restart your PC.";
        }

        // ---------- Glass Cursor ----------

        private void RefreshGlass()
        {
            bool running = GlassCursorService.IsRunning;
            txtGlassState.Text = running ? "Lens on" : "Lens off";
            txtGlassState.Foreground = (System.Windows.Media.Brush)FindResource(running ? "AccentBrush" : "TextDimBrush");
            btnGlassStart.IsEnabled = !running;
            btnGlassStop.IsEnabled = running;
        }

        private async void GlassStartClicked(object sender, RoutedEventArgs e)
        {
            txtGlassStatus.Text = GlassCursorService.Start() ?? "Lens on. Its icon is near the clock.";
            await System.Threading.Tasks.Task.Delay(800);
            RefreshGlass();
        }

        private async void GlassStopClicked(object sender, RoutedEventArgs e)
        {
            txtGlassStatus.Text = GlassCursorService.Stop() ?? "Lens off and the normal pointer is back.";
            await System.Threading.Tasks.Task.Delay(400);
            RefreshGlass();
        }

        private void GlassRestoreClicked(object sender, RoutedEventArgs e)
        {
            txtGlassStatus.Text = GlassCursorService.RestorePointer() ?? "The normal pointer is back.";
            RefreshGlass();
        }

        // ---------- hot-keys ----------

        private static readonly string[] ActionNames = { "Open program or file", "Open website" };

        private void BuildHotkeys()
        {
            panelHotkeys.Children.Clear();
            var list = AppSettings.Current.Hotkeys;
            txtNoHotkeys.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            for (int i = 0; i < list.Count; i++)
            {
                var h = list[i];
                var row = new Grid { Margin = new Thickness(16, 10, 16, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var keyButton = new Button { Content = string.IsNullOrEmpty(h.Combo) ? "Click, then press keys" : h.Combo, Margin = new Thickness(0, 0, 10, 0), HorizontalContentAlignment = HorizontalAlignment.Left };
                keyButton.Click += (_, _) => BeginRecording(h, keyButton);
                row.Children.Add(keyButton);

                var action = new ComboBox { ItemsSource = ActionNames, SelectedIndex = h.Action == "Website" ? 1 : 0, Margin = new Thickness(0, 0, 10, 0) };
                Grid.SetColumn(action, 1);
                row.Children.Add(action);

                var target = new TextBox { Text = h.Target, Tag = h.Action == "Website" ? "https://..." : "Program or file path", Margin = new Thickness(0, 0, 10, 0) };
                target.LostFocus += (_, _) => { h.Target = target.Text.Trim(); SaveHotkeys(); };
                Grid.SetColumn(target, 2);
                row.Children.Add(target);

                var browse = new Button { Content = "Browse", Margin = new Thickness(0, 0, 8, 0), Visibility = h.Action == "Website" ? Visibility.Collapsed : Visibility.Visible };
                browse.Click += (_, _) =>
                {
                    var dlg = new OpenFileDialog { Title = "Choose a program or file" };
                    if (dlg.ShowDialog() != true) return;
                    target.Text = h.Target = dlg.FileName;
                    SaveHotkeys();
                };
                Grid.SetColumn(browse, 3);
                row.Children.Add(browse);

                action.SelectionChanged += (_, _) =>
                {
                    h.Action = action.SelectedIndex == 1 ? "Website" : "Program";
                    browse.Visibility = h.Action == "Website" ? Visibility.Collapsed : Visibility.Visible;
                    target.Tag = h.Action == "Website" ? "https://..." : "Program or file path";
                    SaveHotkeys();
                };

                var remove = new Button { Content = "Remove" };
                remove.Click += (_, _) => { list.Remove(h); SaveHotkeys(); BuildHotkeys(); };
                Grid.SetColumn(remove, 4);
                row.Children.Add(remove);

                panelHotkeys.Children.Add(row);
                panelHotkeys.Children.Add(new Border { Style = (Style)FindResource("Divider") });
            }
        }

        private void AddHotkeyClicked(object sender, RoutedEventArgs e)
        {
            AppSettings.Current.Hotkeys.Add(new HotkeyBinding());
            BuildHotkeys();
        }

        private void SaveHotkeys()
        {
            AppSettings.Save();
            var failed = HotkeyService.Apply(Window.GetWindow(this));
            txtHotkeyStatus.Text = failed.Count == 0 ? "" : "Another app already uses: " + string.Join(", ", failed);
        }

        private void BeginRecording(HotkeyBinding h, Button button)
        {
            if (_recordingButton != null) EndRecording();
            _recording = h;
            _recordingButton = button;
            button.Content = "Press keys...";
            HotkeyService.Stop(); // so the combination being recorded does not trigger anything
            Focus();
        }

        private void EndRecording()
        {
            _recording = null;
            _recordingButton = null;
            SaveHotkeys();
            BuildHotkeys();
        }

        private void PagePreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_recording == null) return;
            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;

            if (key == Key.Escape) { EndRecording(); return; }
            if (key == Key.Back) { _recording.Combo = ""; _recording.Mods = 0; _recording.Vk = 0; EndRecording(); return; }

            uint mods = 0;
            var parts = new List<string>();
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { mods |= 0x2; parts.Add("Ctrl"); }
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) { mods |= 0x1; parts.Add("Alt"); }
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) { mods |= 0x4; parts.Add("Shift"); }
            if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) { mods |= 0x8; parts.Add("Win"); }

            bool fKey = key >= Key.F1 && key <= Key.F24;
            if (mods == 0 && !fKey)
            {
                txtHotkeyStatus.Text = "Add Ctrl, Alt, Shift or Windows, or use an F key.";
                return;
            }

            var duplicate = AppSettings.Current.Hotkeys.FirstOrDefault(x => x != _recording && x.Vk == (uint)KeyInterop.VirtualKeyFromKey(key) && x.Mods == mods);
            if (duplicate != null) { duplicate.Combo = ""; duplicate.Vk = 0; duplicate.Mods = 0; } // the combination moves here

            parts.Add(key.ToString());
            _recording.Mods = mods;
            _recording.Vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            _recording.Combo = string.Join(" + ", parts);
            EndRecording();
        }

        // ---------- key remaps ----------

        private void BuildRemaps()
        {
            panelRemaps.Children.Clear();
            var list = AppSettings.Current.Remaps;
            txtNoRemaps.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var r in list.ToList())
            {
                var row = new Grid { Margin = new Thickness(16, 10, 16, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = $"{r.From}   acts as   {r.To}", VerticalAlignment = VerticalAlignment.Center });
                var remove = new Button { Content = "Remove" };
                remove.Click += (_, _) => { list.Remove(r); BuildRemaps(); txtRemapStatus.Text = "Press Save remaps to apply."; };
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);
                panelRemaps.Children.Add(row);
                panelRemaps.Children.Add(new Border { Style = (Style)FindResource("Divider") });
            }
        }

        private void AddRemapClicked(object sender, RoutedEventArgs e)
        {
            if (cmbFrom.SelectedItem is not string from || cmbTo.SelectedItem is not string to) return;
            if (from == to) { txtRemapStatus.Text = "Pick two different keys."; return; }

            var list = AppSettings.Current.Remaps;
            list.RemoveAll(r => r.From == from); // one remap per key
            list.Add(new KeyRemap { From = from, To = to });
            BuildRemaps();
            txtRemapStatus.Text = "Press Save remaps to apply.";
        }

        private void SaveRemapsClicked(object sender, RoutedEventArgs e)
        {
            var error = KeyRemapService.Write(AppSettings.Current.Remaps);
            if (error == null) AppSettings.Save();
            txtRemapStatus.Text = error != null ? $"Could not save: {error}"
                : AppSettings.Current.Remaps.Count == 0 ? "Remaps removed. They finish after restarting your PC."
                : "Saved. They take effect after restarting your PC.";
        }

        private void ClearRemapsClicked(object sender, RoutedEventArgs e)
        {
            AppSettings.Current.Remaps.Clear();
            BuildRemaps();
            SaveRemapsClicked(sender, e);
        }

        // ---------- connect device ----------

        private async void LoadDevices()
        {
            _devicesLoaded = true;
            txtDevicesBusy.Visibility = Visibility.Visible;
            txtDevicesBusy.Text = "Looking for devices...";
            lstDevices.ItemsSource = null;

            var devices = await DeviceService.ListAsync();
            lstDevices.ItemsSource = devices;
            txtDevicesBusy.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            txtDevicesBusy.Text = "No mice, keyboards or Bluetooth devices were found.";
            titleConnected.Text = devices.Count == 0 ? "Connected now" : $"Connected now ({devices.Count})";
        }

        private void RefreshDevicesClicked(object sender, RoutedEventArgs e) => LoadDevices();

        private static void OpenSettings(string uri)
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
        }

        private void AddBluetoothClicked(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:bluetooth");
        private void OpenDevicesClicked(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:connecteddevices");
    }
}
