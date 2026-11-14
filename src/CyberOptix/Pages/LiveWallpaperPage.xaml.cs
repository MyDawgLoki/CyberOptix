using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Pages
{
    public partial class LiveWallpaperPage : UserControl
    {
        private bool _loading = true;

        public LiveWallpaperPage() => InitializeComponent();

        private string Kind => rbGif.IsChecked == true ? "Gif" : rbWeb.IsChecked == true ? "Web" : "Video";

        public void Initialize()
        {
            _loading = true;
            var s = AppSettings.Current;
            rbVideo.IsChecked = s.LiveKind == "Video";
            rbGif.IsChecked = s.LiveKind == "Gif";
            rbWeb.IsChecked = s.LiveKind == "Web";
            txtSource.Text = s.LiveSource ?? "";
            chkMute.IsChecked = s.LiveMute;
            chkPauseFs.IsChecked = s.LivePauseFullscreen;
            chkPauseMax.IsChecked = s.LivePauseMaximized;
            _loading = false;

            LiveWallpaperService.Error += message =>
            {
                Dispatcher.Invoke(() =>
                {
                    txtStatus.Text = message;
                    UpdateButtons();
                });
            };

            UpdateKindText();
            if (s.LiveEnabled) LiveWallpaperService.Restore();
            UpdateButtons();
            if (LiveWallpaperService.IsRunning) txtStatus.Text = "Live wallpaper is running.";
        }

        private void UpdateKindText()
        {
            switch (Kind)
            {
                case "Gif":
                    rowSource.Title = "GIF file";
                    rowSource.Description = "A .gif image that fills the screen.";
                    txtSource.Tag = "Choose a .gif file";
                    break;
                case "Web":
                    rowSource.Title = "Web page";
                    rowSource.Description = "A local .html file, or a web address starting with https://.";
                    txtSource.Tag = "Choose a file or paste an address";
                    break;
                default:
                    rowSource.Title = "Video file";
                    rowSource.Description = "An .mp4 video. It loops silently by default.";
                    txtSource.Tag = "Choose an .mp4 file";
                    break;
            }
        }

        private void UpdateButtons()
        {
            btnStop.IsEnabled = LiveWallpaperService.IsRunning;
            btnStart.Content = LiveWallpaperService.IsRunning ? "Restart live wallpaper" : "Start live wallpaper";
        }

        private void KindChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.LiveKind = Kind;
            AppSettings.Current.LiveSource = null; // a source from another type would not play
            txtSource.Text = "";
            UpdateKindText();
            AppSettings.Save();
        }

        private void SourceChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.LiveSource = txtSource.Text.Trim();
        }

        private void OptionChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var s = AppSettings.Current;
            s.LiveMute = chkMute.IsChecked == true;
            s.LivePauseFullscreen = chkPauseFs.IsChecked == true;
            s.LivePauseMaximized = chkPauseMax.IsChecked == true;
            AppSettings.Save();
        }

        private void BrowseClicked(object sender, RoutedEventArgs e)
        {
            var filter = Kind switch
            {
                "Gif" => "GIF images|*.gif",
                "Web" => "Web pages|*.html;*.htm",
                _ => "Videos|*.mp4;*.m4v;*.mov;*.wmv",
            };
            var dlg = new OpenFileDialog { Filter = filter };
            if (dlg.ShowDialog() == true) txtSource.Text = dlg.FileName;
        }

        private void StartClicked(object sender, RoutedEventArgs e)
        {
            var source = txtSource.Text.Trim();
            if (source.Length == 0) { txtStatus.Text = "Choose a file first."; return; }

            var host = LiveWallpaperService.RemoteHost(Kind, source);
            if (host != null)
            {
                if (host.Length == 0) { txtStatus.Text = "That is not a valid web address."; return; }
                if (!LiveWallpaperService.IsApproved(host))
                {
                    var ask = MessageBox.Show(
                        "A web page wallpaper runs a real web page inside CyberOptix, and CyberOptix has administrator rights.\n\n" +
                        "Only approve an address you trust completely: " + host + "\n\n" +
                        "The page can only stay on that site; it cannot open windows or talk to CyberOptix. A video or GIF is the safer choice.\n\nApprove " + host + " and start?",
                        "Approve this web address?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                    if (ask != MessageBoxResult.Yes) { txtStatus.Text = "Not approved, so it was not started."; return; }
                    LiveWallpaperService.Approve(host);
                }
            }

            var s = AppSettings.Current;
            s.LiveKind = Kind;
            s.LiveSource = source;
            s.LiveEnabled = true;
            AppSettings.Save();

            txtStatus.Text = "Starting...";
            LiveWallpaperService.Start(Kind, source);
            UpdateButtons();
            txtStatus.Text = "Live wallpaper is running. It stops when you close CyberOptix.";
        }

        private void ForgetClicked(object sender, RoutedEventArgs e)
        {
            int n = AppSettings.Current.LiveApprovedSites.Count;
            AppSettings.Current.LiveApprovedSites.Clear();
            AppSettings.Save();
            if (LiveWallpaperService.RemoteHost(Kind, txtSource.Text.Trim()) != null) { AppSettings.Current.LiveEnabled = false; LiveWallpaperService.Stop(); }
            UpdateButtons();
            txtStatus.Text = n == 0 ? "No web addresses were approved." : $"Forgot {n} approved web address{(n == 1 ? "" : "es")}.";
        }

        private void StopClicked(object sender, RoutedEventArgs e)
        {
            AppSettings.Current.LiveEnabled = false;
            AppSettings.Save();
            LiveWallpaperService.Stop();
            UpdateButtons();
            txtStatus.Text = "Live wallpaper stopped.";
        }
    }
}
