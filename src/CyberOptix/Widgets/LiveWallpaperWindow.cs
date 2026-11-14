using System.Linq;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>A full-screen window that lives behind the desktop icons and plays a video, GIF or web page.</summary>
    public class LiveWallpaperWindow : Window
    {
        private readonly string _kind;
        private readonly string _source;
        private readonly bool _mute;
        private MediaElement? _video;
        private WebView2? _web;
        private bool _paused;

        public event Action<string>? Failed;

        public IntPtr Handle => new WindowInteropHelper(this).Handle;

        public LiveWallpaperWindow(string kind, string source, bool mute)
        {
            _kind = kind;
            _source = source;
            _mute = mute;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Background = Brushes.Black;
            Left = 0; Top = 0; Width = 320; Height = 200; // real size is set natively when attached

            SourceInitialized += (_, _) =>
            {
                if (!DesktopHost.Attach(Handle))
                    Failed?.Invoke("Could not find the Windows desktop to draw behind.");
            };
            Loaded += async (_, _) => await StartContentAsync();
            Closed += (_, _) => { _web?.Dispose(); };
        }

        private async Task StartContentAsync()
        {
            try
            {
                if (_kind == "Video") StartVideo();
                else await StartWebAsync();
            }
            catch (Exception ex)
            {
                Failed?.Invoke(ex.Message);
            }
        }

        private void StartVideo()
        {
            if (!File.Exists(_source)) throw new FileNotFoundException("Video file not found.", _source);

            _video = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.UniformToFill,
                Volume = _mute ? 0 : 0.5,
                IsMuted = _mute,
                Source = new Uri(_source),
            };
            _video.MediaEnded += (_, _) => { _video.Position = TimeSpan.Zero; _video.Play(); };
            _video.MediaFailed += (_, e) => Failed?.Invoke("This video could not be played: " + e.ErrorException.Message);
            Content = _video;
            _video.Play();
        }

        private async Task StartWebAsync()
        {
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CyberOptix", "webview");
            _web = new WebView2
            {
                DefaultBackgroundColor = System.Drawing.Color.Black,
                CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = dataFolder },
            };
            Content = _web;
            try
            {
                await _web.EnsureCoreWebView2Async();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                // Windows 11 includes it; some Windows 10 PCs do not.
                throw new InvalidOperationException("Live wallpaper needs Microsoft's free WebView2 Runtime, which this PC doesn't have. Install it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 and press Start again.");
            }
            _web.CoreWebView2.IsMuted = _mute;
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            // A wallpaper is only ever shown, never used: no shortcuts, autofill, host objects, messages or extra windows.
            _web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            _web.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            _web.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            _web.CoreWebView2.Settings.IsWebMessageEnabled = false;
            _web.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            _web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            _web.CoreWebView2.NavigationStarting += (_, e) =>
            {
                var uri = e.Uri ?? "";
                bool ok = uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                          uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase);
                // A web address wallpaper stays on the host (or its sub-domains) the user approved: a redirect or a link elsewhere is refused.
                var remote = LiveWallpaperService.RemoteHost(_kind, _source);
                if (ok && remote != null && uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var host = Uri.TryCreate(uri, UriKind.Absolute, out var nav) ? nav.IdnHost.ToLowerInvariant() : "";
                    bool onSite = host == remote || host.EndsWith("." + remote, StringComparison.Ordinal);
                    if (!onSite && !host.Equals("wallpaper.local", StringComparison.OrdinalIgnoreCase)) ok = false;
                }
                if (!ok) e.Cancel = true;
            };

            if (_kind == "Gif")
            {
                if (!File.Exists(_source)) throw new FileNotFoundException("GIF file not found.", _source);
                _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "wallpaper.local", Path.GetDirectoryName(_source)!, CoreWebView2HostResourceAccessKind.Allow);
                var url = "https://wallpaper.local/" + Uri.EscapeDataString(Path.GetFileName(_source));
                _web.NavigateToString(
                    "<html><body style='margin:0;background:#000;overflow:hidden'>" +
                    $"<img src='{url}' style='width:100vw;height:100vh;object-fit:cover'></body></html>");
            }
            else if (_source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                _web.CoreWebView2.Navigate(_source);
            }
            else
            {
                if (_source.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Only secure web addresses (https://) can be used.");
                if (!File.Exists(_source)) throw new FileNotFoundException("Web page file not found.", _source);
                // DenyCors: the page can load its own files, but no other site can read them.
                _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "wallpaper.local", Path.GetDirectoryName(_source)!, CoreWebView2HostResourceAccessKind.DenyCors);
                _web.CoreWebView2.Navigate("https://wallpaper.local/" + Uri.EscapeDataString(Path.GetFileName(_source)));
            }
        }

        /// <summary>Stops decoding/rendering while something else (a game) owns the screen.</summary>
        public void SetPaused(bool paused)
        {
            if (paused == _paused) return;
            _paused = paused;

            if (_video != null)
            {
                if (paused) _video.Pause(); else _video.Play();
            }
            if (_web?.CoreWebView2 != null)
            {
                if (paused)
                {
                    _web.Visibility = Visibility.Collapsed;
                    _ = _web.CoreWebView2.TrySuspendAsync();
                }
                else
                {
                    _web.CoreWebView2.Resume();
                    _web.Visibility = Visibility.Visible;
                }
            }
        }
    }
}
