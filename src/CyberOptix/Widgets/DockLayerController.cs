using System;

namespace CyberOptix.Widgets
{
    /// <summary>
    /// Decides when the dock should come in front of the windows, and when it should go back behind them.
    /// It only decides: the dock does the moving. Time is passed in, so every case can be tested.
    ///
    /// Modes (the DockLayer setting):
    ///   Top   - the dock is always in front, so there is nothing to decide.
    ///   Fall  - the dock sits behind windows. Holding the pointer over it (or the strip of screen edge it sits on) brings it forward.
    ///   Stick - the dock sits behind windows and the pointer does nothing. It only comes forward with the Start menu.
    /// In both of the last two it is brought forward while the Start menu or search is open, and while something it opened is in use.
    /// </summary>
    public sealed class DockLayerController
    {
        public enum Move { None, Raise, Lower }

        /// <summary>The pointer must stay over the dock this long before it comes forward, so sweeping across it does nothing.</summary>
        public static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(150);

        /// <summary>After the pointer leaves, the dock waits this long before it goes back, so a brief slip does not flicker it away.</summary>
        public static readonly TimeSpan LeaveDelay = TimeSpan.FromMilliseconds(350);

        private DateTime _hoverSince = DateTime.MinValue;
        private DateTime _wantedUntil = DateTime.MinValue;

        /// <summary>
        /// Whether the pointer is over the dock. The area is the dock itself plus the strip of screen edge it sits on, so pushing the pointer
        /// against the edge of the screen is enough. <paramref name="room"/> is the transparent shadow margin around the dock window, and
        /// <paramref name="pad"/> widens the area a little once the dock is already out, so a slight drift does not send it away.
        /// All values are screen pixels.
        /// </summary>
        public static bool PointerOverDock(int px, int py, int winLeft, int winTop, int winRight, int winBottom, int room, int pad, bool dockAtTop, int screenTop, int screenBottom)
        {
            int left = winLeft + room - pad;
            int right = winRight - room + pad;
            int dockTop = winTop + room;
            int dockBottom = winBottom - room;
            int zoneTop = dockAtTop ? screenTop : dockTop - pad;
            int zoneBottom = dockAtTop ? dockBottom + pad : screenBottom;
            return px >= left && px <= right && py >= zoneTop && py <= zoneBottom;
        }

        /// <summary>True while the dock is in front of the windows because of this controller.</summary>
        public bool InFront { get; private set; }

        public void Reset()
        {
            InFront = false;
            _hoverSince = DateTime.MinValue;
            _wantedUntil = DateTime.MinValue;
        }

        /// <param name="mode">"Top", "Fall" or "Stick".</param>
        /// <param name="startOpen">The Start menu or search is open.</param>
        /// <param name="pointerOver">The pointer is over the dock or the screen edge it sits on.</param>
        /// <param name="mouseDown">A mouse button is held (dragging a window, selecting text): the dock stays out of the way.</param>
        /// <param name="busy">Something the dock opened is being used (a flyout, or an icon being dragged).</param>
        public Move Update(string mode, bool startOpen, bool pointerOver, bool mouseDown, bool busy, DateTime now)
        {
            if (mode != "Fall" && mode != "Stick")
            {
                Reset();
                return Move.None;   // "Top": the dock is just always in front
            }

            bool hover = mode == "Fall" && pointerOver;
            if (!hover) _hoverSince = DateTime.MinValue;

            bool hoverReady = false;
            if (hover)
            {
                if (InFront) hoverReady = true;                                   // already out: staying over it keeps it out
                else if (!mouseDown)                                              // not while dragging something past it
                {
                    if (_hoverSince == DateTime.MinValue) _hoverSince = now;
                    hoverReady = now - _hoverSince >= HoverDelay;
                }
                else _hoverSince = DateTime.MinValue;
            }

            bool reason = startOpen || hoverReady || (busy && InFront);

            if (reason)
            {
                _wantedUntil = now + LeaveDelay;
                if (!InFront) { InFront = true; return Move.Raise; }
                return Move.None;
            }

            if (InFront && now >= _wantedUntil)
            {
                InFront = false;
                return Move.Lower;
            }
            return Move.None;
        }
    }
}
