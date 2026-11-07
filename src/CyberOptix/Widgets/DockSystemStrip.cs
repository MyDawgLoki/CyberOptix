using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>The right-hand end of the dock: hidden icons, microphone, volume, network, Bluetooth, battery, USB eject, and the clock.</summary>
    public class DockSystemStrip : StackPanel
    {
        private readonly Func<bool> _dockOnTop;
        private readonly Func<(int X, int Y)> _anchor;

        private readonly TextBlock _mic = Glyph("");
        private readonly TextBlock _speaker = Glyph("");
        private readonly TextBlock _net = Glyph("");
        private readonly TextBlock _bt = Glyph("");
        private readonly TextBlock _battery = Glyph("");
        private readonly TextBlock _time = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right };
        private readonly TextBlock _date = new() { FontSize = 11.5, TextAlignment = TextAlignment.Right };

        private Border? _btItem, _batteryItem, _micItem, _netItem;
        private bool? _btOn;
        private bool _btBusy;

        public DockSystemStrip(Func<bool> dockOnTop, Func<(int X, int Y)> anchor)
        {
            _dockOnTop = dockOnTop;
            _anchor = anchor;
            Orientation = Orientation.Horizontal;
            VerticalAlignment = VerticalAlignment.Center;

            var chevron = Item(Glyph(""), "Show hidden tray icons", ChevronClicked);
            Children.Add(chevron);

            // Each of these opens a flyout that closes when you click the icon again or click anywhere else (see Toggle).
            // The microphone and volume flyouts hold the device picker, the volume mixer and "listen to this device" inside themselves.
            _micItem = Item(_mic, "Microphone", null);
            _micItem.MouseLeftButtonUp += (_, _) => Toggle(_micItem, () => new AudioFlyout(AudioService.Microphone));
            Children.Add(_micItem);

            var speakerItem = Item(_speaker, "Volume", null);
            speakerItem.MouseLeftButtonUp += (_, _) => Toggle(speakerItem, () => new AudioFlyout(AudioService.Speakers));
            Children.Add(speakerItem);

            _netItem = Item(_net, "Network", null);
            _netItem.MouseLeftButtonUp += (_, _) => Toggle(_netItem, BuildNetwork);
            Children.Add(_netItem);

            _btItem = Item(_bt, "Bluetooth", BluetoothClicked);
            Children.Add(_btItem);

            _batteryItem = Item(_battery, "Battery", () => Open("ms-settings:batterysaver"));
            Children.Add(_batteryItem);

            Children.Add(Item(Glyph(""), "Safely remove hardware", () =>
            {
                try { Process.Start(new ProcessStartInfo(SystemTools.Resolve("rundll32.exe"), "shell32.dll,Control_RunDLL hotplug.dll") { UseShellExecute = true }); } catch { }
            }));

            var clock = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            clock.Children.Add(_time);
            clock.Children.Add(_date);
            _date.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            _clock = clock;
            Children.Add(clock);

            Update();
        }

        // ---------- size ----------

        /// <summary>
        /// Sizes the system icons and the clock from the icon size in pixels. The clock text and the spacing follow, with a floor so the text
        /// stays readable.
        /// </summary>
        private int _glyphPx;
        private readonly System.Collections.Generic.List<Border> _extras = new();
        private StackPanel? _clock;

        /// <summary>
        /// Shows the extra icons the user picked (see <see cref="DockWidgetCatalog"/>), just before the clock, in catalog order.
        /// Ids that no longer exist are ignored.
        /// </summary>
        public void SetExtras(System.Collections.Generic.IEnumerable<string> ids)
        {
            foreach (var old in _extras) Children.Remove(old);
            _extras.Clear();
            _weatherItem = null;

            var wanted = new System.Collections.Generic.HashSet<string>(ids);
            int at = _clock != null ? Children.IndexOf(_clock) : Children.Count;
            foreach (var def in DockWidgetCatalog.Ordered())
            {
                if (!wanted.Contains(def.Id)) continue;
                var run = def.Run;
                Border item;
                if (def.Id == DockWidgetCatalog.WeatherId) item = MakeWeatherItem();
                else if (def.Id == DockWidgetCatalog.MediaId)
                {
                    // The music note: opens the Media Player widget (see below).
                    item = Item(Glyph(def.Glyph), def.Title, null);
                    var note = item;
                    // It has no player of its own: it puts the Media Player widget on the desktop if it is not there already.
                    item.MouseLeftButtonUp += (_, _) =>
                    {
                        if (WidgetManager.IsEnabled("media")) return;
                        WidgetManager.SetEnabled("media", true);
                        WidgetManager.Apply();
                    };
                }
                else if (def.Id == DockWidgetCatalog.WidgetsId)
                {
                    item = Item(Glyph(def.Glyph), def.Title, null);
                    var widgets = item;
                    item.MouseLeftButtonUp += (_, _) => WidgetsWindowService.Toggle(Window.GetWindow(widgets));
                }
                else if (def.Id == DockWidgetCatalog.PowerId)
                {
                    item = Item(Glyph(def.Glyph), def.Title, null);
                    var power = item;
                    item.MouseLeftButtonUp += (_, _) => PowerFlyout.Toggle(power);
                }
                else if (def.AppPath != null)
                {
                    // A program the user added: its own icon, and a click opens it.
                    var appPath = def.AppPath;
                    item = Item(BrandIcon.CreateApp(appPath, _glyphPx > 0 ? _glyphPx : 17), def.Title, run);
                }
                else if (def.VendorId != null)
                {
                    // A hardware maker's app: its own icon, and a click opens the app.
                    var vendor = def.VendorId;
                    item = Item(BrandIcon.CreateVendor(vendor, _glyphPx > 0 ? _glyphPx : 17), "Open " + def.Title, () => HardwareSoftwareService.OpenById(vendor));
                }
                else if (def.BrandId != null)
                {
                    // An app or website: its logo, in its own colors. Opens the app if it is installed, otherwise the site.
                    var brand = BrandLaunchers.Find(def.BrandId);
                    var id = def.BrandId;
                    item = Item(BrandIcon.Create(id, _glyphPx > 0 ? _glyphPx : 17), brand != null ? BrandLaunchers.Tooltip(brand) : def.Title, () => BrandLaunchers.Open(id));
                }
                else item = Item(Glyph(def.Glyph), def.Title, run);
                Children.Insert(at++, item);
                _extras.Add(item);
            }
            if (_glyphPx > 0) ApplySize(_glyphPx, _clockPx);   // new icons get the same size as the rest
        }

        /// <summary>The clock's text size at the standard icon size (6 px bigger than it used to be). It is also the default.</summary>
        public const double DefaultClockSize = 20.0;

        private double? _clockPx;

        /// <summary>The clock size that goes with a system icon size: the standard clock size, scaled the same way as the icons.</summary>
        public static double AutoClockSize(int glyphPx) =>
            Math.Max(8, Math.Round(DefaultClockSize * Math.Clamp(glyphPx, 8, 48) / 23.0, 1));

        /// <summary>
        /// Sizes the icons from <paramref name="glyphPx"/>. The clock text follows the icons unless a size in pixels is given for it;
        /// the date is always a little smaller than the time.
        /// </summary>
        public void ApplySize(int glyphPx, double? clockPx = null)
        {
            _glyphPx = glyphPx;
            _clockPx = clockPx;
            double glyph = Math.Clamp(glyphPx, 8, 48);                   // the icon itself
            double scale = glyph / 23.0;                                  // 23 px is the standard size, the one that sits right next to a 44 px app icon
            double box = Math.Max(16, Math.Round(42 * scale));            // each icon's button

            foreach (var child in Children)
            {
                if (child is Border { Child: TextBlock g } item)
                {
                    item.Width = item.Height = box;
                    item.CornerRadius = new CornerRadius(Math.Max(4, Math.Round(10 * scale)));
                    g.FontSize = glyph;
                }
                else if (child is Border { Child: FrameworkElement { Tag: BrandIcon.Marker } logo } logoItem)
                {
                    logoItem.Width = logoItem.Height = box;
                    logoItem.CornerRadius = new CornerRadius(Math.Max(4, Math.Round(10 * scale)));
                    BrandIcon.Resize(logo, glyph);
                }
                else if (ReferenceEquals(child, _weatherItem))
                {
                    // The weather icon is an icon plus the temperature, so it is wider than the others.
                    var wi = (Border)child;
                    wi.Width = double.NaN;
                    wi.MinWidth = box;
                    wi.Height = box;
                    wi.CornerRadius = new CornerRadius(Math.Max(4, Math.Round(10 * scale)));
                    wi.Padding = new Thickness(Math.Max(3, Math.Round(8 * scale)), 0, Math.Max(3, Math.Round(8 * scale)), 0);
                    _weatherIcon.FontSize = Math.Max(10, glyph);
                    _weatherTemp.FontSize = Math.Max(8, Math.Round(glyph * 0.72, 1));
                }
                else if (child is StackPanel clock)
                {
                    clock.Margin = new Thickness(Math.Max(4, Math.Round(10 * scale)), 0, Math.Max(3, Math.Round(8 * scale)), 0);
                    double time = Math.Clamp(clockPx ?? AutoClockSize(glyphPx), 6, 40);
                    _time.FontSize = time;
                    _date.FontSize = Math.Max(5.5, Math.Round(time * 11.5 / 12.5, 1));   // the same proportion the date always had
                }
            }
        }

        // ---------- weather ----------

        private Border? _weatherItem;
        private TextBlock _weatherIcon = new();
        private TextBlock _weatherTemp = new();
        private WeatherInfo? _weather;
        private string _weatherKey = "";          // the city and units the data above is for
        private string? _weatherError;
        private DateTime _weatherAt = DateTime.MinValue;
        private bool _weatherBusy;

        private Border MakeWeatherItem()
        {
            _weatherIcon = new TextBlock { Text = "\U0001F321", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            _weatherTemp = new TextBlock { Text = "--", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            row.Children.Add(_weatherIcon);
            row.Children.Add(_weatherTemp);

            var item = Item(row, "Weather", null);
            item.MouseLeftButtonUp += (_, _) => Toggle(item, BuildWeatherPanel);
            _weatherItem = item;
            ShowWeather();
            RefreshWeather();
            return item;
        }

        /// <summary>Looks the weather up again when the city or units changed, and every 20 minutes (2 minutes after a failure).</summary>
        private void RefreshWeather()
        {
            if (_weatherItem == null || _weatherBusy) return;
            var s = AppSettings.Current;
            string key = s.City + "|" + s.Fahrenheit;
            var wait = TimeSpan.FromMinutes(_weather == null ? 2 : 20);
            if (key == _weatherKey && DateTime.Now - _weatherAt < wait) return;
            _ = LoadWeather(key, s.City, s.Fahrenheit);
        }

        private async System.Threading.Tasks.Task LoadWeather(string key, string city, bool fahrenheit)
        {
            _weatherBusy = true;
            try
            {
                _weather = await WeatherService.GetAsync(city, fahrenheit);
                _weatherError = null;
            }
            catch (Exception ex)
            {
                if (key != _weatherKey) _weather = null;   // old data for another city would be wrong
                _weatherError = ex.Message;
            }
            finally
            {
                _weatherKey = key;
                _weatherAt = DateTime.Now;
                _weatherBusy = false;
                ShowWeather();
            }
        }

        private void ShowWeather()
        {
            if (_weatherItem == null) return;
            var w = _weather;
            if (w == null)
            {
                _weatherIcon.Text = "\U0001F321";
                _weatherTemp.Text = "--";
                _weatherItem.ToolTip = _weatherError ?? "Loading the weather...";
                return;
            }
            var (icon, text) = WeatherService.Describe(w.Code);
            _weatherIcon.Text = icon;
            _weatherTemp.Text = $"{Math.Round(w.Temp)}°";
            _weatherItem.ToolTip = $"{text}, {Math.Round(w.Temp)}{(w.Fahrenheit ? "°F" : "°C")} in {w.Place}";
        }

        private TextBlock Dim(string text, double size)
        {
            var t = new TextBlock { Text = text, FontSize = size };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            return t;
        }

        /// <summary>The weather details: the big temperature, feels like, humidity, wind, UV index and the next days.</summary>
        private StackPanel BuildWeatherPanel()
        {
            var panel = new StackPanel { MinWidth = 340 };
            var w = _weather;
            if (w == null)
            {
                panel.Children.Add(new TextBlock { Text = _weatherError ?? "Loading the weather...", TextWrapping = TextWrapping.Wrap, MaxWidth = 320 });
                panel.Children.Add(Dim("Change the city on the Widgets page.", 11.5));
                return panel;
            }

            string unit = w.Fahrenheit ? "°F" : "°C";
            var (icon, text) = WeatherService.Describe(w.Code);

            // The big temperature, the condition and the place.
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock { Text = icon, FontSize = 42, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var big = new StackPanel { Orientation = Orientation.Horizontal };
            big.Children.Add(new TextBlock { Text = Math.Round(w.Temp).ToString(), FontSize = 38, FontWeight = FontWeights.SemiBold });
            var unitText = Dim(unit, 15);
            unitText.VerticalAlignment = VerticalAlignment.Top;
            unitText.Margin = new Thickness(2, 8, 0, 0);
            big.Children.Add(unitText);
            words.Children.Add(big);
            words.Children.Add(new TextBlock { Text = text, FontSize = 14 });
            words.Children.Add(Dim(w.Place, 12));
            head.Children.Add(words);
            panel.Children.Add(head);

            // Feels like, humidity, wind, UV.
            var stats = new UniformGrid { Columns = 4 };
            void Stat(string label, string value)
            {
                var cell = new StackPanel { Margin = new Thickness(2, 0, 2, 0) };
                cell.Children.Add(Dim(label.ToUpperInvariant(), 10));
                cell.Children.Add(new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
                stats.Children.Add(cell);
            }
            Stat("Feels like", $"{Math.Round(w.Feels)}{unit}");
            Stat("Humidity", $"{w.Humidity}%");
            Stat("Wind", $"{Math.Round(w.Wind)} {(w.Fahrenheit ? "mph" : "km/h")}");
            Stat("UV index", w.Uv < 0 ? "-" : w.Uv.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            var statBox = new Border { Child = stats, Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 14, 0, 0) };
            statBox.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
            panel.Children.Add(statBox);

            // The next days.
            var days = new UniformGrid { Columns = Math.Max(1, w.Days.Count), Margin = new Thickness(0, 14, 0, 0) };
            for (int i = 0; i < w.Days.Count; i++)
            {
                var d = w.Days[i];
                var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                string name = i == 0 ? "Today" : i == 1 ? "Tomorrow" : d.Date.ToString("ddd");
                col.Children.Add(new TextBlock { Text = name, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center });
                col.Children.Add(new TextBlock { Text = WeatherService.Describe(d.Code).Icon, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) });
                var range = Dim($"{Math.Round(d.High)}°/{Math.Round(d.Low)}°", 11.5);
                range.HorizontalAlignment = HorizontalAlignment.Center;
                col.Children.Add(range);
                days.Children.Add(col);
            }
            panel.Children.Add(days);

            var foot = Dim("Weather from Open-Meteo. Change the city and units on the Widgets page.", 11);
            foot.Margin = new Thickness(0, 12, 0, 0);
            panel.Children.Add(foot);
            return panel;
        }

        // ---------- building blocks ----------

        private static TextBlock Glyph(string text) => new()
        {
            Text = text,
            FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
            FontSize = 17,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        private static Border Item(UIElement content, string tip, Action? click)
        {
            var item = new Border
            {
                Child = content,
                Width = 36, Height = 36,
                CornerRadius = new CornerRadius(10),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tip,
            };
            item.MouseEnter += (_, _) => item.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            if (click != null) item.MouseLeftButtonUp += (_, _) => click();
            return item;
        }

        private static void Open(string uri)
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
        }

        // ---------- live state ----------

        /// <summary>Refreshes every icon. Called by the dock's timer.</summary>
        public void Update()
        {
            var now = DateTime.Now;
            _time.Text = now.ToString("h:mm tt");
            _date.Text = now.ToString("M/d/yyyy");

            bool micMuted = AudioService.GetMute(AudioService.Microphone);
            bool hasMic = AudioService.Available(AudioService.Microphone);
            _micItem!.Visibility = hasMic ? Visibility.Visible : Visibility.Collapsed;
            _mic.SetResourceReference(TextBlock.ForegroundProperty, micMuted ? "HotBrush" : "TextBrush");
            _micItem.ToolTip = micMuted ? "Microphone (muted)" : "Microphone";

            int level = AudioService.GetLevel(AudioService.Speakers);
            bool muted = AudioService.GetMute(AudioService.Speakers) || level == 0;
            _speaker.Text = muted ? "" : "";
            _speaker.SetResourceReference(TextBlock.ForegroundProperty, muted ? "HotBrush" : "TextBrush");
            ((Border)_speaker.Parent).ToolTip = muted ? "Volume (muted)" : $"Volume {level}%";

            var net = NetworkStatusService.Read();
            _net.Text = net.Kind == "Ethernet" ? "" : net.Kind == "Wi-Fi" ? "" : "";
            _net.Opacity = net.Connected ? 1 : 0.5;
            _netItem!.ToolTip = net.Connected ? $"{net.Kind}: {net.Name}" : "Not connected";

            var battery = BatteryService.Read();
            _batteryItem!.Visibility = battery == null ? Visibility.Collapsed : Visibility.Visible;
            if (battery != null)
            {
                _battery.Text = battery.Value.Charging ? "" : "";
                _batteryItem.ToolTip = $"Battery {battery.Value.Percent}%{(battery.Value.Charging ? " (charging)" : "")}";
            }

            _ = RefreshBluetooth();
            RefreshWeather();
        }

        private async System.Threading.Tasks.Task RefreshBluetooth()
        {
            if (_btBusy) return;
            _btBusy = true;
            try
            {
                _btOn = await BluetoothService.IsOnAsync();
                _btItem!.Visibility = _btOn == null ? Visibility.Collapsed : Visibility.Visible;
                _bt.SetResourceReference(TextBlock.ForegroundProperty, _btOn == true ? "AccentBrush" : "TextDimBrush");
                _btItem.ToolTip = _btOn == true ? "Bluetooth on (click to turn off)" : "Bluetooth off (click to turn on)";
            }
            finally
            {
                _btBusy = false;
            }
        }

        // ---------- clicks ----------

        private async void BluetoothClicked()
        {
            if (_btOn == null) return;
            await BluetoothService.SetAsync(!_btOn.Value);
            await RefreshBluetooth();
        }

        private async void ChevronClicked()
        {
            var (x, y) = _anchor();
            bool top = _dockOnTop();
            bool shown = await System.Threading.Tasks.Task.Run(() => TaskbarTakeoverService.ShowHiddenIcons(x, y, top));
            if (!shown) Open("ms-settings:taskbar");
        }

        private Popup NewPopup(UIElement target, UIElement content)
        {
            var card = new Border
            {
                Padding = new Thickness(16, 14, 16, 14),
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(12),
                Child = content,
                MinWidth = 260,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.5, Color = Colors.Black },
            };
            card.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
            card.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextBrush");

            return new Popup
            {
                PlacementTarget = target,
                Placement = _dockOnTop() ? PlacementMode.Bottom : PlacementMode.Top,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                Child = card,
                IsOpen = true,
            };
        }

        // ---------- opening and closing flyouts ----------

        private readonly FlyoutGate _gate = new();
        private Popup? _popup;

        /// <summary>True while one of the dock's flyouts (volume, weather...) is open. The dock stays in front of the windows while it is.</summary>
        public bool FlyoutOpen => _popup is { IsOpen: true };

        /// <summary>
        /// Opens a flyout under an icon. Clicking the same icon again closes it, and so does a click anywhere else, like the
        /// Windows quick settings. There is no x to hunt for.
        /// </summary>
        private void Toggle(UIElement target, Func<UIElement> build)
        {
            if (_gate.IsOpenFor(target))
            {
                if (_popup != null) _popup.IsOpen = false;
                return;
            }
            if (!_gate.ShouldOpen(target, DateTime.UtcNow)) return;   // this click is the end of the one that just closed it

            var popup = NewPopup(target, build());
            _popup = popup;
            _gate.Opened(target);
            popup.Closed += (_, _) =>
            {
                _gate.Closed(target, DateTime.UtcNow);
                if (ReferenceEquals(_popup, popup)) _popup = null;
            };
        }

        private UIElement BuildNetwork()
        {
            var net = NetworkStatusService.Read();
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = net.Connected ? net.Kind : "Not connected", FontWeight = FontWeights.SemiBold });
            var detail = new TextBlock { Text = net.Connected ? net.Name : "Check your cable or Wi-Fi.", Margin = new Thickness(0, 2, 0, 0) };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            panel.Children.Add(detail);

            var open = new Button { Content = "Network settings", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += (_, _) => Open("ms-settings:network");
            panel.Children.Add(open);

            return panel;
        }
    }
}
