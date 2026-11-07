using System;
using System.Windows;
using System.Windows.Threading;

namespace CyberOptix.Widgets
{
    /// <summary>
    /// A window that is meant to sit behind the others (the desktop clock and widgets) is sent to the back while it is built, but Windows then
    /// shows it on top of everything until another window is used. This sends it behind again once it is on screen, and twice more shortly after,
    /// because the browser or game that was already open can take a moment to settle too.
    /// </summary>
    public static class ZOrderSettle
    {
        public static void Run(Window window, Action sendBack)
        {
            window.ContentRendered += (_, _) =>
            {
                Safe(sendBack);
                foreach (var ms in new[] { 400, 1500, 4000 })
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                    timer.Tick += (_, _) => { timer.Stop(); if (window.IsVisible) Safe(sendBack); };
                    timer.Start();
                }
            };
        }

        private static void Safe(Action a) { try { a(); } catch { } }
    }
}
