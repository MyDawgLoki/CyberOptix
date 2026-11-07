using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CyberOptix.Services
{
    /// <summary>
    /// The saved "original settings" file lives in the user's profile, but putting it back runs as administrator. So nothing from that file
    /// is trusted: a registry key or service is only put back if CyberOptix itself is known to change it, values must have the right shape,
    /// and everything pasted into a script is quoted so it can only ever be text.
    /// </summary>
    internal static class RestoreGuard
    {
        private static readonly Regex CatalogPath = new(@"'(HK(?:CU|LM):[^']+)'", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CatalogService = new(@"\bS\s+([A-Za-z0-9_.\-]+)\s+(?:Disabled|Manual|Automatic|DelayedAuto)\b", RegexOptions.Compiled);

        private static readonly Regex Interface = new(
            @"^(?:HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces|Microsoft\.PowerShell\.Core\\Registry::HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces)\\\{[0-9A-Fa-f]{8}(?:-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\}$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex GuidText = new(@"^[0-9A-Fa-f]{8}(?:-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}$", RegexOptions.Compiled);

        // Keys CyberOptix reads or writes outside the tweak catalog.
        private static readonly string[] Fixed =
        {
            @"HKCU:\Control Panel\Mouse",
            @"HKCU:\Control Panel\Cursors",
            @"HKCU:\Control Panel\Desktop",
            @"HKCU:\Control Panel\Desktop\WindowMetrics",
            @"HKCU:\Software\Microsoft\Accessibility",
            @"HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR",
            @"HKCU:\System\GameConfigStore",
            @"HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
            @"HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            @"HKLM:\SYSTEM\CurrentControlSet\Control\Keyboard Layout",
            @"HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
            @"HKLM:\SYSTEM\CurrentControlSet\Control\Power",
        };

        private static readonly Lazy<HashSet<string>> Paths = new(BuildPaths);
        private static readonly Lazy<HashSet<string>> Services = new(BuildServices);

        private static IEnumerable<string> CatalogText() =>
            TweakCatalog.All.SelectMany(t => new[] { t.Apply, t.Revert }).Append(TweakCatalog.PreludeScript);

        private static HashSet<string> BuildPaths()
        {
            var set = new HashSet<string>(Fixed.Select(Normalize), StringComparer.OrdinalIgnoreCase);
            foreach (var text in CatalogText())
                foreach (Match m in CatalogPath.Matches(text))
                    set.Add(Normalize(m.Groups[1].Value));
            return set;
        }

        private static HashSet<string> BuildServices()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SysMain" };
            foreach (var text in CatalogText())
                foreach (Match m in CatalogService.Matches(text))
                    set.Add(m.Groups[1].Value);
            return set;
        }

        private static string Normalize(string path) => path.Replace(@"\\", @"\").TrimEnd('\\');

        /// <summary>True only for a registry value CyberOptix itself is known to change.</summary>
        public static bool RegistryAllowed(string? path, string? name)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 400 || path.Any(char.IsControl)) return false;
            if (name != null && (name.Length > 200 || name.Any(char.IsControl))) return false;
            path = Normalize(path);
            if (Interface.IsMatch(path)) return name is "TcpAckFrequency" or "TCPNoDelay";
            return Paths.Value.Contains(path);
        }

        public static bool ServiceAllowed(string? name) => !string.IsNullOrWhiteSpace(name) && Services.Value.Contains(name);

        public static bool IsGuid(string? text) => text != null && GuidText.IsMatch(text);

        /// <summary>Does the saved text have the shape its registry type needs?</summary>
        public static bool ValueOk(string? kind, string text) => kind switch
        {
            "DWord" => long.TryParse(text, out var d) && d >= int.MinValue && d <= uint.MaxValue,
            "QWord" => long.TryParse(text, out _) || ulong.TryParse(text, out _),
            "Binary" => text.Length == 0 || IsBase64(text),
            _ => text.Length <= 32768,
        };

        private static bool IsBase64(string text)
        {
            var buffer = new byte[text.Length];
            return Convert.TryFromBase64String(text, buffer, out _);
        }

        /// <summary>
        /// A PowerShell single-quoted literal. PowerShell treats the curly single quotes as quote characters too, so each one is doubled
        /// along with the plain apostrophe; nothing can end the string early.
        /// </summary>
        public static string Q(string s)
        {
            var sb = new StringBuilder(s.Length + 2).Append('\'');
            foreach (var c in s)
            {
                sb.Append(c);
                if (c is '\'' or '‘' or '’' or '‚' or '‛') sb.Append(c);
            }
            return sb.Append('\'').ToString();
        }
    }
}
