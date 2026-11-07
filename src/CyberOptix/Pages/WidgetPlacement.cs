using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CyberOptix.Models;
using CyberOptix.Services;
using CyberOptix.Widgets;

namespace CyberOptix.Pages
{
    /// <summary>
    /// A map of every display with the widgets drawn where they sit on that desktop. Click a widget below to add it to the chosen display, drag one
    /// from below onto a display (or drag a widget already on the map) to put it exactly where you want it. Widgets then stay clamped there:
    /// they only move after Unlock Widgets, and Restore Widgets Placing puts them back where the layout was last locked.
    /// </summary>
    public sealed class WidgetPlacement : StackPanel
    {
        private const string WidgetFormat = "pco-widget";

        private readonly Border _mapHost = new() { Height = 300, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0)), ClipToBounds = true };
        private readonly Canvas _map = new();
        private readonly WrapPanel _palette = new();
        private readonly TextBlock _status = new() { FontSize = 13, Margin = new Thickness(4, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _lockText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        private readonly Button _lockButton = new() { MinWidth = 150 };

        private List<DisplayInfo> _displays = new();
        private DisplayInfo? _selected;
        private Point _dragStart;
        private Point _grab;            // where inside a widget the pointer took hold of it, in map units
        private bool _dragged;

        public WidgetPlacement()
        {
            _mapHost.Child = _map;
            _mapHost.SizeChanged += (_, _) => BuildMap();
            _status.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            _lockText.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

            Children.Add(Title("Your screens"));
            Children.Add(Card(new StackPanel
            {
                Children = { _mapHost, Note("Every screen is shown with its widgets where they sit. Drag a widget to move it. Click a screen to choose it for the buttons below.") },
            }, 12));

            Children.Add(Title("Add a widget"));
            Children.Add(Card(new StackPanel
            {
                Children = { _palette, Note("Click a widget to add it to the chosen screen, or drag it onto a screen where you want it. Press x on a widget on the map to remove it.") },
            }, 16));

            Children.Add(Title("Placing"));
            Children.Add(Card(BuildLockCard(), 16));
            Children.Add(_status);

            foreach (var w in WidgetChoices.All) _palette.Children.Add(MakeChip(w.Key, w.Name, w.Glyph));
            IsVisibleChanged += (_, e) => { if (IsVisible) Reload(); };
            LockTexts();
            Loaded += (_, _) => { WidgetLock.Changed -= RefreshLock; WidgetLock.Changed += RefreshLock; RefreshLock(); };
            Unloaded += (_, _) => WidgetLock.Changed -= RefreshLock;
        }

        // ---------- the Placing card ----------

        private UIElement BuildLockCard()
        {
            _lockButton.Click += (_, _) => WidgetLock.Toggle();

            var restore = new Button { Content = "Restore Widgets Placing", MinWidth = 190, Margin = new Thickness(10, 0, 0, 0) };
            restore.ToolTip = "Puts every widget back where it was when the layout was last locked or placed.";
            restore.Click += (_, _) =>
            {
                bool ok = WidgetLock.RestorePlacing();
                _status.Text = ok ? "Widgets are back where they were placed." : "There is no saved placing yet. Place or lock your widgets first.";
                BuildMap();
            };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(_lockButton);
            buttons.Children.Add(restore);

            var menu = new CheckBox
            {
                Content = "Add Unlock Widgets and Restore Widgets Placing to the desktop right-click menu",
                IsChecked = AppSettings.Current.WidgetDesktopMenu,
                Margin = new Thickness(0, 14, 0, 0),
                ToolTip = "Right-click the desktop wallpaper (on Windows 11, under Show more options). Switch this off to remove them. The uninstaller removes them too.",
            };
            menu.Click += (_, _) =>
            {
                AppSettings.Current.WidgetDesktopMenu = menu.IsChecked == true;
                AppSettings.Save();
                DesktopMenuService.Sync();
            };

            var panel = new StackPanel();
            panel.Children.Add(_lockText);
            panel.Children.Add(buttons);
            panel.Children.Add(menu);
            return panel;
        }

        private void LockTexts()
        {
            _lockText.Text = WidgetLock.IsLocked
                ? "Widgets are locked in place. To move them, press Unlock Widgets, or right-click your desktop wallpaper and choose Unlock Widgets."
                : "Widgets are unlocked: drag them on your desktop or on the map above, then press Lock Widgets (or right-click the desktop and choose Lock Widgets) to clamp them where they are.";
            _lockButton.Content = WidgetLock.IsLocked ? "Unlock Widgets" : "Lock Widgets";
        }

        private void RefreshLock()
        {
            LockTexts();
            BuildMap();
        }

        // ---------- small helpers ----------

        private TextBlock Title(string text)
        {
            var t = new TextBlock { Text = text, Margin = new Thickness(4, 0, 0, 10) };
            t.SetResourceReference(StyleProperty, "GroupTitle");
            return t;
        }

        private Border Card(UIElement child, double pad)
        {
            var b = new Border { Child = child, Padding = new Thickness(pad) };
            b.SetResourceReference(StyleProperty, "Card");
            return b;
        }

        private TextBlock Note(string text)
        {
            var t = new TextBlock { Text = text, Margin = new Thickness(2, 10, 0, 0) };
            t.SetResourceReference(StyleProperty, "Note");
            return t;
        }

        private Brush Res(string key) => (Brush)FindResource(key);
        private double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

        private void Reload()
        {
            _displays = DisplayService.GetDisplays();
            _selected = _displays.FirstOrDefault(d => d.Id == _selected?.Id) ?? _displays.FirstOrDefault(d => d.IsPrimary) ?? _displays.FirstOrDefault();
            BuildMap();
            _status.Text = _displays.Count == 0 ? "Windows did not report any displays." : "";
        }

        // ---------- the map ----------

        private void BuildMap()
        {
            _map.Children.Clear();
            double w = _mapHost.ActualWidth, h = _mapHost.ActualHeight;
            if (_displays.Count == 0 || w < 50 || h < 50) return;

            int minX = _displays.Min(d => d.X), minY = _displays.Min(d => d.Y);
            int maxX = _displays.Max(d => d.X + d.Width), maxY = _displays.Max(d => d.Y + d.Height);
            const double pad = 28;
            double scale = Math.Min((w - 2 * pad) / (maxX - minX), (h - 2 * pad) / (maxY - minY));   // map units per real screen pixel
            double offX = (w - (maxX - minX) * scale) / 2, offY = (h - (maxY - minY) * scale) / 2;

            foreach (var d in _displays)
            {
                bool selected = d.Id == _selected?.Id;
                var tile = new Border
                {
                    Width = d.Width * scale,
                    Height = d.Height * scale,
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(2),
                    BorderBrush = selected ? Res("AccentBrush") : Res("StrokeBrush"),
                    Background = Res("InputBrush"),
                    ClipToBounds = true,
                    AllowDrop = true,
                    Cursor = Cursors.Hand,
                    Tag = d,
                    ToolTip = $"Display {d.Number}: {d.Resolution}{(d.IsPrimary ? " (main)" : "")}",
                };
                Canvas.SetLeft(tile, offX + (d.X - minX) * scale);
                Canvas.SetTop(tile, offY + (d.Y - minY) * scale);

                var grid = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) };
                var label = new StackPanel { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(10, 8, 0, 0), IsHitTestVisible = false };
                label.Children.Add(new TextBlock { Text = $"Display {d.Number}" + (d.IsPrimary ? "  ·  main" : ""), FontWeight = FontWeights.SemiBold });
                label.Children.Add(new TextBlock { Text = d.Resolution, FontSize = 12, Opacity = 0.8 });
                grid.Children.Add(label);

                // The widgets on this desktop, drawn where they sit and about as big as they are.
                var overlay = new Canvas();
                foreach (var wdg in WidgetChoices.All.Where(x => WidgetManager.IsEnabled(x.Key) && WidgetChoices.ScreenOf(x.Key, _displays, Scale)?.Id == d.Id))
                    overlay.Children.Add(MakeMarker(wdg.Key, wdg.Name, wdg.Glyph, d, scale));
                grid.Children.Add(overlay);

                tile.Child = grid;
                tile.MouseLeftButtonUp += (_, _) => { _selected = d; BuildMap(); };
                tile.DragEnter += TileDragOver;
                tile.DragOver += TileDragOver;
                tile.DragLeave += (_, _) => tile.BorderBrush = d.Id == _selected?.Id ? Res("AccentBrush") : Res("StrokeBrush");
                tile.Drop += TileDrop;
                _map.Children.Add(tile);
            }
        }

        /// <summary>One placed widget on the map: a rectangle the size and place the real widget has, with its name and an x to remove it.</summary>
        private Border MakeMarker(string key, string name, string glyph, DisplayInfo d, double mapScale)
        {
            double dpi = Scale;
            var (wDip, hDip) = WidgetChoices.SizeOf(key);
            double px = 0, py = 0;
            if (AppSettings.Current.Positions.TryGetValue(key, out var p) && p.Length >= 2) { px = p[0] * dpi; py = p[1] * dpi; }
            else { px = d.X + 40 * dpi; py = d.Y + 80 * dpi; }

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 4, 4, 0) };
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
            row.Children.Add(new TextBlock { Text = name, FontSize = 11, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center });
            var close = new TextBlock
            {
                Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 8, Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Remove widget",
            };
            close.MouseLeftButtonDown += (_, e) => { e.Handled = true; WidgetManager.Remove(key); BuildMap(); };
            row.Children.Add(close);

            var marker = new Border
            {
                Width = Math.Max(96, wDip * dpi * mapScale), Height = Math.Max(30, hDip * dpi * mapScale),
                CornerRadius = new CornerRadius(5), Child = row, Cursor = Cursors.SizeAll,
                Background = Res("AccentBrush"), Opacity = 0.92,
                ToolTip = "Drag to another spot or screen",
            };
            System.Windows.Documents.TextElement.SetForeground(marker, Res("OnAccentBrush"));
            Canvas.SetLeft(marker, (px - d.X) * mapScale);
            Canvas.SetTop(marker, (py - d.Y) * mapScale);

            marker.PreviewMouseLeftButtonDown += (_, e) => { _dragStart = e.GetPosition(null); _grab = e.GetPosition(marker); _dragged = false; };
            marker.PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed) return;
                var now = e.GetPosition(null);
                if (Math.Abs(now.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(now.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                _dragged = true;
                DragDrop.DoDragDrop(marker, new DataObject(WidgetFormat, key), DragDropEffects.Move);
            };
            return marker;
        }

        // ---------- the palette ----------

        private Border MakeChip(string key, string name, string glyph)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            });
            row.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center });

            var chip = new Border
            {
                Child = row, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 6), CornerRadius = new CornerRadius(16),
                Background = Res("InputHoverBrush"), Cursor = Cursors.Hand,
                ToolTip = "Click to add to the chosen screen, or drag onto a screen",
            };
            chip.PreviewMouseLeftButtonDown += (_, e) => { _dragStart = e.GetPosition(null); _grab = new Point(0, 0); _dragged = false; };
            chip.PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed) return;
                var now = e.GetPosition(null);
                if (Math.Abs(now.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(now.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                _dragged = true;
                DragDrop.DoDragDrop(chip, new DataObject(WidgetFormat, key), DragDropEffects.Move);
            };
            chip.MouseLeftButtonUp += (_, _) =>
            {
                if (_dragged) return;
                WidgetChoices.Add(key, _selected, Scale);
                BuildMap();
                _status.Text = $"{WidgetChoices.NameOf(key)} added" + (_selected != null ? $" to Display {_selected.Number}." : ".");
            };
            return chip;
        }

        // ---------- dropping ----------

        private void TileDragOver(object sender, DragEventArgs e)
        {
            var ok = e.Data.GetDataPresent(WidgetFormat);
            e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
            if (ok && sender is Border tile) tile.BorderBrush = Res("TextBrush");
            e.Handled = true;
        }

        private void TileDrop(object sender, DragEventArgs e)
        {
            if (sender is not Border { Tag: DisplayInfo d } tile || !e.Data.GetDataPresent(WidgetFormat)) return;
            var key = (string)e.Data.GetData(WidgetFormat)!;

            // Where on the display was it dropped? Map the widget's top-left corner (where it was grabbed) back to real screen pixels.
            double ts = tile.ActualWidth / d.Width;   // map units per real pixel
            var p = e.GetPosition(tile);
            double px = d.X + (p.X - _grab.X) / ts, py = d.Y + (p.Y - _grab.Y) / ts;

            // Keep the whole widget on this display.
            double dpi = Scale;
            var (wDip, hDip) = WidgetChoices.SizeOf(key);
            px = Math.Clamp(px, d.X + 8, Math.Max(d.X + 8, d.X + d.Width - (wDip + 12) * dpi));
            py = Math.Clamp(py, d.Y + 8, Math.Max(d.Y + 8, d.Y + d.Height - (hDip + 12) * dpi));

            WidgetManager.Place(key, px / dpi, py / dpi);
            _selected = d;
            BuildMap();
            _status.Text = $"{WidgetChoices.NameOf(key)} placed on Display {d.Number}. It stays there until you unlock the widgets.";
            e.Handled = true;
        }
    }
}
