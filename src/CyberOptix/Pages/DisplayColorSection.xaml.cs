using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Pages
{
    /// <summary>Orientation, brightness, contrast, gamma, digital vibrance and hue for the selected display.</summary>
    public partial class DisplayColorSection : UserControl
    {
        private DisplayInfo? _display;
        private bool _loading = true;
        private uint _trialOrientation;
        private int _countdown;

        private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
        private readonly DispatcherTimer _keepTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        /// <summary>Raised after the orientation changed, so the screen map can redraw.</summary>
        public event Action? LayoutChanged;

        public DisplayColorSection()
        {
            InitializeComponent();

            cmbOrientation.ItemsSource = DisplayColorService.Orientations;
            cmbOrientation.DisplayMemberPath = "Name";
            cmbOrientation.SelectedValuePath = "Value";

            _debounce.Tick += (_, _) => { _debounce.Stop(); ApplyRampNow(); };
            _keepTimer.Tick += (_, _) => KeepTick();
        }

        private ChannelColor Channel(DisplayColor c) => cmbChannel.SelectedIndex switch
        {
            1 => c.Red,
            2 => c.Green,
            3 => c.Blue,
            _ => c.All,
        };

        /// <summary>Shows the settings of the selected display.</summary>
        public void SetDisplay(DisplayInfo? display)
        {
            // A trial must never carry over to a different display. (The same display is just a refresh after the rotation.)
            if (_keepTimer.IsEnabled && display?.Id != _display?.Id) RevertClicked(this, new RoutedEventArgs());

            _display = display;
            _loading = true;
            try
            {
                bool usable = display != null && !string.IsNullOrEmpty(display.DeviceName);
                IsEnabled = usable;
                if (!usable)
                {
                    titleColor.Text = "Color and orientation";
                    return;
                }

                titleColor.Text = $"Color and orientation for Display {display!.Number}";
                var saved = DisplayColorService.Get(display.Id);

                cmbOrientation.SelectedValue = DisplayColorService.GetOrientation(display.DeviceName);
                if (cmbChannel.SelectedIndex < 0) cmbChannel.SelectedIndex = 0;
                LoadChannel(saved);

                NvApi.EnsureInit();
                bool nv = NvApi.Available;
                sldVibrance.IsEnabled = sldHue.IsEnabled = btnResetNv.IsEnabled = nv;
                rowVibrance.Opacity = rowHue.Opacity = rowResetNv.Opacity = nv ? 1 : 0.5;
                if (nv)
                {
                    // Show the driver's real value unless the user already set one here.
                    if (NvApi.TryGetVibrance(display.DeviceName, out var vib)) sldVibrance.Value = saved.VibranceTouched ? saved.Vibrance : vib;
                    if (NvApi.TryGetHue(display.DeviceName, out var hue)) sldHue.Value = saved.HueTouched ? saved.Hue : hue;
                }
                else
                {
                    sldVibrance.Value = 50;
                    sldHue.Value = 0;
                    txtStatus.Text = "Digital vibrance and hue need an NVIDIA graphics card and driver.";
                }
            }
            finally
            {
                _loading = false;
            }
        }

        private void LoadChannel(DisplayColor saved)
        {
            var c = Channel(saved);
            sldBrightness.Value = c.Brightness;
            sldContrast.Value = c.Contrast;
            sldGamma.Value = c.Gamma;
        }

        // ---------- brightness / contrast / gamma ----------

        private void ChannelChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _display == null) return;
            _loading = true;
            LoadChannel(DisplayColorService.Get(_display.Id));
            _loading = false;
        }

        private void RampChanged(object sender, RoutedEventArgs e)
        {
            if (_loading || _display == null) return;
            _debounce.Stop();
            _debounce.Start(); // sliders fire constantly while dragging; apply once it settles
        }

        private void ApplyRampNow()
        {
            if (_display == null) return;
            var saved = DisplayColorService.Get(_display.Id);
            var c = Channel(saved);
            c.Brightness = Math.Round(sldBrightness.Value);
            c.Contrast = Math.Round(sldContrast.Value);
            c.Gamma = Math.Round(sldGamma.Value, 2);
            saved.RampTouched = !(saved.All.IsNeutral && saved.Red.IsNeutral && saved.Green.IsNeutral && saved.Blue.IsNeutral) || saved.RampTouched;

            var error = DisplayColorService.ApplyRamp(_display.DeviceName, saved);
            txtStatus.Text = error ?? "";
            AppSettings.Save();
        }

        // ---------- NVIDIA vibrance and hue ----------

        private void VibranceChanged(object sender, RoutedEventArgs e)
        {
            if (_loading || _display == null || !NvApi.Available) return;
            var saved = DisplayColorService.Get(_display.Id);
            saved.Vibrance = (int)sldVibrance.Value;
            saved.VibranceTouched = true;
            txtStatus.Text = NvApi.SetVibrance(_display.DeviceName, saved.Vibrance) ? "" : "The NVIDIA driver did not accept that value.";
            AppSettings.Save();
        }

        private void HueChanged(object sender, RoutedEventArgs e)
        {
            if (_loading || _display == null || !NvApi.Available) return;
            var saved = DisplayColorService.Get(_display.Id);
            saved.Hue = (int)sldHue.Value;
            saved.HueTouched = true;
            txtStatus.Text = NvApi.SetHue(_display.DeviceName, saved.Hue) ? "" : "The NVIDIA driver did not accept that value.";
            AppSettings.Save();
        }

        private bool Confirm(string text) =>
            MessageBox.Show(Window.GetWindow(this), text, "Reset to default", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

        /// <summary>Puts the vibrance and hue sliders on what the driver now reports.</summary>
        private void ShowDriverValues()
        {
            if (_display == null) return;
            _loading = true;
            if (NvApi.TryGetVibrance(_display.DeviceName, out var vib)) sldVibrance.Value = vib;
            if (NvApi.TryGetHue(_display.DeviceName, out var hue)) sldHue.Value = hue;
            _loading = false;
        }

        private void ResetNvClicked(object sender, RoutedEventArgs e)
        {
            if (_display == null || !NvApi.Available) return;
            if (!Confirm($"Reset digital vibrance and hue to the NVIDIA defaults for Display {_display.Number}?\n\n" +
                         "The NVIDIA driver stores these (the same settings as NVIDIA Control Panel), so this changes them there too.")) return;

            bool ok = NvApi.ResetToDefaults(_display.DeviceName);
            var saved = DisplayColorService.Get(_display.Id);
            saved.VibranceTouched = false; saved.Vibrance = 50;
            saved.HueTouched = false; saved.Hue = 0;
            AppSettings.Save();

            ShowDriverValues();
            txtStatus.Text = ok ? "Digital vibrance and hue are back to the NVIDIA defaults." : "The NVIDIA driver did not accept the reset.";
        }

        private void ResetClicked(object sender, RoutedEventArgs e)
        {
            if (_display == null) return;
            if (!Confirm($"Reset the brightness, contrast, gamma{(NvApi.Available ? ", digital vibrance and hue" : "")} of Display {_display.Number} to the defaults?")) return;

            var saved = new DisplayColor();
            AppSettings.Current.DisplayColors[_display.Id] = saved;

            DisplayColorService.ApplyRamp(_display.DeviceName, saved);
            bool nvOk = true;
            if (NvApi.Available) nvOk = NvApi.ResetToDefaults(_display.DeviceName);
            AppSettings.Save();

            _loading = true;
            LoadChannel(saved);
            _loading = false;
            if (NvApi.Available) ShowDriverValues();
            txtStatus.Text = nvOk ? "Colors reset to the defaults." : "Colors reset, but the NVIDIA driver did not accept the vibrance and hue reset.";
        }

        // ---------- orientation with a safety countdown ----------

        private void RotateClicked(object sender, RoutedEventArgs e)
        {
            if (_display == null || cmbOrientation.SelectedValue is not uint wanted) return;
            if (wanted == DisplayColorService.GetOrientation(_display.DeviceName)) { txtStatus.Text = "That display already uses this orientation."; return; }

            var error = DisplayColorService.TryOrientation(_display.DeviceName, wanted);
            if (error != null) { txtStatus.Text = error; return; }

            _trialOrientation = wanted;
            _countdown = 15;
            UpdateKeepText();
            panelKeep.Visibility = Visibility.Visible;
            btnRotate.IsEnabled = false;
            _keepTimer.Start();
            LayoutChanged?.Invoke();
        }

        private void UpdateKeepText() =>
            txtKeep.Text = $"Keep this orientation? Going back to the previous one in {_countdown} second{(_countdown == 1 ? "" : "s")}.";

        private void KeepTick()
        {
            _countdown--;
            if (_countdown <= 0) { RevertClicked(this, new RoutedEventArgs()); return; }
            UpdateKeepText();
        }

        private void KeepClicked(object sender, RoutedEventArgs e)
        {
            _keepTimer.Stop();
            panelKeep.Visibility = Visibility.Collapsed;
            btnRotate.IsEnabled = true;
            if (_display == null) return;

            txtStatus.Text = DisplayColorService.CommitOrientation(_display.DeviceName, _trialOrientation) ?? "Orientation saved.";

            // A mode change clears the color curve, so put the saved one back.
            var saved = DisplayColorService.Get(_display.Id);
            if (saved.RampTouched) DisplayColorService.ApplyRamp(_display.DeviceName, saved);
            LayoutChanged?.Invoke();
        }

        private void RevertClicked(object sender, RoutedEventArgs e)
        {
            _keepTimer.Stop();
            panelKeep.Visibility = Visibility.Collapsed;
            btnRotate.IsEnabled = true;
            if (_display == null) return;

            DisplayColorService.RevertOrientation(_display.DeviceName);
            _loading = true;
            cmbOrientation.SelectedValue = DisplayColorService.GetOrientation(_display.DeviceName);
            _loading = false;
            txtStatus.Text = "Went back to the previous orientation.";
            LayoutChanged?.Invoke();
        }
    }
}
