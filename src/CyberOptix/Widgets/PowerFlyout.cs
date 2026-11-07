using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>
    /// The small menu that opens above the Power icon on the dock: Restart and Shut down. Neither is a button of its own on the dock, so nothing
    /// can be clicked by accident; each one still asks before it does anything. It closes when you click anywhere else.
    /// </summary>
    public sealed class PowerFlyout : Window
    {
        private static PowerFlyout? _open;

        /// <summary>Opens the menu above the icon, or closes it if it is already open.</summary>
        public static void Toggle(FrameworkElement anchor)
        {
            if (_open != null) { _open.Close(); return; }
            try
            {
                var flyout = new PowerFlyout(anchor);
                _open = flyout;
                flyout.Closed += (_, _) => { if (_open == flyout) _open = null; };
                flyout.Show();
            }
            catch { _open = null; }
        }

        private PowerFlyout(FrameworkElement anchor)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SetResourceReference(ForegroundProperty, "TextBrush");
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

            var stack = new StackPanel();
            stack.Children.Add(Row("", "Restart", () => DockWidgetCatalog.PowerAction(true)));
            stack.Children.Add(Row("", "Shut down", () => DockWidgetCatalog.PowerAction(false)));
            var card = new Border
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(6), Margin = new Thickness(14), BorderThickness = new Thickness(1), Child = stack,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.45, Color = Colors.Black },
            };
            card.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
            Content = card;

            Deactivated += (_, _) => Close();
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
            Loaded += (_, _) => PlaceAbove(anchor);
        }

        /// <summary>Centered over the icon, with its bottom just above the icon (below it when there is no room above, for a dock at the top).</summary>
        private void PlaceAbove(FrameworkElement anchor)
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(anchor);
                var topLeft = anchor.PointToScreen(new Point(0, 0));
                double aw = anchor.ActualWidth * dpi.DpiScaleX, ah = anchor.ActualHeight * dpi.DpiScaleY;
                double w = ActualWidth * dpi.DpiScaleX, h = ActualHeight * dpi.DpiScaleY;
                double x = topLeft.X + aw / 2 - w / 2;
                double y = topLeft.Y - h + 8 * dpi.DpiScaleY;   // the window's own margin is empty room, so it sits almost on the icon
                if (y < 0) y = topLeft.Y + ah - 8 * dpi.DpiScaleY;
                var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)topLeft.X, (int)topLeft.Y)).WorkingArea;
                x = Math.Clamp(x, screen.Left, Math.Max(screen.Left, screen.Right - w));
                Left = x / dpi.DpiScaleX;
                Top = y / dpi.DpiScaleY;
                Activate();
            }
            catch { }
        }

        private UIElement Row(string glyph, string text, Action action)
        {
            var icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 15, Width = 22, VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = text, FontSize = 13, Margin = new Thickness(8, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            var row = new Border
            {
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, label } },
                Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(9), Background = Brushes.Transparent, Cursor = Cursors.Hand, MinWidth = 150,
            };
            row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (_, _) =>
            {
                Close();
                Dispatcher.BeginInvoke(action, System.Windows.Threading.DispatcherPriority.Background);
            };
            return row;
        }
    }
}
