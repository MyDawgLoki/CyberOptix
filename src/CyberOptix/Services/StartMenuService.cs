using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CyberOptix.Services
{
    /// <summary>
    /// The Windows Start menu and search. This only reads which one is open, and moves it down to the bottom edge of the screen.
    /// Windows leaves room under the menu for the taskbar even when the taskbar is hidden or replaced by the dock, which leaves a gap.
    /// </summary>
    public static class StartMenuService
    {
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int Size; public RECT Monitor; public RECT Work; public uint Flags; }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int max);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        /// <summary>The programs that draw the Start menu and search.</summary>
        private static readonly string[] Hosts = { "StartMenuExperienceHost.exe", "SearchHost.exe", "SearchApp.exe" };

        /// <summary>True when the front window is the Search menu (rather than the Start menu). Set by <see cref="IsOpen"/>.</summary>
        public static bool LastWasSearch { get; private set; }

        /// <summary>
        /// True while the Start menu or search is the window in front. <paramref name="window"/> is that window.
        /// Cheap enough to call many times a second: it only looks further when the front window is a Store-style app window.
        /// </summary>
        public static bool IsOpen(out IntPtr window)
        {
            window = IntPtr.Zero;
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;

            var cls = new StringBuilder(64);
            GetClassName(fg, cls, cls.Capacity);
            if (cls.ToString() != "Windows.UI.Core.CoreWindow" || !IsWindowVisible(fg)) return false;

            GetWindowThreadProcessId(fg, out var pid);
            var process = OpenProcess(0x1000 /* query limited information */, false, pid);
            if (process == IntPtr.Zero) return false;
            try
            {
                var sb = new StringBuilder(512);
                int size = sb.Capacity;
                if (!QueryFullProcessImageName(process, 0, sb, ref size)) return false;
                var path = sb.ToString();
                foreach (var host in Hosts)
                    if (path.EndsWith(@"\" + host, StringComparison.OrdinalIgnoreCase)) { window = fg; LastWasSearch = !host.StartsWith("StartMenu", StringComparison.OrdinalIgnoreCase); return true; }
                return false;
            }
            finally { CloseHandle(process); }
        }

        /// <summary>The widest gap under the menu that is treated as "the room Windows leaves for the taskbar" (a 200 % taskbar plus its margin is about 120 px).</summary>
        public const int MaxGap = 200;

        /// <summary>The gap (in pixels) to close: how far the menu's bottom edge is above the bottom of the screen, or 0 when there is nothing to fix.</summary>
        public static int GapToClose(int menuBottom, int screenBottom)
        {
            int gap = screenBottom - menuBottom;
            return gap >= 3 && gap <= MaxGap ? gap : 0;   // already at the bottom, below it, or too far to be the taskbar's room
        }

        /// <summary>The bottom edge (screen pixels) of what the menu actually shows, found through UI Automation, or null when it can't be read.</summary>
        private static int? VisiblePanelBottom(IntPtr window, RECT win)
        {
            try
            {
                var root = System.Windows.Automation.AutomationElement.FromHandle(window);
                var walker = System.Windows.Automation.TreeWalker.RawViewWalker;
                int winHeight = win.Bottom - win.Top, best = int.MinValue, seen = 0;
                var stack = new System.Collections.Generic.Stack<(System.Windows.Automation.AutomationElement El, int Depth)>();
                stack.Push((root, 0));
                while (stack.Count > 0 && seen < 80)
                {
                    var (el, depth) = stack.Pop();
                    for (var c = walker.GetFirstChild(el); c != null && seen < 80; c = walker.GetNextSibling(c))
                    {
                        seen++;
                        var b = c.Current.BoundingRectangle;
                        if (!c.Current.IsOffscreen && !b.IsEmpty && b.Width > 100 && b.Height > 40 && b.Height < winHeight - 4)
                        {
                            int bot = (int)Math.Round(b.Bottom);
                            if (bot <= win.Bottom + 2 && bot > best) best = bot;   // the lowest thing that is not the whole window
                        }
                        if (depth < 2) stack.Push((c, depth + 1));
                    }
                }
                return best == int.MinValue ? null : best;
            }
            catch { return null; }
        }

        /// <summary>Moves the menu down so its bottom edge is the bottom of the screen. True if it moved.</summary>
        public static bool ClampToBottom(IntPtr window)
        {
            try
            {
                if (!GetWindowRect(window, out var r)) return false;
                var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(MonitorFromWindow(window, 2 /* the nearest screen */), ref info)) return false;
                // Windows draws the panel inside a larger see-through window, so the window's edge can already be at the
                // bottom of the screen while the panel is still held up by the taskbar's room. Measure the panel itself.
                int bottom = VisiblePanelBottom(window, r) ?? r.Bottom;
                int gap = GapToClose(bottom, info.Monitor.Bottom);
                if (gap == 0) return false;
                // keep its size and its place in the stack and do not take focus: only the position changes
                return SetWindowPos(window, IntPtr.Zero, r.Left, r.Top + gap, 0, 0, 0x0001 | 0x0004 | 0x0010);
            }
            catch { return false; }
        }
    }
}
