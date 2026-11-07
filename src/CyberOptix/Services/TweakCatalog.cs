using System.Collections.Generic;
using System.Linq;
using System.Text;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    /// <summary>One Windows tweak: its text, its hover hint, and the PowerShell that applies and undoes it.</summary>
    public record Tweak(string Id, string Group, string Title, string Short, string Why, string Safety, string Manual,
                        bool GamingDefault, string Apply, string Revert)
    {
        /// <summary>The full hover hint.</summary>
        public string Hint => $"Why it helps: {Why}\n\nSafety: {Safety}\n\nBy hand: {Manual}";
    }

    public static class TweakCatalog
    {
        public const string Appearance = "Appearance and responsiveness";
        public const string Privacy = "Privacy and telemetry";
        public const string Services = "Background services";
        public const string Startup = "Startup, power and input";

        public static readonly string[] Groups = { Appearance, Privacy, Services, Startup };

        // Widgets only exist on Windows 11; Windows 10 has News and interests instead.
        private static readonly HashSet<string> Windows11Only = new() { "widgets" };
        private static readonly HashSet<string> Windows10Only = new() { "feeds" };

        /// <summary>True when this tweak makes sense on the Windows this is running on.</summary>
        public static bool AppliesHere(Tweak t) => CyberOptix.Services.OsInfo.IsWindows11 ? !Windows10Only.Contains(t.Id) : !Windows11Only.Contains(t.Id);

        /// <summary>The tweaks to show and apply on this PC.</summary>
        public static IEnumerable<Tweak> Available => All.Where(AppliesHere);

        // Helpers available inside every script (see Prelude):
        //   R path name value [type]   set a registry value        D path name   delete one
        //   S name mode                set a service's start type (Disabled | Manual | Automatic | DelayedAuto) and stop/start it
        private static Tweak T(string id, string group, string title, string shortText, string why, string safety, string manual,
                               bool gaming, string apply, string revert) =>
            new(id, group, title, shortText, why, safety, manual, gaming, apply, revert);

        private const string Pers = @"HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string Cdm = @"HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        private const string Priv = @"HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy";
        private const string Pol = @"HKLM:\SOFTWARE\Policies\Microsoft\Windows";

        public static readonly Tweak[] All =
        {
            // ---------- Appearance and responsiveness ----------
            T("transparency", Appearance, "Turn off transparency effects", "Removes blur on the taskbar, Start menu and windows.",
              "Removes the blur/transparency processing on the taskbar, Start menu, and windows. Slightly lowers GPU load.",
              "Completely safe.",
              "Settings → Accessibility → Visual effects → turn off Transparency effects.",
              true, $"R '{Pers}' EnableTransparency 0", $"R '{Pers}' EnableTransparency 1"),

            T("animations", Appearance, "Turn off animation effects", "Disables window animations and transitions.",
              "Disables window animations and transitions. Makes the desktop feel snappier.",
              "Completely safe.",
              "Settings → Accessibility → Visual effects → turn off Animation effects.",
              true, "Anim 0", "Anim 1"),

            T("widgets", Appearance, "Turn off Widgets", "Hides the Widgets button and turns the feature off.",
              "Turns Widgets off by policy and hides its taskbar button, so its background processes stop.",
              "Completely safe.",
              @"Right-click the taskbar → Taskbar settings → turn off Widgets. Or registry: HKLM\SOFTWARE\Policies\Microsoft\Dsh → AllowNewsAndInterests = 0.",
              true, @"R 'HKLM:\SOFTWARE\Policies\Microsoft\Dsh' AllowNewsAndInterests 0; R 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' TaskbarDa 0",
              @"D 'HKLM:\SOFTWARE\Policies\Microsoft\Dsh' AllowNewsAndInterests; R 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' TaskbarDa 1"),

            T("feeds", Appearance, "Turn off News and interests", "Removes the news and weather button from the taskbar.",
              "Turns off the News and interests feed on the taskbar, which loads web content in the background.",
              "Completely safe.",
              "Right-click the taskbar → News and interests → Turn off.",
              true, @"R 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Feeds' ShellFeedsTaskbarViewMode 2; R 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds' EnableFeeds 0",
              @"R 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Feeds' ShellFeedsTaskbarViewMode 0; D 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds' EnableFeeds"),

            T("tips", Appearance, "Turn off tips and suggestions", "Removes tips, suggestions and promotional content.",
              "Removes extra background processes related to tips, suggestions, and promotional content.",
              "Completely safe.",
              "Settings → Personalization → Start → turn off “Show recommendations for tips, shortcuts, new apps, and more”. Also check Notifications → Additional settings.",
              true,
              $"foreach($n in 'SubscribedContent-338388Enabled','SubscribedContent-338389Enabled','SubscribedContent-310093Enabled','SubscribedContent-353694Enabled','SubscribedContent-353696Enabled','SoftLandingEnabled','SystemPaneSuggestionsEnabled'){{ R '{Cdm}' $n 0 }}; R 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced' Start_IrisRecommendations 0",
              $"foreach($n in 'SubscribedContent-338388Enabled','SubscribedContent-338389Enabled','SubscribedContent-310093Enabled','SubscribedContent-353694Enabled','SubscribedContent-353696Enabled','SoftLandingEnabled','SystemPaneSuggestionsEnabled'){{ R '{Cdm}' $n 1 }}; R 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced' Start_IrisRecommendations 1"),

            T("searchhighlights", Appearance, "Turn off search highlights", "Hides the highlights shown in the search box.",
              "Disables the search highlights/content that appears in the search box.",
              "Completely safe.",
              "Settings → Privacy & security → Search permissions → turn off “Show search highlights”.",
              true, @"R 'HKCU:\Software\Microsoft\Windows\CurrentVersion\SearchSettings' IsDynamicSearchBoxEnabled 0", @"R 'HKCU:\Software\Microsoft\Windows\CurrentVersion\SearchSettings' IsDynamicSearchBoxEnabled 1"),

            T("searchsuggest", Appearance, "Turn off search box suggestions", "Search box shows no web suggestions.",
              "Disables web-powered suggestions in the search box, reducing lag and network calls.",
              "Completely safe.",
              @"Registry: HKCU\Software\Policies\Microsoft\Windows\Explorer → DisableSearchBoxSuggestions = 1 (DWORD).",
              true, @"R 'HKCU:\Software\Policies\Microsoft\Windows\Explorer' DisableSearchBoxSuggestions 1", @"D 'HKCU:\Software\Policies\Microsoft\Windows\Explorer' DisableSearchBoxSuggestions"),

            T("websearch", Appearance, "Turn off web search in Start", "Start search stops asking Bing.",
              "Stops Start menu search from querying the web (Bing), reducing lag.",
              "Completely safe.",
              "Settings → Privacy & security → Search permissions → turn off the cloud content search options. Or use the registry method of the search box suggestions tweak.",
              true, @"R 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search' BingSearchEnabled 0; R 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search' CortanaConsent 0",
              @"R 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search' BingSearchEnabled 1; D 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search' CortanaConsent"),

            T("notifications", Appearance, "Turn off app notifications", "Hides toast pop-ups from apps.",
              "Reduces interruptions and associated background processes.",
              "Completely safe. You will not see any app pop-ups until you turn this back on.",
              "Settings → System → Notifications → turn off the notifications you don't need. Use Do Not Disturb when desired.",
              false, @"R 'HKCU:\Software\Microsoft\Windows\CurrentVersion\PushNotifications' ToastEnabled 0", @"R 'HKCU:\Software\Microsoft\Windows\CurrentVersion\PushNotifications' ToastEnabled 1"),

            // ---------- Privacy and telemetry ----------
            T("optionaldiag", Privacy, "Turn off optional diagnostic data", "Windows sends only the required diagnostic data.",
              "Limits the amount of diagnostic data Windows collects and sends.",
              "Safe. Required diagnostic data stays enabled.",
              "Settings → Privacy & security → Diagnostics & feedback → turn off “Send optional diagnostic data”.",
              true, $"R '{Pol}\\DataCollection' AllowTelemetry 1", $"D '{Pol}\\DataCollection' AllowTelemetry"),

            T("diagtrack", Privacy, "Disable Connected User Experiences and Telemetry", "Stops the DiagTrack service.",
              "Reduces background telemetry collection and network calls.",
              "Generally safe. Does not break Windows Update or core features for most users.",
              "services.msc → Connected User Experiences and Telemetry → Startup type = Disabled (or Manual). Or in PowerShell: sc stop DiagTrack & sc config DiagTrack start=disabled",
              true, "S DiagTrack Disabled", "S DiagTrack Automatic"),

            T("advertisingid", Privacy, "Turn off advertising ID", "Stops apps getting a personal ad ID.",
              "Stops Windows from generating and sharing an advertising ID with apps.",
              "Completely safe.",
              "Settings → Privacy & security → General → turn off “Let apps show me personalized ads by using my advertising ID”.",
              true, @"R 'HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo' Enabled 0", @"R 'HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo' Enabled 1"),

            T("tailored", Privacy, "Turn off tailored experiences", "No personalized tips and ads from diagnostic data.",
              "Stops Microsoft from using diagnostic data to offer personalized tips and ads.",
              "Completely safe.",
              "Settings → Privacy & security → Diagnostics & feedback → turn off “Tailored experiences”.",
              true, $"R '{Priv}' TailoredExperiencesWithDiagnosticDataEnabled 0", $"R '{Priv}' TailoredExperiencesWithDiagnosticDataEnabled 1"),

            T("activityhistory", Privacy, "Turn off activity history", "Stops storing and syncing your activity.",
              "Stops Windows from storing and potentially syncing activity history.",
              "Completely safe.",
              "Settings → Privacy & security → Activity history → uncheck “Store my activity history on this device” and “Send my activity history to Microsoft”.",
              true, $"R '{Pol}\\System' EnableActivityFeed 0; R '{Pol}\\System' PublishUserActivities 0; R '{Pol}\\System' UploadUserActivities 0",
              $"D '{Pol}\\System' EnableActivityFeed; D '{Pol}\\System' PublishUserActivities; D '{Pol}\\System' UploadUserActivities"),

            T("deliveryopt", Privacy, "Turn off Delivery Optimization (P2P updates)", "Updates come only from Microsoft, not from other PCs.",
              "Stops your PC from downloading updates from other devices and uploading updates to others. Reduces background network and disk activity.",
              "Safe. Updates still come from Microsoft.",
              "Settings → Windows Update → Advanced options → Delivery Optimization → turn off “Allow downloads from other devices”.",
              true, $"R '{Pol}\\DeliveryOptimization' DODownloadMode 0", $"D '{Pol}\\DeliveryOptimization' DODownloadMode"),

            T("backgroundapps", Privacy, "Stop apps running in the background", "Stops Microsoft Store apps running in the background.",
              "Stops Microsoft Store (UWP) apps from running in the background and waking the CPU when not in use. Classic desktop programs are not affected.",
              "Safe for apps you don't need constantly running. Apps that need background access (mail, chat) will only update while open.",
              "Settings → Apps → Installed apps → pick an app → Advanced options → Background apps permissions = Never.",
              false, $"R '{Pol}\\AppPrivacy' LetAppsRunInBackground 2", $"D '{Pol}\\AppPrivacy' LetAppsRunInBackground"),

            T("geolocation", Privacy, "Disable Geolocation service", "Stops the location service and blocks apps from using location.",
              "Stops the location service and denies location access to apps, so location background activity ends.",
              "Safe if you don't need location features (weather apps, Find My Device, and so on). The Weather widget here does not need it.",
              "services.msc → Geolocation Service → Startup type = Disabled (or Manual). Also disable it in Settings → Privacy & security → Location.",
              false, @"S lfsvc Disabled; R 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location' Value Deny String", @"S lfsvc Manual; R 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location' Value Allow String"),

            // ---------- Background services ----------
            T("xboxgamebar", Services, "Turn off Xbox Game Bar", "Stops the controller button and start-up tips from opening Game Bar.",
              "Stops the Xbox button on a controller from opening Game Bar and hides its start-up tips. Game Bar recording is switched off separately.",
              "Safe if you don't use Game Bar features.",
              "Settings → Gaming → Xbox Game Bar → turn off.",
              true, @"R 'HKCU:\Software\Microsoft\GameBar' UseNexusForGameBarEnabled 0; R 'HKCU:\Software\Microsoft\GameBar' ShowStartupPanel 0", @"R 'HKCU:\Software\Microsoft\GameBar' UseNexusForGameBarEnabled 1; R 'HKCU:\Software\Microsoft\GameBar' ShowStartupPanel 1"),

            T("captures", Services, "Turn off background recording", "Turns off the “Record what happened” clip buffer.",
              "Disables the always-on shadow recording buffer that consumes resources.",
              "Safe if you don't use Game Bar clip recording.",
              "Settings → Gaming → Captures → turn off “Record what happened”.",
              true, @"R 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' HistoricalCaptureEnabled 0", @"D 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' HistoricalCaptureEnabled"),

            T("xboxservices", Services, "Disable Xbox services", "Xbox Live Auth, Game Save, Networking and Accessory services.",
              "Removes Xbox-related background services if you don't use the Xbox app, Game Pass, or Xbox controllers.",
              "Safe if you don't use any Xbox ecosystem features. Some games or controllers may need them.",
              "services.msc → disable the Xbox services: Xbox Accessory Management Service, Xbox Live Auth Manager, Xbox Live Game Save, Xbox Live Networking Service.",
              false, "S XboxGipSvc Disabled; S XblAuthManager Disabled; S XblGameSave Disabled; S XboxNetApiSvc Disabled", "S XboxGipSvc Manual; S XblAuthManager Manual; S XblGameSave Manual; S XboxNetApiSvc Manual"),

            T("wsearch", Services, "Disable Windows Search indexing", "Stops the Windows Search indexing service.",
              "Stops continuous file indexing, which reduces disk activity.",
              "Safe if you don't rely on fast Windows Search. Search still works but is slower.",
              "services.msc → Windows Search → Startup type = Disabled (or Manual).",
              false, "S WSearch Disabled", "S WSearch DelayedAuto"),

            T("fax", Services, "Disable Fax", "Stops the legacy Fax service.",
              "Removes an unused legacy service.",
              "Completely safe for virtually everyone.",
              "services.msc → Fax → Startup type = Disabled.",
              true, "S Fax Disabled", "S Fax Manual"),

            T("remoteregistry", Services, "Disable Remote Registry", "Keeps the Remote Registry service switched off.",
              "Slightly reduces attack surface and minor overhead.",
              "Safe on a home or standalone PC. Do not disable if you manage the PC remotely via registry.",
              "services.msc → Remote Registry → Startup type = Disabled.",
              true, "S RemoteRegistry Disabled", "S RemoteRegistry Disabled"),

            T("touchkeyboard", Services, "Disable Touch Keyboard and Handwriting service", "Stops the touch keyboard and handwriting service.",
              "Removes processes related to the touch keyboard and handwriting (unused on most desktops).",
              "Safe on non-touch desktops.",
              "services.msc → Touch Keyboard and Handwriting Panel Service → Startup type = Disabled (or Manual).",
              false, "S TabletInputService Disabled", "S TabletInputService Manual"),

            T("spooler", Services, "Set Print Spooler to manual", "Starts the Print Spooler only when you print, not at every boot.",
              "Removes printer-related background polling.",
              "Safe only if you rarely print. This sets it to Manual (starts when needed) rather than fully disabling it. If printing stops working, turn this off and save.",
              "services.msc → Print Spooler → Startup type = Manual or Disabled.",
              false, "S Spooler Manual", "S Spooler Automatic"),

            T("maps", Services, "Disable Downloaded Maps Manager", "Stops the offline maps update service.",
              "Removes offline map update processes.",
              "Safe if you don't use offline maps.",
              "services.msc → Downloaded Maps Manager → Startup type = Disabled.",
              true, "S MapsBroker Disabled", "S MapsBroker DelayedAuto"),

            T("insider", Services, "Disable Windows Insider service", "Stops the Windows Insider service.",
              "Removes Insider-related background activity.",
              "Safe if you are not enrolled in the Windows Insider Program.",
              "services.msc → Windows Insider Service → Startup type = Disabled.",
              true, "S wisvc Disabled", "S wisvc Manual"),

            // ---------- Startup, power and input ----------
            T("onedrive", Startup, "Stop OneDrive starting at login", "OneDrive no longer starts when you sign in.",
              "Prevents OneDrive from launching and syncing at login if you don't need constant sync.",
              "Safe if you don't rely on real-time OneDrive sync. It stays installed and you can still open it yourself.",
              "Task Manager → Startup apps → disable OneDrive. Or OneDrive settings → uncheck “Start OneDrive automatically”.",
              false, "Startup OneDrive 0", "Startup OneDrive 1"),

            T("hibernation", Startup, "Turn off hibernation", "Frees several GB of disk space.",
              "Frees several GB of disk space (removes hiberfil.sys) and removes related overhead.",
              "Safe on a desktop that stays plugged in. You lose hibernate, and Fast Startup stops working.",
              "In an admin PowerShell or Command Prompt run: powercfg -h off",
              false, "powercfg -h off", "powercfg -h on"),

            T("faststartup", Startup, "Turn off Fast Startup", "Shutting down fully each time, so boots are more consistent.",
              "Can improve shutdown/boot consistency and avoid some hybrid sleep issues on certain systems.",
              "Generally safe. Boot may be slightly slower. Useful if you dual-boot or have shutdown-related issues.",
              @"Control Panel → Power Options → Choose what the power buttons do → Change settings that are currently unavailable → uncheck “Turn on fast startup”. Or registry: HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power → HiberbootEnabled = 0.",
              false, @"R 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' HiberbootEnabled 0", @"R 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' HiberbootEnabled 1"),

            T("mouseaccel", Startup, "Turn off mouse acceleration", "The pointer moves exactly as far as your hand does. Good for aiming.",
              "Provides more consistent mouse movement (important for gaming and aiming).",
              "Completely safe. It's a preference. Takes full effect after restarting your PC.",
              "Control Panel → Mouse → Pointer Options → uncheck “Enhance pointer precision”.",
              true, "Mouse 0", "Mouse 1"),
        };

        public static Tweak? Find(string id) => All.FirstOrDefault(t => t.Id == id);

        /// <summary>The helper functions every tweak script uses. Also used by the restore script.</summary>
        public const string PreludeScript = Prelude;

        private const string Prelude = @"
$ErrorActionPreference = 'Stop'
# PowerShell ships an alias 'r' (Invoke-History) that would win over the R function below, so remove it first.
Remove-Item Alias:r -Force -ErrorAction SilentlyContinue
$log = Join-Path $env:TEMP 'cyberoptix_tweaks.log'
Remove-Item $log -ErrorAction SilentlyContinue
function R($p,$n,$v,$t='DWord'){ if(!(Test-Path $p)){ New-Item -Path $p -Force | Out-Null }; New-ItemProperty -Path $p -Name $n -Value $v -PropertyType $t -Force | Out-Null }
function D($p,$n){ if(Test-Path $p){ Remove-ItemProperty -Path $p -Name $n -ErrorAction SilentlyContinue } }
function S($n,$mode){
  if(!(Get-Service $n -ErrorAction SilentlyContinue)){ return }
  if($mode -eq 'Disabled'){ Stop-Service $n -Force -ErrorAction SilentlyContinue; Set-Service $n -StartupType Disabled }
  elseif($mode -eq 'DelayedAuto'){ sc.exe config $n start= delayed-auto | Out-Null }
  else { Set-Service $n -StartupType $mode }
}
function Startup($name,$on){
  $p='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
  if(!(Test-Path $p)){ New-Item -Path $p -Force | Out-Null }
  if(!(Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $name -ErrorAction SilentlyContinue)){ return }
  $b = New-Object byte[] 12; $b[0] = $(if($on){2}else{3})
  New-ItemProperty -Path $p -Name $name -Value $b -PropertyType Binary -Force | Out-Null
}
Add-Type -Namespace PCO -Name Native -MemberDefinition '[DllImport(""user32.dll"")] public static extern bool SystemParametersInfo(uint a, uint b, IntPtr c, uint d); [DllImport(""user32.dll"")] public static extern bool SystemParametersInfo(uint a, uint b, int[] c, uint d);'
function Anim($on){
  R 'HKCU:\Control Panel\Desktop\WindowMetrics' MinAnimate $(if($on){'1'}else{'0'}) String
  [PCO.Native]::SystemParametersInfo(0x1043, 0, [IntPtr]$(if($on){1}else{0}), 3) | Out-Null
}
function Mouse($on){
  $v = if($on){ @('1','6','10') } else { @('0','0','0') }
  R 'HKCU:\Control Panel\Mouse' MouseSpeed $v[0] String
  R 'HKCU:\Control Panel\Mouse' MouseThreshold1 $v[1] String
  R 'HKCU:\Control Panel\Mouse' MouseThreshold2 $v[2] String
  [PCO.Native]::SystemParametersInfo(4, 0, [int[]]@([int]$v[1],[int]$v[2],[int]$v[0]), 3) | Out-Null
}
function Try-Step($id,[scriptblock]$s){ try { & $s } catch { Add-Content $log ""$id : $($_.Exception.Message)"" } }
";

        /// <summary>
        /// Builds one script for every catalog tweak: checked ones are applied; unchecked ones are undone only if this app applied them before.
        /// Returns null when there is nothing to do.
        /// </summary>
        public static string? BuildScript(SettingsProfile profile, ICollection<string> appliedBefore)
        {
            var sb = new StringBuilder(Prelude);
            int steps = 0;
            foreach (var t in All)
            {
                if (!AppliesHere(t)) continue;
                bool on = profile.IsOn(t);
                if (!on && !appliedBefore.Contains(t.Id)) continue;
                sb.Append("Try-Step '").Append(t.Id).Append("' { ").Append(on ? t.Apply : t.Revert).AppendLine(" }");
                steps++;
            }
            return steps == 0 ? null : sb.ToString();
        }
    }
}
