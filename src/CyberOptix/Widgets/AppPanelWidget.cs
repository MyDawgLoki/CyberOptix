using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>A desktop panel of app tiles. Click a tile to open that app, or jump to its window if it is already running.</summary>
    public class AppPanelWidget : DesktopWidget
    {
        public AppPanelWidget(AppPanel panel) : base("panel:" + panel.Id, 300, 520)
        {
            var body = new StackPanel();
            body.Children.Add(Text(14, panel.Title, 1, FontWeights.SemiBold));

            var tiles = new WrapPanel { Margin = new Thickness(0, 12, 0, 0), MaxWidth = 4 * 84 };
            foreach (var path in panel.Apps) tiles.Children.Add(MakeTile(path));

            if (panel.Apps.Count == 0)
            {
                var empty = Text(12, "No apps yet. Add some on the Dock page.", 0.6);
                empty.Margin = new Thickness(0, 10, 0, 0);
                body.Children.Add(empty);
            }
            else
            {
                body.Children.Add(tiles);
            }

            Card.Padding = new Thickness(18, 16, 18, 14);
            Card.Child = WithClose(body);
        }

        private static FrameworkElement MakeTile(string path)
        {
            var icon = new Image { Source = DockService.IconFor(path), Width = 40, Height = 40, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

            var name = new TextBlock
            {
                Text = DockService.DisplayName(path), FontSize = 11, Opacity = 0.8, TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0), MaxWidth = 72,
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(icon);
            stack.Children.Add(name);

            var tile = new Border
            {
                Child = stack, Width = 80, Padding = new Thickness(4, 8, 4, 8), Margin = new Thickness(2),
                CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                ToolTip = DockService.DisplayName(path),
            };
            tile.MouseEnter += (_, _) => tile.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
            tile.MouseLeftButtonDown += (_, e) => e.Handled = true; // a click on a tile must not start dragging the panel
            tile.MouseLeftButtonUp += (_, _) =>
            {
                var running = DockService.RunningWindows(IntPtr.Zero)
                    .Where(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase)).ToList();
                if (running.Count == 0) DockService.Launch(path);
                else DockService.Activate(running[0].Handle);
            };
            return tile;
        }
    }
}
