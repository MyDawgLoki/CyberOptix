using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>
    /// A dock of its own for one group of icons (AI platforms, or social, streaming and chat). It sticks flush to one edge of the main screen
    /// (top, bottom, left or right) and lays its icons out in a row, or in a column on the left and right edges.
    /// </summary>
    public class IconGroupDockWindow : Window
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr h, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private const double Room = 14;   // transparent space round the dock for its glow and shadow

        /// <summary>Where an icon dock can sit: an edge of the screen, a corner, or to the left or right of the main dock.</summary>
        public static readonly (string Key, string Name)[] Positions =
        {
            ("Top", "Top"), ("Bottom", "Bottom"), ("Left", "Left"), ("Right", "Right"),
            ("TopLeft", "Top left"), ("TopRight", "Top right"), ("BottomLeft", "Bottom left"), ("BottomRight", "Bottom right"),
            ("BesideRight", "Right of main dock"), ("BesideLeft", "Left of main dock"),
        };

        public string Group { get; }
        private readonly Border _glass;
        private readonly Border _glow;
        private readonly StackPanel _icons = new();
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _layerTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
        private readonly DockLayerController _layerCtl = new();
        private string _appliedLayer = "";
        private string _signature = "";
        private string _glowSignature = "";
        private string _position = "Top";

        public IconGroupDockWindow(string group)
        {
            Group = group;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -20000; Top = -20000;
            SetResourceReference(ForegroundProperty, "TextBrush");
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

            _glass = new Border
            {
                CornerRadius = new CornerRadius(20), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(Room),
                BorderThickness = new Thickness(1), Child = _icons,
            };
            _glass.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
            _glass.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
            _glow = new Border
            {
                CornerRadius = new CornerRadius(20), Margin = new Thickness(Room), IsHitTestVisible = false, Visibility = Visibility.Collapsed,
                Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 14 },
            };
            var layers = new Grid();
            layers.Children.Add(_glow);
            layers.Children.Add(_glass);
            Content = layers;

            SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80);   // tool window: not in Alt+Tab
                Place();
            };
            SizeChanged += (_, _) => IconGroupDockService.PlaceAll();
            DpiChanged += (_, _) => IconGroupDockService.PlaceAll();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaysChanged;
            Closed += (_, _) => { _timer.Stop(); _layerTimer.Stop(); Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaysChanged; };
            // Each dock has its own layer, like the main dock: in front, falling behind windows (comes forward on hover), or sticking behind them.
            _layerTimer.Tick += (_, _) => LayerTick();
            _timer.Tick += (_, _) => Tick();
            Loaded += (_, _) => { _timer.Start(); _layerTimer.Start(); Tick(); LayerTick(); };
            Refresh();
        }

        private void DisplaysChanged(object? s, EventArgs e) => Dispatcher.BeginInvoke(new Action(IconGroupDockService.PlaceAll));

        /// <summary>The icons this dock shows: the ones ticked on the Dock page that belong to its group, in catalog order.</summary>
        public static List<DockWidgetDef> IconsFor(string key) => IconsByGroup(key).SelectMany(x => x).ToList();

        /// <summary>The icons of a dock (a list per group, so a dock could show several groups with a line between them).</summary>
        public static List<List<DockWidgetDef>> IconsByGroup(string key)
        {
            var chosen = new HashSet<string>(AppSettings.Current.DockExtraIcons);
            return new[] { key }.Select(g => DockWidgetCatalog.InGroup(g).Where(d => chosen.Contains(d.Id)).ToList()).Where(l => l.Count > 0).ToList();
        }

        /// <summary>Re-reads the settings and rebuilds the icons when something changed.</summary>
        public void Refresh()
        {
            var s = AppSettings.Current;
            _position = IconGroupDockService.PositionFor(Group);
            int glyph = s.DockWidgetAuto ? AppSettings.AutoWidgetPx(s.DockIconSize) : s.DockWidgetSize;
            var groups = IconsByGroup(Group);
            string sig = $"{_position}|{glyph}|{string.Join("/", groups.Select(g => string.Join(",", g.Select(i => i.Id))))}";
            if (sig != _signature)
            {
                _signature = sig;
                Build(groups, glyph);
            }
            ApplyGlow();
        }

        private void Build(List<List<DockWidgetDef>> groups, int glyphPx)
        {
            bool vertical = _position is "Left" or "Right";
            _icons.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            _icons.Children.Clear();
            double glyph = Math.Clamp(glyphPx, 8, 48);
            double scale = glyph / 23.0;
            double box = Math.Max(16, Math.Round(42 * scale));
            foreach (var def in groups.SelectMany((g, gi) => g.Select(d => (Def: d, StartsGroup: g[0] == d && gi > 0))).Select(x => x))
            {
                if (def.StartsGroup)
                    _icons.Children.Add(new Border   // a thin line between groups
                    {
                        Width = vertical ? box * 0.6 : 1, Height = vertical ? 1 : box * 0.6, Opacity = 0.45, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = vertical ? new Thickness(0, 5, 0, 5) : new Thickness(5, 0, 5, 0), Background = (Brush)Application.Current.FindResource("TextDimBrush"),
                    });
                var def2 = def.Def;
                var brand = def2.BrandId != null ? BrandLaunchers.Find(def2.BrandId) : null;
                var logo = BrandIcon.For(def2, glyph);
                var item = new Border
                {
                    Child = logo, Width = box, Height = box, CornerRadius = new CornerRadius(Math.Max(4, Math.Round(10 * scale))),
                    Background = Brushes.Transparent, Cursor = Cursors.Hand,
                    ToolTip = brand != null ? BrandLaunchers.Tooltip(brand) : def2.VendorId != null || def2.AppPath != null ? "Open " + def2.Title : def2.Title,
                };
                item.MouseEnter += (_, _) => item.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
                item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
                var run = def2.Run;
                item.MouseLeftButtonUp += (_, _) => { if (def2.Id == DockWidgetCatalog.PowerId) PowerFlyout.Toggle(item); else if (def2.AppPath != null) run(); else if (def2.BrandId != null) BrandLaunchers.Open(def2.BrandId); else if (def2.VendorId != null) HardwareSoftwareService.OpenById(def2.VendorId); else run(); };
                _icons.Children.Add(item);
            }
        }

        /// <summary>This dock's layer: "Top" (always in front), "Fall" (behind windows, comes forward on hover; the default) or "Stick" (behind windows, comes forward with the Start menu).</summary>
        public static string LayerFor(string group) =>
            AppSettings.Current.IndividualDockLayers.TryGetValue(group, out var v) && v is "Top" or "Stick" ? v : "Fall";

        private void Raise() => SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // topmost; no move, no resize, no focus
        private void SendBack()
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero) SetWindowPos(h, (IntPtr)1, 0, 0, 0, 0, 0x13);   // HWND_BOTTOM, NOSIZE | NOMOVE | NOACTIVATE
        }

        /// <summary>Runs about twenty times a second: brings the dock forward when the pointer rests on it (or the screen edge it sits on) or the Start menu opens, and sends it back after.</summary>
        private void LayerTick()
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            string layer = LayerFor(Group);
            if (layer != _appliedLayer)
            {
                _appliedLayer = layer;
                _layerCtl.Reset();
                Topmost = layer == "Top";
                if (layer == "Top") Raise(); else SendBack();
            }
            if (layer == "Top" || Visibility != Visibility.Visible) return;

            bool startOpen = StartMenuService.IsOpen(out _);
            bool over = layer == "Fall" && PointerOverThisDock(h, _layerCtl.InFront);
            switch (_layerCtl.Update(layer, startOpen, over, ScreenProbe.ButtonDown(), false, DateTime.UtcNow))
            {
                case DockLayerController.Move.Raise: Raise(); break;
                case DockLayerController.Move.Lower: SendBack(); break;
            }
        }

        private bool PointerOverThisDock(IntPtr h, bool alreadyOut)
        {
            if (!ScreenProbe.Pointer(out var px, out var py)) return false;
            if (!ScreenProbe.WindowRect(h, out var l, out var t, out var r, out var b)) return false;
            if (!ScreenProbe.MonitorRect(h, out _, out var screenTop, out _, out var screenBottom)) return false;
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int room = (int)Math.Round(Room * dpi), pad = alreadyOut ? (int)Math.Round(14 * dpi) : 0;
            bool top = _position.StartsWith("Top") || (_position.StartsWith("Beside") && AppSettings.Current.DockPosition == "Top");
            bool bottom = _position.StartsWith("Bottom") || (_position.StartsWith("Beside") && AppSettings.Current.DockPosition != "Top");
            if (top || bottom) return DockLayerController.PointerOverDock(px, py, l, t, r, b, room, pad, top, screenTop, screenBottom);   // the dock plus the screen edge behind it
            return px >= l + room - pad && px <= r - room + pad && py >= t + room - pad && py <= b - room + pad;                           // a dock on the left or right edge: just the dock
        }
        private void ApplyGlow()
        {
            var s = AppSettings.Current;
            string hex = MediaColorService.ForDock is Color m ? $"#{m.R:X2}{m.G:X2}{m.B:X2}" : s.DockGlowColor;
            string sig = s.DockGlow ? "on" + hex : "off";
            if (sig == _glowSignature) return;
            _glowSignature = sig;
            if (s.DockGlow && ThemeService.TryParse(hex, out var color))
            {
                var solid = Color.FromRgb(color.R, color.G, color.B);
                _glow.Background = new SolidColorBrush(solid);
                _glow.Opacity = 0.9;
                _glow.Visibility = Visibility.Visible;
                _glass.BorderBrush = new SolidColorBrush(solid);
                _glass.Effect = null;
            }
            else
            {
                _glow.Visibility = Visibility.Collapsed;
                _glass.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
                _glass.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.5, Color = Colors.Black };
            }
        }

        private void Tick()
        {
            Refresh();
            var s = AppSettings.Current;
            if (s.DockHideFullscreen)
            {
                DesktopHost.Query(new WindowInteropHelper(this).Handle, out var covers, out var maximized);
                bool hide = covers && LayerFor(Group) == "Top";   // behind the windows it is never hidden, or the pointer could not bring it back
                var want = hide ? Visibility.Hidden : Visibility.Visible;
                if (Visibility != want) Visibility = want;
            }
            else if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Puts the dock flush to its edge of the main screen. When both icon docks share an edge, the AI dock takes the first half of it and the
        /// social dock the second, so they never sit on each other (and stay clear of the main dock in the middle of the edge).
        /// </summary>
        /// <summary>The size of the dock itself (not the room round it), in pixels, so a dock beside it knows how far along to go.</summary>
        public int VisibleWidthPx()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            return Math.Max(0, (int)Math.Round((ActualWidth - 2 * Room) * dpi.DpiScaleX));
        }

        public void Place()
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            var main = DockService.Monitors().FirstOrDefault(m => m.Primary) ?? DockService.Monitors().FirstOrDefault();
            if (main == null) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            int w = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), ht = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
            if (w <= 0 || ht <= 0) return;
            int room = (int)Math.Round(Room * dpi.DpiScaleX), gap = (int)Math.Round(14 * dpi.DpiScaleX);
            int vw = w - 2 * room, vh = ht - 2 * room;   // the dock itself
            int left = main.WorkLeft, top = main.WorkTop, right = main.WorkRight, bottom = main.WorkBottom;

            // Docks in the same place: along an edge they split it between them; in a corner or beside the main dock they line up one after another.
            var sharing = IconGroupDockService.InPlacedOrder(IconGroupDockService.ActiveKeys().Where(k => IconGroupDockService.PositionFor(k) == _position));
            int slot = Math.Max(0, sharing.IndexOf(Group)), slots = Math.Max(1, sharing.Count);
            int before = 0;   // how much room the docks ahead of this one in the same place take
            for (int i = 0; i < slot; i++) before += (IconGroupDockService.Get(sharing[i])?.VisibleWidthPx() ?? 0) + gap;

            int x, y;
            switch (_position)
            {
                case "Bottom": y = bottom - ht + room; x = AlongEdge(left, right, vw, slot, slots) - room; break;
                case "Left": x = left - room; y = AlongEdge(top, bottom, vh, slot, slots) - room; break;
                case "Right": x = right - w + room; y = AlongEdge(top, bottom, vh, slot, slots) - room; break;
                case "TopLeft": x = left - room + before; y = top - room; break;
                case "TopRight": x = right - w + room - before; y = top - room; break;
                case "BottomLeft": x = left - room + before; y = bottom - ht + room; break;
                case "BottomRight": x = right - w + room - before; y = bottom - ht + room; break;
                case "BesideRight":
                case "BesideLeft":
                    if (DockService.MainDockRect() is { } m)
                    {
                        int visibleLeft = _position == "BesideLeft" ? m.X - gap - before - vw : m.X + m.Width + gap + before;
                        visibleLeft = Math.Clamp(visibleLeft, left, Math.Max(left, right - vw));   // never off the screen
                        x = visibleLeft - room;
                        int visibleTop = Math.Clamp(m.Y + (m.Height - vh) / 2, top, Math.Max(top, bottom - vh));
                        y = visibleTop - room;                                                 // level with it
                    }
                    else { y = top - room; x = AlongEdge(left, right, vw, slot, slots) - room; }   // no main dock: like Top
                    break;
                default: y = top - room; x = AlongEdge(left, right, vw, slot, slots) - room; break;
            }
            SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);   // NOSIZE | NOZORDER | NOACTIVATE
        }

        /// <summary>The start of the dock along an edge: centered in its own share of the edge (the whole edge when it has it to itself).</summary>
        private static int AlongEdge(int from, int to, int size, int slot, int slots)
        {
            int share = (to - from) / Math.Max(1, slots);
            return from + slot * share + (share - size) / 2;
        }
    }
}
