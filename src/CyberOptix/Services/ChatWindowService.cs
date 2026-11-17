using CyberOptix.Models;
using CyberOptix.Widgets;

namespace CyberOptix.Services
{
    /// <summary>Owns the single AI Agents Chat window: opens or closes it to match the setting, and restores it at start-up.</summary>
    public static class ChatWindowService
    {
        private static ChatWindow? _window;

        public static bool IsOpen => _window != null;

        /// <summary>Opens or closes the Chat window to match <see cref="AppSettings.AiChatEnabled"/>. Safe to call any time settings change.</summary>
        public static void Apply()
        {
            if (AppSettings.Current.AiChatEnabled) Show(); else Hide();
        }

        public static void Show()
        {
            if (_window != null) { _window.Show(); _window.Activate(); return; }
            var window = new ChatWindow();
            window.Closed += (_, _) =>
            {
                if (_window == window) _window = null;
                if (AppSettings.Current.AiChatEnabled)
                {
                    AppSettings.Current.AiChatEnabled = false;
                    AppSettings.Save();
                }
            };
            _window = window;
            window.Show();
        }

        public static void Hide() { _window?.Close(); _window = null; }

        public static void Stop() => Hide();
    }
}
