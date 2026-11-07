using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using CyberOptix.Services;

namespace CyberOptix.Pages
{
    /// <summary>Lets the user pick which groups of original settings to put back. Groups CyberOptix never changed are shown but cannot be ticked.</summary>
    public class RestoreDialog : Window
    {
        private readonly List<(CheckBox Box, System.Action<RestoreOptions, bool> Set)> _rows = new();

        private static RestoreOptions None() => new()
        {
            Tweaks = false, Services = false, DisplayColors = false, Rotation = false, Remaps = false, Pointer = false, Scrolling = false, Cursors = false,
        };

        public RestoreOptions Result { get; private set; } = None();

        public RestoreDialog(RestoreOptions changed)
        {
            Title = "Restore original Windows settings";
            Width = 520; SizeToContent = SizeToContent.Height; MaxHeight = 760;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "PopupBrush");

            var root = new StackPanel { Margin = new Thickness(24) };
            root.Children.Add(Text("Choose what to put back", 17, true, "TextBrush"));
            root.Children.Add(Text("Pick only what you want undone. Anything you leave unticked stays the way it is now. Items CyberOptix never changed are greyed out.", 12.5, false, "TextDimBrush", new Thickness(0, 6, 0, 14)));

            Add(root, "Windows tweaks", "The tweaks you switched on, plus the performance profile (power plan, Game DVR, GPU scheduling, network).", changed.Tweaks, (o, v) => o.Tweaks = v);
            Add(root, "Background services", "Services the tweaks stopped or disabled, and services stopped by Free up resources.", changed.Services, (o, v) => o.Services = v);
            Add(root, "Display colors", "Brightness, contrast, gamma, digital vibrance and hue.", changed.DisplayColors, (o, v) => o.DisplayColors = v);
            Add(root, "Display rotation", "Puts each screen back in its original orientation.", changed.Rotation, (o, v) => o.Rotation = v);
            Add(root, "Key remaps", "Removes your key remaps. They finish after restarting your PC.", changed.Remaps, (o, v) => o.Remaps = v);
            Add(root, "Pointer speed and precision", "Mouse speed and Enhance pointer precision.", changed.Pointer, (o, v) => o.Pointer = v);
            Add(root, "Scrolling", "How many lines one mouse-wheel notch scrolls.", changed.Scrolling, (o, v) => o.Scrolling = v);
            Add(root, "Cursor theme and size", "Your cursor theme and cursor size.", changed.Cursors, (o, v) => o.Cursors = v);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var ok = new Button { Content = "Restore selected", Padding = new Thickness(14, 6, 14, 6), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(14, 6, 14, 6), IsCancel = true };
            ok.Click += (_, _) =>
            {
                var r = None();
                foreach (var (box, set) in _rows) set(r, box.IsChecked == true);
                Result = r;
                DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private void Add(Panel parent, string title, string detail, bool changed, System.Action<RestoreOptions, bool> set)
        {
            var box = new CheckBox { IsChecked = changed, IsEnabled = changed, Margin = new Thickness(0, 0, 0, 12), Opacity = changed ? 1 : 0.5 };
            var stack = new StackPanel();
            stack.Children.Add(Text(title + (changed ? "" : "  (not changed)"), 13.5, true, "TextBrush"));
            stack.Children.Add(Text(detail, 12, false, "TextDimBrush", new Thickness(0, 2, 0, 0)));
            box.Content = stack;
            parent.Children.Add(box);
            _rows.Add((box, set));
        }

        private static TextBlock Text(string text, double size, bool bold, string brush, Thickness? margin = null)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            if (margin != null) t.Margin = margin.Value;
            return t;
        }
    }
}
