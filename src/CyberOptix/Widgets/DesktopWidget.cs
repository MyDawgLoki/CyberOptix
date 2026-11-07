using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>Borderless, draggable, taskbar-less window that sits on the desktop (or on top, if configured).</summary>
    public abstract class DesktopWidget : Window
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr h, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private readonly string _key;

        protected readonly Border Card = new()
        {
            CornerRadius = new CornerRadius(16),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20),
        };

        protected DesktopWidget(string key, double defaultTop, double defaultRightOffset = 340)
        {
            _key = key;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            SetResourceReference(ForegroundProperty, "TextBrush");
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            WindowStartupLocation = WindowStartupLocation.Manual;
            // The card floats in a little free space, so a glow can fall round it.
            Content = new Grid { Margin = new Thickness(20), Children = { Card } };

            var saved = AppSettings.Current.Positions.TryGetValue(key, out var p) && p.Length == 2 ? p : null;
            Left = saved?[0] ?? SystemParameters.WorkArea.Right - defaultRightOffset;
            Top = saved?[1] ?? defaultTop;

            Card.SetResourceReference(Border.BackgroundProperty, "PopupBrush"); // follows the app's tint live
            ApplyLook();

            SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80); // WS_EX_TOOLWINDOW: hide from Alt+Tab
                SendBottomIfNeeded();
            };
            // A widget can change size (a longer title, weather arriving), and one dragged or placed near an edge can end up half off the screen.
            // Either way it is pulled back so the whole card stays on the screen it is on.
            SizeChanged += (_, _) => KeepOnScreen();
            Loaded += (_, _) => KeepOnScreen();
            DpiChanged += (_, _) => KeepOnScreen();
            Activated += (_, _) => SendBottomIfNeeded();
            ZOrderSettle.Run(this, SendBottomIfNeeded);   // a new window starts on top: put it behind the others once it is shown
            MouseLeftButtonDown += (_, _) =>
            {
                if (WidgetLock.IsLocked) return;    // clamped in place until "Unlock Widgets"
                try { DragMove(); } catch { }
                KeepOnScreen();
                AppSettings.Current.Positions[_key] = new[] { Left, Top };
                AppSettings.Save();
            };
            WidgetLock.Changed += LockChanged;
            Closed += (_, _) => WidgetLock.Changed -= LockChanged;
            LockChanged();
            Loaded += (_, _) => AddLockButton();
            WindowGlow.Attach(this, c => { _glowColor = c; ApplyGlow(); });
            Closing += (_, _) =>
            {
                AppSettings.Current.Positions[_key] = new[] { Left, Top };
                AppSettings.Save();
            };
        }

        private Color? _glowColor;

        /// <summary>The glowing edge (App Settings): the card's border and a soft glow round it, or its usual faint edge when the glow is off.</summary>
        private void ApplyGlow()
        {
            if (_glowColor is Color c)
                Card.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = c, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.9 };
            else Card.Effect = null;
            LockChanged();
        }

        /// <summary>While the widgets are unlocked each one shows an accent outline and a move cursor; locked, it looks as it always did.</summary>
        private void LockChanged()
        {
            if (WidgetLock.IsLocked)
            {
                Card.BorderBrush = _glowColor is Color g ? new SolidColorBrush(g) : new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
                Card.BorderThickness = new Thickness(1);
                Card.Cursor = null;
                Card.ToolTip = null;
            }
            else
            {
                Card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                Card.BorderThickness = new Thickness(2);
                Card.Cursor = System.Windows.Input.Cursors.SizeAll;
                Card.ToolTip = "Drag to move. When you are done, right-click the desktop and choose Lock Widgets.";
            }
        }

        private bool _hasClose;

        /// <summary>Puts the padlock over whatever the widget showed, once it has been built (once only).</summary>
        private void AddLockButton()
        {
            if (Card.Child == null || Card.Child is Grid { Tag: "lockhost" }) return;
            var content = Card.Child;
            Card.Child = null;
            var host = new Grid { Tag = "lockhost" };
            host.Children.Add(content);
            host.Children.Add(LockButton(_hasClose));
            Card.Child = host;
        }

        /// <summary>
        /// A padlock in the corner of every desktop widget, beside the close button when it has one. Clicking it locks the widgets in place (the same lock as "Lock Widgets" on the desktop menu) and the icon
        /// turns into an open padlock; clicking that unlocks them so they can be dragged again.
        /// </summary>
        private UIElement LockButton(bool hasClose)
        {
            var icon = new TextBlock
            {
                FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 11, Opacity = 0.55, Padding = new Thickness(8),
                Margin = new Thickness(0, -14, hasClose ? 12 : -14, 0), Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            };
            void Sync()
            {
                icon.Text = WidgetLock.IsLocked ? "" : "";   // padlock / open padlock
                icon.ToolTip = WidgetLock.IsLocked ? "Locked in place. Click to unlock the widgets so they can be moved." : "Unlocked. Click to lock the widgets in place.";
            }
            Sync();
            icon.MouseEnter += (_, _) => icon.Opacity = 1;
            icon.MouseLeave += (_, _) => icon.Opacity = 0.55;
            icon.MouseLeftButtonDown += (_, e) => { e.Handled = true; WidgetLock.Toggle(); };
            Action changed = () => icon.Dispatcher.BeginInvoke(new Action(Sync));
            WidgetLock.Changed += changed;
            Closed += (_, _) => WidgetLock.Changed -= changed;
            return icon;
        }

        /// <summary>Wraps a widget body with a small close (x) button in its corner.</summary>
        protected UIElement WithClose(UIElement content)
        {
            _hasClose = true;
            var grid = new Grid();
            grid.Children.Add(content);
            var close = new TextBlock
            {
                Text = "",
                FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
                FontSize = 10,
                Opacity = 0.55,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Close widget",
                Padding = new Thickness(8),
                Margin = new Thickness(0, -14, -14, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
            };
            close.MouseEnter += (_, _) => close.Opacity = 1;
            close.MouseLeave += (_, _) => close.Opacity = 0.55;
            close.MouseLeftButtonDown += (_, e) => { e.Handled = true; WidgetManager.Remove(_key); };
            grid.Children.Add(close);
            return grid;
        }

        public void MoveTo(double x, double y)
        {
            Left = x;
            Top = y;
            KeepOnScreen();
            AppSettings.Current.Positions[_key] = new[] { Left, Top };
        }

        private const double GlowRoom = 20;   // the free space round the card (see the constructor)

        /// <summary>Moves the widget so its whole card is inside the usable area of the screen it is mostly on.</summary>
        public void KeepOnScreen()
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero || ActualWidth < 1 || ActualHeight < 1) return;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            var monitors = DockService.Monitors();
            var mon = monitors.FirstOrDefault(m => m.Handle == DockService.MonitorOf(handle));
            if (mon == null) return;

            // the card (not the empty glow space round it), in device-independent units
            double minX = mon.WorkLeft / dpi.DpiScaleX - GlowRoom, maxX = mon.WorkRight / dpi.DpiScaleX - ActualWidth + GlowRoom;
            double minY = mon.WorkTop / dpi.DpiScaleY - GlowRoom, maxY = mon.WorkBottom / dpi.DpiScaleY - ActualHeight + GlowRoom;
            double x = Math.Clamp(Left, minX, Math.Max(minX, maxX)), y = Math.Clamp(Top, minY, Math.Max(minY, maxY));
            if (Math.Abs(x - Left) < 0.5 && Math.Abs(y - Top) < 0.5) return;
            Left = x;
            Top = y;
            AppSettings.Current.Positions[_key] = new[] { Left, Top };
        }

        public void ApplyLook()
        {
            Opacity = AppSettings.Current.WidgetOpacity;
            Topmost = AppSettings.Current.WidgetsOnTop;
            SendBottomIfNeeded();
        }

        private void SendBottomIfNeeded()
        {
            if (Topmost) return;
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero)
                SetWindowPos(h, (IntPtr)1, 0, 0, 0, 0, 0x13); // HWND_BOTTOM, NOSIZE|NOMOVE|NOACTIVATE
        }

        protected static TextBlock Text(double size, string text = "", double opacity = 1, FontWeight? weight = null) => new()
        {
            FontSize = size,
            Text = text,
            Opacity = opacity,
            FontWeight = weight ?? FontWeights.Normal,
        };
    }
}
