using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CyberOptix.Models;
using CyberOptix.Services;
using CyberOptix.Widgets;

namespace CyberOptix.Pages
{
    /// <summary>The settings for the AI Agents Chat window: which platforms it offers, where it sits, and the user's own local LLM.</summary>
    public partial class AiAgentsPage : UserControl
    {
        private static readonly (string Id, string Name)[] Platforms =
        {
            ("grok", "Grok"), ("perplexity", "Perplexity"), ("chatgpt", "ChatGPT"), ("claude", "Claude"), ("gemini", "Gemini"),
        };
        private static readonly string[] LayerKeys = { "Top", "Fall", "Stick" };

        private bool _loading = true;

        public AiAgentsPage() => InitializeComponent();

        public void Initialize()
        {
            _loading = true;
            var s = AppSettings.Current;

            chkEnabled.IsChecked = s.AiChatEnabled;

            cmbDefault.Items.Clear();
            foreach (var (id, name) in Platforms.Append(("localllm", "Local LLM"))) cmbDefault.Items.Add(new ComboBoxItem { Content = name, Tag = id });
            int sel = cmbDefault.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == s.AiChatPlatform);
            cmbDefault.SelectedIndex = Math.Max(0, sel);

            cmbLayer.Items.Clear();
            foreach (var name in new[] { "Stay On Top Of Windows", "Fall Behind Windows (Default)", "Stick Behind Windows" }) cmbLayer.Items.Add(new ComboBoxItem { Content = name });
            cmbLayer.SelectedIndex = Array.IndexOf(LayerKeys, s.AiChatLayer is "Top" or "Stick" ? s.AiChatLayer : "Fall");

            BuildPlatformRow();

            txtLocalUrl.Text = s.AiChatLocalLlmUrl;
            txtLocalKey.Password = "";
            UpdateLocalStatus();

            _loading = false;
        }

        private void BuildPlatformRow()
        {
            pnlPlatforms.Children.Clear();
            foreach (var (id, name) in Platforms)
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 24, 0) };
                item.Children.Add(BrandIcon.Create(id, 24));
                item.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
                pnlPlatforms.Children.Add(item);
            }
        }

        private void EnabledClicked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.AiChatEnabled = chkEnabled.IsChecked == true;
            AppSettings.Save();
            ChatWindowService.Apply();
        }

        private void DefaultPlatformChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || cmbDefault.SelectedItem is not ComboBoxItem item) return;
            AppSettings.Current.AiChatPlatform = (string)item.Tag;
            AppSettings.Save();
        }

        private void LayerChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || cmbLayer.SelectedIndex < 0) return;
            AppSettings.Current.AiChatLayer = LayerKeys[cmbLayer.SelectedIndex];
            AppSettings.Save();   // the Chat window reads it on its own, within a moment
        }

        private void OpenClicked(object sender, RoutedEventArgs e)
        {
            AppSettings.Current.AiChatEnabled = true;
            AppSettings.Save();
            chkEnabled.IsChecked = true;
            ChatWindowService.Apply();
        }

        private void UpdateLocalStatus()
        {
            var s = AppSettings.Current;
            bool approved = s.AiChatLocalLlmUrl.Length > 0 && Uri.TryCreate(s.AiChatLocalLlmUrl, UriKind.Absolute, out var u) &&
                             s.AiChatApprovedHosts.Contains(u.IdnHost.ToLowerInvariant());
            string key = s.AiChatLocalLlmKeyEnc.Length > 0 ? " An API key is saved for it." : "";
            txtLocalStatus.Text = approved ? "Approved and ready." + key
                : s.AiChatLocalLlmUrl.Length > 0 ? "Not approved yet: press Save and approve."
                : "Not set.";
        }

        private void SaveLocalClicked(object sender, RoutedEventArgs e)
        {
            var url = txtLocalUrl.Text.Trim();
            if (url.Length == 0) { txtLocalStatus.Text = "Type an address first."; return; }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
            {
                txtLocalStatus.Text = "That is not a valid http:// or https:// address.";
                return;
            }

            var host = u.IdnHost.ToLowerInvariant();
            var s = AppSettings.Current;
            if (!s.AiChatApprovedHosts.Contains(host))
            {
                var ask = MessageBox.Show(
                    "The Local LLM tab opens a real web page inside CyberOptix, and CyberOptix has administrator rights.\n\n" +
                    "Only approve an address you trust completely: " + host + "\n\n" +
                    "It can use your microphone, the same as any page you open there, and (if you set one below) its API key is sent only to this address.\n\n" +
                    "Approve " + host + "?",
                    "Approve this address?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (ask != MessageBoxResult.Yes) { txtLocalStatus.Text = "Not approved, so it was not saved."; return; }
                s.AiChatApprovedHosts.Add(host);
            }

            s.AiChatLocalLlmUrl = url;
            if (txtLocalKey.Password.Length > 0) s.AiChatLocalLlmKeyEnc = SecretProtector.Protect(txtLocalKey.Password);
            txtLocalKey.Password = "";
            AppSettings.Save();
            UpdateLocalStatus();
        }

        private void ForgetLocalClicked(object sender, RoutedEventArgs e)
        {
            var s = AppSettings.Current;
            s.AiChatApprovedHosts.Clear();
            s.AiChatLocalLlmUrl = "";
            s.AiChatLocalLlmKeyEnc = "";
            AppSettings.Save();
            txtLocalUrl.Text = "";
            txtLocalKey.Password = "";
            UpdateLocalStatus();
        }
    }
}
