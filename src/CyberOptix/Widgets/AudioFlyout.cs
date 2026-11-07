using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>
    /// The dock's volume and microphone flyout. The main view has the level, mute and a button to pick the sound output (or input);
    /// from there you can also open the volume mixer (outputs) or turn on "Listen to this device" (microphones).
    /// Everything happens inside the one flyout, so there are no extra windows to close: click the icon again or click anywhere else.
    /// </summary>
    public sealed class AudioFlyout : Border
    {
        private readonly int _flow;
        private bool _building;

        public AudioFlyout(int flow)
        {
            _flow = flow;
            Width = 360;   // fixed, so a long description wraps instead of stretching the flyout
            ShowMain();
        }

        private bool Output => _flow == AudioService.Speakers;

        // ---------- small building blocks ----------

        private static TextBlock Glyph(string glyph, double size = 16) => new()
        {
            Text = glyph,
            FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
            FontSize = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        private static TextBlock Dim(string text, double size = 12)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            return t;
        }

        private static Button IconButton(string glyph, string tip, Action click)
        {
            var b = new Button { Content = Glyph(glyph), ToolTip = tip, MinWidth = 36, MinHeight = 34, Padding = new Thickness(8, 4, 8, 4) };
            b.Click += (_, _) => click();
            return b;
        }

        /// <summary>The top of a sub-view: a back arrow and a title.</summary>
        private UIElement Header(string title)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var back = IconButton("", "Back", ShowMain);
            DockPanel.SetDock(back, Dock.Left);
            row.Children.Add(back);
            row.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
            return row;
        }

        /// <summary>
        /// A full-width clickable row for lists. (A Button would do, but the app's button style centres whatever is inside it,
        /// and list rows read better with their text on the left.)
        /// </summary>
        private static Border Row(UIElement content, Action click, Thickness margin)
        {
            var normal = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            var hover = new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));
            var row = new Border
            {
                Child = content, Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(8),
                Background = normal, Cursor = System.Windows.Input.Cursors.Hand, Margin = margin,
            };
            row.MouseEnter += (_, _) => row.Background = hover;
            row.MouseLeave += (_, _) => row.Background = normal;
            row.MouseLeftButtonUp += (_, _) => click();
            return row;
        }

        /// <summary>A full-width row that opens another view, like "Volume mixer  >".</summary>
        private static Border NavRow(string text, string? hint, Action click)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = text });
            if (hint != null) left.Children.Add(Dim(hint, 11));
            grid.Children.Add(left);
            var arrow = Glyph("", 12);
            Grid.SetColumn(arrow, 1);
            grid.Children.Add(arrow);

            return Row(grid, click, new Thickness(0, 10, 0, 0));
        }

        private static void Open(string uri)
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
        }

        private static string DeviceName(int flow) => AudioService.Devices(flow).FirstOrDefault(d => d.IsDefault)?.Name ?? "No device";

        // ---------- main view ----------

        private void ShowMain()
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = Output ? "Volume" : "Microphone", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            panel.Children.Add(Dim(DeviceName(_flow), 12));

            // [mute] ---- slider ---- 72%  [pick device]
            var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var muteGlyph = Glyph(Output ? "" : "", 16);
            var mute = new Button { Content = muteGlyph, MinWidth = 36, MinHeight = 34, Padding = new Thickness(8, 4, 8, 4), ToolTip = "Mute" };
            void ShowMuted()
            {
                bool muted = AudioService.GetMute(_flow) || (Output && AudioService.GetLevel(_flow) == 0);
                if (Output) muteGlyph.Text = muted ? "" : "";
                muteGlyph.SetResourceReference(TextBlock.ForegroundProperty, muted ? "HotBrush" : "TextBrush");
                mute.ToolTip = muted ? "Unmute" : "Mute";
            }
            mute.Click += (_, _) => { AudioService.SetMute(_flow, !AudioService.GetMute(_flow)); ShowMuted(); };
            row.Children.Add(mute);

            var slider = new Slider
            {
                Minimum = 0, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true,
                Value = Math.Max(0, AudioService.GetLevel(_flow)), Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);

            var value = new TextBlock { Text = $"{(int)slider.Value}", Width = 30, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            Grid.SetColumn(value, 2);
            row.Children.Add(value);

            slider.ValueChanged += (_, _) =>
            {
                AudioService.SetLevel(_flow, (int)slider.Value);
                value.Text = $"{(int)slider.Value}";
                ShowMuted();
            };

            var pick = IconButton("", Output ? "Select a sound output" : "Select a sound input", ShowDevices);
            Grid.SetColumn(pick, 3);
            row.Children.Add(pick);
            panel.Children.Add(row);
            ShowMuted();

            if (Output)
            {
                panel.Children.Add(NavRow("Volume mixer", "Set the volume of each app", ShowMixer));
            }
            else
            {
                AddListenToggle(panel);
            }

            var more = new Button { Content = "More sound settings", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
            more.Click += (_, _) => Open("ms-settings:sound");
            panel.Children.Add(more);

            Child = panel;
        }

        // ---------- microphone: listen to this device ----------

        private void AddListenToggle(StackPanel panel)
        {
            var id = AudioService.DefaultDeviceId(_flow);
            bool? listening = id == null ? null : AudioService.GetListen(id);

            var box = new CheckBox
            {
                Content = "Listen to this device",
                Tag = "Plays your microphone through your speakers or headphones so you can hear yourself. Use headphones, or you will get an echo.",
                IsChecked = listening == true,
                IsEnabled = listening != null,
                Margin = new Thickness(0, 12, 0, 0),
            };
            var status = Dim("", 11);
            status.Margin = new Thickness(0, 6, 0, 0);

            void Changed(bool on)
            {
                if (_building || id == null) return;
                if (AudioService.SetListen(id, on)) { status.Text = ""; return; }
                _building = true;
                box.IsChecked = !on;   // Windows would not change it: put the tick back
                _building = false;
                status.Text = "Windows would not change that. Try Sound settings instead.";
            }
            box.Checked += (_, _) => Changed(true);
            box.Unchecked += (_, _) => Changed(false);

            panel.Children.Add(box);
            panel.Children.Add(status);
        }

        // ---------- pick the sound output or input ----------

        private void ShowDevices()
        {
            var panel = new StackPanel();
            panel.Children.Add(Header(Output ? "Select a sound output" : "Select a sound input"));

            var devices = AudioService.Devices(_flow);
            if (devices.Count == 0) panel.Children.Add(Dim("No devices found.", 12));

            var status = Dim("", 11);
            status.Margin = new Thickness(0, 8, 0, 0);

            foreach (var device in devices)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var tick = Glyph(device.IsDefault ? "" : "", 14);
                tick.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                grid.Children.Add(tick);
                var name = new TextBlock { Text = device.Name, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = device.Name, HorizontalAlignment = HorizontalAlignment.Left };
                Grid.SetColumn(name, 1);
                grid.Children.Add(name);

                var chosen = device;
                panel.Children.Add(Row(grid, () =>
                {
                    if (chosen.IsDefault) return;
                    if (AudioService.SetDefault(chosen.Id)) ShowDevices();
                    else status.Text = "Windows would not switch to that device.";
                }, new Thickness(0, 0, 0, 4)));
            }
            panel.Children.Add(status);
            Child = panel;
        }

        // ---------- volume mixer ----------

        private void ShowMixer()
        {
            var panel = new StackPanel();
            panel.Children.Add(Header("Volume mixer"));

            var list = new StackPanel();
            list.Children.Add(MixerRow("", DeviceName(AudioService.Speakers), () => AudioService.GetLevel(AudioService.Speakers),
                                       v => AudioService.SetLevel(AudioService.Speakers, v)));

            var sessions = AudioService.Sessions();
            foreach (var s in sessions)
            {
                var session = s;
                list.Children.Add(MixerRow(session.Muted ? "" : "", session.Name, () => session.Level, v => session.SetLevel(v),
                                           () => session.Muted, m => session.SetMuted(m)));
            }
            if (sessions.Count == 0) list.Children.Add(Dim("No app is playing sound right now.", 12));

            panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Child = panel;
        }

        /// <summary>One line of the mixer: a mute button, the app's name, its slider and its level.</summary>
        private static UIElement MixerRow(string glyph, string name, Func<int> getLevel, Action<int> setLevel, Func<bool>? getMuted = null, Action<bool>? setMuted = null)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = Glyph(glyph, 14);
            var button = new Button { Content = icon, MinWidth = 34, MinHeight = 32, Padding = new Thickness(6, 2, 6, 2), IsEnabled = setMuted != null, ToolTip = "Mute" };
            void Paint()
            {
                bool muted = getMuted?.Invoke() ?? false;
                icon.Text = muted ? "" : "";
                icon.SetResourceReference(TextBlock.ForegroundProperty, muted ? "HotBrush" : "TextBrush");
            }
            if (setMuted != null) button.Click += (_, _) => { setMuted(!(getMuted?.Invoke() ?? false)); Paint(); };
            Paint();
            row.Children.Add(button);

            var label = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);

            var slider = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true, Value = getLevel(), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(slider, 2);
            row.Children.Add(slider);

            var value = new TextBlock { Text = $"{(int)slider.Value}", Width = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(value, 3);
            row.Children.Add(value);

            slider.ValueChanged += (_, _) => { setLevel((int)slider.Value); value.Text = $"{(int)slider.Value}"; Paint(); };
            return row;
        }
    }
}
