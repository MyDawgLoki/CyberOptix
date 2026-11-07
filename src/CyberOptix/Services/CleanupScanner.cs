using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CyberOptix.Services
{
    /// <summary>
    /// Finds what can be stopped to free memory on THIS PC, instead of shipping a fixed list.
    /// Third-party software is discovered from running services and processes (grouped by the program they belong to);
    /// Windows and Microsoft items come from a short, hand-checked catalogue and only appear when they are present and running.
    /// Anything security, driver, audio, anti-cheat or shell related is never listed.
    /// </summary>
    public static class CleanupScanner
    {
        private record MsEntry(string Id, string Name, string Short, string Hint, string[] Services, string[] Processes, bool Caution = false);

        // Hand-checked Windows and Microsoft items that are safe to stop for a gaming session. Each is listed on its own.
        private static readonly MsEntry[] MicrosoftCatalogue =
        {
            new("telemetry", "Connected User Experiences and Telemetry", "Sends diagnostic data to Microsoft.",
                "Stops the DiagTrack service. Windows keeps working normally; it only stops collecting and sending usage and diagnostic data.", new[] { "DiagTrack" }, Array.Empty<string>()),
            new("search", "Windows Search indexing", "Builds the file index behind Start and Explorer search.",
                "Stops Windows Search. Search still works, only slower, and the index is not kept up to date until it is restored.", new[] { "WSearch" }, Array.Empty<string>()),
            new("diagnostics", "Diagnostic Policy Service", "Troubleshooting and problem detection.",
                "Stops the Diagnostic Policy Service and its helpers. Windows troubleshooters will not run until it is restored.", new[] { "DPS", "WdiServiceHost", "WdiSystemHost" }, Array.Empty<string>()),
            new("compat", "Program Compatibility Assistant", "Pops up compatibility prompts for older apps.",
                "Stops the Program Compatibility Assistant, which watches apps for compatibility problems.", new[] { "PcaSvc" }, Array.Empty<string>()),
            new("inventory", "Inventory and Compatibility Appraisal", "Scans the PC for app compatibility data.",
                "Stops the inventory service that gathers installed-app information for Microsoft.", new[] { "InventorySvc" }, Array.Empty<string>()),
            new("errorreporting", "Windows Error Reporting", "Sends crash reports to Microsoft.",
                "Stops the Windows Error Reporting service. Crashes are not reported until it is restored.", new[] { "WerSvc" }, Array.Empty<string>()),
            new("deliveryopt", "Delivery Optimization", "Shares Windows update downloads with other PCs.",
                "Stops Delivery Optimization. Windows updates still download, directly from Microsoft.", new[] { "DoSvc" }, Array.Empty<string>()),
            new("maps", "Downloaded Maps Manager", "Keeps offline maps up to date.",
                "Stops the offline maps service. Only matters if you use the Windows Maps app offline.", new[] { "MapsBroker" }, Array.Empty<string>()),
            new("geolocation", "Geolocation Service", "Tracks this PC's location for apps.",
                "Stops the location service. Apps that ask for your location get none until it is restored.", new[] { "lfsvc" }, Array.Empty<string>()),
            new("gaming", "Microsoft Gaming Services", "Needed by the Xbox app and Microsoft Store games.",
                "Stops Microsoft Gaming Services. The Xbox app and games from the Microsoft Store will not launch until it is restored.",
                new[] { "GamingServices", "GamingServicesNet" }, Array.Empty<string>(), Caution: true),
            new("xbox", "Xbox Live services", "Xbox sign-in, cloud saves and networking.",
                "Stops the Xbox Live auth, game save and networking services. Xbox features and cloud saves for Store games stop until restored.",
                new[] { "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc" }, Array.Empty<string>(), Caution: true),
            new("spooler", "Print Spooler", "Lets you print.",
                "Stops the Print Spooler. Nothing can print until it is restored. Safe if you are not printing.", new[] { "Spooler" }, Array.Empty<string>(), Caution: true),
            new("fax", "Fax", "Legacy fax support.", "Stops the Windows Fax service, which almost nobody uses.", new[] { "Fax" }, Array.Empty<string>()),
            new("retaildemo", "Retail Demo Service", "Store demo mode.", "Stops the retail demo service, which is only used on shop display PCs.", new[] { "RetailDemo" }, Array.Empty<string>()),
            new("insider", "Windows Insider Service", "Supports Windows Insider builds.", "Stops the Insider service. Only matters if you are in the Windows Insider program.", new[] { "wisvc" }, Array.Empty<string>()),
            new("officeupdate", "Office updater (Click-to-Run)", "Installs Office updates in the background.",
                "Stops the Office Click-to-Run service. Office apps keep working; updates wait until it is restored.", new[] { "ClickToRunSvc" }, Array.Empty<string>()),
            new("edgeupdate", "Microsoft Edge updater", "Keeps Edge up to date.",
                "Stops the Edge update services. Edge keeps working and updates itself later.", new[] { "edgeupdate", "edgeupdatem", "MicrosoftEdgeElevationService" }, Array.Empty<string>()),
            new("onedrive", "OneDrive", "Syncs your OneDrive folders.",
                "Closes OneDrive. Files stop syncing until you open it again.", Array.Empty<string>(), new[] { "OneDrive" }),
            new("teams", "Microsoft Teams", "Chat and meetings.",
                "Closes Microsoft Teams. You will not get messages or calls until you open it again.", Array.Empty<string>(), new[] { "ms-teams", "Teams", "msteams" }),
            new("phonelink", "Phone Link", "Connects your phone to this PC.",
                "Closes Phone Link. Notifications and messages from your phone stop until you open it again.", Array.Empty<string>(), new[] { "PhoneExperienceHost", "YourPhone", "CrossDeviceService" }),
            new("widgets", "Windows Widgets", "The news and weather panel.",
                "Closes the Widgets panel and its helper. It starts again the next time you open it.", Array.Empty<string>(), new[] { "Widgets", "WidgetService", "WidgetBoard" }),
            new("copilot", "Copilot", "Microsoft's AI assistant.", "Closes Copilot. It starts again when you open it.", Array.Empty<string>(), new[] { "Copilot", "M365Copilot" }),
            new("gamebar", "Xbox Game Bar", "Game overlay and capture.",
                "Closes the Game Bar helpers. The overlay and Win+G capture return when you next use them.", Array.Empty<string>(), new[] { "GameBar", "GameBarFTServer", "GameBarPresenceWriter" }),
            new("edge", "Microsoft Edge", "Closes every Edge window.",
                "Closes all Microsoft Edge windows and its background tasks, including any tabs with unsaved work.", Array.Empty<string>(), new[] { "msedge" }, Caution: true),
        };

        // Never listed: security software, drivers, audio, anti-cheat, the shell, and the tools this app and developers run on.
        private static readonly Regex ProtectedName = new(
            "^(CyberOptix|claude|cowork.*|codex.*|Code|node|python|pythonw|git|dotnet|powershell|pwsh|cmd|conhost|OpenConsole|WindowsTerminal|devenv|explorer|dwm|csrss|winlogon|lsass|services|svchost|sihost|ctfmon|RuntimeBroker|" +
            "EPSecurityService|EPIntegrationService|EPUpdateService|EPProtectedService|nvcontainer|NVDisplay\\.Container|nvspcap64|NVIDIA.*|Audiosrv|RtkAudUService.*|RtkNGUI.*|RAVCpl.*|" +
            "EasyAntiCheat.*|BEService|BEDaisy|vgc|vgtray|FACEIT.*|PnkBstr.*|Steam|steamwebhelper|steamservice|EpicGamesLauncher|EpicWebHelper)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ProtectedCompany = new(
            "Microsoft|NVIDIA|Advanced Micro Devices|\\bAMD\\b|Intel|Realtek|Dolby|Waves Audio|Synaptics|Elan|Qualcomm|MediaTek|Broadcom|Marvell|" +
            "Avast|AVG|McAfee|Norton|Symantec|Gen Digital|Kaspersky|Bitdefender|Malwarebytes|ESET|CrowdStrike|Sophos|Trend Micro|Webroot|Cylance|SentinelOne|Carbon Black|" +
            "Easy Anti-Cheat|BattlEye|Riot Games|FACEIT|Valve|Epic Games|Anthropic|GitHub|Python Software Foundation|OpenJS",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Listed, but flagged as a caution and never ticked for you.
        private static readonly Regex CautionName = new(
            "vpn|wireguard|openvpn|tailscale|zerotier|teamviewer|anydesk|rustdesk|splashtop|parsec|remote|screenconnect|connectwise|datto|ninja|tunnel|cloudflare|" +
            "1password|bitwarden|keepass|lastpass|dashlane|backup|sync|dropbox|onedrive|googledrive|icloud|obs|streamlabs|spotify|chrome|firefox|opera|brave|vivaldi",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Services and processes this scanner will never stop, shared with the script that actually does the stopping.</summary>
        public const string ProtectedServicesPattern =
            "^(EPSecurityService|EPIntegrationService|EPUpdateService|NVDisplay\\.ContainerLocalSystem|NvContainerLocalSystem|Audiosrv|AudioEndpointBuilder|Winmgmt|BFE|mpssvc|WinDefend|Sense|SecurityHealthService|wscsvc|DcomLaunch|Power|PlugPlay|RpcSs|RpcEptMapper|EventLog|LSM|SamSs|ProfSvc|Schedule|CryptSvc|Dhcp|Dnscache|nsi|NlaSvc|LanmanWorkstation|LanmanServer|BrokerInfrastructure|StateRepository|TrustedInstaller|EasyAntiCheat.*|BEService|vgc|vgk)$";
        public const string ProtectedProcessesPattern =
            "^(claude|cowork.*|codex.*|Code|node|python|pythonw|git|dotnet|powershell|pwsh|cmd|conhost|OpenConsole|WindowsTerminal|devenv|explorer|dwm|csrss|winlogon|lsass|services|svchost|sihost|ctfmon|RuntimeBroker|EPSecurityService|nvcontainer|NVDisplay\\.Container|CyberOptix|EasyAntiCheat.*|BEService|vgc)$";

        // Install folders that belong to drivers, security software and Windows itself, even when the file carries no version info.
        private static readonly Regex ProtectedPath = new(
            @"\\(NVIDIA Corporation|NVIDIA|AMD|Intel|Realtek|Dolby|Waves|Windows Defender|Windows Security|Microsoft Shared|Common Files\\Microsoft Shared)\\|\\WindowsApps\\Microsoft[^\\]*\\",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Dictionary<string, (string Company, string Product, string Description)> VersionCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Scans this PC. Takes a second or two, so call it off the UI thread.</summary>
        public static List<CleanTarget> Scan()
        {
            var running = ProcessSnapshot();
            var runningNames = new HashSet<string>(running.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            var services = RunningServices();
            var serviceNames = new HashSet<string>(services.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
            var result = new List<CleanTarget>();
            var never = new HashSet<string>(CyberOptix.Models.AppSettings.Current.NeverKill, StringComparer.OrdinalIgnoreCase);

            // ----- Windows and Microsoft: only what is actually present and running -----
            var claimedProcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in MicrosoftCatalogue)
            {
                var svc = e.Services.Where(serviceNames.Contains).ToArray();
                var procs = e.Processes.Where(runningNames.Contains).ToArray();
                if (svc.Length == 0 && procs.Length == 0) continue;
                if (e.Services.Concat(e.Processes).Any(never.Contains)) continue; // the user marked it never-kill
                foreach (var p in procs) claimedProcs.Add(p);
                double mem = running.Where(p => procs.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).Sum(p => p.MemMB)
                           + services.Where(s => svc.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).Select(s => s.Pid).Distinct()
                                     .Sum(pid => running.Where(p => p.Pid == pid && !procs.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).Sum(p => p.MemMB));
                result.Add(new CleanTarget("ms:" + e.Id, RamCleaner.Microsoft, e.Name, Describe(e.Short, e.Caution), e.Hint, svc, procs, mem, e.Caution));
            }

            // ----- Third-party: discovered, grouped by the program they belong to -----
            var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
            Group Get(string key, string company, string product, string fallback)
            {
                if (!groups.TryGetValue(key, out var g))
                    groups[key] = g = new Group { Company = company, Product = product, Fallback = fallback };
                return g;
            }

            foreach (var s in services)
            {
                if (s.Path.Length == 0 || IsWindowsPath(s.Path)) continue;
                var (company, product, desc) = Version(s.Path);
                if (IsProtected(s.Name, company, product, s.Path)) continue;
                var key = KeyFor(company, product, s.Path);
                var g = Get(key, company, product, Path.GetFileNameWithoutExtension(s.Path));
                if (never.Contains(s.Name) || never.Contains(Path.GetFileNameWithoutExtension(s.Path))) { g.Blocked = true; continue; }
                g.Services.Add(s.Name);
                g.Display.Add(string.IsNullOrWhiteSpace(s.Display) ? desc : s.Display);
                if (s.Pid > 0) g.Pids.Add(s.Pid);
            }

            foreach (var p in running)
            {
                if (p.Path.Length == 0 || IsWindowsPath(p.Path) || claimedProcs.Contains(p.Name)) continue;
                var (company, product, desc) = Version(p.Path);
                if (IsProtected(p.Name, company, product, p.Path)) continue;
                var key = KeyFor(company, product, p.Path);
                var g = Get(key, company, product, p.Name);
                if (never.Contains(p.Name)) { g.Blocked = true; continue; }
                if (!g.Processes.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) g.Processes.Add(p.Name);
                g.Pids.Add(p.Pid);
                if (p.HasWindow) g.HasWindow = true;
                if (g.Display.Count == 0 && desc.Length > 0) g.Display.Add(desc);
            }

            foreach (var kv in groups)
            {
                var g = kv.Value;
                if (g.Blocked) continue; // one part of this program is never-kill, so none of it is offered
                var pids = new HashSet<int>(g.Pids);
                double mem = running.Where(p => pids.Contains(p.Pid)).Sum(p => p.MemMB);
                if (mem < 8 && g.Services.Count == 0) continue; // ignore tiny leftovers that would free nothing
                string name = PickName(g);
                bool caution = g.HasWindow || CautionName.IsMatch(name) || CautionName.IsMatch(g.Company) || g.Services.Any(CautionName.IsMatch);
                string kind = g.Services.Count > 0 && g.Processes.Count > 0 ? "Background service and app"
                            : g.Services.Count > 0 ? "Background service"
                            : g.HasWindow ? "Open app" : "Background app";
                string by = g.Company.Length > 0 && !name.Contains(g.Company, StringComparison.OrdinalIgnoreCase) ? " by " + g.Company : "";
                var parts = new List<string>();
                if (g.Services.Count > 0) parts.Add("stops " + string.Join(", ", g.Services.Take(4)) + (g.Services.Count > 4 ? " and more" : ""));
                if (g.Processes.Count > 0) parts.Add("closes " + string.Join(", ", g.Processes.Take(4)) + (g.Processes.Count > 4 ? " and more" : ""));
                string hint = char.ToUpper(parts[0][0]) + string.Join("; ", parts).Substring(1) + ". " +
                    (g.HasWindow ? "It has a window open, so anything unsaved in it is lost. " : "") +
                    (caution && !g.HasWindow ? "This kind of software is often needed (VPN, remote access, sync, passwords, browsers), so check you do not need it right now. " : "") +
                    "Nothing is uninstalled; start it again whenever you want.";
                result.Add(new CleanTarget("tp:" + Slug(kv.Key), RamCleaner.ThirdParty, name,
                    Describe(kind + by, caution), hint, g.Services.ToArray(), g.Processes.ToArray(), mem, caution));
            }

            return result
                .OrderBy(t => t.Group == RamCleaner.ThirdParty ? 0 : 1)
                .ThenByDescending(t => t.MemMB)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>One running program the user can mark as never-kill.</summary>
        public record Candidate(string Name, string Description, double MemMB, bool HasWindow);

        /// <summary>Running programs that could appear in a scan (not Windows itself, and not already always-protected), one per process name.</summary>
        public static List<Candidate> Candidates()
        {
            var list = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in ProcessSnapshot())
            {
                if (p.Path.Length == 0 || IsWindowsPath(p.Path)) continue;
                var (company, product, desc) = Version(p.Path);
                if (IsProtected(p.Name, company, product, p.Path)) continue;
                string what = product.Length > 0 && !product.Equals(p.Name, StringComparison.OrdinalIgnoreCase) ? product : desc;
                if (company.Length > 0 && what.Length > 0 && !what.Contains(company, StringComparison.OrdinalIgnoreCase)) what += " by " + company;
                list[p.Name] = list.TryGetValue(p.Name, out var old)
                    ? old with { MemMB = old.MemMB + p.MemMB, HasWindow = old.HasWindow || p.HasWindow }
                    : new Candidate(p.Name, what, p.MemMB, p.HasWindow);
            }
            return list.Values.OrderByDescending(x => x.MemMB).ToList();
        }

        // ---------- helpers ----------

        private class Group
        {
            public string Company = "", Product = "", Fallback = "";
            public List<string> Services = new(), Processes = new(), Display = new();
            public HashSet<int> Pids = new();
            public bool HasWindow, Blocked;
        }

        private record Proc(int Pid, string Name, string Path, double MemMB, bool HasWindow);
        private record Svc(string Name, string Display, string Path, int Pid);

        private static string PickName(Group g)
        {
            bool filenameLike(string t) => Regex.IsMatch(t, @"\.(exe|dll|app)$", RegexOptions.IgnoreCase);
            if (g.Product.Length > 0 && !filenameLike(g.Product) && !g.Product.Equals(g.Company, StringComparison.OrdinalIgnoreCase)) return g.Product;
            var d = g.Display.FirstOrDefault(x => x.Length > 0 && !filenameLike(x));
            if (d != null) return d;
            if (g.Product.Length > 0 && !filenameLike(g.Product)) return g.Product;
            if (g.Company.Length > 0)
                return g.Fallback.Length > 0 && !g.Company.Contains(g.Fallback, StringComparison.OrdinalIgnoreCase) ? g.Company + " (" + g.Fallback + ")" : g.Company;
            return g.Fallback;
        }

        private static string Describe(string what, bool caution) =>
            (caution ? "Caution. " : "") + what;

        private static bool IsProtected(string name, string company, string product, string path = "") =>
            ProtectedName.IsMatch(name) || (path.Length > 0 && (ProtectedName.IsMatch(Path.GetFileNameWithoutExtension(path)) || ProtectedPath.IsMatch(path))) || (company.Length > 0 && ProtectedCompany.IsMatch(company)) || (product.Length > 0 && ProtectedCompany.IsMatch(product));

        private static string KeyFor(string company, string product, string path) =>
            company.Length > 0 && product.Length > 0 ? company + "|" + product : Path.GetFileNameWithoutExtension(path);

        private static string Slug(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

        private static bool IsWindowsPath(string path)
        {
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return path.StartsWith(win, StringComparison.OrdinalIgnoreCase);
        }

        private static (string Company, string Product, string Description) Version(string path)
        {
            if (VersionCache.TryGetValue(path, out var hit)) return hit;
            (string, string, string) v = ("", "", "");
            try
            {
                var fvi = FileVersionInfo.GetVersionInfo(path);
                v = ((fvi.CompanyName ?? "").Trim(), (fvi.ProductName ?? "").Trim(), (fvi.FileDescription ?? "").Trim());
            }
            catch { }
            VersionCache[path] = v;
            return v;
        }

        private static List<Proc> ProcessSnapshot()
        {
            var list = new List<Proc>();
            int me = Environment.ProcessId;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == me || p.Id <= 4) continue;
                    string path = "";
                    try { path = p.MainModule?.FileName ?? ""; } catch { }
                    list.Add(new Proc(p.Id, p.ProcessName, path, p.WorkingSet64 / 1048576.0, p.MainWindowHandle != IntPtr.Zero));
                }
                catch { }
                finally { p.Dispose(); }
            }
            return list;
        }

        private static List<Svc> RunningServices()
        {
            var list = new List<Svc>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = SystemTools.PowerShell,
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-CimInstance Win32_Service | Where-Object { $_.State -eq 'Running' } | Select-Object Name,DisplayName,PathName,ProcessId | ConvertTo-Json -Compress\"",
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return list;
                string json = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(20000);
                if (string.IsNullOrWhiteSpace(json)) return list;
                using var doc = JsonDocument.Parse(json);
                var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToArray() : new[] { doc.RootElement };
                foreach (var e in items)
                {
                    string name = e.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                    string disp = e.TryGetProperty("DisplayName", out var d) ? d.GetString() ?? "" : "";
                    string pathName = e.TryGetProperty("PathName", out var pn) ? pn.GetString() ?? "" : "";
                    int pid = e.TryGetProperty("ProcessId", out var pi) && pi.ValueKind == JsonValueKind.Number ? pi.GetInt32() : 0;
                    if (name.Length > 0) list.Add(new Svc(name, disp, ExePath(pathName), pid));
                }
            }
            catch { }
            return list;
        }

        /// <summary>Pulls the program path out of a service command line such as "C:\Program Files\X\x.exe" -k arg.</summary>
        private static string ExePath(string commandLine)
        {
            commandLine = commandLine.Trim();
            if (commandLine.Length == 0) return "";
            if (commandLine[0] == '"')
            {
                int end = commandLine.IndexOf('"', 1);
                return end > 1 ? commandLine.Substring(1, end - 1) : "";
            }
            int exe = commandLine.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe > 0 ? commandLine.Substring(0, exe + 4) : commandLine;
        }
    }
}
