using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>Draws the logo for an app or website icon on the dock, from its vector outline in the logo's own color.</summary>
    public static class BrandIcon
    {
        /// <summary>The marker the dock uses to find these icons again when it resizes them.</summary>
        public const string Marker = "brand";

        public static FrameworkElement Create(string id, double size)
        {
            var launcher = BrandLaunchers.Find(id);
            if (id == "multiset") return MultiSetGem(size);   // its logo is a purple faceted hexagon: drawn here, facet by facet

            // Grok Bot is a program on this PC: its own icon, taken from its Start menu shortcut.
            if (id == BrandLaunchers.GrokBotId || (launcher?.StartMenuName != null && !BrandIconPaths.Paths.ContainsKey(id)))
            {
                var shortcut = BrandLaunchers.StartMenuShortcut(launcher?.StartMenuName ?? "Grok Bot");
                var picture = shortcut != null ? DockService.ShortcutIcon(shortcut) : null;
                if (picture != null)
                {
                    var image = new Image { Source = picture, Width = size, Height = size, Stretch = Stretch.Uniform, Tag = Marker, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    return image;
                }
                return Fallback(size);
            }

            // Claude Duet is a Claude product: it uses the Claude outline, in its own color
            string pathKey = id == "claudeduet" ? "claude" : id;
            if (launcher != null && BrandIconPaths.Paths.TryGetValue(pathKey, out var data))
            {
                try
                {
                    ThemeService.TryParse(launcher.ColorHex, out var color);
                    var geometry = Geometry.Parse(data);
                    geometry.Freeze();
                    return new Path
                    {
                        Data = geometry, Fill = new SolidColorBrush(color), Stretch = Stretch.Uniform, Width = size, Height = size, Tag = Marker,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    };
                }
                catch { }
            }
            // no outline for this one: a rounded square in its color with its first letter
            if (launcher != null) return Badge(launcher, size);
            return Fallback(size);
        }

        /// <summary>The MultiSet AI shape: a hexagon cut into six purple facets, lit from the upper left, with a soft purple glow.</summary>
        private static FrameworkElement MultiSetGem(double size)
        {
            var canvas = new Canvas { Width = 24, Height = 24 };
            var hex = new[] { new Point(12, 1.2), new Point(21.4, 6.6), new Point(21.4, 17.4), new Point(12, 22.8), new Point(2.6, 17.4), new Point(2.6, 6.6) };
            var hub = new Point(11.2, 11.6);   // the point the facets meet at, a little off center like a cut stone
            string[] colors = { "#C9A8FF", "#A57CFF", "#8A56F2", "#5F34C9", "#4A2796", "#7D4FE3" };   // light at the upper left, dark at the bottom
            for (int i = 0; i < 6; i++)
            {
                ThemeService.TryParse(colors[i], out var c);
                var facet = new System.Windows.Shapes.Polygon
                {
                    Points = new PointCollection { hub, hex[i], hex[(i + 1) % 6] }, Fill = new SolidColorBrush(c),
                    Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0x20, 0x0A, 0x50)), StrokeThickness = 0.35, StrokeLineJoin = PenLineJoin.Round,
                };
                canvas.Children.Add(facet);
            }
            return new Viewbox
            {
                Child = canvas, Width = size, Height = size, Stretch = Stretch.Uniform, Tag = Marker,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0x8A, 0x56, 0xF2), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.55 },
            };
        }

        /// <summary>A plain stand-in logo: a rounded square in the service's color with its first letter. Used where no official outline is bundled.</summary>
        private static FrameworkElement Badge(BrandLauncher launcher, double size)
        {
            ThemeService.TryParse(launcher.ColorHex, out var color);
            var letter = new TextBlock
            {
                Text = launcher.Name.Substring(0, 1).ToUpperInvariant(), FontWeight = FontWeights.Bold, FontSize = size * 0.62, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.26), Background = new SolidColorBrush(color), Tag = Marker, Child = letter,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        }

        /// <summary>The icon of a hardware maker's app (MSI Center, iCUE...): the app's own icon as Windows shows it in the Start menu.</summary>
        public static FrameworkElement CreateVendor(string vendorId, double size)
        {
            var appId = HardwareSoftwareService.StartAppIdOf(vendorId);
            ImageSource? picture = null;
            if (appId != null && appId.Contains("://"))
            {
                // a Steam program (Wallpaper Engine): the icon is in its Start-menu shortcut
                var shortcut = HardwareSoftwareService.ShortcutFileFor(vendorId);
                if (shortcut != null) picture = DockService.IconFor(shortcut);
            }
            else if (appId != null) picture = ShellApps.IconOf(ShellApps.PathFor(appId), 64);
            if (picture == null) return Fallback(size);
            var image = new Image { Source = picture, Width = size, Height = size, Stretch = Stretch.Uniform, Tag = Marker, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }

        /// <summary>The icon for one catalog entry that draws a logo: a brand's, or a hardware maker's app icon.</summary>
        public static FrameworkElement For(DockWidgetDef def, double size) =>
            def.AppPath != null ? CreateApp(def.AppPath, size)
            : def.VendorId != null ? CreateVendor(def.VendorId, size)
            : def.BrandId == null && !string.IsNullOrEmpty(def.Glyph) ? CreateGlyph(def.Glyph, size)
            : Create(def.BrandId ?? def.Id, size);

        /// <summary>The icon of a built-in tool (Lock this PC, Task Manager...): its Windows icon-font glyph, the same one the main dock shows.</summary>
        public static FrameworkElement CreateGlyph(string glyph, double size) => new TextBlock
        {
            Text = glyph, FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = size, Tag = Marker,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>The icon of a program or shortcut the user added: its own icon, as Windows shows it.</summary>
        public static FrameworkElement CreateApp(string path, double size)
        {
            var picture = DockService.IconFor(path);
            if (picture == null) return Fallback(size);
            var image = new Image { Source = picture, Width = size, Height = size, Stretch = Stretch.Uniform, Tag = Marker, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }

        /// <summary>A plain "app" glyph for when no logo is available (Grok Bot not installed, or an outline that would not load).</summary>
        private static FrameworkElement Fallback(double size)
        {
            var text = new TextBlock
            {
                Text = "", FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = size, Tag = Marker,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            return text;
        }

        /// <summary>Sets the size of one of these icons (a logo, a picture or the plain glyph).</summary>
        public static void Resize(FrameworkElement icon, double size)
        {
            if (icon is TextBlock t) { t.FontSize = size; return; }
            if (icon is Border badge && badge.Child is TextBlock letter) { badge.Width = badge.Height = size; badge.CornerRadius = new CornerRadius(size * 0.26); letter.FontSize = size * 0.62; return; }
            icon.Width = icon.Height = size;
        }
    }
}
