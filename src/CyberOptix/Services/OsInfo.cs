using System;

namespace CyberOptix.Services
{
    /// <summary>Which Windows this is, so the parts that only exist on Windows 11 can be left out on Windows 10.</summary>
    public static class OsInfo
    {
        public static int Build { get; } = Environment.OSVersion.Version.Build;

        /// <summary>Windows 11 is build 22000 or newer. Setting the environment variable CYBEROPTIX_FAKE_WINDOWS10=1 makes the app act like Windows 10 (for testing).</summary>
        public static bool IsWindows11 { get; } = Build >= 22000 && Environment.GetEnvironmentVariable("CYBEROPTIX_FAKE_WINDOWS10") != "1";

        /// <summary>The Mica and Acrylic backdrops need Windows 11 22H2 (build 22621) or newer.</summary>
        public static bool SupportsBackdrops { get; } = IsWindows11 && Build >= 22621;
    }
}
