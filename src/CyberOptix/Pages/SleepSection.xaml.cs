using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using CyberOptix.Services;

namespace CyberOptix.Pages
{
    /// <summary>The Sleep section of the Display page: Windows' screen-off and sleep timers, plugged in and (on a laptop) on battery.</summary>
    public partial class SleepSection : UserControl
    {
        private static readonly int[] Minutes = { 1, 2, 3, 5, 10, 15, 20, 25, 30, 45, 60, 120, 180, 240, 300 };
        private bool _loading = true;

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public SleepSection() => InitializeComponent();

        private static string Label(int minutes) =>
            minutes == 0 ? "Never" : minutes < 60 ? minutes + (minutes == 1 ? " minute" : " minutes") : minutes / 60 + (minutes == 60 ? " hour" : " hours");

        private void Fill(ComboBox box, SleepSettingsService.Timer timer, bool ac)
        {
            box.Items.Clear();
            int seconds = SleepSettingsService.Get(timer, ac);
            int current = seconds < 0 ? -1 : (int)Math.Round(seconds / 60.0);
            var choices = Minutes.Append(0).ToList();
            if (current > 0 && !choices.Contains(current)) choices.Add(current);   // a value Windows had that is not in the list
            foreach (var m in choices.OrderBy(m => m == 0 ? int.MaxValue : m))
            {
                var item = new ComboBoxItem { Content = Label(m), Tag = m };
                box.Items.Add(item);
                if (m == current) box.SelectedItem = item;
            }
            box.IsEnabled = seconds >= 0;
        }

        public void Initialize()
        {
            _loading = true;
            Fill(cmbScreenAc, SleepSettingsService.Timer.ScreenOff, true);
            Fill(cmbSleepAc, SleepSettingsService.Timer.Sleep, true);
            panelBattery.Visibility = SleepSettingsService.HasBattery ? Visibility.Visible : Visibility.Collapsed;
            Fill(cmbScreenDc, SleepSettingsService.Timer.ScreenOff, false);
            Fill(cmbSleepDc, SleepSettingsService.Timer.Sleep, false);
            _loading = false;
        }

        private void Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || sender is not ComboBox box || box.SelectedItem is not ComboBoxItem { Tag: int minutes }) return;
            var timer = box == cmbScreenAc || box == cmbScreenDc ? SleepSettingsService.Timer.ScreenOff : SleepSettingsService.Timer.Sleep;
            bool ac = box == cmbScreenAc || box == cmbSleepAc;
            txtStatus.Text = SleepSettingsService.Set(timer, ac, minutes * 60) ? "Saved." : "Windows would not take that change.";
        }

        private void ScreensOffClicked(object sender, RoutedEventArgs e)
        {
            // WM_SYSCOMMAND / SC_MONITORPOWER, 2 = off. The screens come back on any mouse move or key press.
            SendMessage(new System.Windows.Interop.WindowInteropHelper(Window.GetWindow(this)!).Handle, 0x0112, (IntPtr)0xF170, (IntPtr)2);
        }
    }
}
