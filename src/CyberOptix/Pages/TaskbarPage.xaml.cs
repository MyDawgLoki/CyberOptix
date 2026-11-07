using System;
using System.Windows;
using System.Windows.Controls;
using CyberOptix.Services;

namespace CyberOptix.Pages
{
    public partial class TaskbarPage : UserControl
    {
        private bool _loading = true;

        public TaskbarPage() => InitializeComponent();

        /// <summary>Reads the current Windows values into the controls. Called by MainWindow once loaded.</summary>
        public void Initialize()
        {
            _loading = true;
            const string adv = TaskbarService.Adv;
            const string pers = TaskbarService.Personalize;

            if (!OsInfo.IsWindows11)
            {
                // Windows 10 has no centred icons or Chat button, and "Widgets" there is News and interests.
                HideRow(chkCenter);
                HideRow(chkChat);
                chkWidgets.Content = "News and interests";
                chkWidgets.Tag = "The news and weather button on the taskbar.";
            }
            chkCenter.IsChecked = TaskbarService.GetDword(adv, "TaskbarAl", 1) == 1;
            chkSmallIcons.IsChecked = TaskbarService.GetDword(adv, "TaskbarSi", 1) == 0;
            chkAutoHide.IsChecked = TaskbarService.GetAutoHide();
            cmbSearch.SelectedIndex = Math.Clamp(TaskbarService.GetDword(TaskbarService.Search, "SearchboxTaskbarMode", 2), 0, 3);
            cmbCombine.SelectedIndex = Math.Clamp(TaskbarService.GetDword(adv, "TaskbarGlomLevel", 0), 0, 2);
            chkTaskView.IsChecked = TaskbarService.GetDword(adv, "ShowTaskViewButton", 1) == 1;
            chkWidgets.IsChecked = OsInfo.IsWindows11 ? TaskbarService.GetDword(adv, "TaskbarDa", 1) == 1 : TaskbarService.GetDword(FeedsKey, "ShellFeedsTaskbarViewMode", 0) != 2;
            chkChat.IsChecked = TaskbarService.GetDword(adv, "TaskbarMn", 1) == 1;
            chkShowDesktop.IsChecked = TaskbarService.GetDword(adv, "TaskbarSd", 0) == 1;
            chkDark.IsChecked = TaskbarService.GetDword(pers, "SystemUsesLightTheme", 1) == 0;
            chkTransparent.IsChecked = TaskbarService.GetDword(pers, "EnableTransparency", 1) == 1;
            chkAccent.IsChecked = TaskbarService.GetDword(pers, "ColorPrevalence", 0) == 1;
            _loading = false;
        }

        /// <summary>Puts every taskbar setting on this page back to Windows' own default.</summary>
        public void ResetToDefault()
        {
            chkCenter.IsChecked = true;
            chkSmallIcons.IsChecked = false;
            chkAutoHide.IsChecked = false;
            cmbSearch.SelectedIndex = 2;
            cmbCombine.SelectedIndex = 0;
            chkTaskView.IsChecked = true;
            chkWidgets.IsChecked = true;
            chkChat.IsChecked = true;
            chkShowDesktop.IsChecked = false;
            chkDark.IsChecked = false;
            chkTransparent.IsChecked = true;
            chkAccent.IsChecked = false;
            _loading = false;
            foreach (FrameworkElement c in new FrameworkElement[] { chkCenter, chkSmallIcons, chkAutoHide, cmbSearch, cmbCombine, chkTaskView, chkWidgets, chkChat, chkShowDesktop, chkDark, chkTransparent, chkAccent })
                Changed(c, new RoutedEventArgs());
            txtStatus.Text = "The taskbar settings are back to Windows' defaults. Press Restart Explorer to see them all.";
        }

        private const string FeedsKey = @"Software\Microsoft\Windows\CurrentVersion\Feeds";

        /// <summary>Hides a setting and the divider under it.</summary>
        private static void HideRow(FrameworkElement row)
        {
            row.Visibility = Visibility.Collapsed;
            if (row.Parent is Panel panel)
            {
                int i = panel.Children.IndexOf(row);
                if (i >= 0 && i + 1 < panel.Children.Count && panel.Children[i + 1] is Border) panel.Children[i + 1].Visibility = Visibility.Collapsed;
                else if (i > 0 && panel.Children[i - 1] is Border) panel.Children[i - 1].Visibility = Visibility.Collapsed;
            }
        }

        private static int Flag(CheckBox c) => c.IsChecked == true ? 1 : 0;

        // Shared handler: CheckBox.Click and ComboBox.SelectionChanged both pass a RoutedEventArgs-derived type.
        private void Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            const string adv = TaskbarService.Adv;
            const string pers = TaskbarService.Personalize;

            try
            {
                switch (((FrameworkElement)sender).Name)
                {
                    case nameof(chkCenter): TaskbarService.SetDword(adv, "TaskbarAl", Flag(chkCenter)); break;
                    case nameof(chkSmallIcons): TaskbarService.SetDword(adv, "TaskbarSi", chkSmallIcons.IsChecked == true ? 0 : 1); break;
                    case nameof(chkAutoHide): TaskbarService.SetAutoHide(chkAutoHide.IsChecked == true); break;
                    case nameof(cmbSearch): TaskbarService.SetDword(TaskbarService.Search, "SearchboxTaskbarMode", cmbSearch.SelectedIndex); break;
                    case nameof(cmbCombine):
                        TaskbarService.SetDword(adv, "TaskbarGlomLevel", cmbCombine.SelectedIndex);
                        TaskbarService.SetDword(adv, "MMTaskbarGlomLevel", cmbCombine.SelectedIndex);
                        break;
                    case nameof(chkTaskView): TaskbarService.SetDword(adv, "ShowTaskViewButton", Flag(chkTaskView)); break;
                    case nameof(chkWidgets):
                        if (OsInfo.IsWindows11) TaskbarService.SetDword(adv, "TaskbarDa", Flag(chkWidgets));
                        else TaskbarService.SetDword(FeedsKey, "ShellFeedsTaskbarViewMode", chkWidgets.IsChecked == true ? 0 : 2);
                        break;
                    case nameof(chkChat): TaskbarService.SetDword(adv, "TaskbarMn", Flag(chkChat)); break;
                    case nameof(chkShowDesktop): TaskbarService.SetDword(adv, "TaskbarSd", Flag(chkShowDesktop)); break;
                    case nameof(chkDark): TaskbarService.SetDword(pers, "SystemUsesLightTheme", chkDark.IsChecked == true ? 0 : 1); break;
                    case nameof(chkTransparent): TaskbarService.SetDword(pers, "EnableTransparency", Flag(chkTransparent)); break;
                    case nameof(chkAccent): TaskbarService.SetDword(pers, "ColorPrevalence", Flag(chkAccent)); break;
                }
                txtStatus.Text = "Saved. If the taskbar doesn't update, click Restart Explorer.";
            }
            catch (Exception ex)
            {
                // Windows protects some of these keys on newer builds; report instead of crashing.
                txtStatus.Text = $"Could not change that setting: {ex.Message}";
            }
        }

        private async void RestartExplorerClicked(object sender, RoutedEventArgs e)
        {
            TaskbarService.RestartExplorer();
            txtStatus.Text = "Explorer restarted. The taskbar will reappear in a moment.";

            // Restarting Explorer rebuilds the desktop layer, which also removes a running live wallpaper.
            if (CyberOptix.Models.AppSettings.Current.LiveEnabled)
            {
                await System.Threading.Tasks.Task.Delay(3000);
                LiveWallpaperService.Restore();
            }
        }
    }
}
