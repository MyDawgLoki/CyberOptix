using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>
    /// The window the dock's Widgets icon opens. Click anything in it to put it on the desktop, click it again to take it off:
    /// the desktop widgets (clock, weather, media player, temperatures, CPU, GPU), a desktop tile for every tool, Windows settings page, app
    /// and website icon the dock has, and Windows' own Widgets board. Like the dock's menus it closes when you click away.
    /// </summary>
    public sealed class WidgetsWindow : Window
    {
        private readonly List<Action> _refresh = new();
        private bool _closing;

        public WidgetsWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            Width = 620;
            Height = Math.Min(680, SystemParameters.WorkArea.Height - 40);
            WindowStartupLocation = WindowStartupLocation.Manual;
            SetResourceReference(ForegroundProperty, "TextBrush");
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            Content = BuildCard();

            Closing += (_, _) => _closing = true;
            Deactivated += (_, _) => { if (!_closing) Close(); };      // a menu: it closes when you click away
            KeyDown += (_, e) => { if (e.Key == Key.Escape && !_closing) Close(); };
            Closed += (_, _) => WidgetsWindowService.WindowClosed(this);
            Opacity = 0;
            Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        }

        private FrameworkElement BuildCard()
        {
            var card = new Border { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Padding = new Thickness(20, 16, 6, 14), Margin = new Thickness(16) };
            card.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
            var plainEffect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.5, Color = Colors.Black };
            card.Effect = plainEffect;
            CyberOptix.Services.WindowGlow.Attach(this, c => CyberOptix.Services.WindowGlow.ApplyOuter(card, c, plainEffect, "StrokeBrush"));

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var head = new StackPanel { Margin = new Thickness(0, 0, 14, 8) };
            head.Children.Add(new TextBlock { Text = "Widgets", FontSize = 18, FontWeight = FontWeights.SemiBold });
            var hint = new TextBlock
            {
                Text = "Click one to put it on your desktop, click it again to take it off. They stay locked where they land. To move them, right-click the desktop and choose Unlock Widgets.",
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            head.Children.Add(hint);
            root.Children.Add(head);

            var body = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };

            // ---- desktop widgets ----
            body.Children.Add(Heading("Desktop widgets"));
            var widgets = new WrapPanel();
            foreach (var w in WidgetChoices.All)
            {
                var key = w.Key;
                widgets.Children.Add(Tile(w.Name, Glyph(w.Glyph, 24), $"Put the {w.Name} widget on your desktop",
                    () => WidgetManager.IsEnabled(key),
                    () =>
                    {
                        if (WidgetManager.IsEnabled(key)) WidgetManager.Remove(key);
                        else WidgetChoices.Add(key, null, VisualTreeHelper.GetDpi(this).DpiScaleX);
                    }));
            }
            body.Children.Add(widgets);

            // ---- the tools, settings pages, apps and websites ----
            foreach (var group in DockWidgetCatalog.Groups)
            {
                var defs = DockWidgetCatalog.All.Where(d => d.Group == group && !IsDesktopWidget(d.Id) && DockWidgetCatalog.Available(d)).ToList();
                if (defs.Count == 0) continue;
                body.Children.Add(Heading(group == DockWidgetCatalog.Live ? "More" : group));
                var wrap = new WrapPanel();
                foreach (var def in defs)
                {
                    var id = def.Id;
                    FrameworkElement icon = def.BrandId != null || def.VendorId != null || def.AppPath != null ? BrandIcon.For(def, 26) : Glyph(def.Glyph, 24);
                    wrap.Children.Add(Tile(def.Title, icon, def.Description,
                        () => AppSettings.Current.DesktopTiles.Contains(id),
                        () =>
                        {
                            var tiles = AppSettings.Current.DesktopTiles;
                            if (!tiles.Remove(id)) tiles.Add(id);
                            WidgetManager.Apply();
                        }));
                }
                body.Children.Add(wrap);
            }

            // ---- Windows' own widgets ----
            if (StockWidgets.IsAvailable)
            {
                body.Children.Add(Heading("Windows widgets"));
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                var open = new Button { Content = "Open the Windows Widgets board" };
                open.Click += (_, _) => { Close(); StockWidgets.Toggle(); };
                row.Children.Add(open);
                body.Children.Add(row);
                var note = new TextBlock
                {
                    Text = "Windows keeps its own widgets (weather, news, calendar...) in its Widgets board and does not let other apps place them on the desktop, so this opens the board.",
                    FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4),
                };
                note.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
                body.Children.Add(note);
            }

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = body };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
            card.Child = root;
            return card;
        }

        /// <summary>The widgets that are real desktop widgets already (so they have no tile of their own), and the Widgets icon itself.</summary>
        private static bool IsDesktopWidget(string id) =>
            id == DockWidgetCatalog.WeatherId || id == DockWidgetCatalog.MediaId || id == DockWidgetCatalog.WidgetsId;

        private TextBlock Heading(string text)
        {
            var t = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 14, 0, 6) };
            return t;
        }

        private static TextBlock Glyph(string glyph, double size)
        {
            var t = new TextBlock { Text = glyph, FontSize = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            return t;
        }

        /// <summary>One clickable square: its icon and name. Lit in the accent color while it is on the desktop.</summary>
        private Border Tile(string name, FrameworkElement icon, string tip, Func<bool> isOn, Action toggle)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var iconHost = new Border { Height = 30, Child = icon, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(iconHost);
            stack.Children.Add(new TextBlock
            {
                Text = name, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 86, Margin = new Thickness(0, 5, 0, 0),
            });

            var tile = new Border
            {
                Width = 96, Height = 84, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(4), CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1), Child = stack, Cursor = Cursors.Hand, ToolTip = tip, Background = Brushes.Transparent,
            };
            void Look()
            {
                bool on = isOn();
                if (on)
                {
                    tile.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
                    tile.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                }
                else
                {
                    tile.Background = Brushes.Transparent;
                    tile.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
                }
            }
            Look();
            _refresh.Add(Look);
            tile.MouseEnter += (_, _) => { if (!isOn()) tile.SetResourceReference(Border.BackgroundProperty, "InputHoverBrush"); };
            tile.MouseLeave += (_, _) => Look();
            tile.MouseLeftButtonUp += (_, _) => { toggle(); foreach (var r in _refresh) r(); };
            return tile;
        }
    }

    /// <summary>Owns the one Widgets window.</summary>
    public static class WidgetsWindowService
    {
        private static WidgetsWindow? _window;

        /// <summary>The dock's Widgets icon: opens the window, or closes it when it is already open.</summary>
        public static void Toggle(Window? dock)
        {
            if (_window != null) { _window.Close(); return; }

            var window = new WidgetsWindow();
            _window = window;
            var area = SystemParameters.WorkArea;
            if (dock != null && dock.IsLoaded)
            {
                bool dockOnTop = AppSettings.Current.DockPosition == "Top";
                window.Left = Math.Clamp(dock.Left + dock.ActualWidth / 2 - window.Width / 2, 0, Math.Max(0, SystemParameters.PrimaryScreenWidth - window.Width));
                window.Top = dockOnTop ? dock.Top + dock.ActualHeight - 24 : dock.Top + 24 - window.Height;
            }
            else
            {
                window.Left = area.Left + (area.Width - window.Width) / 2;
                window.Top = area.Top + 70;
            }
            window.Show();
            window.Activate();
        }

        internal static void WindowClosed(WidgetsWindow w)
        {
            if (_window == w) _window = null;
        }

        public static void StopAll()
        {
            var w = _window;
            _window = null;
            if (w != null) { try { w.Close(); } catch { } }
        }
    }
}
