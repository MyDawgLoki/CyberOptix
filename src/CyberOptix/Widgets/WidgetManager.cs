using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>Creates and closes the desktop widgets so they match <see cref="AppSettings"/>.</summary>
    public static class WidgetManager
    {
        private static readonly Dictionary<string, Func<DesktopWidget>> Factories = new()
        {
            ["clock"] = () => new ClockWidget(),
            ["weather"] = () => new WeatherWidget(),
            ["media"] = () => new MediaWidget(),
            ["temps"] = () => new TempsWidget(),
            ["cpu"] = () => new CpuWidget(),
            ["gpu"] = () => new GpuWidget(),
        };

        private static readonly Dictionary<string, DesktopWidget> Open = new();

        // "Close widgets while gaming": the widgets stay switched on in the settings, they are just not shown while a game has the screen.
        private static bool _gaming;
        private static int _clearTicks;
        private static DispatcherTimer? _gameWatch;

        /// <summary>Starts watching for a game or other fullscreen app. Safe to call more than once.</summary>
        public static void StartGameWatch()
        {
            if (_gameWatch != null) return;
            _gameWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _gameWatch.Tick += (_, _) => CheckGame();
            _gameWatch.Start();
        }

        private static void CheckGame()
        {
            bool want = AppSettings.Current.WidgetsCloseWhileGaming;
            bool hideForGame = AppSettings.Current.HideWidgetsOnBrowserOrGame;
            bool showWithBrowser = AppSettings.Current.ShowWidgetsWithBrowser;
            bool hideOnFocus = hideForGame || !showWithBrowser;
            bool covers = false;
            if (want)
            {
                var own = Application.Current?.MainWindow is { } mw ? new WindowInteropHelper(mw).Handle : IntPtr.Zero;
                DesktopHost.Query(own, out covers, out _, sameMonitorOnly: false); // a game on any screen counts
            }

            // Which window is in front decides the rest. A browser keeps the widgets on screen while "Show widget windows when a browser window is
            // focused" is on (that includes a browser playing a video fullscreen: that is not a game). With it off, a browser hides them.
            ForegroundApp.Read(out bool browserInFront, out bool gameInFront);
            if (browserInFront && showWithBrowser) covers = false;
            if (hideForGame && gameInFront) covers = true;                 // "Hide widget windows when browser or game window is focused", for games
            if (!showWithBrowser && browserInFront) covers = true;

            if (covers)
            {
                _clearTicks = 0;
                if (!_gaming) { _gaming = true; Apply(); }
            }
            else if (_gaming && (!(want || hideOnFocus) || ++_clearTicks >= 2)) // two clear checks in a row, so a brief alt-tab does not flicker them
            {
                _gaming = false;
                _clearTicks = 0;
                Apply();
            }
        }

        public static bool IsEnabled(string key) => key switch
        {
            "clock" => AppSettings.Current.ClockWidget,
            "weather" => AppSettings.Current.WeatherWidget,
            "media" => AppSettings.Current.MediaWidget,
            _ when key.StartsWith("tile:") => AppSettings.Current.DesktopTiles.Contains(key.Substring(5)),
            _ => AppSettings.Current.MonitorWidgets.Contains(key),
        };

        public static void SetEnabled(string key, bool on)
        {
            var s = AppSettings.Current;
            switch (key)
            {
                case "clock": s.ClockWidget = on; break;
                case "weather": s.WeatherWidget = on; break;
                case "media": s.MediaWidget = on; break;
                case var tile when tile.StartsWith("tile:"):
                    s.DesktopTiles.Remove(tile.Substring(5));
                    if (on) s.DesktopTiles.Add(tile.Substring(5));
                    break;
                default:
                    s.MonitorWidgets.Remove(key);
                    if (on) s.MonitorWidgets.Add(key);
                    break;
            }
        }

        public static void Apply()
        {
            // Fixed widgets plus one per app panel.
            var wanted = new Dictionary<string, Func<DesktopWidget>>(Factories);
            foreach (var panel in AppSettings.Current.AppPanels)
            {
                var p = panel;
                wanted["panel:" + p.Id] = () => new AppPanelWidget(p);
            }

            foreach (var id in new List<string>(AppSettings.Current.DesktopTiles))
            {
                var tileId = id;
                wanted["tile:" + tileId] = () => new TileWidget(tileId);
            }

            foreach (var key in wanted.Keys)
            {
                if (IsEnabled(key) && !_gaming)
                {
                    if (!Open.ContainsKey(key))
                    {
                        var widget = wanted[key]();
                        Open[key] = widget;
                        widget.Closed += (_, _) => { if (Open.TryGetValue(key, out var cur) && cur == widget) Open.Remove(key); };
                        widget.Show();
                    }
                    Open[key].ApplyLook();
                }
                else if (Open.TryGetValue(key, out var w))
                {
                    w.Close();
                }
            }

            // A panel that was deleted
            foreach (var key in new List<string>(Open.Keys))
                if (!wanted.ContainsKey(key)) Open[key].Close();

            AppSettings.Save();
        }

        public static void RefreshWeather()
        {
            if (Open.TryGetValue("weather", out var w) && w is WeatherWidget weather) weather.Refresh();
            DesktopClockService.ReloadWeather();
        }

        /// <summary>Turns a widget on (if needed) and puts its top-left corner at the given desktop position.</summary>
        public static void Place(string key, double x, double y)
        {
            AppSettings.Current.Positions[key] = new[] { x, y };
            SetEnabled(key, true);
            Apply();
            if (Open.TryGetValue(key, out var w)) w.MoveTo(x, y);
            AppSettings.Save();
            WidgetLock.SavePlacing();   // placing a widget on purpose is part of the layout it can be restored to
        }

        /// <summary>Moves every open widget (and a pinned Apps panel) to the position saved for it.</summary>
        public static void ApplyPositions()
        {
            foreach (var kv in Open)
                if (AppSettings.Current.Positions.TryGetValue(kv.Key, out var p) && p.Length >= 2) kv.Value.MoveTo(p[0], p[1]);
            AppsPanelService.ApplySavedPosition();
        }

        public static void Remove(string key)
        {
            if (key == "media") AppSettings.Current.MediaWidgetAutoShow = false;   // closing the media player means: do not bring it back by itself
            SetEnabled(key, false);
            Apply();
        }

        public static void ResetPositions()
        {
            foreach (var w in new List<DesktopWidget>(Open.Values)) w.Close();
            AppSettings.Current.Positions.Clear();
            AppSettings.Current.WidgetPlacing.Clear();
            Apply();
        }
    }
}
