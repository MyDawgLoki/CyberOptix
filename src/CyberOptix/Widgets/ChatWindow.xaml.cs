using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>
    /// The floating Chat window: a tab for each of five AI platforms plus the user's own local LLM, each showing that platform's own web
    /// page (so sign-in, voice input and its transcription are the platform's own, not re-built here). It sits in front of or behind
    /// other windows the same way an individual dock does ("Fall Behind Window" / "Stick On Top Of Window"), set on the AI Agents page.
    /// </summary>
    public partial class ChatWindow : Window
    {
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        /// <summary>The platforms the tab strip offers, in the order they appear.</summary>
        private static readonly (string Id, string Name)[] Platforms =
        {
            ("grok", "Grok"), ("perplexity", "Perplexity"), ("chatgpt", "ChatGPT"), ("claude", "Claude"), ("gemini", "Gemini"), ("localllm", "Local LLM"),
        };

        /// <summary>The web hosts of the five AI platforms (from BrandLaunchers), so the microphone can be granted to them without asking.</summary>
        private static readonly HashSet<string> BrandHosts = new(
            Platforms.Where(p => p.Id != "localllm")
                .Select(p => BrandLaunchers.Find(p.Id)?.Url)
                .Where(u => u != null)
                .Select(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) ? uri.IdnHost.ToLowerInvariant() : null)
                .Where(h => h != null)!);

        private readonly DockLayerController _layerCtl = new();
        private readonly DispatcherTimer _layerTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
        private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
        private readonly Dictionary<string, Border> _tabs = new();
        private string _appliedLayer = "";
        private string _platform = AppSettings.Current.AiChatPlatform;
        private WebView2? _web;
        private bool _webReady;
        private bool _restoringBounds = true;

        public ChatWindow()
        {
            InitializeComponent();

            var s = AppSettings.Current;
            Width = Math.Max(MinWidth, s.AiChatWidth);
            Height = Math.Max(MinHeight, s.AiChatHeight);
            if (!Platforms.Any(p => p.Id == _platform)) _platform = "chatgpt";

            SourceInitialized += (_, _) => { PlaceInitial(); _restoringBounds = false; };
            Loaded += async (_, _) =>
            {
                BuildTabs();
                _layerTimer.Tick += (_, _) => LayerTick();
                _layerTimer.Start();
                LayerTick();
                await EnsureWebAsync();
                NavigateToPlatform(_platform);
            };
            SizeChanged += (_, _) => { if (!_restoringBounds) ScheduleSave(); };
            LocationChanged += (_, _) => { if (!_restoringBounds) ScheduleSave(); };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveBounds(); };
            Closed += (_, _) =>
            {
                _layerTimer.Stop();
                _saveTimer.Stop();
                SaveBounds();
                _web?.Dispose();
            };
        }

        /// <summary>Centers the window the first time it ever opens; after that it reopens where it was left (clamped onto the screen).</summary>
        private void PlaceInitial()
        {
            var s = AppSettings.Current;
            var area = SystemParameters.WorkArea;
            if (s.AiChatX < 0 || s.AiChatY < 0)
            {
                Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
                Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
            }
            else
            {
                Left = Math.Min(Math.Max(s.AiChatX, area.Left), Math.Max(area.Left, area.Right - 80));
                Top = Math.Min(Math.Max(s.AiChatY, area.Top), Math.Max(area.Top, area.Bottom - 80));
            }
        }

        private void ScheduleSave() { _saveTimer.Stop(); _saveTimer.Start(); }

        private void SaveBounds()
        {
            var s = AppSettings.Current;
            s.AiChatX = Left; s.AiChatY = Top; s.AiChatWidth = Width; s.AiChatHeight = Height;
            AppSettings.Save();
        }

        private void CloseClicked(object sender, RoutedEventArgs e) => Close();

        // ---------- platform tabs ----------

        private void BuildTabs()
        {
            pnlTabs.Children.Clear();
            _tabs.Clear();
            foreach (var (id, name) in Platforms)
            {
                var item = new Border
                {
                    Width = 32, Height = 28, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent,
                    Cursor = Cursors.Hand, Margin = new Thickness(2, 0, 2, 0), Child = PlatformIcon(id, 18), ToolTip = name,
                };
                item.MouseEnter += (_, _) => { if (_platform != id) item.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)); };
                item.MouseLeave += (_, _) => { if (_platform != id) item.Background = Brushes.Transparent; };
                item.MouseLeftButtonUp += (_, _) => SwitchPlatform(id);
                _tabs[id] = item;
                pnlTabs.Children.Add(item);
            }
            HighlightTab();
        }

        private void HighlightTab()
        {
            foreach (var (id, border) in _tabs)
                border.Background = id == _platform ? new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
            pnlLogo.Children.Clear();
            pnlLogo.Children.Add(PlatformIcon(_platform, 20));
        }

        private static FrameworkElement PlatformIcon(string id, double size) =>
            id == "localllm" ? LocalLlmGlyph(size) : BrandIcon.Create(id, size);

        /// <summary>A plain "device" glyph for the Local LLM tab: there is no brand logo for the user's own local model.</summary>
        private static FrameworkElement LocalLlmGlyph(double size) => new TextBlock
        {
            Text = "", FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = size,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        private void SwitchPlatform(string id)
        {
            if (id == _platform) return;
            _platform = id;
            AppSettings.Current.AiChatPlatform = id;
            AppSettings.Save();
            HighlightTab();
            NavigateToPlatform(id);
        }

        // ---------- the web page ----------

        private async System.Threading.Tasks.Task EnsureWebAsync()
        {
            if (_web != null) return;
            var dataFolder = Path.Combine(AppPaths.Folder, "aichat-webview");
            _web = new WebView2 { CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = dataFolder } };
            webHost.Children.Add(_web);
            try
            {
                await _web.EnsureCoreWebView2Async();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                ShowEmpty("The Chat window needs Microsoft's free WebView2 Runtime. Install it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 and reopen the Chat window.");
                return;
            }
            catch { return; }

            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;   // no passwords or form data are kept by this window: only the sign-in session
            core.Settings.IsGeneralAutofillEnabled = false;
            core.NewWindowRequested += NewWindowRequested;
            core.PermissionRequested += PermissionRequested;
            core.NavigationStarting += NavigationStarting;
            core.WebResourceRequested += WebResourceRequested;   // only does anything while the Local LLM tab has a key filter (see UpdateKeyFilter)
            _webReady = true;
        }

        private void ShowEmpty(string message)
        {
            if (_web != null) _web.Visibility = Visibility.Collapsed;
            txtEmpty.Text = message;
            txtEmpty.Visibility = Visibility.Visible;
        }

        private void NavigateToPlatform(string id)
        {
            if (!_webReady || _web?.CoreWebView2 == null) return;

            UpdateKeyFilter(false);
            if (id == "localllm")
            {
                var url = AppSettings.Current.AiChatLocalLlmUrl;
                if (!TryApprovedLocalHost(url, out _))
                {
                    ShowEmpty("Set and approve a local LLM address on the AI Agents page, then come back to this tab.");
                    return;
                }
                txtEmpty.Visibility = Visibility.Collapsed;
                _web.Visibility = Visibility.Visible;
                UpdateKeyFilter(true);
                _web.CoreWebView2.Navigate(url);
                return;
            }

            var launcher = BrandLaunchers.Find(id);
            if (launcher?.Url == null) { ShowEmpty(Platforms.FirstOrDefault(p => p.Id == id).Name + " is not available."); return; }
            txtEmpty.Visibility = Visibility.Collapsed;
            _web.Visibility = Visibility.Visible;
            _web.CoreWebView2.Navigate(launcher.Url);
        }

        private static bool TryApprovedLocalHost(string? url, out string host)
        {
            host = "";
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
            if (u.Scheme != "http" && u.Scheme != "https") return false;
            host = u.IdnHost.ToLowerInvariant();
            return AppSettings.Current.AiChatApprovedHosts.Contains(host);
        }

        private static bool HostMatches(string host, string approved) =>
            host.Length > 0 && (host == approved || host.EndsWith("." + approved, StringComparison.Ordinal));

        private string? _keyFilter;
        private string _llmKey = "";
        private Uri? _llmBase;

        /// <summary>
        /// The local LLM's key is read (decrypted) once when its tab opens, and requests are only looked at for that address
        /// (scheme, host and port), not for every request of every page.
        /// </summary>
        private void UpdateKeyFilter(bool forLocalLlm)
        {
            var core = _web?.CoreWebView2;
            if (core == null) return;
            if (_keyFilter != null)
            {
                try { core.RemoveWebResourceRequestedFilter(_keyFilter, CoreWebView2WebResourceContext.All); } catch { }
                _keyFilter = null;
            }
            _llmKey = "";
            _llmBase = null;
            if (!forLocalLlm) return;

            var s = AppSettings.Current;
            if (!Uri.TryCreate(s.AiChatLocalLlmUrl, UriKind.Absolute, out var b)) return;
            var key = SecretProtector.Unprotect(s.AiChatLocalLlmKeyEnc);
            if (string.IsNullOrEmpty(key)) return;
            _llmKey = key;
            _llmBase = b;
            _keyFilter = b.GetLeftPart(UriPartial.Authority) + "/*";
            core.AddWebResourceRequestedFilter(_keyFilter, CoreWebView2WebResourceContext.All);
        }

        /// <summary>Only the local LLM's own address (same scheme, host and port) is sent the bearer token, and only when one has been set for it.</summary>
        private void WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (_llmBase == null || _llmKey.Length == 0) return;
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var req)) return;
            if (!string.Equals(req.Scheme, _llmBase.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(req.IdnHost, _llmBase.IdnHost, StringComparison.OrdinalIgnoreCase) ||
                req.Port != _llmBase.Port) return;
            e.Request.Headers.SetHeader("Authorization", "Bearer " + _llmKey);
        }

        /// <summary>The microphone is granted without asking, but only to the five AI platforms and an approved local LLM address. Everything else is denied.</summary>
        private void PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            e.Handled = true;
            if (e.PermissionKind != CoreWebView2PermissionKind.Microphone) { e.State = CoreWebView2PermissionState.Deny; return; }
            var host = Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) ? u.IdnHost.ToLowerInvariant() : "";
            bool allowed = BrandHosts.Any(h => HostMatches(host, h)) || AppSettings.Current.AiChatApprovedHosts.Any(h => HostMatches(host, h));
            e.State = allowed ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
        }

        /// <summary>A platform's own tab can go anywhere a normal sign-in needs (its web address is not locked down). The Local LLM tab
        /// stays on the address the user approved: it is the user's own address, not a reviewed public site.</summary>
        private void NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (_platform != "localllm") return;
            var uri = e.Uri ?? "";
            bool ok = uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            if (ok && Uri.TryCreate(AppSettings.Current.AiChatLocalLlmUrl, UriKind.Absolute, out var baseUri) && Uri.TryCreate(uri, UriKind.Absolute, out var navUri))
                ok = HostMatches(navUri.IdnHost.ToLowerInvariant(), baseUri.IdnHost.ToLowerInvariant());
            if (!ok) e.Cancel = true;
        }

        /// <summary>A sign-in popup (Google, Microsoft and the like open one): given its own small window sharing the same session, so
        /// cookies set there carry straight back to the main tab. It closes itself once the site is done with it.</summary>
        private void NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            var wanted = e.Uri ?? "";
            if (!wanted.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !wanted.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase))
            {
                e.Handled = true;   // a link to anything but a secure web address opens nothing
                return;
            }
            var deferral = e.GetDeferral();
            e.Handled = true;
            var popup = new Window
            {
                Width = 480, Height = 640, Title = "Sign in", Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            };
            var popupWeb = new WebView2 { CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = Path.Combine(AppPaths.Folder, "aichat-webview") } };
            popup.Content = popupWeb;
            popup.Closed += (_, _) => popupWeb.Dispose();
            _ = popupWeb.EnsureCoreWebView2Async().ContinueWith(t => Dispatcher.Invoke(() =>
            {
                if (t.IsFaulted || popupWeb.CoreWebView2 == null) { deferral.Complete(); popup.Close(); return; }
                e.NewWindow = popupWeb.CoreWebView2;
                var pc = popupWeb.CoreWebView2;
                pc.Settings.AreDevToolsEnabled = false;
                pc.Settings.AreHostObjectsAllowed = false;
                pc.Settings.IsWebMessageEnabled = false;
                pc.Settings.IsPasswordAutosaveEnabled = false;
                pc.Settings.IsGeneralAutofillEnabled = false;
                pc.NavigationStarting += (_, n) =>
                {
                    var u = n.Uri ?? "";
                    if (!u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !u.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase)) n.Cancel = true;
                };
                popupWeb.CoreWebView2.WindowCloseRequested += (_, _) => popup.Close();
                deferral.Complete();
                popup.Show();
            }));
        }

        // ---------- layer: in front, behind (hover brings it forward), or behind (only the Start menu brings it forward) ----------

        private void LayerTick()
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            string layer = AppSettings.Current.AiChatLayer is "Top" or "Stick" ? AppSettings.Current.AiChatLayer : "Fall";
            if (layer != _appliedLayer)
            {
                _appliedLayer = layer;
                _layerCtl.Reset();
                Topmost = layer == "Top";
                if (layer == "Top") Raise(); else SendBack();
            }
            if (layer == "Top") return;

            bool startOpen = StartMenuService.IsOpen(out _);
            bool over = layer == "Fall" && PointerOverThisWindow(h, _layerCtl.InFront);
            switch (_layerCtl.Update(layer, startOpen, over, ScreenProbe.ButtonDown(), false, DateTime.UtcNow))
            {
                case DockLayerController.Move.Raise: Raise(); break;
                case DockLayerController.Move.Lower: SendBack(); break;
            }
        }

        private void Raise() => SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);

        private void SendBack()
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero) SetWindowPos(h, (IntPtr)1, 0, 0, 0, 0, 0x13);
        }

        private bool PointerOverThisWindow(IntPtr h, bool alreadyOut)
        {
            if (!ScreenProbe.Pointer(out var px, out var py)) return false;
            if (!ScreenProbe.WindowRect(h, out var l, out var t, out var r, out var b)) return false;
            double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
            int pad = alreadyOut ? (int)Math.Round(14 * dpi) : 0;
            return px >= l - pad && px <= r + pad && py >= t - pad && py <= b + pad;
        }
    }
}
