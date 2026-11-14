using System;
using System.Windows.Threading;
using CyberOptix.Models;
using CyberOptix.Widgets;

namespace CyberOptix.Services
{
    /// <summary>Owns the single live-wallpaper window and pauses it while a game or fullscreen app is in front.</summary>
    public static class LiveWallpaperService
    {
        private static LiveWallpaperWindow? _window;
        private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromSeconds(1) };

        public static bool IsRunning => _window != null;
        public static bool IsPaused { get; private set; }

        /// <summary>Raised on the UI thread when the wallpaper fails to start or is stopped by the system.</summary>
        public static event Action<string>? Error;

        static LiveWallpaperService()
        {
            Timer.Tick += (_, _) => CheckPause();
        }

        /// <summary>The host of a web address wallpaper (kind Web and https://), or null for a file.</summary>
        public static string? RemoteHost(string kind, string source)
        {
            if (kind != "Web" || !source.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
            return Uri.TryCreate(source, UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host) ? u.IdnHost.ToLowerInvariant() : "";
        }

        public static bool IsApproved(string host) => host.Length > 0 && AppSettings.Current.LiveApprovedSites.Contains(host);

        public static void Approve(string host)
        {
            if (host.Length == 0 || IsApproved(host)) return;
            AppSettings.Current.LiveApprovedSites.Add(host);
            AppSettings.Save();
        }

        public static void Start(string kind, string source)
        {
            Stop();

            // A web address runs a web page with this app's administrator rights, so it only runs for a host the user approved.
            var remote = RemoteHost(kind, source);
            if (remote != null && !IsApproved(remote))
            {
                Error?.Invoke("That web address has not been approved. Press Start and confirm it first.");
                return;
            }
            var window = new LiveWallpaperWindow(kind, source, AppSettings.Current.LiveMute);
            window.Failed += message =>
            {
                if (_window == window) Stop();
                Error?.Invoke(message);
            };
            window.Closed += (_, _) =>
            {
                // Closed by something other than Stop(), e.g. Explorer restarting and taking the desktop with it.
                if (_window == window)
                {
                    _window = null;
                    Timer.Stop();
                    Error?.Invoke("The live wallpaper was closed by Windows. Press Start to bring it back.");
                }
            };
            _window = window;
            IsPaused = false;
            window.Show();
            Timer.Start();
        }

        /// <summary>Starts again from saved settings (used at launch and after Explorer restarts).</summary>
        public static void Restore()
        {
            var s = AppSettings.Current;
            if (s.LiveEnabled && !string.IsNullOrWhiteSpace(s.LiveSource))
                Start(s.LiveKind, s.LiveSource);
        }

        public static void Stop()
        {
            Timer.Stop();
            var window = _window;
            _window = null;
            IsPaused = false;
            window?.Close();
        }

        private static void CheckPause()
        {
            if (_window == null) return;
            var s = AppSettings.Current;

            DesktopHost.Query(_window.Handle, out var covers, out var maximized);
            bool pause = (s.LivePauseFullscreen && covers) || (s.LivePauseMaximized && (maximized || covers));

            _window.SetPaused(pause);
            IsPaused = pause;
        }
    }
}
