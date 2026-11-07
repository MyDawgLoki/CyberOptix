using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CyberOptix.Controls;
using CyberOptix.Services;

namespace CyberOptix.Pages
{
    /// <summary>
    /// All of Glass Cursor's settings. They live in the same file Glass Cursor's tray menu edits, and both sides watch it, so a change made
    /// here shows up in the tray (and its Settings window) straight away, and a change made there shows up here.
    /// </summary>
    public partial class GlassCursorSettingsPanel : UserControl
    {
        private static readonly string[] Presets = { "#000000", "#111111", "#FFFFFF", "#FF18DC", "#F70600", "#18DCFF", "#00FF66", "#FFD400" };

        private readonly GlassCursorSettingsFile _store = GlassCursorSettingsFile.Shared;
        private readonly List<Action<GlassCursorSettings>> _refreshers = new();
        private readonly List<Action<JsonObject>> _pending = new();
        private readonly DispatcherTimer _flush = new() { Interval = TimeSpan.FromMilliseconds(140) };
        private TextBlock? _status;
        private bool _built, _loading, _subscribed, _reloadAfterFlush;
        private int _groupCount;

        public GlassCursorSettingsPanel()
        {
            InitializeComponent();
            _flush.Tick += (_, _) => FlushNow();
            Unloaded += (_, _) => Release();
        }

