using System;
using System.Collections.Generic;
using System.Linq;
using CyberOptix.Models;
using CyberOptix.Widgets;

namespace CyberOptix.Services
{
    /// <summary>
    /// Starts, updates and stops the icon docks: each icon group (AI platforms, social, software...) can sit in a dock of its own (the
    /// "Individual Dock" switch next to the group's name on the Dock page). Icons can be dragged from one group to another there.
    /// </summary>
    public static class IconGroupDockService
    {
        private static readonly Dictionary<string, IconGroupDockWindow> Windows = new();

        /// <summary>The docks that should be on screen right now, in a fixed order: each group's own dock.</summary>
        public static List<string> ActiveKeys()
        {
            var s = AppSettings.Current;
            var keys = new List<string>();
            foreach (var group in DockWidgetCatalog.DockableGroups)
            {
                if (s.IndividualDocks.ContainsKey(group) && IconGroupDockWindow.IconsFor(group).Count > 0) keys.Add(group);
            }
            return keys;
        }

        /// <summary>Where a dock sits: an edge, a corner, or to the left or right of the main dock.</summary>
        public static string PositionFor(string key)
        {
            var s = AppSettings.Current;
            string? p = s.IndividualDocks.TryGetValue(key, out var v) ? v : null;
            if (p == "Beside") p = "BesideRight";   // the old "next to the main dock" is now "right of main dock"
            return p != null && IconGroupDockWindow.Positions.Any(x => x.Key == p) ? p : "Top";
        }

        /// <summary>Remembers that this dock was just given its place, so it lines up after the docks that were placed there before it.</summary>
        public static void NotePlaced(string key)
        {
            var o = AppSettings.Current.DockPlaceOrder;
            o.Remove(key);
            o.Add(key);
        }

        /// <summary>The docks, in the order they were placed (docks never placed follow in the usual order).</summary>
        public static List<string> InPlacedOrder(IEnumerable<string> keys)
        {
            var o = AppSettings.Current.DockPlaceOrder;
            return keys.Select((k, i) => (k, i)).OrderBy(x => { int j = o.IndexOf(x.k); return j < 0 ? int.MaxValue : j; }).ThenBy(x => x.i).Select(x => x.k).ToList();
        }

        /// <summary>Puts every dock where it belongs again (each one's place depends on the others' sizes).</summary>
        public static void PlaceAll() { foreach (var w in Windows.Values) w.Place(); }

        public static void Apply()
        {
            try
            {
                var keys = ActiveKeys();
                foreach (var gone in Windows.Keys.Where(k => !keys.Contains(k)).ToList())
                {
                    var old = Windows[gone];
                    Windows.Remove(gone);
                    old.Close();
                }
                foreach (var key in keys)
                {
                    if (Windows.TryGetValue(key, out var existing)) { existing.Refresh(); continue; }
                    var window = new IconGroupDockWindow(key);
                    var captured = key;
                    window.Closed += (_, _) => { if (Windows.TryGetValue(captured, out var cur) && cur == window) Windows.Remove(captured); };
                    Windows[key] = window;
                    window.Show();
                }
                PlaceAll();   // docks in one place share it, so each needs to know about the others
            }
            catch { }
        }

        /// <summary>The dock with this key, if it is on screen (the docks use it to line up next to each other).</summary>
        internal static IconGroupDockWindow? Get(string key) => Windows.TryGetValue(key, out var w) ? w : null;

        public static void Stop()
        {
            foreach (var w in Windows.Values.ToList()) w.Close();
            Windows.Clear();
        }
    }
}
