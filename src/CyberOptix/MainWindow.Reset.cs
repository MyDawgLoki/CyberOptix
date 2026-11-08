using System;
using System.Windows;
using System.Windows.Controls;
using CyberOptix.Models;
using CyberOptix.Services;
using CyberOptix.Widgets;

namespace CyberOptix
{
    /// <summary>The "Reset this page to default" button at the bottom of each page, and what it does for each page.</summary>
    public partial class MainWindow
    {
        /// <summary>Re-reads the Optimizer's saved profiles (after Home or a reset changed them).</summary>
        public void ReloadOptimizer()
        {
            _gamingDraft = AppSettings.Current.GamingProfile.Clone();
            _generalDraft = AppSettings.Current.GeneralProfile.Clone();
            ShowProfile();
            UpdateDirty();
        }

        private void AddResetButtons()
        {
            AddResetButton(stkOptimizer, "Optimizer");
            AddResetButton(StackOf(pgTasks), "Tasks");
            AddResetButton(StackOf(pgDisplay), "Display");
            AddResetButton(StackOf(pgWidgetsTab), "Widgets");
            AddResetButton(StackOf(pgHardware), "Hardware");
            AddResetButton(StackOf(pgDock), "Dock");
            AddResetButton(StackOf(pgTaskbar), "Taskbar");
            AddResetButton(StackOf(pgWindows), "Windows");
            AddResetButton(StackOf(pgSettings), "Settings");
        }

        /// <summary>The panel a page stacks its content in (a page is a scroll viewer holding one stack).</summary>
        private static StackPanel? StackOf(UserControl page) => (page.Content as ScrollViewer)?.Content as StackPanel;

        private void AddResetButton(StackPanel? stack, string page)
        {
            if (stack == null) return;
            var button = new Button
            {
                Content = "Reset this page to default", Margin = new Thickness(0, 28, 0, 8), HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = "Puts only the settings on this page back to CyberOptix's defaults. Nothing on the other pages changes.",
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(button, "reset_" + page);
            button.Click += (_, _) =>
            {
                if (MessageBox.Show("Put the settings on this page back to their defaults?\n\nNothing on the other pages changes.", "Reset this page",
                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                ResetPage(page);
            };
            stack.Children.Add(button);
        }

        private void ResetPage(string page)
        {
            try
            {
                switch (page)
                {
                    case "Optimizer":
                        SettingsDefaults.Reset(SettingsDefaults.Optimizer);
                        ReloadOptimizer();
                        break;
                    case "Tasks":
                        SettingsDefaults.Reset(SettingsDefaults.TaskManager);
                        AppRestart.Now();   // its lists are built when it opens, so start fresh
                        break;
                    case "Display":
                        SettingsDefaults.Reset(SettingsDefaults.Display);
                        LiveWallpaperService.Stop();
                        DesktopClockService.Apply();
                        pgDisplay.Initialize();
                        break;
                    case "Widgets":
                        SettingsDefaults.Reset(SettingsDefaults.Widgets);
                        WidgetManager.Apply();
                        pgWidgetsTab.Initialize();
                        break;
                    case "Hardware":
                        SettingsDefaults.Reset(SettingsDefaults.Hardware);
                        pgHardware.Initialize();
                        break;
                    case "Dock":
                        SettingsDefaults.Reset(SettingsDefaults.Dock);
                        pgDock.Initialize();
                        DockService.Apply();
                        break;
                    case "Taskbar":
                        pgTaskbar.ResetToDefault();   // these are Windows' own settings: back to Windows' defaults
                        break;
                    case "Windows":
                        SettingsDefaults.Reset(SettingsDefaults.WindowManager);
                        WindowManagerService.Disable();
                        pgWindows.Initialize();
                        break;
                    case "Settings":
                        SettingsDefaults.Reset(SettingsDefaults.AppSettingsPage);
                        ThemeService.Apply();
                        WindowsAccentService.Sync();
                        pgSettings.Initialize();
                        _ = System.Threading.Tasks.Task.Run(AutoStartService.Sync);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("That did not finish: " + ex.Message, "Reset this page", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