        // ---------- lifecycle ----------

        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                EnsureBuilt();
                _store.StartWatching();
                if (!_subscribed)
                {
                    _store.ChangedExternally += OnExternalChange;
                    _subscribed = true;
                }
                Reload();
            }
            else
            {
                Release();
            }
        }

        private void Release()
        {
            FlushNow();
            GlassCursorKeyCapture.End();
            if (_subscribed)
            {
                _store.ChangedExternally -= OnExternalChange;
                _subscribed = false;
            }
        }

        private void OnExternalChange() => Dispatcher.BeginInvoke(new Action(Reload));

        /// <summary>Shows what is in the file right now in every control.</summary>
        private void Reload()
        {
            if (!_built) return;
            if (_pending.Count > 0)
            {
                // The user is mid-change: let it be saved first, then show the combined result.
                _reloadAfterFlush = true;
                return;
            }
            _loading = true;
            try
            {
                var settings = _store.Load();
                foreach (var refresh in _refreshers) refresh(settings);
                UpdateStatus();
            }
            finally
            {
                _loading = false;
            }
        }

        private void UpdateStatus()
        {
            if (_status == null) return;
            _status.Text = "A change here or in the tray icon's Glass Cursor settings applies right away and shows up in both.";
        }

        // ---------- saving ----------

        private void Queue(Action<JsonObject> edit, bool now = false)
        {
            if (_loading) return;
            _pending.Add(edit);
            if (now)
            {
                FlushNow();
            }
            else
            {
                _flush.Stop();
                _flush.Start();
            }
        }

        private void FlushNow()
        {
            _flush.Stop();
            if (_pending.Count > 0)
            {
                var edits = _pending.ToArray();
                _pending.Clear();
                try { _store.Update(o => { foreach (var edit in edits) edit(o); }); } catch { }
            }
            if (_reloadAfterFlush)
            {
                _reloadAfterFlush = false;
                Reload();
            }
        }

        // ---------- building the page ----------

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            root.Children.Add(new TextBlock { Text = "Glass Cursor settings", Style = (Style)FindResource("GroupTitle"), Margin = new Thickness(4, 0, 0, 6) });
            _status = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 0, 4), Foreground = (Brush)FindResource("TextDimBrush") };
            root.Children.Add(_status);

            SubHeading("Cursor lens");
            root.Children.Add(CardOf(
                SegmentRow("Cursor lens", "Off hides the lens. Free Roam follows your mouse; Static Center is a crosshair in the middle of the screen.",
                    new[] { "Off", "Free Roam", "Static Center" },
                    s => !s.StartOn ? 0 : s.LensCentered ? 2 : 1,
                    (o, i) => { o["StartOn"] = i != 0; if (i != 0) o["LensCentered"] = i == 2; }),
                ToggleRow("Hide the Windows pointer while the lens is on", "Free Roam only. In Static Center the normal pointer always stays visible so you can find it.", s => s.HidePointer, "HidePointer"),
                ToggleRow("Centre on the active window instead of the screen", "Only for games played in a window.", s => s.CentreOnWindow, "CentreOnWindow"),
                SliderRow("Lens size", "Diameter of the lens.", 20, 200, 2, s => s.Diameter, "Diameter", v => $"{v:0} px"),
                SliderRow("Lens magnification", "Whole numbers (2.0x, 3.0x) give the cleanest pixels.", 15, 80, 1, s => s.Zoom, "Zoom", v => $"{v / 10.0:0.0}x", 10)));

            SubHeading("Right Click To Zoom");
            root.Children.Add(CardOf(
                SegmentRow("Right Click To Zoom", "Hold the right mouse button to zoom. Static Center zooms the middle of the screen; Follow Mouse zooms under the pointer.",
                    new[] { "Off", "Static Center", "Follow Mouse" },
                    s => !s.ScopeEnabled ? 0 : s.ScopeFollowCursor ? 2 : 1,
                    (o, i) => { o["ScopeEnabled"] = i != 0; if (i != 0) o["ScopeFollowCursor"] = i == 2; }),
                SliderRow("Hold right-click for", "How long to hold before the zoom appears.", 0, 2000, 50, s => s.ScopeDelayMs, "ScopeDelayMs",
                    v => v == 0 ? "instant" : $"{v / 1000.0:0.00} s"),
                SliderRow("Static Center: zoom window size", "The size of the zoom window in the middle of the screen.", 100, 1220, 20, s => Math.Min(s.ScopeDiameter, 1220), "ScopeDiameter", v => $"{v:0} px"),
                SliderRow("Static Center: magnification", "", 15, 80, 1, s => s.ScopeZoom, "ScopeZoom", v => $"{v / 10.0:0.0}x", 10),
                ToggleRow("Crisp pixels", "Turns off Windows' pixel smoothing, which makes the zoom look soft.", s => s.ScopeCrisp, "ScopeCrisp"),
                SliderRow("Clarity boost (contrast)", "Adds contrast to the static zoom.", 0, 60, 5, s => s.ScopeContrast, "ScopeContrast", v => v == 0 ? "off" : $"+{v:0}%"),
                SliderRow("Follow Mouse: zoom window size", "The size of the zoom window that follows the pointer.", 100, 1220, 20, s => s.CursorZoomDiameter, "CursorZoomDiameter", v => $"{v:0} px"),
                SliderRow("Follow Mouse: magnification", "", 15, 80, 1, s => s.CursorZoomZoom, "CursorZoomZoom", v => $"{v / 10.0:0.0}x", 10)));

            SubHeading("Appearance");
            root.Children.Add(CardOf(
                ColorRow("Outer ring colour", "The ring on the cursor lens.", "RingColor", "#000000", s => s.RingColor),
                ColorRow("Centre dot colour", "The dot on the cursor lens and the Follow Mouse zoom.", "DotColor", "#111111", s => s.DotColor),
                SliderRow("Centre dot size", "Clicks land on the dot in the middle of the lens.", 1, 21, 2, s => s.DotSize, "DotSize", v => v == 3 ? "3 px (small plus)" : $"{v:0} px")));

            SubHeading("Keybinds");
            var binds = new List<UIElement>();
            foreach (var (action, title, description, def) in GlassCursorKeys.Actions)
                binds.Add(KeybindRow(action, title, description, def));
            root.Children.Add(CardOf(binds.ToArray()));
            root.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("Note"),
                Text = "Click a box, then press the keys (Ctrl, Alt or Shift with a key, or an F key on its own). Backspace clears it, Esc cancels. " +
                       "Fixed keys that always work: Ctrl + Alt + R turns the lens off and brings the normal pointer back, Ctrl + Alt + = and - change the zoom, Ctrl + Alt + ] and [ change the lens size.",
            });
        }

        private void SubHeading(string text) =>
            root.Children.Add(new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 18, 0, 8) });

        private Border CardOf(params UIElement[] rows)
        {
            var stack = new StackPanel();
            for (var i = 0; i < rows.Length; i++)
            {
                if (i > 0) stack.Children.Add(new Border { Style = (Style)FindResource("Divider") });
                stack.Children.Add(rows[i]);
            }
            return new Border { Style = (Style)FindResource("Card"), Child = stack };
        }

        private static SettingRow Row(string title, string description, UIElement content) =>
            new SettingRow { Title = title, Description = string.IsNullOrEmpty(description) ? null : description, Content = content };

        private UIElement ToggleRow(string title, string description, Func<GlassCursorSettings, bool> read, string property)
        {
            var box = new CheckBox { Content = title, Tag = description };
            box.Checked += (_, _) => Queue(o => o[property] = true, now: true);
            box.Unchecked += (_, _) => Queue(o => o[property] = false, now: true);
            _refreshers.Add(s => box.IsChecked = read(s));
            return box;
        }

        private UIElement SegmentRow(string title, string description, string[] options, Func<GlassCursorSettings, int> read, Action<JsonObject, int> write)
        {
            var group = "gcseg" + _groupCount++;
            var buttons = new List<RadioButton>();
            var host = new StackPanel { Orientation = Orientation.Horizontal };
            for (var i = 0; i < options.Length; i++)
            {
                var index = i;
                var button = new RadioButton { Content = options[i], GroupName = group, Style = (Style)FindResource("Segment") };
                button.Checked += (_, _) => Queue(o => write(o, index), now: true);
                buttons.Add(button);
                host.Children.Add(button);
            }
            var frame = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0)),
                BorderBrush = (Brush)FindResource("StrokeBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(3),
                Child = host,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _refreshers.Add(s => buttons[Math.Clamp(read(s), 0, buttons.Count - 1)].IsChecked = true);
            return Row(title, description, frame);
        }

        /// <summary>A slider. <paramref name="scale"/> 10 shows tenths (a magnification of 2.3x is the number 23 on the slider and 2.3 in the file).</summary>
        private UIElement SliderRow(string title, string description, double min, double max, double step,
            Func<GlassCursorSettings, double> read, string property, Func<double, string> format, double scale = 1)
        {
            var slider = new Slider
            {
                Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 5, TickFrequency = step,
                IsSnapToTickEnabled = true, Width = 220, VerticalAlignment = VerticalAlignment.Center,
            };
            var value = new TextBlock
            {
                Width = 120, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Medium, Foreground = (Brush)FindResource("TextDimBrush"),
            };
            slider.ValueChanged += (_, _) =>
            {
                value.Text = format(slider.Value);
                if (_loading) return;
                var v = slider.Value;
                Queue(o => o[property] = scale == 1 ? JsonValue.Create((int)Math.Round(v)) : JsonValue.Create(Math.Round(v / scale, 2)));
            };
            _refreshers.Add(s =>
            {
                slider.Value = Math.Clamp(read(s) * scale, min, max);
                value.Text = format(slider.Value);
            });
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(slider);
            content.Children.Add(value);
            return Row(title, description, content);
        }

        private UIElement ColorRow(string title, string description, string property, string fallback, Func<GlassCursorSettings, string> read)
        {
            var current = fallback;
            var swatch = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
                BorderBrush = (Brush)FindResource("StrokeBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0),
            };
            var hex = new TextBox { Width = 92, MaxLength = 7, Tag = "#RRGGBB", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var reset = new Button { Content = "Reset", MinHeight = 28, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

            var swatchX = new TextBlock
            {
                Text = "\u2715", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextDimBrush"), Visibility = Visibility.Collapsed,
            };
            swatch.Child = swatchX;

            void Show(string h)
            {
                bool none = string.Equals(h, "none", StringComparison.OrdinalIgnoreCase);
                swatchX.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
                if (none) swatch.Background = Brushes.Transparent;
                else { try { swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)); } catch { } }
                hex.Text = none ? "none" : h;
            }
            void Commit(string h)
            {
                current = h;
                Show(h);
                Queue(o => o[property] = h, now: true);
            }
            void FromText()
            {
                var t = hex.Text.Trim();
                if (string.Equals(t, "none", StringComparison.OrdinalIgnoreCase)) { Commit("none"); return; }
                if (!t.StartsWith('#')) t = "#" + t;
                if (t.Length == 7 && t.Skip(1).All(Uri.IsHexDigit)) Commit(t.ToUpperInvariant());
                else Show(current);
            }
            hex.LostKeyboardFocus += (_, _) => FromText();
            hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { FromText(); Keyboard.ClearFocus(); } };
            reset.Click += (_, _) => Commit(fallback);
            _refreshers.Add(s => { current = read(s); Show(current); });

            var content = new StackPanel { Orientation = Orientation.Horizontal };

            // An empty box with an X: no colour at all, so the ring (or the dot) is simply not drawn.
            var noneChip = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand,
                Background = Brushes.Transparent, ToolTip = "None: don't draw this",
                BorderBrush = (Brush)FindResource("StrokeBrush"), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "\u2715", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)FindResource("TextDimBrush"),
                },
            };
            noneChip.MouseLeftButtonUp += (_, _) => Commit("none");
            content.Children.Add(noneChip);

            foreach (var preset in Presets)
            {
                var chip = new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(preset)), ToolTip = preset,
                    BorderBrush = (Brush)FindResource("StrokeBrush"), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
                };
                var code = preset;
                chip.MouseLeftButtonUp += (_, _) => Commit(code);
                content.Children.Add(chip);
            }
            content.Children.Add(swatch);
            content.Children.Add(hex);
            content.Children.Add(reset);
            return Row(title, description, content);
        }

        private UIElement KeybindRow(string action, string title, string description, int defaultKeys)
        {
            var box = new KeybindBox();
            box.Capturing += on => { if (on) GlassCursorKeyCapture.Begin(); else GlassCursorKeyCapture.End(); };
            box.KeysPicked += keys =>
            {
                FlushNow();
                try { _store.SetKeybind(action, keys); } catch { }
                Reload();
            };
            var reset = new Button { Content = "Default", MinHeight = 28, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            reset.Click += (_, _) =>
            {
                FlushNow();
                try { _store.SetKeybind(action, defaultKeys); } catch { }
                Reload();
            };
            _refreshers.Add(s => box.Display(s.KeybindFor(action)));

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(box);
            content.Children.Add(reset);
            return Row(title, description, content);
        }
    }
}
