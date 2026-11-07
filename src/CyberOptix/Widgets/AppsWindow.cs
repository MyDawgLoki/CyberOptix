using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>
    /// A desktop in a window: the apps, folders and files on the user's desktop as tiles. Click one to open it.
    /// Opened from the dock's Apps button it behaves like a menu and closes when you click away. "Pin to desktop" turns it into a widget that
    /// stays exactly where you put it, behind your other windows, and comes back at every launch.
    /// </summary>
    public class AppsWindow : Window
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        public const string PositionKey = "apps";

        private readonly WrapPanel _tiles = new() { Margin = new Thickness(0, 4, 0, 0) };
        private readonly TextBlock _count = new() { FontSize = 12 };
        private readonly TextBlock _empty = new() { Text = "Nothing on your desktop yet.", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 0), Visibility = Visibility.Collapsed };
        private readonly DispatcherTimer _savePosition = new() { Interval = TimeSpan.FromMilliseconds(400) };
        private readonly DispatcherTimer _saveSize = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private ScrollViewer? _scroller;
        private readonly Border _moveButton = new();
        private readonly Border _moveBackButton = new();
        private readonly Border _confirmBar = new();
        private readonly TextBlock _confirmText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        private IDisposable? _watch;
        private bool _temporarilyOnTop;

        public bool Pinned { get; private set; }

        public AppsWindow(bool pinned)
        {
            Pinned = pinned;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            // Drag any edge or corner to resize. The icons re-flow to fit, and the size is remembered.
            ResizeMode = ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0, ResizeBorderThickness = new Thickness(14), GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
            });
            MinWidth = 360; MinHeight = 320;
            var saved = AppSettings.Current;
            Width = saved.AppsWidth >= MinWidth ? Math.Min(saved.AppsWidth, SystemParameters.WorkArea.Width) : 690;
            Height = saved.AppsHeight >= MinHeight ? Math.Min(saved.AppsHeight, SystemParameters.WorkArea.Height) : 560;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SetResourceReference(ForegroundProperty, "TextBrush");
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

            Content = BuildCard();
            ApplyMode();
            Reload();

            SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80); // WS_EX_TOOLWINDOW: not in Alt+Tab
                SendBottomIfNeeded();
            };
            Activated += (_, _) => SendBottomIfNeeded();
            Closing += (_, _) => _closing = true;
            Deactivated += (_, _) =>
            {
                if (_closing) return;                                   // closing already deactivates the window; closing again would throw
                if (!Pinned) { Close(); return; }                       // a menu closes when you click away
                if (_temporarilyOnTop) { _temporarilyOnTop = false; Topmost = AppSettings.Current.WidgetsOnTop; SendBottomIfNeeded(); }
            };
            KeyDown += (_, e) => { if (e.Key == Key.Escape && !Pinned && !_closing) Close(); };
            LocationChanged += (_, _) => { if (Pinned) { _savePosition.Stop(); _savePosition.Start(); } };
            SizeChanged += (_, _) => { _saveSize.Stop(); _saveSize.Start(); };
            _saveSize.Tick += (_, _) =>
            {
                _saveSize.Stop();
                AppSettings.Current.AppsWidth = Math.Round(ActualWidth);
                AppSettings.Current.AppsHeight = Math.Round(ActualHeight);
                AppSettings.Save();
            };
            _savePosition.Tick += (_, _) => { _savePosition.Stop(); SavePosition(); };
            Closed += (_, _) =>
            {
                _watch?.Dispose();
                if (Pinned) SavePosition();
                AppsPanelService.WindowClosed(this);
            };

            _watch = DesktopItemsService.Watch(() => Dispatcher.BeginInvoke(new Action(Reload)));

            Opacity = 0;
            Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        }

        private bool _closing;

        private const double MinTile = 96;

        /// <summary>Stretches the tiles so every row fills the width exactly: as many columns as fit, shared out evenly.</summary>
        private void FitTiles()
        {
            if (_scroller == null) return;
            double available = _scroller.ViewportWidth;
            if (available <= 0) available = _scroller.ActualWidth;
            if (available <= 0) return;

            int columns = Math.Max(1, (int)Math.Floor(available / (MinTile + 4)));
            double width = Math.Max(MinTile, Math.Floor(available / columns) - 4);
            foreach (var tile in _tiles.Children.OfType<FrameworkElement>()) tile.Width = width;
        }

        // ---------- look ----------

        private FrameworkElement BuildCard()
        {
            var card = new Border { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Padding = new Thickness(20, 16, 20, 14), Margin = new Thickness(16) };
            card.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
            var plainEffect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.5, Color = Colors.Black };
            card.Effect = plainEffect;
            CyberOptix.Services.WindowGlow.Attach(this, c => CyberOptix.Services.WindowGlow.ApplyOuter(card, c, plainEffect, "StrokeBrush"));

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // header: title and count on the left, the header is also the drag handle. There is no close button: a menu closes when you click away, and a pinned panel is removed from its right-click menu.
            var header = new Grid { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.MouseLeftButtonDown += (_, _) => { if (Pinned && !WidgetLock.IsLocked) { try { DragMove(); } catch { } SavePosition(); } };

            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = "Apps", FontSize = 18, FontWeight = FontWeights.SemiBold });
            _count.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            titles.Children.Add(_count);
            header.Children.Add(titles);

            grid.Children.Add(header);

            var body = new Grid();
            var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _tiles };
            _scroller = scroller;
            scroller.SizeChanged += (_, _) => FitTiles();
            body.Children.Add(scroller);
            _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            body.Children.Add(_empty);
            Grid.SetRow(body, 1);
            grid.Children.Add(body);

            var footer = BuildFooter();
            Grid.SetRow(footer, 2);
            grid.Children.Add(footer);

            card.Child = grid;
            BuildWindowMenu(card);
            return card;
        }

        private void ApplyMode()
        {
            if (Pinned)
            {
                Topmost = AppSettings.Current.WidgetsOnTop;
                ShowActivated = false;
            }
            else
            {
                Topmost = true;
            }
            if (_status.Text.Length == 0 || _status.Text == PinHint) _status.Text = Pinned ? "" : PinHint;
        }

        private const string PinHint = "Right-click the window to pin it to the desktop.";

        /// <summary>The window's right-click menu: pin it to the desktop, or take it off again.</summary>
        private void BuildWindowMenu(FrameworkElement target)
        {
            var menu = new ContextMenu();
            var item = new MenuItem();
            item.Click += (_, _) => TogglePin();
            menu.Items.Add(item);
            menu.Opened += (_, _) => item.Header = Pinned ? "Unpin From Desktop" : "Pin To Desktop";
            target.ContextMenu = menu;
        }

        // ---------- pinning ----------

        private void TogglePin()
        {
            if (Pinned)
            {
                // Unpin = take it off the desktop.
                AppSettings.Current.AppsPinned = false;
                AppSettings.Save();
                Pinned = false; // so closing does not write a position
                Close();
                return;
            }

            Pinned = true;
            AppSettings.Current.AppsPinned = true;
            if (_status.Text == PinHint) _status.Text = "";
            ApplyMode();
            SavePosition();       // it stays exactly where it is now
            SendBottomIfNeeded();
        }

        private void SavePosition()
        {
            AppSettings.Current.Positions[PositionKey] = new[] { Left, Top };
            AppSettings.Save();
        }

        /// <summary>Raises a pinned panel above other windows just until you click elsewhere.</summary>
        public void BringToFront()
        {
            _temporarilyOnTop = true;
            Topmost = true;
            Activate();
        }

        private void SendBottomIfNeeded()
        {
            if (!Pinned || Topmost) return;
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero) SetWindowPos(h, (IntPtr)1, 0, 0, 0, 0, 0x13); // HWND_BOTTOM, no size, no move, no activate
        }

        // ---------- moving desktop shortcuts here ----------

        private static void StyleChip(Border chip, string glyph, string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = glyph, FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.Medium, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            chip.Child = row;
            chip.Padding = new Thickness(12, 7, 14, 7);
            chip.CornerRadius = new CornerRadius(10);
            chip.BorderThickness = new Thickness(1);
            chip.Cursor = Cursors.Hand;
            chip.Margin = new Thickness(0, 0, 8, 0);
            chip.Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            chip.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
            chip.MouseEnter += (_, _) => chip.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            chip.MouseLeave += (_, _) => chip.Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
        }

        private FrameworkElement BuildFooter()
        {
            var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

            StyleChip(_moveButton, "\uE8DE", "Move Desktop Items Here");
            _moveButton.ToolTip = new TextBlock { Text = "Takes everything off your desktop (shortcuts, apps, files and folders) and puts it in this panel. You can move it all back.", TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
            _moveButton.MouseLeftButtonUp += (_, _) => AskToMove();

            StyleChip(_moveBackButton, "\uE7A7", "Move To Desktop");
            _moveBackButton.ToolTip = new TextBlock { Text = "Puts everything in this panel back on your desktop, in the place it came from. Start-up entries follow them, and nothing is duplicated.", TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
            _moveBackButton.MouseLeftButtonUp += (_, _) => MoveAllBack();

            var buttons = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(_moveButton);
            buttons.Children.Add(_moveBackButton);
            _status.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            buttons.Children.Add(_status);

            // The question before anything is moved
            _confirmBar.CornerRadius = new CornerRadius(10);
            _confirmBar.Padding = new Thickness(14, 10, 14, 10);
            _confirmBar.BorderThickness = new Thickness(1);
            _confirmBar.Visibility = Visibility.Collapsed;
            _confirmBar.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            _confirmBar.SetResourceReference(Border.BorderBrushProperty, "AccentBorderBrush");
            var confirmBody = new StackPanel();
            confirmBody.Children.Add(_confirmText);
            var confirmButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var yes = new Border(); StyleChip(yes, "\uE73E", "Move them");
            yes.MouseLeftButtonUp += (_, _) => MoveNow();
            var no = new Border(); StyleChip(no, "\uE711", "Cancel");
            no.MouseLeftButtonUp += (_, _) => { _confirmBar.Visibility = Visibility.Collapsed; buttons.Visibility = Visibility.Visible; };
            confirmButtons.Children.Add(yes);
            confirmButtons.Children.Add(no);
            confirmBody.Children.Add(confirmButtons);
            _confirmBar.Child = confirmBody;

            _confirmBar.Tag = buttons;
            footer.Children.Add(buttons);
            footer.Children.Add(_confirmBar);
            return footer;
        }

        private void AskToMove()
        {
            var plan = DesktopItemsService.PlanMove();
            if (plan.Total == 0)
            {
                _status.Text = "There is nothing on your desktop to move.";
                return;
            }

            // Say plainly what is about to move.
            var parts = new System.Collections.Generic.List<string>();
            if (plan.Shortcuts.Count > 0) parts.Add($"{plan.Shortcuts.Count} app shortcut{(plan.Shortcuts.Count == 1 ? "" : "s")}");
            if (plan.Folders.Count > 0) parts.Add($"{plan.Folders.Count} folder{(plan.Folders.Count == 1 ? "" : "s")}");
            if (plan.Files.Count > 0) parts.Add($"{plan.Files.Count} file{(plan.Files.Count == 1 ? "" : "s")}");
            if (plan.Duplicates.Count > 0) parts.Add($"{plan.Duplicates.Count} copy of a shortcut{(plan.Duplicates.Count == 1 ? "" : "s")} this panel already has");
            string what = string.Join(", ", parts);

            _status.Text = "";
            _confirmText.Text = $"Move {what} off your desktop into this panel? They stay one click away here, and Move To Desktop puts every one back where it came from. " +
                                "Nothing is deleted. Folders and files go into a hidden folder inside your Desktop, so they stay on the same drive (and in OneDrive, if your desktop is there). " +
                                (plan.Shared > 0 ? $"{plan.Shared} of the shortcuts are on the shared desktop, so they leave the desktop of every Windows account on this PC until you move them back. " : "") +
                                "A shortcut this panel already has is tucked away instead of shown twice.";
            if (_confirmBar.Tag is UIElement buttons) buttons.Visibility = Visibility.Collapsed;
            _confirmBar.Visibility = Visibility.Visible;
        }

        private void MoveNow()
        {
            var r = DesktopItemsService.MoveDesktopItemsHere();
            _confirmBar.Visibility = Visibility.Collapsed;
            if (_confirmBar.Tag is UIElement buttons) buttons.Visibility = Visibility.Visible;
            _status.Text = $"Moved {r.Moved} item{(r.Moved == 1 ? "" : "s")} here ({r.Shortcuts} app shortcut{(r.Shortcuts == 1 ? "" : "s")}, {r.Folders} folder{(r.Folders == 1 ? "" : "s")}, {r.Files} file{(r.Files == 1 ? "" : "s")})."
                           + (r.Duplicates > 0 ? $" {r.Duplicates} extra cop{(r.Duplicates == 1 ? "y was" : "ies were")} tucked away." : "")
                           + (r.Updated > 0 ? $" {r.Updated} start-up or pinned entr{(r.Updated == 1 ? "y was" : "ies were")} updated to the new location." : "")
                           + (r.Failed > 0 ? $" {r.Failed} could not be moved (in use, or no permission), so they stayed on the desktop." : "");
            Reload();
        }

        private void MoveAllBack()
        {
            var r = DesktopItemsService.MoveAllBack();
            int total = r.Moved + r.Merged;
            _status.Text = total == 0 && r.Failed == 0
                ? "Nothing to move back."
                : $"Moved {total} item{(total == 1 ? "" : "s")} back to the desktop."
                  + (r.Merged > 0 ? $" {r.Merged} {(r.Merged == 1 ? "was" : "were")} already there, so no copy was made." : "")
                  + (r.Updated > 0 ? $" {r.Updated} start-up or pinned entr{(r.Updated == 1 ? "y was" : "ies were")} updated to the desktop location." : "")
                  + (r.Failed > 0 ? $" {r.Failed} could not be moved." : "");
            Reload();
        }

        // ---------- tiles ----------

        public void Reload()
        {
            _tiles.Children.Clear();
            var items = InSavedOrder(DesktopItemsService.List());
            foreach (var item in items) _tiles.Children.Add(MakeTile(item));
            FitTiles();
            _empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _count.Text = items.Count == 1 ? "1 item" : $"{items.Count} items";
            _moveBackButton.Visibility = DesktopItemsService.MovedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The tiles in the order the user dragged them into. Anything not placed yet follows, in the usual order.</summary>
        private static List<DesktopItem> InSavedOrder(List<DesktopItem> items)
        {
            var saved = AppSettings.Current.AppsOrder;
            if (saved.Count == 0) return items;
            var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < saved.Count; i++) rank.TryAdd(saved[i], i);
            return items.OrderBy(it => rank.TryGetValue(it.Path, out var r) ? r : int.MaxValue).ToList();
        }

        private const string TileFormat = "pco-apptile";
        private Point _dragFrom;

        private void SaveOrder()
        {
            AppSettings.Current.AppsOrder = _tiles.Children.OfType<FrameworkElement>().Select(t => t.Tag as string).Where(p => p != null).Select(p => p!).ToList();
            AppSettings.Save();
        }

        private FrameworkElement MakeTile(DesktopItem item)
        {
            // A shortcut shows its program's icon rather than a shortcut arrow.
            var icon = new Image
            {
                Source = item.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? DockService.ShortcutIcon(item.Path) : DockService.IconFor(item.Path),
                Width = 42, Height = 42, Stretch = Stretch.Uniform,
            };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

            var name = new TextBlock
            {
                Text = item.Name, FontSize = 11.5, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 32, Margin = new Thickness(0, 7, 0, 0),
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(icon);
            stack.Children.Add(name);

            var tile = new Border
            {
                Child = stack, Width = 96, Height = 98, Padding = new Thickness(5, 9, 5, 5), Margin = new Thickness(2),
                CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = item.Path,
                Tag = item.Path, AllowDrop = true,
            };
            // Drag a tile onto another one to put it there.
            tile.PreviewMouseLeftButtonDown += (_, e) => _dragFrom = e.GetPosition(this);
            tile.PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed) return;
                var now = e.GetPosition(this);
                if (Math.Abs(now.X - _dragFrom.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(now.Y - _dragFrom.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                tile.Opacity = 0.5;
                try { DragDrop.DoDragDrop(tile, new DataObject(TileFormat, item.Path), DragDropEffects.Move); }
                finally { tile.Opacity = 1; tile.Background = Brushes.Transparent; }
            };
            tile.DragOver += (_, e) =>
            {
                bool ok = e.Data.GetDataPresent(TileFormat);
                e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
                if (ok) tile.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
                e.Handled = true;
            };
            tile.DragLeave += (_, _) => tile.Background = Brushes.Transparent;
            tile.Drop += (_, e) =>
            {
                tile.Background = Brushes.Transparent;
                if (!e.Data.GetDataPresent(TileFormat)) return;
                var from = _tiles.Children.OfType<FrameworkElement>().FirstOrDefault(t => (t.Tag as string) == (string)e.Data.GetData(TileFormat)!);
                if (from == null || ReferenceEquals(from, tile)) return;
                int to = _tiles.Children.IndexOf(tile);
                _tiles.Children.Remove(from);
                _tiles.Children.Insert(Math.Min(to, _tiles.Children.Count), from);
                SaveOrder();
                e.Handled = true;
            };
            tile.MouseEnter += (_, _) => tile.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
            tile.MouseLeftButtonDown += (_, e) => e.Handled = true;         // clicking a tile must not drag the panel
            tile.MouseLeftButtonUp += (_, _) => OpenItem(item);

            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Open" };
            open.Click += (_, _) => OpenItem(item);
            var show = new MenuItem { Header = "Show in folder" };
            show.Click += (_, _) => DockService.ShowInFolder(item.Path);
            menu.Items.Add(open);
            menu.Items.Add(show);
            if (item.Moved)
            {
                var back = new MenuItem { Header = "Move back to desktop" };
                back.Click += (_, _) =>
                {
                    _status.Text = DesktopItemsService.MoveBack(item.Path) ? $"{item.Name} is back on the desktop." : "That could not be moved.";
                    Reload();
                };
                menu.Items.Add(back);
            }
            tile.ContextMenu = menu;
            return tile;
        }

        private void OpenItem(DesktopItem item)
        {
            DockService.Launch(item.Path);
            if (!Pinned) Close();
        }
    }

    /// <summary>Owns the one Apps window, whether it is open as a menu or pinned to the desktop.</summary>
    public static class AppsPanelService
    {
        private static AppsWindow? _window;

        /// <summary>The dock's Apps button: opens the menu, closes it, or raises the pinned panel.</summary>
        public static void Toggle(Window? dock)
        {
            if (_window != null)
            {
                if (_window.Pinned) _window.BringToFront();
                else _window.Close();
                return;
            }
            Open(pinned: false, dock);
        }

        /// <summary>Moves a pinned panel to the position saved for it (used by "Restore Widgets Placing").</summary>
        public static void ApplySavedPosition()
        {
            if (_window is { Pinned: true } w && AppSettings.Current.Positions.TryGetValue(AppsWindow.PositionKey, out var p) && p.Length == 2)
            {
                w.Left = p[0];
                w.Top = p[1];
            }
        }

        /// <summary>At launch: brings the pinned panel back where it was.</summary>
        public static void RestorePinned()
        {
            if (AppSettings.Current.AppsPinned && _window == null) Open(pinned: true, null);
        }

        public static void StopAll()
        {
            var w = _window;
            _window = null;
            if (w != null) { try { w.Close(); } catch { } }
        }

        internal static void WindowClosed(AppsWindow w)
        {
            if (_window == w) _window = null;
        }

        private static void Open(bool pinned, Window? dock)
        {
            var window = new AppsWindow(pinned);
            _window = window;

            var area = SystemParameters.WorkArea;
            if (pinned)
            {
                var saved = AppSettings.Current.Positions.TryGetValue(AppsWindow.PositionKey, out var p) && p.Length == 2 ? p : null;
                window.Left = saved?[0] ?? area.Left + 40;
                window.Top = saved?[1] ?? area.Top + 70;
            }
            else if (dock != null && dock.IsLoaded)
            {
                // Just beside the dock, on the side facing the screen's middle.
                bool dockOnTop = AppSettings.Current.DockPosition == "Top";
                window.Left = Math.Clamp(dock.Left + 8, 0, Math.Max(0, SystemParameters.PrimaryScreenWidth - window.Width));
                window.Top = dockOnTop ? dock.Top + dock.ActualHeight - 24 : dock.Top + 24 - window.Height;
            }
            else
            {
                window.Left = area.Left + 40;
                window.Top = area.Top + 70;
            }

            window.Show();
            if (!pinned) window.Activate();
        }
    }
}
