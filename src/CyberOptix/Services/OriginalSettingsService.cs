using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    /// <summary>One registry value as it was before CyberOptix changed anything.</summary>
    public class RegRec
    {
        public string Path { get; set; } = "";       // PowerShell style: HKCU:\... or HKLM:\...
        public string Name { get; set; } = "";       // "" = the key's default value
        public bool Exists { get; set; }
        public string Kind { get; set; } = "";       // DWord | QWord | String | ExpandString | MultiString | Binary
        public JsonElement? Value { get; set; }      // Binary is base64, MultiString is an array
        public List<string> Tags { get; set; } = new();  // which tweak or feature changes it
    }

    /// <summary>A Windows service as it was: start mode and whether it was running.</summary>
    public class SvcRec
    {
        public string Name { get; set; } = "";
        public int Start { get; set; }               // 2 automatic, 3 manual, 4 disabled
        public bool Delayed { get; set; }
        public bool Running { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    public class DisplayBase
    {
        public string Id { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string? Ramp { get; set; }            // base64 of the 768 gamma-ramp values
        public int? Vibrance { get; set; }
        public int? Hue { get; set; }
        public uint Orientation { get; set; }
    }

    public class OriginalSettings
    {
        public string Taken { get; set; } = "";
        public bool TweaksCaptured { get; set; }
        public List<string> Preexisting { get; set; } = new();   // tweaks that were already on before this feature existed
        public List<RegRec> Registry { get; set; } = new();
        public List<SvcRec> Services { get; set; } = new();
        public string? PowerScheme { get; set; }
        public Dictionary<string, DisplayBase> Displays { get; set; } = new();
    }

    /// <summary>Which groups of settings to put back. Each one can be chosen on its own.</summary>
    public class RestoreOptions
    {
        public bool Tweaks { get; set; } = true;         // Windows tweaks and the performance profile (registry, power plan)
        public bool Services { get; set; } = true;       // services the tweaks stopped, and "Free up resources"
        public bool DisplayColors { get; set; } = true;
        public bool Rotation { get; set; } = true;
        public bool Remaps { get; set; } = true;
        public bool Pointer { get; set; } = true;        // pointer speed and precision
        public bool Scrolling { get; set; } = true;
        public bool Cursors { get; set; } = true;        // cursor theme and size
        /// <summary>Resets NVIDIA digital vibrance and hue to the driver defaults on every display, whoever changed them. Never part of "everything"; it has to be asked for.</summary>
        public bool NvidiaDefaults { get; set; }

        private bool[] Flags() => new[] { Tweaks, Services, DisplayColors, Rotation, Remaps, Pointer, Scrolling, Cursors, NvidiaDefaults };

        /// <summary>"tweaks,services,..." as passed on the command line by the uninstaller. Empty means everything.</summary>
        public static RestoreOptions Parse(string? list)
        {
            var o = new RestoreOptions();
            if (string.IsNullOrWhiteSpace(list)) return o;
            var on = new HashSet<string>(list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            o.Tweaks = on.Contains("tweaks"); o.Services = on.Contains("services"); o.DisplayColors = on.Contains("colors");
            o.Rotation = on.Contains("rotation"); o.Remaps = on.Contains("remaps"); o.Pointer = on.Contains("pointer");
            o.Scrolling = on.Contains("scrolling"); o.Cursors = on.Contains("cursors");
            o.NvidiaDefaults = on.Contains("nvidia");
            return o;
        }

        public bool Any => Flags().Any(f => f);
    }

    /// <summary>
    /// Remembers how Windows was set up before CyberOptix first changed anything, and puts it back on request (or when uninstalling).
    /// Covers the tweaks, the stopped services, display colors, key remaps and the mouse settings.
    /// </summary>
    public static class OriginalSettingsService
    {
        public static string FilePath => AppPaths.Secure("originals.json");

        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

        /// <summary>Completes when the PowerShell part of the snapshot has finished.</summary>
        public static Task Ready { get; private set; } = Task.CompletedTask;

        public static bool HasSnapshot => File.Exists(FilePath);

        public static void WaitReady()
        {
            try { Ready.Wait(60000); } catch { }
        }

        private static OriginalSettings? Load()
        {
            try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<OriginalSettings>(File.ReadAllText(FilePath), Json) : null; }
            catch { return null; }
        }

        private static void Save(OriginalSettings o)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(o, Json));
            }
            catch { }
        }

        // ---------- taking the snapshot ----------

        /// <summary>Call once at launch, before anything is applied. Does nothing when a snapshot already exists.</summary>
        public static void Start()
        {
            var existing = Load();
            if (existing != null)
            {
                if (!existing.TweaksCaptured) Ready = Task.Run(() => CaptureTweaks(existing));
                return;
            }

            var o = new OriginalSettings { Taken = DateTime.Now.ToString("s"), Preexisting = AppSettings.Current.AppliedTweaks.ToList() };
            try { CaptureDisplays(o); } catch { }
            try { CaptureInputRegistry(o); } catch { }
            Save(o);
            Ready = Task.Run(() => CaptureTweaks(o));
        }

        private static void CaptureDisplays(OriginalSettings o)
        {
            NvApi.EnsureInit();
            foreach (var d in DisplayService.GetDisplays())
            {
                if (string.IsNullOrEmpty(d.DeviceName)) continue;
                AppSettings.Current.DisplayColors.TryGetValue(d.Id, out var saved);
                var b = new DisplayBase { Id = d.Id, DeviceName = d.DeviceName, Orientation = DisplayColorService.GetOrientation(d.DeviceName) };

                // A change made before this version existed cannot be known, so the normal values are used for it.
                var ramp = saved is { RampTouched: true } ? DisplayColorService.IdentityRamp() : DisplayColorService.ReadRamp(d.DeviceName);
                if (ramp != null) b.Ramp = Convert.ToBase64String(ramp.SelectMany(BitConverter.GetBytes).ToArray());

                if (NvApi.Available)
                {
                    if (saved is { VibranceTouched: true }) b.Vibrance = 50;
                    else if (NvApi.TryGetVibrance(d.DeviceName, out var v)) b.Vibrance = v;
                    if (saved is { HueTouched: true }) b.Hue = 0;
                    else if (NvApi.TryGetHue(d.DeviceName, out var h)) b.Hue = h;
                }
                o.Displays[d.Id] = b;
            }
        }

        private static readonly string[] MouseValues = { "MouseSensitivity", "MouseSpeed", "MouseThreshold1", "MouseThreshold2", "WheelScrollLines" };

        private static void CaptureInputRegistry(OriginalSettings o)
        {
            foreach (var n in MouseValues) Merge(o, ReadReg(Registry.CurrentUser, "HKCU:", @"Control Panel\Mouse", n, "mouse"));
            using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors"))
                foreach (var n in k?.GetValueNames() ?? Array.Empty<string>())
                    Merge(o, ReadReg(Registry.CurrentUser, "HKCU:", @"Control Panel\Cursors", n, "mouse"));
            Merge(o, ReadReg(Registry.CurrentUser, "HKCU:", @"Software\Microsoft\Accessibility", "CursorSize", "mouse"));
            Merge(o, ReadReg(Registry.CurrentUser, "HKCU:", @"Software\Microsoft\Accessibility", "CursorBaseSize", "mouse"));
            Merge(o, ReadReg(Registry.LocalMachine, "HKLM:", @"SYSTEM\CurrentControlSet\Control\Keyboard Layout", "Scancode Map", "remap"));
        }

        private static RegRec ReadReg(RegistryKey hive, string prefix, string sub, string name, string tag)
        {
            var r = new RegRec { Path = prefix + "\\" + sub, Name = name, Tags = new List<string> { tag } };
            try
            {
                using var k = hive.OpenSubKey(sub);
                if (k == null || !k.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return r;
                r.Exists = true;
                r.Kind = k.GetValueKind(name).ToString();
                var v = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (v is byte[] bytes) v = Convert.ToBase64String(bytes);
                r.Value = JsonSerializer.SerializeToElement(v);
            }
            catch { }
            return r;
        }

        private static void Merge(OriginalSettings o, RegRec r)
        {
            var have = o.Registry.FirstOrDefault(x => string.Equals(x.Path, r.Path, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Name, r.Name, StringComparison.OrdinalIgnoreCase));
            if (have == null) { o.Registry.Add(r); return; }
            foreach (var t in r.Tags) if (!have.Tags.Contains(t)) have.Tags.Add(t);
        }

        private static void Merge(OriginalSettings o, SvcRec s)
        {
            var have = o.Services.FirstOrDefault(x => string.Equals(x.Name, s.Name, StringComparison.OrdinalIgnoreCase));
            if (have == null) { o.Services.Add(s); return; }
            foreach (var t in s.Tags) if (!have.Tags.Contains(t)) have.Tags.Add(t);
        }

        private class CaptureDto
        {
            public List<RegRec> Reg { get; set; } = new();
            public List<SvcRec> Svc { get; set; } = new();
            public string? Scheme { get; set; }
        }

        /// <summary>Reads every registry value and service the tweaks touch, without changing anything (no administrator needed).</summary>
        private static void CaptureTweaks(OriginalSettings o)
        {
            var outFile = ScriptFile.TempName("capture", ".json");
            try
            {
                using (var sf = ScriptFile.Create(BuildCaptureScript(outFile), "capture"))
                {
                    var psi = new ProcessStartInfo(SystemTools.PowerShell, $"-NoProfile -ExecutionPolicy Bypass -File \"{sf.Path}\"")
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                    using (var p = Process.Start(psi)) p?.WaitForExit(90000);
                }
                if (!File.Exists(outFile)) return;

                var dto = JsonSerializer.Deserialize<CaptureDto>(File.ReadAllText(outFile), Json);
                if (dto == null) return;
                foreach (var r in dto.Reg) Merge(o, r);
                foreach (var s in dto.Svc) Merge(o, s);
                o.PowerScheme = RestoreGuard.IsGuid(dto.Scheme) ? dto.Scheme : null;
                o.TweaksCaptured = true;
                Save(o);
            }
            catch { }
            finally
            {
                try { File.Delete(outFile); } catch { }
            }
        }

        private static string BuildCaptureScript(string outFile)
        {
            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
            sb.AppendLine("$outFile = " + RestoreGuard.Q(outFile));
            sb.AppendLine("""
Remove-Item Alias:r -Force -ErrorAction SilentlyContinue
$reg = [ordered]@{}; $svc = [ordered]@{}; $cur = 'x'
function Rec($p,$n){
  $key = "$p|$n"
  if(-not $reg.Contains($key)){
    $o = [ordered]@{ path=$p; name=$n; exists=$false; kind=''; value=$null; tags=@() }
    try {
      $k = Get-Item -LiteralPath $p -ErrorAction Stop
      if($k.GetValueNames() -contains $n){
        $o.exists = $true; $o.kind = $k.GetValueKind($n).ToString()
        $v = $k.GetValue($n, $null, 'DoNotExpandEnvironmentNames')
        if($v -is [byte[]]){ $v = [Convert]::ToBase64String($v) }
        $o.value = $v
      }
    } catch {}
    $reg[$key] = $o
  }
  if($reg[$key].tags -notcontains $cur){ $reg[$key].tags += $cur }
}
function RecSvc($n){
  if(-not $svc.Contains($n)){
    $s = Get-Service $n -ErrorAction SilentlyContinue
    if($s){
      $k = Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$n"
      $svc[$n] = [ordered]@{ name=$n; start=[int]$k.Start; delayed=([int]$k.DelayedAutostart -eq 1); running=($s.Status -eq 'Running'); tags=@() }
    }
  }
  if($svc.Contains($n) -and ($svc[$n].tags -notcontains $cur)){ $svc[$n].tags += $cur }
}
# The tweak scripts call these, so here they only record the current value instead of changing it.
function R($p,$n,$v,$t){ Rec $p $n }
function D($p,$n){ Rec $p $n }
function S($n,$mode){ RecSvc $n }
function Startup($name,$on){ Rec 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' $name }
function Anim($on){ Rec 'HKCU:\Control Panel\Desktop\WindowMetrics' 'MinAnimate'; Rec 'HKCU:\Control Panel\Desktop' 'UserPreferencesMask' }
function Mouse($on){ foreach($n in 'MouseSpeed','MouseThreshold1','MouseThreshold2','MouseSensitivity'){ Rec 'HKCU:\Control Panel\Mouse' $n } }
function powercfg { Rec 'HKLM:\SYSTEM\CurrentControlSet\Control\Power' 'HibernateEnabled' }
function Try-Step($id,[scriptblock]$s){ $script:cur = $id; try { & $s } catch {} }
""");
            foreach (var t in TweakCatalog.All)
            {
                sb.AppendLine($"Try-Step '{t.Id}' {{ {t.Apply} }}");
                sb.AppendLine($"Try-Step '{t.Id}' {{ {t.Revert} }}");
            }
            sb.AppendLine("""
$cur = 'profile'
Rec 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' 'AppCaptureEnabled'
Rec 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled'
Rec 'HKCU:\System\GameConfigStore' 'GameDVR_FSEBehaviorMode'
Rec 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' 'HwSchMode'
Rec 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' 'VisualFXSetting'
RecSvc 'SysMain'
foreach($i in Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces'){ Rec $i.PSPath 'TcpAckFrequency'; Rec $i.PSPath 'TCPNoDelay' }
$scheme = $null
$line = (& "$env:windir\System32\powercfg.exe" /getactivescheme) -join ' '
if($line -match '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})'){ $scheme = $Matches[1] }
$result = [ordered]@{ reg = @($reg.Values); svc = @($svc.Values); scheme = $scheme }
$result | ConvertTo-Json -Depth 6 | Out-File -Encoding utf8 $outFile
""");
            return sb.ToString();
        }

        // ---------- putting it back ----------

        private static string Q(string s) => RestoreGuard.Q(s);

        private static string ValueText(RegRec r)
        {
            if (r.Value is not { } v) return "";
            return v.ValueKind switch
            {
                JsonValueKind.Array => string.Join("\n", v.EnumerateArray().Select(e => e.ToString())),
                JsonValueKind.Null => "",
                _ => v.ToString(),
            };
        }

        private static readonly HashSet<string> Kinds = new() { "DWord", "QWord", "String", "ExpandString", "MultiString", "Binary" };

        /// <summary>Which of the original settings are in use right now (so the dialogs can tick them), and which were changed by CyberOptix.</summary>
        public static RestoreOptions WhatChanged()
        {
            var s = AppSettings.Current;
            var o = Load();
            var pre = new HashSet<string>(o?.Preexisting ?? new List<string>());
            var touched = ExpandTouched(s.Touched);
            bool anyTweak = s.AppliedTweaks.Count > 0 || touched.Contains("profile");

            bool rotated = false;
            try
            {
                if (o != null)
                    foreach (var d in DisplayService.GetDisplays())
                        if (o.Displays.TryGetValue(d.Id, out var b) && !string.IsNullOrEmpty(d.DeviceName) && DisplayColorService.GetOrientation(d.DeviceName) != b.Orientation) rotated = true;
            }
            catch { }

            return new RestoreOptions
            {
                Tweaks = anyTweak,
                Services = anyTweak || RamCleaner.HasRestorePoint,
                DisplayColors = s.DisplayColors.Values.Any(c => !c.IsDefault),
                Rotation = rotated,
                Remaps = touched.Contains("remap"),
                Pointer = touched.Contains("pointer"),
                Scrolling = touched.Contains("scroll"),
                Cursors = touched.Contains("cursors"),
            };
        }

        private static HashSet<string> ExpandTouched(IEnumerable<string> touched)
        {
            var set = new HashSet<string>(touched);
            if (set.Remove("mouse")) { set.Add("pointer"); set.Add("scroll"); set.Add("cursors"); } // older versions used one tag
            return set;
        }

        private static string Category(RegRec r)
        {
            if (r.Path.Contains(@"\Control Panel\Cursors", StringComparison.OrdinalIgnoreCase) || r.Path.EndsWith(@"\Microsoft\Accessibility", StringComparison.OrdinalIgnoreCase)) return "cursors";
            if (r.Name == "WheelScrollLines") return "scroll";
            if (r.Name == "Scancode Map") return "remap";
            if (r.Path.EndsWith(@"\Control Panel\Mouse", StringComparison.OrdinalIgnoreCase)) return "pointer";
            return "";
        }

        /// <summary>
        /// Puts the chosen groups of settings back (registry, services and power plan through elevated PowerShell, then display colors,
        /// then the services stopped by "Free up resources"). Returns a short summary. Safe to call when nothing was changed.
        /// </summary>
        public static string RestoreAll(RestoreOptions? opt = null)
        {
            opt ??= new RestoreOptions();

            // NVIDIA vibrance and hue go back to the driver defaults. This needs no saved copy, so it runs first and on its own.
            string nvidia = "";
            if (opt.NvidiaDefaults)
            {
                try
                {
                    NvApi.EnsureInit();
                    if (NvApi.Available && NvApi.ResetAllDisplays())
                    {
                        nvidia = "NVIDIA digital vibrance and hue went back to the driver defaults. ";
                        foreach (var c in AppSettings.Current.DisplayColors.Values)
                        {
                            c.VibranceTouched = false; c.Vibrance = 50;
                            c.HueTouched = false; c.Hue = 0;
                        }
                        AppSettings.Save();
                    }
                }
                catch { }
            }

            WaitReady();
            var o = Load();
            if (o == null) return nvidia + "CyberOptix has no saved copy of your original settings yet, so nothing else was changed.";

            var s = AppSettings.Current;
            var preexisting = new HashSet<string>(o.Preexisting);
            var touched = ExpandTouched(s.Touched);
            var tweakIds = new HashSet<string>(s.AppliedTweaks.Where(id => !preexisting.Contains(id)));
            var notes = new List<string>();
            var done = new List<string>();

            // Glass Cursor swaps the pointer in memory; stop it first so the saved cursors can be applied.
            try { if ((opt.Cursors || opt.Pointer) && GlassCursorService.IsRunning) GlassCursorService.Stop(); } catch { }

            // 1. Registry, services, power plan.
            var script = BuildRestoreScript(o, opt, tweakIds, touched, s.AppliedTweaks.Where(preexisting.Contains).ToList());
            if (script != null)
            {
                try
                {
                    using var sf = ScriptFile.Create(script, "restore");
                    var psi = new ProcessStartInfo(SystemTools.PowerShell, $"-NoProfile -ExecutionPolicy Bypass -File \"{sf.Path}\"")
                    { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden, Verb = "runas" };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(120000);
                    var log = Path.Combine(Path.GetTempPath(), "cyberoptix_tweaks.log");
                    var failed = File.Exists(log) ? File.ReadAllLines(log).Where(l => l.Trim().Length > 0).ToList() : new List<string>();
                    if (failed.Count > 0) notes.Add($"{failed.Count} setting(s) could not be put back ({string.Join("; ", failed.Take(3))})");
                    done.Add("the Windows settings you picked");
                }
                catch
                {
                    return "Administrator permission was not given, so nothing was changed.";
                }
            }

            // 2. Display colors and rotation.
            try { if (RestoreDisplays(o, opt)) done.Add("display colors or rotation"); } catch (Exception ex) { notes.Add("display colors: " + ex.Message); }

            // 3. Services stopped by "Free up resources".
            try
            {
                if (opt.Services && RamCleaner.HasRestorePoint) { RamCleaner.Restore(); done.Add("services stopped by Free up resources"); }
            }
            catch { }

            // 4. Forget what was put back, so the app matches Windows again.
            if (opt.Tweaks && opt.Services) s.AppliedTweaks.Clear();
            if (opt.Tweaks) touched.Remove("profile");
            if (opt.Remaps) { touched.Remove("remap"); s.Remaps.Clear(); }
            if (opt.Pointer) touched.Remove("pointer");
            if (opt.Scrolling) touched.Remove("scroll");
            if (opt.Cursors) touched.Remove("cursors");
            s.Touched = touched.ToList();
            if (opt.DisplayColors) s.DisplayColors.Clear();
            AppSettings.Save();

            var summary = nvidia + (done.Count == 0 ? "Nothing else needed to be put back." : "Put back: " + string.Join(", ", done) + ".");
            if (opt.Remaps) summary += " Key remaps finish after restarting your PC.";
            if (notes.Count > 0) summary += " " + string.Join(" ", notes);
            return summary;
        }

        private static string? BuildRestoreScript(OriginalSettings o, RestoreOptions opt, HashSet<string> tweakIds, HashSet<string> touched, List<string> preexistingApplied)
        {
            bool profile = touched.Contains("profile");
            bool CatOn(string c) => c switch
            {
                "cursors" => opt.Cursors && touched.Contains("cursors"),
                "scroll" => opt.Scrolling && touched.Contains("scroll"),
                "pointer" => opt.Pointer && touched.Contains("pointer"),
                "remap" => opt.Remaps && touched.Contains("remap"),
                _ => false,
            };
            bool TweakTag(List<string> tags, bool allowProfile) => tags.Any(t => tweakIds.Contains(t) || (allowProfile && profile && t == "profile"));
            bool WantReg(RegRec r)
            {
                if (r.Exists && !Kinds.Contains(r.Kind)) return false;
                // The saved file is not trusted: only keys CyberOptix itself changes, with values of the right shape, go back.
                if (!RestoreGuard.RegistryAllowed(r.Path, r.Name)) return false;
                if (r.Exists && !RestoreGuard.ValueOk(r.Kind, ValueText(r))) return false;
                var c = Category(r);
                if (c != "") return CatOn(c) || (opt.Tweaks && TweakTag(r.Tags, true) && c == "pointer");
                return opt.Tweaks && TweakTag(r.Tags, true);
            }

            var sb = new StringBuilder(TweakCatalog.PreludeScript);
            sb.AppendLine("""
function RegSet($p,$n,$exists,$kind,$v){
  try {
    if($exists){
      if(!(Test-Path -LiteralPath $p)){ New-Item -Path $p -Force | Out-Null }
      switch($kind){
        'Binary' { $val = [Convert]::FromBase64String($v) }
        'DWord' { $val = [int]$v }
        'QWord' { $val = [long]$v }
        'MultiString' { $val = @($v -split "`n") }
        default { $val = [string]$v }
      }
      if($n -eq ''){ Set-ItemProperty -LiteralPath $p -Name '(default)' -Value $val }
      else { New-ItemProperty -LiteralPath $p -Name $n -Value $val -PropertyType $kind -Force | Out-Null }
    } elseif($n -ne '' -and (Test-Path -LiteralPath $p)) {
      Remove-ItemProperty -LiteralPath $p -Name $n -ErrorAction SilentlyContinue
    }
  } catch { Add-Content $log "$p $n : $($_.Exception.Message)" }
}
function RegSvc($n,$start,$delayed,$running){
  try {
    if(!(Get-Service $n -ErrorAction SilentlyContinue)){ return }
    $m = switch($start){ 2 { if($delayed){ 'delayed-auto' } else { 'auto' } } 3 { 'demand' } 4 { 'disabled' } default { '' } }
    if($m){ sc.exe config $n start= $m | Out-Null }
    if($running){ Start-Service $n -ErrorAction SilentlyContinue }
  } catch { Add-Content $log "service $n : $($_.Exception.Message)" }
}
""");
            int steps = 0;
            foreach (var r in o.Registry.Where(WantReg))
            {
                sb.AppendLine($"RegSet {Q(r.Path)} {Q(r.Name)} ${(r.Exists ? "true" : "false")} {Q(r.Kind)} {Q(ValueText(r))}");
                steps++;
            }
            if (opt.Services)
                foreach (var v in o.Services.Where(v => RestoreGuard.ServiceAllowed(v.Name) && v.Start is >= 2 and <= 4 && TweakTag(v.Tags, true)))
                {
                    sb.AppendLine($"RegSvc {Q(v.Name)} {v.Start} ${(v.Delayed ? "true" : "false")} ${(v.Running ? "true" : "false")}");
                    steps++;
                }

            if (opt.Tweaks && profile && RestoreGuard.IsGuid(o.PowerScheme))
            {
                sb.AppendLine($"Try-Step 'powerplan' {{ powercfg /setactive {o.PowerScheme} }}");
                steps++;
            }
            if (opt.Tweaks && tweakIds.Contains("hibernation"))
            {
                var hib = o.Registry.FirstOrDefault(r => r.Name == "HibernateEnabled");
                if (hib is { Exists: true })
                {
                    sb.AppendLine($"Try-Step 'hibernation' {{ powercfg -h {(ValueText(hib) == "1" ? "on" : "off")} }}");
                    steps++;
                }
            }
            if (opt.Tweaks && tweakIds.Contains("animations"))
            {
                var anim = o.Registry.FirstOrDefault(r => r.Name == "MinAnimate");
                int animOn = anim == null || !anim.Exists || ValueText(anim) != "0" ? 1 : 0;
                sb.AppendLine($"Try-Step 'animations' {{ [PCO.Native]::SystemParametersInfo(0x1043, 0, [IntPtr]{animOn}, 3) | Out-Null }}");
                steps++;
            }

            // Make the restored mouse values take effect right now.
            bool accel = opt.Tweaks && tweakIds.Contains("mouseaccel");
            if (CatOn("pointer") || accel)
            {
                sb.AppendLine("""
Try-Step 'pointer' {
  $m = Get-ItemProperty 'HKCU:\Control Panel\Mouse'
  if($m.MouseSensitivity){ [PCO.Native]::SystemParametersInfo(0x71, 0, [IntPtr][int]$m.MouseSensitivity, 3) | Out-Null }
  if($null -ne $m.MouseSpeed){ [PCO.Native]::SystemParametersInfo(4, 0, [int[]]@([int]$m.MouseThreshold1,[int]$m.MouseThreshold2,[int]$m.MouseSpeed), 3) | Out-Null }
}
""");
                steps++;
            }
            if (CatOn("scroll"))
            {
                sb.AppendLine("""
Try-Step 'scroll' {
  $m = Get-ItemProperty 'HKCU:\Control Panel\Mouse'
  $wl = if($m.WheelScrollLines){ [int]$m.WheelScrollLines } else { 3 }
  [PCO.Native]::SystemParametersInfo(0x69, [uint32]$wl, [IntPtr]::Zero, 3) | Out-Null
}
""");
                steps++;
            }
            if (CatOn("cursors"))
            {
                sb.AppendLine("Try-Step 'cursors' { [PCO.Native]::SystemParametersInfo(0x57, 0, [IntPtr]::Zero, 3) | Out-Null }");
                steps++;
            }

            // Tweaks that were already on before CyberOptix started remembering: Windows' normal setting is the best "original" available.
            if (opt.Tweaks && opt.Services)
                foreach (var id in preexistingApplied)
                {
                    var t = TweakCatalog.Find(id);
                    if (t == null) continue;
                    sb.AppendLine($"Try-Step '{t.Id}' {{ {t.Revert} }}");
                    steps++;
                }
            return steps == 0 ? null : sb.ToString();
        }

        private static bool RestoreDisplays(OriginalSettings o, RestoreOptions opt)
        {
            bool any = false;
            NvApi.EnsureInit();
            foreach (var d in DisplayService.GetDisplays())
            {
                if (string.IsNullOrEmpty(d.DeviceName) || !o.Displays.TryGetValue(d.Id, out var b)) continue;
                AppSettings.Current.DisplayColors.TryGetValue(d.Id, out var saved);

                if (opt.DisplayColors && saved is { RampTouched: true } && b.Ramp != null)
                {
                    var bytes = Convert.FromBase64String(b.Ramp);
                    var ramp = new ushort[bytes.Length / 2];
                    Buffer.BlockCopy(bytes, 0, ramp, 0, bytes.Length);
                    if (DisplayColorService.WriteRamp(d.DeviceName, ramp)) any = true;
                }
                if (opt.DisplayColors && NvApi.Available)
                {
                    if (saved is { VibranceTouched: true } && b.Vibrance is int v && NvApi.SetVibrance(d.DeviceName, v)) any = true;
                    if (saved is { HueTouched: true } && b.Hue is int h && NvApi.SetHue(d.DeviceName, h)) any = true;
                }
                if (opt.Rotation && DisplayColorService.GetOrientation(d.DeviceName) != b.Orientation)
                {
                    if (DisplayColorService.TryOrientation(d.DeviceName, b.Orientation) == null &&
                        DisplayColorService.CommitOrientation(d.DeviceName, b.Orientation) == null) any = true;
                }
            }
            return any;
        }
    }
}
