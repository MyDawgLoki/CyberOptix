using System.Windows;
using System.Windows.Controls;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Pages
{
    public partial class WindowManagerPage : UserControl
    {
        private bool _loading = true;

        public WindowManagerPage() => InitializeComponent();

        public void Initialize()
        {
            _loading = true;
            var s = AppSettings.Current;
            chkHotkeys.IsChecked = s.HotkeysEnabled;
            sldGap.Value = s.Gap;
            _loading = false;
            if (s.HotkeysEnabled) EnableHotkeys();
        }

        private void EnableHotkeys()
        {
            var failed = WindowManagerService.Enable(Window.GetWindow(this)!);
            txtStatus.Text = failed.Count == 0
                ? "Hotkeys active."
                : $"Hotkeys active, but another app already uses Ctrl+Alt+{string.Join(", ", failed)}.";
        }

        private void HotkeysClicked(object sender, RoutedEventArgs e)
        {
            var on = chkHotkeys.IsChecked == true;
            AppSettings.Current.HotkeysEnabled = on;
            AppSettings.Save();
            if (on) EnableHotkeys();
            else
            {
                WindowManagerService.Disable();
                txtStatus.Text = "Hotkeys off.";
            }
        }

        private void GapChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.Gap = (int)sldGap.Value;
            AppSettings.Save();
        }

        private void TileClicked(object sender, RoutedEventArgs e)
        {
            var n = WindowManagerService.TileAll();
            txtStatus.Text = n == 0 ? "No windows to tile." : $"Tiled {n} window{(n == 1 ? "" : "s")}.";
        }
    }
}
