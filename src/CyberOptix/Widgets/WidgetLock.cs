using System;
using System.Collections.Generic;
using System.Linq;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>
    /// Widgets stay clamped in place. They can be moved only after "Unlock Widgets" (from the desktop's right-click menu or the Widgets page),
    /// and "Lock Widgets" clamps them where they are. "Restore Widgets Placing" puts every widget back where it was when the layout was last
    /// locked (or placed from the Widgets page). The lock always starts locked at launch. Everything here runs on the UI thread.
    /// </summary>
    public static class WidgetLock
    {
        public static bool IsLocked { get; private set; } = true;

        /// <summary>Raised after the lock is switched on or off.</summary>
        public static event Action? Changed;

        /// <summary>At launch: if no layout has been saved yet, the current positions become it, so "Restore Widgets Placing" always has something to restore.</summary>
        public static void Init()
        {
            var s = AppSettings.Current;
            if (s.WidgetPlacing.Count == 0 && s.Positions.Count > 0) SavePlacing();
        }

        public static void Unlock() => Set(false);

        public static void Lock()
        {
            SavePlacing();       // locking confirms the layout: this is where "Restore Widgets Placing" comes back to
            Set(true);
        }

        public static void Toggle()
        {
            if (IsLocked) Unlock(); else Lock();
        }

        private static void Set(bool locked)
        {
            if (IsLocked == locked) return;
            IsLocked = locked;
            Changed?.Invoke();
            DesktopMenuService.Refresh();
        }

        /// <summary>Remembers where every widget is now as "the placing".</summary>
        public static void SavePlacing()
        {
            var s = AppSettings.Current;
            s.WidgetPlacing = s.Positions.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
            AppSettings.Save();
        }

        /// <summary>Puts every widget back where it was in the saved placing. False when nothing has been saved yet.</summary>
        public static bool RestorePlacing()
        {
            var s = AppSettings.Current;
            if (s.WidgetPlacing.Count == 0) return false;
            foreach (var kv in s.WidgetPlacing) s.Positions[kv.Key] = kv.Value.ToArray();
            AppSettings.Save();
            WidgetManager.ApplyPositions();
            return true;
        }

        /// <summary>The commands the desktop menu can send (and nothing else is accepted).</summary>
        public static bool Run(string command)
        {
            switch (command)
            {
                case "unlock": Unlock(); return true;
                case "lock": Lock(); return true;
                case "toggle": Toggle(); return true;
                case "restore": RestorePlacing(); return true;
                default: return false;
            }
        }
    }
}
