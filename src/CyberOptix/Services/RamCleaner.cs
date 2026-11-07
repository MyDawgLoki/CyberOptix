using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CyberOptix.Services
{
    /// <summary>One thing the cleaner can stop: a few services and/or processes that belong to one program.</summary>
    public record CleanTarget(string Id, string Group, string Name, string Short, string Hint, string[] Services, string[] Processes,
                             double MemMB = 0, bool Caution = false);

    /// <summary>
    /// Frees memory by stopping third-party and Windows services and processes the user ticks (found by <see cref="CleanupScanner"/>), once per button press.
    /// Nothing here runs on a timer or stays running: <see cref="Run"/> starts one PowerShell, waits for it, and is done.
    /// Services are stopped and set to Manual (not Disabled), and their original start modes are saved so <see cref="Restore"/> can undo it.
    /// </summary>
    public static class RamCleaner
    {
        public const string ThirdParty = "Third-party software";
        public const string Microsoft = "Windows and Microsoft";

        public static string StatePath => AppPaths.Secure("ram-state.json");

        public static bool HasRestorePoint => File.Exists(StatePath);

        // Skipped even if something slips into a list: security software, GPU drivers, audio, anti-cheat, the shell and developer tools.
        private const string ProtectedServices = CleanupScanner.ProtectedServicesPattern;
        private const string ProtectedProcesses = CleanupScanner.ProtectedProcessesPattern;

        private static string List(IEnumerable<string> names) => string.Join(",", names.Select(RestoreGuard.Q));

        /// <summary>Runs the chosen targets once and returns a short summary of what happened.</summary>
        public static string Run(IEnumerable<CleanTarget> chosen)
        {
            if (!chosen.Any()) return "Nothing is switched on.";

            var services = chosen.SelectMany(t => t.Services).Distinct().ToList();
            var processes = chosen.SelectMany(t => t.Processes).Distinct().ToList();
            return Execute(BuildRun(services, processes, CyberOptix.Models.AppSettings.Current.NeverKill.ToList()));
        }

        /// <summary>Puts every service this cleaner changed back to its original start mode, and starts the ones that were running.</summary>
        public static string Restore() =>
            !HasRestorePoint ? "There is nothing to restore." : Execute(BuildRestore());

        private static string Prelude(string outFile) => $@"
$ErrorActionPreference = 'Continue'
$out = {RestoreGuard.Q(outFile)}
$state = {RestoreGuard.Q(StatePath)}
function Say($m) {{ Add-Content -Path $out -Value $m }}
";

        private static string BuildRun(List<string> services, List<string> processes, List<string> never) => @"
$protectedSvc = '" + ProtectedServices + @"'
$protectedProc = '" + ProtectedProcesses + @"'
$never = @(" + List(never) + @")
$services = @(" + List(services) + @")
$processes = @(" + List(processes) + @")
$new = @(); $stopped = 0; $closed = 0
foreach ($name in $services) {
  if ($name -match $protectedSvc -or $never -contains $name) { Say ""skipped protected service $name""; continue }
  $cim = Get-CimInstance Win32_Service -Filter ""Name='$name'"" -ErrorAction SilentlyContinue
  if (-not $cim) { continue }
  $running = $cim.State -eq 'Running'
  $new += [pscustomobject]@{ Name = $name; StartMode = $cim.StartMode; WasRunning = $running }
  try { if ($running) { Stop-Service -Name $name -Force -ErrorAction Stop; $stopped++ } }
  catch { Say (""could not stop $name : "" + $_.Exception.Message); continue }
  if ($cim.StartMode -ne 'Disabled') {
    try { Set-Service -Name $name -StartupType Manual -ErrorAction Stop }
    catch {
      # Some Windows services (Gaming Services, for one) are protected: they can be stopped, but their start setting cannot be changed.
      if ($_.Exception.Message -match 'denied') { Say (""$name was stopped. Windows protects its start setting, so it can start again by itself if a Store game or the Xbox app needs it."") }
      else { Say (""$name was stopped, but its start setting could not be changed : "" + $_.Exception.Message) }
    }
  }
}
if ($new.Count) {
  # Keep the ORIGINAL start modes if a state file already exists.
  if (Test-Path $state) {
    $old = @(Get-Content $state -Raw | ConvertFrom-Json)
    $new = @($old) + @($new | Where-Object { $_.Name -notin $old.Name })
  }
  New-Item -ItemType Directory -Force -Path (Split-Path $state) | Out-Null
  $new | ConvertTo-Json | Set-Content -Path $state -Encoding UTF8
}
foreach ($p in $processes) {
  if ($p -match $protectedProc -or $never -contains $p) { Say ""skipped protected process $p""; continue }
  $found = @(Get-Process -Name $p -ErrorAction SilentlyContinue)
  if ($found.Count -eq 0) { continue }
  $found | Stop-Process -Force -ErrorAction SilentlyContinue
  $closed += $found.Count
}
$os = Get-CimInstance Win32_OperatingSystem
Say (""stopped $stopped service(s), closed $closed process(es). Free memory now: {0:N1} GB of {1:N1} GB"" -f ($os.FreePhysicalMemory/1MB), ($os.TotalVisibleMemorySize/1MB))
";

        private static string BuildRestore() => @"
if (-not (Test-Path $state)) { Say 'There is nothing to restore.'; exit }
$saved = @(Get-Content $state -Raw | ConvertFrom-Json)
$n = 0
$protectedSvc = '" + ProtectedServices + @"'
$modes = @('Automatic','Auto','Manual','Disabled')
foreach ($e in $saved) {
  if ($e.Name -match $protectedSvc -or $modes -notcontains $e.StartMode) { Say (""skipped $($e.Name)""); continue }
  $mode = if ($e.StartMode -eq 'Auto') { 'Automatic' } else { $e.StartMode }
  try {
    Set-Service -Name $e.Name -StartupType $mode -ErrorAction Stop
    if ($e.WasRunning) { Start-Service -Name $e.Name -ErrorAction Stop }
    $n++
  } catch { Say (""could not restore $($e.Name) : "" + $_.Exception.Message) }
}
Remove-Item $state -ErrorAction SilentlyContinue
Say (""restored $n service(s)."")
";

        /// <summary>Starts one elevated PowerShell, waits for it to finish, and returns what it reported. Nothing keeps running afterwards.</summary>
        private static string Execute(string body)
        {
            var outFile = ScriptFile.TempName("clean", ".txt");
            try
            {
                using var sf = ScriptFile.Create(Prelude(outFile) + body, "clean");
                var psi = new ProcessStartInfo
                {
                    FileName = SystemTools.PowerShell,
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{sf.Path}\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                using var p = Process.Start(psi);
                if (p == null) return "Windows would not start PowerShell.";
                if (!p.WaitForExit(60000)) { try { p.Kill(); } catch { } return "Timed out after 60 seconds."; }
            }
            catch (Exception ex)
            {
                return $"Could not run: {ex.Message}";
            }

            try
            {
                return File.Exists(outFile)
                    ? string.Join("  ", File.ReadAllLines(outFile).Select(l => l.Trim()).Where(l => l.Length > 0))
                    : "Done.";
            }
            finally
            {
                try { File.Delete(outFile); } catch { }
            }
        }
    }
}
