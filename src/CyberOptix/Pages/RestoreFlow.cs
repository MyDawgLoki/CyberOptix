using System;
using System.Threading.Tasks;
using System.Windows;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Pages
{
    /// <summary>"Restore original Windows settings": shows what changed, puts back what the user ticks, then restarts CyberOptix.</summary>
    public static class RestoreFlow
    {
        /// <summary>Runs the whole flow. Returns a message to show, or null when CyberOptix is restarting.</summary>
        public static async Task<string?> RunAsync(Window? owner)
        {
            if (!OriginalSettingsService.HasSnapshot)
                return "CyberOptix has no saved copy of your original settings yet. Close and reopen it, then try again.";

            var dialog = new RestoreDialog(OriginalSettingsService.WhatChanged()) { Owner = owner };
            if (dialog.ShowDialog() != true) return "";
            var choice = dialog.Result;
            if (!choice.Any) return "Nothing was ticked, so nothing was changed.";

            var summary = await Task.Run(() => OriginalSettingsService.RestoreAll(choice));
            if (summary.StartsWith("Administrator") || summary.StartsWith("CyberOptix has no saved")) return summary;

            // The Optimizer switches go back to a fresh start when the tweaks were undone, so they match Windows again.
            if (choice.Tweaks && choice.Services)
            {
                AppSettings.Current.GamingProfile = SettingsProfile.DefaultGaming();
                AppSettings.Current.GeneralProfile = SettingsProfile.DefaultGeneral();
                AppSettings.Save();
            }
            MessageBox.Show(summary + "\n\nCyberOptix will restart now.", "Restore original Windows settings", MessageBoxButton.OK, MessageBoxImage.Information);
            return AppRestart.Now() ? null : summary;
        }
    }
}
