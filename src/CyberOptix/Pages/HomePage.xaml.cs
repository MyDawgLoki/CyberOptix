using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Pages
{
    /// <summary>Home: one-click choices to optimize Windows, restore the original settings, or reset CyberOptix's own settings.</summary>
    public partial class HomePage : UserControl
    {
        /// <summary>The catalog tweaks "Turn off Windows bloatware" switches on: ads, suggestions, telemetry and similar background noise.</summary>
        private static readonly string[] Bloat =
        {
            "widgets", "feeds", "tips", "searchhighlights", "searchsuggest", "websearch", "optionaldiag", "diagtrack", "advertisingid", "tailored", "activityhistory", "deliveryopt",
        };

        private static readonly string[] DisplayTweaks = { "transparency", "animations" };

        public HomePage() => InitializeComponent();

        private static bool Confirm(string text, string title) =>
            MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

        private async void BloatClicked(object sender, RoutedEventArgs e)
        {
            if (!Confirm("Turn off the Windows ads, suggestions, telemetry and similar background items?\n\nA restore point is made first, and you can undo this with Restore original Windows settings.", "Turn off Windows bloatware")) return;
            btnBloat.IsEnabled = false;
            txtStatus.Text = "Turning off Windows bloatware (this takes a moment)...";
            try
            {
                await Task.Run(() => TweakService.ApplyTweaks(Bloat));
                txtStatus.Text = "Done. The Windows bloatware settings are turned off. Some of them take effect after you sign out or restart.";
                (Application.Current.MainWindow as MainWindow)?.ReloadOptimizer();
            }
            catch (Exception ex) { txtStatus.Text = "That did not finish: " + ex.Message; }
            btnBloat.IsEnabled = true;
        }

        private async void DisplayClicked(object sender, RoutedEventArgs e)
        {
            if (!Confirm("Turn off transparency and animation effects, and apply your Gaming profile's display colors to the main display?\n\nA restore point is made first.", "Optimize display settings")) return;
            btnDisplay.IsEnabled = false;
            txtStatus.Text = "Optimizing the display settings...";
            try
            {
                await Task.Run(() => TweakService.ApplyTweaks(DisplayTweaks));
                var note = DisplayColorService.ApplyProfileToMainDisplay(AppSettings.Current.GamingProfile);   // talks to the display on the UI thread
                txtStatus.Text = "Done. Effects are turned off and the display colors are applied" + (note != null ? $". Not applied: {note}" : ".");
                (Application.Current.MainWindow as MainWindow)?.ReloadOptimizer();
            }
            catch (Exception ex) { txtStatus.Text = "That did not finish: " + ex.Message; }
            btnDisplay.IsEnabled = true;
        }

        private async void RestoreClicked(object sender, RoutedEventArgs e)
        {
            btnRestore.IsEnabled = false;
            var message = await RestoreFlow.RunAsync(Window.GetWindow(this));
            btnRestore.IsEnabled = true;
            if (message != null) txtStatus.Text = message;
        }

        private void ResetAllClicked(object sender, RoutedEventArgs e)
        {
            if (!Confirm("Put every CyberOptix setting back to its default?\n\nThe programs, folders and games you added are kept, and Windows itself is not changed. CyberOptix restarts afterward.", "Reset all settings")) return;
            try
            {
                SettingsDefaults.ResetAll();
                if (!AppRestart.Now()) txtStatus.Text = "The settings are back to default. Close and reopen CyberOptix to see them all.";
            }
            catch (Exception ex) { txtStatus.Text = "That did not finish: " + ex.Message; }
        }
    }
}
