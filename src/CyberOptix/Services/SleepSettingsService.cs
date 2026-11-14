using System;
using System.Runtime.InteropServices;

namespace CyberOptix.Services
{
    /// <summary>
    /// Reads and sets Windows' own "turn off the screen" and "put the PC to sleep" timers for the active power plan,
    /// plugged in and on battery. These are the same values as Windows Settings, System, Power. Nothing is stored by CyberOptix.
    /// </summary>
    public static class SleepSettingsService
    {
        public enum Timer { ScreenOff, Sleep }

        private static readonly Guid SubVideo = new("7516b95f-f776-4464-8c53-06167f40cc99");
        private static readonly Guid VideoIdle = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
        private static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
        private static readonly Guid StandbyIdle = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

        [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
        [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

        /// <summary>True on a laptop or anything else with a battery, so the "on battery" rows are worth showing.</summary>
        public static bool HasBattery => GetSystemPowerStatus(out var st) && st.BatteryFlag != 128 /* no system battery */ && st.BatteryFlag != 255 /* unknown */;

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus { public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
        [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

        private static (Guid Sub, Guid Setting) Ids(Timer t) => t == Timer.ScreenOff ? (SubVideo, VideoIdle) : (SubSleep, StandbyIdle);

        private static bool ActiveScheme(out Guid scheme)
        {
            scheme = Guid.Empty;
            if (PowerGetActiveScheme(IntPtr.Zero, out var p) != 0 || p == IntPtr.Zero) return false;
            try { scheme = Marshal.PtrToStructure<Guid>(p); return true; }
            finally { LocalFree(p); }
        }

        /// <summary>Seconds until the timer fires (0 = never), or -1 when it cannot be read.</summary>
        public static int Get(Timer timer, bool pluggedIn)
        {
            try
            {
                if (!ActiveScheme(out var scheme)) return -1;
                var (sub, setting) = Ids(timer);
                uint value;
                uint rc = pluggedIn
                    ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out value)
                    : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out value);
                return rc == 0 ? (int)Math.Min(value, int.MaxValue) : -1;
            }
            catch { return -1; }
        }

        /// <summary>Sets the timer, in seconds (0 = never), for the active power plan.</summary>
        public static bool Set(Timer timer, bool pluggedIn, int seconds)
        {
            try
            {
                if (seconds < 0 || seconds > 24 * 3600 || !ActiveScheme(out var scheme)) return false;
                var (sub, setting) = Ids(timer);
                uint rc = pluggedIn
                    ? PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, (uint)seconds)
                    : PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, (uint)seconds);
                if (rc != 0) return false;
                return PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;   // makes Windows pick the new value up now
            }
            catch { return false; }
        }
    }
}
