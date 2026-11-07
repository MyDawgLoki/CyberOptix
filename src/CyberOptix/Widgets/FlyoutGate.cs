using System;

namespace CyberOptix.Widgets
{
    /// <summary>
    /// Lets a dock icon close its own flyout when you click it a second time, like the Windows quick settings do.
    /// A WPF popup closes itself the moment you press the mouse anywhere outside it, including on the icon that opened it, and the
    /// click is only finished a moment later. Without this, that finished click would open the flyout again straight away.
    /// So the gate remembers which icon's flyout just closed, and ignores that icon's click for a short time.
    /// </summary>
    public sealed class FlyoutGate
    {
        /// <summary>How long after a flyout closes the same icon's click still counts as the click that closed it.</summary>
        public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(450);

        private object? _openFor;
        private object? _closedFor;
        private DateTime _closedAt = DateTime.MinValue;

        /// <summary>True while the flyout of this icon is on screen.</summary>
        public bool IsOpenFor(object target) => ReferenceEquals(_openFor, target);

        /// <summary>Whether a click on this icon should open its flyout now.</summary>
        public bool ShouldOpen(object target, DateTime now) =>
            !IsOpenFor(target) && !(ReferenceEquals(_closedFor, target) && now - _closedAt < Window);

        public void Opened(object target) => _openFor = target;

        public void Closed(object target, DateTime now)
        {
            if (ReferenceEquals(_openFor, target)) _openFor = null;
            _closedFor = target;
            _closedAt = now;
        }
    }
}
