using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Pages
{
    /// <summary>
    /// A map of every display with the desktop clock drawn on it as a block. Drag the block to any spot on any screen and the real clock
    /// follows when you let go. (The nine "Place it" positions still work: choosing one puts the clock back on its edge or corner.)
    /// </summary>
    public sealed class ClockPlacementMap : Border
    {
        private readonly Canvas _map = new();
        private List<DisplayInfo> _displays = new();
        private Border? _marker;
        private Point _grab;
        private double _mapScale, _offX, _offY;
        private int _minX, _minY;

        /// <summary>Raised after the clock was dropped on a new spot, so the page can update its other controls.</summary>
        public event Action? Moved;

        public ClockPlacementMap()
        {
            Height = 220;
            CornerRadius = new CornerRadius(10);
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0));
            ClipToBounds = true;
            Child = _map;
            SizeChanged += (_, _) => Rebuild();
            IsVisibleChanged += (_, _) => { if (IsVisible) Reload(); };
        }

        /// <summary>Redraws the block after a size, theme or place change, without asking Windows for the displays again.</summary>
        public void Refresh() => Rebuild();

        public void Reload()
        {
            _displays = DisplayService.GetDisplays();
            Rebuild();
        }

        private double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;

        /// <summary>About how big the real clock is on screen, in pixels, from its theme and size setting.</summary>
        private static (double W, double H) ClockSize(double dpi)
        {
            var s = AppSettings.Current;
            var t = DesktopClockThemes.Find(s.DesktopClockTheme);
            double k = Math.Clamp(s.DesktopClockScale, 0.4, 3.0);
            double w = (t.Stacked ? t.TimeSize * 1.4 : t.TimeSize * 3.6) * k + 48;
            double h = (t.Stacked ? t.TimeSize * 1.9 : t.TimeSize * 1.35) * k + (s.DesktopClockShowDate ? t.DateSize * 1.5 * k : 0) + 48;
            return (w * dpi, h * dpi);
        }

        /// <summary>The clock's center on a display, in screen pixels, for the saved spot (a dragged one, or one of the nine places).</summary>
        private (DisplayInfo Display, double X, double Y)? CenterOnScreen(double dpi)
        {
            if (_displays.Count == 0) return null;
            var s = AppSettings.Current;
            var d = _displays.FirstOrDefault(x => x.Id == s.DesktopClockDisplay) ?? _displays.FirstOrDefault(x => x.IsPrimary) ?? _displays[0];
            var (w, h) = ClockSize(dpi);
            if (s.DesktopClockCustom)
                return (d, d.X + Math.Clamp(s.DesktopClockX, 0, 1) * d.Width, d.Y + Math.Clamp(s.DesktopClockY, 0, 1) * d.Height);

            double margin = 40 * dpi, a = 0;
            string anchor = s.DesktopClockAnchor;
            double x = anchor.EndsWith("Left") ? d.X + margin + w / 2 : anchor.EndsWith("Right") ? d.X + d.Width - margin - w / 2 : d.X + d.Width / 2.0;
            double y = anchor.StartsWith("Top") ? d.Y + margin + 40 * dpi + h / 2 : anchor.StartsWith("Bottom") ? d.Y + d.Height - margin - h / 2 : d.Y + d.Height / 2.0;
            _ = a;
            return (d, x, y);
        }

        private void Rebuild()
        {
            _map.Children.Clear();
            _marker = null;
            double w = ActualWidth, h = ActualHeight;
            if (_displays.Count == 0 || w < 50 || h < 50) return;

            _minX = _displays.Min(d => d.X); _minY = _displays.Min(d => d.Y);
            int maxX = _displays.Max(d => d.X + d.Width), maxY = _displays.Max(d => d.Y + d.Height);
            const double pad = 18;
            _mapScale = Math.Min((w - 2 * pad) / (maxX - _minX), (h - 2 * pad) / (maxY - _minY));
            _offX = (w - (maxX - _minX) * _mapScale) / 2;
            _offY = (h - (maxY - _minY) * _mapScale) / 2;

            foreach (var d in _displays)
            {
                var tile = new Border
                {
                    Width = d.Width * _mapScale, Height = d.Height * _mapScale, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2),
                    Background = (Brush)FindResource("InputBrush"), IsHitTestVisible = false,
                    ToolTip = $"Display {d.Number}: {d.Resolution}",
                };
                tile.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
                var label = new TextBlock { Text = $"Display {d.Number}" + (d.IsPrimary ? "  ·  main" : ""), FontSize = 11, Margin = new Thickness(8, 5, 0, 0), Opacity = 0.7 };
                tile.Child = label;
                Canvas.SetLeft(tile, _offX + (d.X - _minX) * _mapScale);
                Canvas.SetTop(tile, _offY + (d.Y - _minY) * _mapScale);
                _map.Children.Add(tile);
            }

            if (!AppSettings.Current.DesktopClock) return;   // nothing to place while the clock is off
            var center = CenterOnScreen(Dpi);
            if (center == null) return;
            var (cw, ch) = ClockSize(Dpi);

            var text = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            text.Children.Add(new TextBlock { Text = DateTime.Now.ToString("h:mm"), FontWeight = FontWeights.SemiBold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
            text.Children.Add(new TextBlock { Text = "drag me", FontSize = 9, Opacity = 0.8, HorizontalAlignment = HorizontalAlignment.Center });
            _marker = new Border
            {
                Width = Math.Max(54, cw * _mapScale), Height = Math.Max(30, ch * _mapScale), CornerRadius = new CornerRadius(5),
                Cursor = Cursors.SizeAll, Opacity = 0.92, Child = text, ToolTip = "Drag the clock to where you want it",
            };
            _marker.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            System.Windows.Documents.TextElement.SetForeground(_marker, (Brush)FindResource("OnAccentBrush"));
            Canvas.SetLeft(_marker, _offX + (center.Value.X - _minX) * _mapScale - _marker.Width / 2);
            Canvas.SetTop(_marker, _offY + (center.Value.Y - _minY) * _mapScale - _marker.Height / 2);

            _marker.MouseLeftButtonDown += MarkerDown;
            _marker.MouseMove += MarkerMove;
            _marker.MouseLeftButtonUp += MarkerUp;
            _map.Children.Add(_marker);
        }

        private void MarkerDown(object sender, MouseButtonEventArgs e)
        {
            if (_marker == null) return;
            _grab = e.GetPosition(_marker);
            _marker.CaptureMouse();
            e.Handled = true;
        }

        private void MarkerMove(object sender, MouseEventArgs e)
        {
            if (_marker == null || !_marker.IsMouseCaptured) return;
            var p = e.GetPosition(_map);
            double x = Math.Clamp(p.X - _grab.X, 0, Math.Max(0, ActualWidth - _marker.Width));
            double y = Math.Clamp(p.Y - _grab.Y, 0, Math.Max(0, ActualHeight - _marker.Height));
            Canvas.SetLeft(_marker, x);
            Canvas.SetTop(_marker, y);
        }

        private void MarkerUp(object sender, MouseButtonEventArgs e)
        {
            if (_marker == null || !_marker.IsMouseCaptured) return;
            _marker.ReleaseMouseCapture();

            // Where the middle of the block is, in real screen pixels; the display it landed on (or the nearest one) takes the clock.
            double cx = Canvas.GetLeft(_marker) + _marker.Width / 2, cy = Canvas.GetTop(_marker) + _marker.Height / 2;
            double sx = (cx - _offX) / _mapScale + _minX, sy = (cy - _offY) / _mapScale + _minY;
            var d = _displays.FirstOrDefault(x => sx >= x.X && sx < x.X + x.Width && sy >= x.Y && sy < x.Y + x.Height)
                    ?? _displays.OrderBy(x => Math.Pow(Math.Clamp(sx, x.X, x.X + x.Width) - sx, 2) + Math.Pow(Math.Clamp(sy, x.Y, x.Y + x.Height) - sy, 2)).First();

            var s = AppSettings.Current;
            s.DesktopClockDisplay = d.Id;
            s.DesktopClockCustom = true;
            s.DesktopClockX = Math.Round(Math.Clamp((sx - d.X) / d.Width, 0, 1), 4);
            s.DesktopClockY = Math.Round(Math.Clamp((sy - d.Y) / d.Height, 0, 1), 4);
            AppSettings.Save();
            DesktopClockService.Apply();
            Rebuild();
            Moved?.Invoke();
            e.Handled = true;
        }
    }
}
