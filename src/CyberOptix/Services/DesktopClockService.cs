using System;
using CyberOptix.Models;
using CyberOptix.Widgets;

namespace CyberOptix.Services
{
    /// <summary>Starts, updates and stops the Desktop Clock to match its settings on the Display page.</summary>
    public static class DesktopClockService
    {
        private static DesktopClockWindow? _window;
        private static ClockWeatherIconWindow? _icon;

        /// <summary>The city or units changed: the weather icon on the clock looks the weather up again.</summary>
        public static void ReloadWeather() => _icon?.Reload();

        /// <summary>Call after any Desktop Clock setting changes (and once at start-up). Shows, redraws or closes the clock.</summary>
        public static void Apply()
        {
            try
            {
                if (!AppSettings.Current.DesktopClock)
                {
                    _icon?.Close();
                    _icon = null;
                    _window?.Close();
                    _window = null;
                    return;
                }
                if (_window == null)
                {
                    _window = new DesktopClockWindow();
                    _window.Closed += (_, _) => _window = null;
                    _window.Show();
                }
                else _window.Refresh(true);

                // The weather icon on the clock's left.
                if (AppSettings.Current.DesktopClockWeather)
                {
                    if (_icon == null)
                    {
                        _icon = new ClockWeatherIconWindow(_window);
                        _icon.Closed += (_, _) => _icon = null;
                        _icon.Show();
                    }
                    else _icon.Reload();
                }
                else if (_icon != null)
                {
                    _icon.Close();
                    _icon = null;
                }
            }
            catch { }
        }
    }
}
