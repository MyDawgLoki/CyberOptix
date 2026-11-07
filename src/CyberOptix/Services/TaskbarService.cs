using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace CyberOptix.Services
{
    /// <summary>Reads/writes the per-user registry values that control the Windows taskbar.</summary>
    public static class TaskbarService
    {
        public const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        public const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";
        public const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string StuckRects = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3";

        public static int GetDword(string subKey, string name, int fallback)
        {
            using var k = Registry.CurrentUser.OpenSubKey(subKey);
            return k?.GetValue(name) is int i ? i : fallback;
        }

        public static void SetDword(string subKey, string name, int value)
        {
            using var k = Registry.CurrentUser.CreateSubKey(subKey);
            k.SetValue(name, value, RegistryValueKind.DWord);
        }

        public static bool GetAutoHide()
        {
            using var k = Registry.CurrentUser.OpenSubKey(StuckRects);
            return k?.GetValue("Settings") is byte[] b && b.Length > 8 && (b[8] & 1) == 1;
        }

        public static void SetAutoHide(bool on)
        {
            using var k = Registry.CurrentUser.OpenSubKey(StuckRects, true)
                ?? throw new InvalidOperationException("Taskbar layout key not found.");
            if (k.GetValue("Settings") is byte[] b && b.Length > 8)
            {
                b[8] = (byte)(on ? (b[8] | 1) : (b[8] & ~1));
                k.SetValue("Settings", b, RegistryValueKind.Binary);
            }
        }

        /// <summary>Kills explorer.exe; Windows restarts the shell automatically.</summary>
        public static void RestartExplorer()
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); } catch { }
            }
        }
    }
}
