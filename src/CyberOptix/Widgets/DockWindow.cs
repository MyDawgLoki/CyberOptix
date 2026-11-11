using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>The floating dock: pinned apps, then other running apps. It floats above the desktop and does not replace the taskbar.</summary>
    public class DockWindow : Window
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool RegisterShellHookWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool DeregisterShellHookWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);

        private DockMonitor? _monitor;        // set on the extra docks (one per other display); null on the main dock
        private uint _shellMessage;
        private bool IsSecondary => _monitor != null;

        private sealed class DockApp
        {
            public string Path = "";
            public bool Pinned;
            public List<IntPtr> Windows = new();
            public int Badge;
            public bool Flashing;
            public bool RestoreMain;   // the CyberOptix window is minimized into the dock; clicking this brings it back
        }

        // The dock's window is bigger than the dock itself, so the drop shadow has room to fade out. These keep the visible dock close to
        // the edge of the screen and to the windows below it.
        private const double ShadowRoom = 16;   // transparent margin around the dock, all the way round
        private const double EdgeGap = 0;       // visible space between the dock and the screen edge (or the Windows taskbar): none, it sits flush against the edge it is on
        private const double WindowGap = 4;     // visible space between the dock and a maximized window, when the dock reserves its space

        /// <summary>How far the dock's window sits past the screen edge so that only <see cref="EdgeGap"/> of space shows.</summary>
        private const double EdgeOffset = ShadowRoom - EdgeGap;

        private readonly StackPanel _items = new() { Orientation = Orientation.Horizontal };

        // Behind the windows, and coming forward on hover or with the Start menu (the "Fall" and "Stick" layers of the DockLayer setting).
        private readonly DockLayerController _layer = new();
        private readonly DispatcherTimer _layerTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
        private string _appliedLayer = "Top";
        private DateTime _startSeen = DateTime.MinValue;

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1200) };
        private string _signature = "";
        private DockSystemStrip? _strip;
        /// <summary>The Recycle Bin's name in the saved icon order (DockOrder), next to the paths of the apps.</summary>
        private const string BinTag = "special:recyclebin";
        private Border? _start, _separator, _appsButton, _bin;
        private Image? _binIcon;
        private bool _binFull;
        private DateTime _binChecked = DateTime.MinValue;

        [DllImport("user32.dll")] private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr change);
        private bool _dragging;
        private Point _pressPoint;
        private string? _pressedPath;
        private Border? _dropMarker;

        public IntPtr Handle => new WindowInteropHelper(this).Handle;

        [StructLayout(LayoutKind.Sequential)] private struct WinRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out WinRect r);

        /// <summary>Where the dock itself is on screen, in pixels (not the transparent room round it), or null before it has a window.</summary>
        public (int X, int Y, int Width, int Height)? VisibleRectPx()
        {
            var h = Handle;
            if (h == IntPtr.Zero || ActualWidth < 1 || !GetWindowRect(h, out var r)) return null;
            int room = (int)Math.Round(ShadowRoom * VisualTreeHelper.GetDpi(this).DpiScaleX);
            return (r.Left + room, r.Top + room, r.Right - r.Left - 2 * room, r.Bottom - r.Top - 2 * room);
        }

        public DockWindow(DockMonitor? monitor = null)
        {
            _monitor = monitor;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SetResourceReference(ForegroundProperty, "TextBrush");
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

            var glass = new Border
            {
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(ShadowRoom),
                BorderThickness = new Thickness(1),
                Child = BuildRoot(),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.5, Color = Colors.Black },
            };
            glass.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
            glass.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
            _glass = glass;
            _glow = new Border { CornerRadius = new CornerRadius(20), Margin = new Thickness(ShadowRoom), IsHitTestVisible = false, Visibility = Visibility.Collapsed, Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 14 } };
            var layers = new Grid();
            layers.Children.Add(_glow);
            layers.Children.Add(glass);
            Content = layers;

            SourceInitialized += (_, _) =>
            {
                var h = Handle;
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80); // WS_EX_TOOLWINDOW: not in Alt+Tab

                // CyberOptix runs as administrator, and Windows blocks drag and drop from the (ordinary) File Explorer into an elevated window unless
                // these messages are allowed through. That is what lets files be dropped on the Recycle Bin.
                foreach (uint message in new uint[] { 0x233 /* WM_DROPFILES */, 0x4A /* WM_COPYDATA */, 0x49 /* WM_COPYGLOBALDATA */ })
                    ChangeWindowMessageFilterEx(h, message, 1 /* MSGFLT_ALLOW */, IntPtr.Zero);

                if (!IsSecondary)
                {
                    // The shell tells this window when another window flashes for attention; the extra docks share what it hears.
                    _shellMessage = RegisterWindowMessage("SHELLHOOK");
                    HwndSource.FromHwnd(h)?.AddHook(ShellHook);
                    RegisterShellHookWindow(h);
                }
            };
            SizeChanged += (_, _) => Reposition();
            // the icon docks beside this one follow it whenever it grows, shrinks or moves (apps opening, the clock changing width...)
            SizeChanged += (_, _) => Dispatcher.BeginInvoke(new Action(IconGroupDockService.PlaceAll), System.Windows.Threading.DispatcherPriority.Background);
            LocationChanged += (_, _) => Dispatcher.BeginInvoke(new Action(IconGroupDockService.PlaceAll), System.Windows.Threading.DispatcherPriority.Background);
            DpiChanged += (_, _) => Reposition();   // moving between displays of different scale re-lays the dock out
            _timer.Tick += (_, _) => Tick();
            Loaded += (_, _) =>
            {
                _timer.Start();
                ApplyLayerMode();
                _layerTimer.Start();
            };
            _layerTimer.Tick += (_, _) => LayerTick();
            _items.AllowDrop = true;
            _items.Background = Brushes.Transparent;
            _items.DragOver += ItemsDragOver;
            _items.DragLeave += (_, _) => ClearDropMarker();
            _items.Drop += ItemsDrop;
            Closing += (_, _) => { if (!IsSecondary) TaskbarTakeoverService.Unreserve(Handle); };
            Closed += (_, _) =>
            {
                _timer.Stop();
                _layerTimer.Stop();
                if (!IsSecondary)
                {
                    try { DeregisterShellHookWindow(Handle); } catch { }
                    DockService.Flashing.Clear();
                }
            };
        }

        /// <summary>Hears about windows that start or stop flashing, or come to the front.</summary>
        private IntPtr ShellHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != 0 && (uint)msg == _shellMessage)
            {
                int code = wParam.ToInt32();
                var window = lParam;
                bool changed = false;
                if (code == 0x8006) changed = DockService.Flashing.Add(window);                                   // HSHELL_FLASH: started flashing
                else if (code == 0x0006 || code == 0x0004 || code == 0x8004) changed = DockService.Flashing.Remove(window);   // redrawn normally, or activated
                if (changed)
                {
                    DockService.RaiseFlashChanged();
                    Dispatcher.BeginInvoke(new Action(() => { Tick(); }));
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>The display this dock sits on changed (resolution, taskbar size, or a display was rearranged).</summary>
        public void UpdateMonitor(DockMonitor monitor)
        {
            _monitor = monitor;
            Reposition();
        }

        private StackPanel BuildRoot()
        {
            var root = new StackPanel { Orientation = Orientation.Horizontal };

            // The extra docks on other displays are just the app icons.
            if (IsSecondary)
            {
                root.Children.Add(_items);
                return root;
            }

            // A Windows logo that opens the Start menu (shown when the dock stands in for the taskbar).
            var logo = new Grid { Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            logo.RowDefinitions.Add(new RowDefinition()); logo.RowDefinitions.Add(new RowDefinition());
            logo.ColumnDefinitions.Add(new ColumnDefinition()); logo.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 2; c++)
                {
                    var square = new System.Windows.Shapes.Rectangle { Margin = new Thickness(1), RadiusX = 1, RadiusY = 1 };
                    square.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush");
                    Grid.SetRow(square, r); Grid.SetColumn(square, c);
                    logo.Children.Add(square);
                }
            _start = new Border
            {
                Child = logo, Width = 34, Height = 34, CornerRadius = new CornerRadius(11), Background = Brushes.Transparent,
                Cursor = Cursors.Hand, ToolTip = "Start", Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            _start.MouseEnter += (_, _) => _start.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            _start.MouseLeave += (_, _) => _start.Background = Brushes.Transparent;
            _start.MouseLeftButtonUp += (_, _) => TaskbarTakeoverService.OpenStart();
            root.Children.Add(_start);

            // Apps: the desktop in a window. It sits at the left end of the dock.
            var appsGlyph = new TextBlock
            {
                Text = "\uE71D", FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 19,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            appsGlyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            _appsButton = new Border
            {
                Child = appsGlyph, Width = 34, Height = 34, CornerRadius = new CornerRadius(11), Background = Brushes.Transparent,
                Cursor = Cursors.Hand, ToolTip = "Apps: your desktop apps, files and folders", Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            _appsButton.MouseEnter += (_, _) => _appsButton.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            _appsButton.MouseLeave += (_, _) => _appsButton.Background = Brushes.Transparent;
            _appsButton.MouseLeftButtonUp += (_, _) => AppsPanelService.Toggle(this);
            root.Children.Add(_appsButton);

            root.Children.Add(_items);

            BuildBin();   // the Recycle Bin is one of the dock's icons: Rebuild puts it among the apps where the user dragged it

            _separator = new Border { Width = 1, Margin = new Thickness(8, 10, 8, 10), VerticalAlignment = VerticalAlignment.Stretch };
            _separator.SetResourceReference(Border.BackgroundProperty, "DividerBrush");
            root.Children.Add(_separator);

            _strip = new DockSystemStrip(
                () => AppSettings.Current.DockPosition == "Top",
                () =>
                {
                    double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                    bool top = AppSettings.Current.DockPosition == "Top";
                    return ((int)((Left + ActualWidth - 140) * scale), (int)((top ? Top + ActualHeight - 16 : Top + 16) * scale));
                });
            root.Children.Add(_strip);
            return root;
        }

        // ---------- the Recycle Bin ----------

        /// <summary>The Recycle Bin on the dock: its own icon (empty or full), click to open, right-click to empty, and files dropped on it go in like normal.</summary>
        private Border BuildBin()
        {
            _binIcon = new Image { Width = 22, Height = 22, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(_binIcon, BitmapScalingMode.HighQuality);
            _bin = new Border
            {
                Child = _binIcon, Width = 34, Height = 34, CornerRadius = new CornerRadius(11), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                ToolTip = "Recycle Bin", Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, AllowDrop = true,
                Visibility = Visibility.Collapsed,
            };
            // It lifts when the pointer is over it, the same as the app icons do.
            var lift = new ScaleTransform(1, 1);
            _bin.RenderTransform = lift;
            _bin.RenderTransformOrigin = new Point(0.5, 1);
            _bin.MouseEnter += (_, _) => Animate(lift, 1.18, _bin, true, Brushes.Transparent);
            _bin.MouseLeave += (_, _) => Animate(lift, 1.0, _bin, false, Brushes.Transparent);
            _bin.Tag = BinTag;
            // Click opens it; pressing and dragging moves it among the apps (the same drag the app icons use).
            _bin.MouseLeftButtonDown += (_, e) => { _pressedPath = BinTag; _pressPoint = e.GetPosition(this); };
            _bin.MouseLeftButtonUp += (_, _) =>
            {
                if (_pressedPath == BinTag) RecycleBinService.Open();
                _pressedPath = null;
            };
            _bin.MouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || _pressedPath != BinTag || _dragging) return;
                var now = e.GetPosition(this);
                if (Math.Abs(now.X - _pressPoint.X) < 8 && Math.Abs(now.Y - _pressPoint.Y) < 8) return;

                _pressedPath = null;
                _dragging = true;
                _bin!.Opacity = 0.45;
                try { DragDrop.DoDragDrop(_bin, new DataObject("cyberoptix-dock-app", BinTag), DragDropEffects.Move); }
                finally
                {
                    _bin.Opacity = 1;
                    _bin.Background = Brushes.Transparent;
                    ClearDropMarker();
                    _dragging = false;
                    Reload();
                }
            };

            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Open" };
            open.Click += (_, _) => RecycleBinService.Open();
            var empty = new MenuItem { Header = "Empty Recycle Bin" };
            empty.Click += (_, _) => { RecycleBinService.Empty(Handle); UpdateBin(true); };
            menu.Items.Add(open);
            menu.Items.Add(empty);
            _bin.ContextMenu = menu;

            // Dropping files, folders or shortcuts on it sends them to the Recycle Bin, the same as pressing Delete in File Explorer.
            DragEventHandler over = (_, e) =>
            {
                if (e.Data.GetDataPresent("cyberoptix-dock-app")) return;   // an icon being rearranged: the strip handles it

                bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
                e.Effects = files ? DragDropEffects.Move : DragDropEffects.None;
                if (files) _bin!.Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
                e.Handled = true;
            };
            _bin.DragEnter += over;
            _bin.DragOver += over;
            _bin.DragLeave += (_, _) => _bin!.Background = Brushes.Transparent;
            _bin.Drop += (_, e) =>
            {
                _bin!.Background = Brushes.Transparent;
                if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
                e.Handled = true;
                var owner = Handle;
                System.Threading.Tasks.Task.Run(() => RecycleBinService.Recycle(files, owner))
                    .ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() => UpdateBin(true))));
            };
            return _bin;
        }

        /// <summary>Shows or hides the bin (main dock only) and keeps its icon and tooltip right: empty or full, and how many items.</summary>
        private void UpdateBin(bool now)
        {
            if (_bin == null || _binIcon == null) return;
            bool show = !IsSecondary && AppSettings.Current.DockShowRecycleBin;
            _bin.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            if (!now && _binIcon.Source != null && DateTime.UtcNow - _binChecked < TimeSpan.FromSeconds(3)) return;   // it only needs checking now and then
            _binChecked = DateTime.UtcNow;

            long items = RecycleBinService.Count();
            bool full = items > 0;
            if (full != _binFull || _binIcon.Source == null)
            {
                _binFull = full;
                _binIcon.Source = RecycleBinService.Icon(full);
            }
            _bin.ToolTip = items == 0 ? "Recycle Bin (empty). Drop files here to delete them."
                                      : $"Recycle Bin ({items} item{(items == 1 ? "" : "s")}). Drop files here to delete them.";
        }

        public void Reload()
        {
            _signature = "";
            Tick();
        }

        private void Reposition()
        {
            var s = AppSettings.Current;
            bool top = s.DockPosition == "Top";

            if (IsSecondary)
            {
                if (Handle == IntPtr.Zero || _monitor == null) return;
                double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                int w = (int)Math.Round(ActualWidth * dpi), h = (int)Math.Round(ActualHeight * dpi);
                int trim = (int)Math.Round(EdgeOffset * dpi);
                int x = _monitor.WorkLeft + ((_monitor.WorkRight - _monitor.WorkLeft) - w) / 2;
                int y = top ? _monitor.WorkTop - trim : _monitor.WorkBottom - h + trim;
                SetWindowPos(Handle, LayerInsertAfter(), x, y, 0, 0, 0x0001 | 0x0010 /* no resize, no activate */);
                return;
            }

            // Only the "Top" layer keeps windows out from under the dock. When the dock lies behind the windows they use the whole screen.
            if (s.DockReplaceTaskbar && Layer == "Top" && !s.DockFloatOverWindows)
            {
                // Reserve the strip the dock covers, so maximized windows stop at the dock instead of going behind it.
                double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                int screenW = (int)Math.Round(SystemParameters.PrimaryScreenWidth * scale);
                int screenH = (int)Math.Round(SystemParameters.PrimaryScreenHeight * scale);
                // The strip kept free is the dock itself plus the small gaps: to the screen edge on one side and to the windows on the other.
                double dockHeight = ActualHeight - 2 * ShadowRoom;
                int heightPx = (int)Math.Round((dockHeight + EdgeGap + WindowGap) * scale);
                var rc = TaskbarTakeoverService.Reserve(Handle, top, heightPx, screenW, screenH);
                Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
                // Top: the dock is EdgeGap below the strip's top. Bottom: it is WindowGap below the strip's top (the strip's far edge is the screen's).
                Top = top ? rc.Top / scale - EdgeOffset : rc.Top / scale + WindowGap - ShadowRoom;
                return;
            }

            TaskbarTakeoverService.Unreserve(Handle);
            // The primary display, minus the Windows taskbar. With the dock standing in for the taskbar the whole screen is free.
            var area = s.DockReplaceTaskbar ? new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight) : SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = top ? area.Top - EdgeOffset : area.Bottom - ActualHeight + EdgeOffset;
        }

        // ---------- in front of, or behind, the windows ----------

        /// <summary>"Top" (always in front, the default), "Fall" (behind windows, comes forward on hover) or "Stick" (behind windows, comes forward with the Start menu).</summary>
        private static string Layer => AppSettings.Current.DockLayer is "Fall" or "Stick" ? AppSettings.Current.DockLayer : "Top";

        private IntPtr LayerInsertAfter() => Layer == "Top" || _layer.InFront ? new IntPtr(-1) /* HWND_TOPMOST */ : new IntPtr(1) /* HWND_BOTTOM */;

        private void BringForward() => SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // topmost; no move, no resize, no focus
        private void SendBehind() => SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);      // bottom of the stack

        /// <summary>Puts the dock in front of or behind the windows when the setting changed.</summary>
        private void ApplyLayerMode()
        {
            var layer = Layer;
            if (layer == _appliedLayer) return;
            _appliedLayer = layer;
            _layer.Reset();
            Topmost = layer == "Top";
            if (Handle != IntPtr.Zero)
            {
                if (layer == "Top") BringForward();
                else SendBehind();
            }
            Reposition();   // the dock reserves space only in front of the windows
        }

        /// <summary>
        /// Runs many times a second. Brings the dock forward when it should be (the pointer over it in "Fall", the Start menu open),
        /// sends it back when that is over, and moves the Start menu down to the bottom edge of the screen.
        /// </summary>
        private void LayerTick()
        {
            var s = AppSettings.Current;
            string layer = Layer;
            bool clampStart = !IsSecondary && (s.DockStartAtBottom || s.DockSearchAtBottom) && (s.DockReplaceTaskbar || layer != "Top");
            if (layer == "Top" && !clampStart) return;   // nothing to watch
            if (Handle == IntPtr.Zero) return;

            var now = DateTime.UtcNow;
            if (!IsSecondary && s.DockReplaceTaskbar) TaskbarTakeoverService.KeepWorkArea();
            IntPtr startWindow = IntPtr.Zero;
            bool startOpen = !IsSecondary && StartMenuService.IsOpen(out startWindow);

            if (startOpen)
            {
                if (_startSeen == DateTime.MinValue) _startSeen = now;
                // The menu slides in, so keep nudging it for a moment after it opens.
                if (clampStart && (StartMenuService.LastWasSearch ? s.DockSearchAtBottom : s.DockStartAtBottom)) StartMenuService.ClampToBottom(startWindow);
            }
            else
            {
                _startSeen = DateTime.MinValue;
            }

            if (layer == "Top" || Visibility != Visibility.Visible) return;

            bool over = layer == "Fall" && PointerOverDock(_layer.InFront);
            bool busy = _dragging || (_strip?.FlyoutOpen ?? false);
            switch (_layer.Update(layer, startOpen, over, ScreenProbe.ButtonDown(), busy, now))
            {
                case DockLayerController.Move.Raise: BringForward(); break;
                case DockLayerController.Move.Lower: SendBehind(); break;
            }
        }

        private bool PointerOverDock(bool alreadyOut)
        {
            if (!ScreenProbe.Pointer(out var px, out var py)) return false;
            if (!ScreenProbe.WindowRect(Handle, out var l, out var t, out var r, out var b)) return false;
            if (!ScreenProbe.MonitorRect(Handle, out _, out var screenTop, out _, out var screenBottom)) return false;
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int room = (int)Math.Round(ShadowRoom * dpi);
            int pad = alreadyOut ? (int)Math.Round(14 * dpi) : 0;
            return DockLayerController.PointerOverDock(px, py, l, t, r, b, room, pad, AppSettings.Current.DockPosition == "Top", screenTop, screenBottom);
        }

        private Border? _glass, _glow;
        private string _glowSignature = "";

        /// <summary>The soft glow around the dock's outside edge, in the chosen color. Off: the normal border and soft black shadow.</summary>
        private void ApplyGlow()
        {
            var s = AppSettings.Current;
            // "Match the Media Player Color" also colors the glow while something is playing.
            string hex = MediaColorService.ForDock is Color m ? $"#{m.R:X2}{m.G:X2}{m.B:X2}" : s.DockGlowColor;
            string sig = s.DockGlow ? "on" + hex : "off";
            if (_glass == null || sig == _glowSignature) return;
            _glowSignature = sig;

            if (s.DockGlow && ThemeService.TryParse(hex, out var color))
            {
                // A blurred layer of the glow color sits behind the dock and spills out round its edge (the window leaves 16 px free for it).
                var solid = Color.FromRgb(color.R, color.G, color.B);
                _glow!.Background = new SolidColorBrush(solid);
                _glow.Opacity = 0.9;
                _glow.Visibility = Visibility.Visible;
                _glass.BorderBrush = new SolidColorBrush(solid);
                _glass.Effect = null;
            }
            else
            {
                _glow!.Visibility = Visibility.Collapsed;
                _glass.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
                _glass.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.5, Color = Colors.Black };
            }
        }

        private void Tick()
        {
            var s = AppSettings.Current;
            ApplyLayerMode();
            ApplyGlow();
            UpdateBin(false);

            // System icons, the Start button, and the Windows taskbar when the dock replaces it (main dock only).
            if (!IsSecondary)
            {
                _strip!.Visibility = _separator!.Visibility = s.DockShowSystem ? Visibility.Visible : Visibility.Collapsed;
                _start!.Visibility = s.DockReplaceTaskbar ? Visibility.Visible : Visibility.Collapsed;
                if (s.DockShowSystem) _strip.Update();
                if (s.DockReplaceTaskbar) TaskbarTakeoverService.Hide();
                else TaskbarTakeoverService.RestoreTaskbar();
            }
            DockService.Flashing.RemoveWhere(h => !IsWindow(h) || DockService.IsForeground(h));

            // Step aside while a game or other fullscreen app has the screen.
            if (s.DockHideFullscreen)
            {
                DesktopHost.Query(Handle, out var covers, out var maximized);
                // With the dock behind the windows it is never hidden: it is already out of the way, and a hidden dock can neither be
                // brought forward by the pointer nor by the Start menu (any window that fills the screen, maximized or not, used to hide it for good).
                bool hide = covers && Layer == "Top";
                var want = hide ? Visibility.Hidden : Visibility.Visible;
                if (Visibility != want) Visibility = want;
                if (hide) return;
            }
            else if (Visibility != Visibility.Visible)
            {
                Visibility = Visibility.Visible;
            }

            if (_dragging) return;
            var apps = BuildApps(s);
            var signature = $"{s.DockIconSize}|{s.DockWidgetAuto}|{s.DockWidgetSize}|{s.DockClockAuto}|{s.DockClockSize}|{s.DockPosition}|{s.DockReplaceTaskbar}|{s.DockShowSystem}|{s.DockShowRecycleBin}|{string.Join(",", s.DockExtraIcons)}|{string.Join(",", s.IndividualDocks.Select(kv => kv.Key + kv.Value))}|" +
                            string.Join(";", apps.Select(a => $"{a.Path}:{a.Pinned}:{string.Join(",", a.Windows)}:{a.Badge}:{a.Flashing}:{a.RestoreMain}"));
            if (signature == _signature)
            {
                // Same apps; just keep the window handles current so clicks reach the right window.
                return;
            }
            _signature = signature;
            Rebuild(apps, s.DockIconSize);
        }

        private List<DockApp> BuildApps(AppSettings s)
        {
            var apps = s.DockPinned
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => new DockApp { Path = p, Pinned = true })
                .ToList();

            if (!s.DockShowRunning && apps.Count == 0) return apps;

            var thisMonitor = _monitor?.Handle ?? DockService.PrimaryMonitor();
            bool Wanted(IntPtr window)
            {
                if (!s.DockAllDisplays || s.DockAppsOn == "All") return true;
                bool here = DockService.MonitorOf(window) == thisMonitor;
                return s.DockAppsOn == "Window" ? here : (IsSecondary ? here : true);   // "MainAndWindow": the main dock lists everything
            }

            foreach (var w in DockService.RunningWindows(Handle))
            {
                if (!Wanted(w.Handle)) continue;
                var app = apps.FirstOrDefault(a => string.Equals(a.Path, w.Path, StringComparison.OrdinalIgnoreCase));
                if (app == null)
                {
                    if (!s.DockShowRunning) continue;
                    app = new DockApp { Path = w.Path };
                    apps.Add(app);
                }
                app.Windows.Add(w.Handle);
            }

            if (DockService.MainHiddenInDock && !IsSecondary)
            {
                var me = Environment.ProcessPath ?? "";
                var self = apps.FirstOrDefault(a => string.Equals(a.Path, me, StringComparison.OrdinalIgnoreCase));
                if (self == null) { self = new DockApp { Path = me }; apps.Add(self); }
                self.RestoreMain = true;
            }

            foreach (var app in apps)
            {
                // Opera puts the unread count of its sidebar chats in a tab title, which is not worth a badge
                if (s.DockShowBadges && !(app.Path ?? "").Contains("opera", StringComparison.OrdinalIgnoreCase)) app.Badge = app.Windows.Sum(DockService.BadgeFor);
                if (s.DockShowFlashing) app.Flashing = app.Windows.Any(DockService.Flashing.Contains);
            }

            // The order the user dragged them into. Anything new goes at the end.
            var order = s.DockOrder;
            if (order.Count > 0)
            {
                int Rank(DockApp a)
                {
                    int i = order.FindIndex(p => string.Equals(p, a.Path, StringComparison.OrdinalIgnoreCase));
                    return i < 0 ? int.MaxValue : i;
                }
                apps = apps.OrderBy(Rank).ToList(); // a stable sort: apps that are not listed keep their usual order
            }
            return apps;
        }

        private void Rebuild(List<DockApp> apps, int size)
        {
            var settings = AppSettings.Current;
            _strip?.SetExtras(settings.DockExtraIcons.Where(id => !DockWidgetCatalog.IsInOwnDock(id)));   // the extra system icons picked on the Dock page
            // Either the same proportion to the app icons as before, or the size picked on the Dock page.
            // The clock and date do the same, or use their own size when they are not following the icons.
            _strip?.ApplySize(settings.DockWidgetAuto ? AppSettings.AutoWidgetPx(size) : settings.DockWidgetSize,
                              settings.DockClockAuto ? null : settings.DockClockSize);
            _items.Children.Clear();
            var order = settings.DockOrder;
            int binRank = order.FindIndex(p => p == BinTag);
            bool binHere = _bin != null && !IsSecondary && settings.DockShowRecycleBin;
            bool binPlaced = false;
            foreach (var app in apps)
            {
                if (binHere && !binPlaced && binRank >= 0)
                {
                    int r = order.FindIndex(p => string.Equals(p, app.Path, StringComparison.OrdinalIgnoreCase));
                    if (r < 0 || r > binRank) { _items.Children.Add(_bin!); binPlaced = true; }
                }
                _items.Children.Add(MakeItem(app, size));
            }
            if (binHere && !binPlaced) _items.Children.Add(_bin!);   // not moved yet (or last): at the end, as before
            if (apps.Count == 0)
            {
                _items.Children.Add(new TextBlock
                {
                    Text = "Pin apps from the Dock page", Margin = new Thickness(14, 12, 14, 12),
                    Foreground = (Brush)Application.Current.FindResource("TextDimBrush"),
                });
            }
            UpdateLayout();
            Reposition();
        }

        private FrameworkElement MakeItem(DockApp app, int size)
        {
            var image = new Image
            {
                Source = DockService.IconFor(app.Path),
                Width = size, Height = size,
                Stretch = Stretch.Uniform,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 4, Height = 4, Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = app.Windows.Count > 0 || app.RestoreMain ? Visibility.Visible : Visibility.Hidden,
            };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush");

            var flash = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xA8, 0x1F));
            if (app.Flashing) dot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xA8, 0x1F));

            var stack = new StackPanel();
            stack.Children.Add(image);
            stack.Children.Add(dot);

            // A number on the icon when the app's window title starts with one, like (3).
            var content = new Grid();
            content.Children.Add(stack);
            if (app.Badge > 0)
            {
                content.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)),
                    CornerRadius = new CornerRadius(9), MinWidth = 18, Height = 18, Padding = new Thickness(4, 0, 4, 0),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -3, -5, 0),
                    Child = new TextBlock
                    {
                        Text = app.Badge > 99 ? "99+" : app.Badge.ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                });
            }

            var scale = new ScaleTransform(1, 1);
            var item = new Border
            {
                Child = content,
                Padding = new Thickness(6, 3, 6, 2),
                CornerRadius = new CornerRadius(12),
                Background = app.Flashing ? flash : Brushes.Transparent,
                Cursor = Cursors.Hand,
                RenderTransform = scale,
                RenderTransformOrigin = new Point(0.5, 1),
                ToolTip = DockService.DisplayName(app.Path) + (app.Windows.Count > 1 ? $"  ({app.Windows.Count} windows)" : "")
                          + (app.Badge > 0 ? $"  ({app.Badge} new)" : "") + (app.Flashing ? "  - needs your attention" : ""),
            };

            // Lift the icon slightly on hover.
            item.MouseEnter += (_, _) => Animate(scale, 1.18, item, true, app.Flashing ? flash : Brushes.Transparent);
            item.MouseLeave += (_, _) => Animate(scale, 1.0, item, false, app.Flashing ? flash : Brushes.Transparent);
            item.Tag = app.Path;
            item.MouseLeftButtonDown += (_, e) => { _pressedPath = app.Path; _pressPoint = e.GetPosition(this); };
            item.MouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || _pressedPath != app.Path || _dragging) return;
                var now = e.GetPosition(this);
                if (Math.Abs(now.X - _pressPoint.X) < 8 && Math.Abs(now.Y - _pressPoint.Y) < 8) return;

                _pressedPath = null; // it is a drag now, not a click
                _dragging = true;
                item.Opacity = 0.45;
                try { DragDrop.DoDragDrop(item, new DataObject("cyberoptix-dock-app", app.Path), DragDropEffects.Move); }
                finally
                {
                    item.Opacity = 1;
                    ClearDropMarker();
                    _dragging = false;
                    Reload();
                }
            };
            item.MouseLeftButtonUp += (_, _) =>
            {
                if (_pressedPath == app.Path) Clicked(app); // pressed and released without dragging
                _pressedPath = null;
            };
            item.MouseRightButtonUp += (_, e) => { ShowMenu(app, item); e.Handled = true; };
            return item;
        }

        // ---------- rearranging by dragging ----------

        /// <summary>Which gap (0 to count) the cursor is nearest to along the icon strip.</summary>
        private int GapAt(Point p)
        {
            for (int i = 0; i < _items.Children.Count; i++)
            {
                if (_items.Children[i] is not FrameworkElement child) continue;
                var center = child.TranslatePoint(new Point(child.ActualWidth / 2, 0), _items).X;
                if (p.X < center) return i;
            }
            return _items.Children.Count;
        }

        private void ItemsDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("cyberoptix-dock-app")) { e.Effects = DragDropEffects.None; return; }
            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            // Light up the icon the dragged one would land in front of.
            ClearDropMarker();
            int gap = GapAt(e.GetPosition(_items));
            var target = gap < _items.Children.Count ? _items.Children[gap] as Border : _items.Children.Count > 0 ? _items.Children[^1] as Border : null;
            if (target != null)
            {
                _dropMarker = target;
                target.BorderThickness = new Thickness(gap < _items.Children.Count ? 2 : 0, 0, gap < _items.Children.Count ? 0 : 2, 0);
                target.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            }
        }

        private void ClearDropMarker()
        {
            if (_dropMarker == null) return;
            _dropMarker.BorderThickness = new Thickness(0);
            _dropMarker = null;
        }

        private void ItemsDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData("cyberoptix-dock-app") is not string dragged) return;
            int gap = GapAt(e.GetPosition(_items));
            ClearDropMarker();

            // Take the order as it is on screen, move the dragged icon to the gap, and remember the result.
            var order = _items.Children.OfType<Border>().Select(b => b.Tag as string).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
            int from = order.FindIndex(p => string.Equals(p, dragged, StringComparison.OrdinalIgnoreCase));
            if (from < 0) return;

            order.RemoveAt(from);
            if (gap > from) gap--;
            order.Insert(Math.Clamp(gap, 0, order.Count), dragged);

            AppSettings.Current.DockOrder = order;
            AppSettings.Save();
            e.Handled = true;
        }

        private static void Animate(ScaleTransform scale, double to, Border item, bool hover, Brush resting)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            item.Background = hover ? new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)) : resting;
        }

        private void Clicked(DockApp app)
        {
            if (app.RestoreMain)
            {
                (Application.Current.MainWindow as MainWindow)?.ShowFromTray();
                return;
            }
            if (app.Windows.Count == 0)
            {
                DockService.Launch(app.Path);
                return;
            }

            // Clicking again moves on to the app's next window.
            int current = app.Windows.FindIndex(DockService.IsForeground);
            DockService.Activate(app.Windows[(current + 1) % app.Windows.Count]);
        }

        private void ShowMenu(DockApp app, UIElement target)
        {
            var menu = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };

            var open = new MenuItem { Header = app.Windows.Count > 0 ? "Open new window" : "Open" };
            open.Click += (_, _) => DockService.Launch(app.Path);
            menu.Items.Add(open);

            var pin = new MenuItem { Header = app.Pinned ? "Unpin from dock" : "Pin to dock" };
            pin.Click += (_, _) => { if (app.Pinned) DockService.Unpin(app.Path); else DockService.Pin(app.Path); };
            menu.Items.Add(pin);

            if (app.Windows.Count > 0)
            {
                var close = new MenuItem { Header = app.Windows.Count > 1 ? "Close all windows" : "Close window" };
                close.Click += (_, _) => app.Windows.ForEach(DockService.CloseWindow);
                menu.Items.Add(close);
            }
            menu.IsOpen = true;
        }
    }
}
