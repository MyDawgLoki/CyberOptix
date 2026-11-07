using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>
    /// A small icon on the desktop that opens one tool, Windows settings page, app or website (the same ones the dock's icons open).
    /// Like every widget it stays clamped where it is until the widgets are unlocked. A click opens it.
    /// </summary>
    public sealed class TileWidget : DesktopWidget
    {
        public TileWidget(string id) : base("tile:" + id, 120 + Math.Max(0, AppSettings.Current.DesktopTiles.IndexOf(id)) % 8 * 96, 120 + Math.Max(0, AppSettings.Current.DesktopTiles.IndexOf(id)) / 8 * 110)
        {
            var def = DockWidgetCatalog.Find(id);
            Card.Padding = new Thickness(14, 12, 14, 10);
            Card.MinWidth = 84;

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            FrameworkElement icon;
            if (def != null && (def.BrandId != null || def.VendorId != null || def.AppPath != null)) icon = BrandIcon.For(def, 34);
            else
            {
                icon = new TextBlock
                {
                    Text = def?.Glyph ?? "", FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center,
                    FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
                };
            }
            stack.Children.Add(icon);
            stack.Children.Add(new TextBlock
            {
                Text = def?.Title ?? id, FontSize = 11.5, Margin = new Thickness(0, 7, 0, 0), TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, MaxWidth = 92,
            });
            Card.Child = WithClose(stack);

            if (def != null)
            {
                Card.Cursor = Cursors.Hand;
                Card.MouseLeftButtonUp += (_, e) =>
                {
                    if (WidgetLock.IsLocked && !e.Handled) def.Run();   // unlocked, a click is only for dragging
                };
            }
        }
    }
}
