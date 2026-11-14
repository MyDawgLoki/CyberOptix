using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
namespace CyberOptix.Services
{
    public static class TweakService
    {
        public static void ApplyProfile(Models.SettingsProfile profile)
        {
            OriginalSettingsService.WaitReady();          // the "before" copy must exist before anything changes
            Models.AppSettings.Current.Touch("profile");
            // One PowerShell for everything that needs it (the restore point first, then the service and the network tweak). The registry
            // values and the power plan are set directly, so applying a profile no longer starts a PowerShell for each one.
            var batch = new System.Collections.Generic.List<string>
            {
                "Checkpoint-Computer -Description 'CyberOptix Apply' -RestorePointType MODIFY_SETTINGS -ErrorAction SilentlyContinue",
                profile.DisableSysMain ? "Stop-Service SysMain -Force -ErrorAction SilentlyContinue; Set-Service SysMain -StartupType Disabled" : "Set-Service SysMain -StartupType Automatic; Start-Service SysMain -ErrorAction SilentlyContinue",
            };
            if (profile.DisableNagle)
                batch.Add(@"$interfaces = Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces'; foreach ($i in $interfaces) { New-ItemProperty -Path $i.PSPath -Name 'TcpAckFrequency' -Value 1 -PropertyType DWord -Force | Out-Null; New-ItemProperty -Path $i.PSPath -Name 'TCPNoDelay' -Value 1 -PropertyType DWord -Force | Out-Null }");
            RunPowerShell(string.Join("; ", batch), 90000);
            if (profile.UltimatePerformance) PowerPlanService.UseUltimate();   // made once, then reused
            else PowerPlanService.UseBalanced();
            SetReg("HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR", "AppCaptureEnabled", profile.DisableGameDVR ? 0 : 1);
            SetReg("HKCU:\\System\\GameConfigStore", "GameDVR_Enabled", profile.DisableGameDVR ? 0 : 1);
            SetReg("HKCU:\\System\\GameConfigStore", "GameDVR_FSEBehaviorMode", profile.DisableFullscreenOptimizations ? 2 : 0);
            SetReg("HKLM:\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "HwSchMode", profile.HardwareAcceleratedGPUScheduling ? 2 : 1);
            if (profile.DisableVisualEffects)
                SetReg("HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects", "VisualFXSetting", 2);
            ApplyCatalog(profile);
        }
        /// <summary>
        /// Switches the given catalog tweaks on (a restore point first) and leaves everything else as it is: what was already applied stays applied.
        /// The Optimizer's saved profiles are updated to match, so pressing Save there later does not undo them.
        /// </summary>
        public static void ApplyTweaks(System.Collections.Generic.IEnumerable<string> ids)
        {
            var wanted = ids.Where(id => TweakCatalog.Find(id) is { } t && TweakCatalog.AppliesHere(t)).ToList();
            if (wanted.Count == 0) return;
            OriginalSettingsService.WaitReady();          // the "before" copy must exist before anything changes
            var s = Models.AppSettings.Current;
            s.Touch("profile");
            RunPowerShell("Checkpoint-Computer -Description 'CyberOptix Apply' -RestorePointType MODIFY_SETTINGS -ErrorAction SilentlyContinue");
            var profile = new Models.SettingsProfile { Mode = "General" };
            foreach (var id in s.AppliedTweaks) profile.Tweaks[id] = true;
            foreach (var id in wanted) profile.Tweaks[id] = true;
            ApplyCatalog(profile);
            foreach (var p in new[] { s.GamingProfile, s.GeneralProfile })
                foreach (var id in wanted) p.Tweaks[id] = true;
            Models.AppSettings.Save();
        }

        /// <summary>Runs every catalog tweak in a single elevated PowerShell, so Save does not start dozens of processes.</summary>
        private static void ApplyCatalog(Models.SettingsProfile profile)
        {
            var applied = Models.AppSettings.Current.AppliedTweaks;
            var script = TweakCatalog.BuildScript(profile, applied);
            if (script == null) return;

            try
            {
                using var sf = ScriptFile.Create(script, "tweaks");
                var psi = new ProcessStartInfo { FileName = SystemTools.PowerShell, Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{sf.Path}\"", Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
                Process.Start(psi)?.WaitForExit(60000);
            }
            catch { return; }

            applied.Clear();
            applied.AddRange(TweakCatalog.Available.Where(t => profile.IsOn(t)).Select(t => t.Id));
            Models.AppSettings.Save();
        }

        /// <summary>Sets a DWord value directly (this app already runs as administrator). Paths look like HKCU:\Software\... or HKLM:\SYSTEM\...</summary>
        private static void SetReg(string path, string name, int value)
        {
            try
            {
                var root = path.StartsWith(@"HKCU:\", StringComparison.OrdinalIgnoreCase) ? Microsoft.Win32.Registry.CurrentUser
                         : path.StartsWith(@"HKLM:\", StringComparison.OrdinalIgnoreCase) ? Microsoft.Win32.Registry.LocalMachine : null;
                if (root == null) return;
                using var key = root.CreateSubKey(path.Substring(6));
                key?.SetValue(name, value, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch { }
        }
        private static void RunPowerShell(string script, int timeoutMs = 10000)
        {
            var psi = new ProcessStartInfo { FileName = SystemTools.PowerShell, Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"", Verb = "runas", UseShellExecute = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            try { Process.Start(psi)?.WaitForExit(timeoutMs); } catch { }
        }
    }
}
