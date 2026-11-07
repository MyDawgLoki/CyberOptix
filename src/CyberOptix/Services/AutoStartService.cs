using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    /// <summary>
    /// Starts CyberOptix when the user signs in to Windows.
    /// CyberOptix needs administrator rights, and Windows will not start such a program from the usual Run entry without asking at every
    /// sign-in (or skips it). So this registers a scheduled task that runs at sign-in with the highest rights. That needs no prompt, and
    /// it is how other elevated tools start with Windows. The task only starts this program, with --startup, a few seconds after sign-in.
    /// </summary>
    public static class AutoStartService
    {
        /// <summary>The name of the scheduled task. Tests use their own name so they never touch the real one.</summary>
        public static string TaskName { get; set; } = "CyberOptix";

        /// <summary>What the task passes to CyberOptix, so it knows it was started by Windows and can start quietly.</summary>
        public const string StartupArgument = "--startup";

        private static string ExePath => Environment.ProcessPath ?? "";

        private static (int Code, string Output) Run(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo(SystemTools.Schtasks, arguments)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return (-1, "");
                string output = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } return (-1, output); }
                return (p.ExitCode, output);
            }
            catch
            {
                return (-1, "");
            }
        }

        /// <summary>The task as Windows wants it written: it runs for this user, at sign-in, hidden from nobody, with the highest rights.</summary>
        public static string BuildXml(string user, string exe)
        {
            string e(string text) => SecurityElement.Escape(text) ?? "";
            string dir = Path.GetDirectoryName(exe) ?? "";
            return
$@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Starts CyberOptix when you sign in to Windows. Switch it off in CyberOptix, under Settings, Startup.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{e(user)}</UserId>
      <Delay>PT10S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{e(user)}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{e(exe)}</Command>
      <Arguments>{StartupArgument}</Arguments>
      <WorkingDirectory>{e(dir)}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
        }

        /// <summary>True when the task exists. <paramref name="command"/> is the program it starts.</summary>
        public static bool IsRegistered(out string? command)
        {
            command = null;
            var (code, output) = Run($"/Query /TN \"{TaskName}\" /XML");
            if (code != 0) return false;
            var m = Regex.Match(output, "<Command>(.*?)</Command>", RegexOptions.Singleline);
            if (m.Success) command = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim().Trim('"');
            return true;
        }

        /// <summary>Creates (or replaces) the task so it starts the program that is running now.</summary>
        public static bool Enable()
        {
            var exe = ExePath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return false;
            var file = Path.Combine(Path.GetTempPath(), "cyberoptix-task-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                File.WriteAllText(file, BuildXml(WindowsIdentity.GetCurrent().Name, exe), Encoding.Unicode);   // Windows wants UTF-16
                return Run($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F").Code == 0;
            }
            catch { return false; }
            finally { try { File.Delete(file); } catch { } }
        }

        /// <summary>Removes the task. Fine if it was never there.</summary>
        public static bool Disable()
        {
            if (!IsRegistered(out _)) return true;
            return Run($"/Delete /TN \"{TaskName}\" /F").Code == 0;
        }

        private static bool SamePath(string? a, string? b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>Switches startup on or off, and says whether Windows now matches what was asked.</summary>
        public static bool Apply(bool on)
        {
            if (on) return Enable() && IsRegistered(out _);
            return Disable() && !IsRegistered(out _);
        }

        /// <summary>
        /// Makes Windows match the setting. If startup is on but the task is missing, or points at another copy of CyberOptix (it was moved or
        /// updated), it is fixed. If startup is off but a task is left over, it is removed. Cheap enough to run every time CyberOptix starts.
        /// </summary>
        public static void Sync()
        {
            try
            {
                bool want = AppSettings.Current.StartWithWindows;
                bool there = IsRegistered(out var command);
                if (want && (!there || !SamePath(command, ExePath))) Enable();
                else if (!want && there) Disable();
            }
            catch { }
        }

        /// <summary>
        /// Whether ordinary programs can change the folder CyberOptix is in. Because the task starts CyberOptix with administrator rights,
        /// a program that could replace its files could get those rights at the next sign-in. Program Files is safe; a folder in your
        /// profile or on another drive often is not. Null when it cannot be worked out.
        /// </summary>
        public static bool? FolderOpenToUsers() => FolderOpenToUsers(ExePath);

        public static bool? FolderOpenToUsers(string exePath)
        {
            try
            {
                var dir = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(dir)) return null;
                // Anyone an ordinary program can be running as: all users, and this user's own account (a normal, non-elevated program
                // started by the user runs as the user, so a folder only the user can write to is just as open to it).
                var users = new System.Collections.Generic.List<SecurityIdentifier>
                {
                    new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                    new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                };
                var me = WindowsIdentity.GetCurrent().User;
                if (me != null) users.Add(me);
                const FileSystemRights write = FileSystemRights.WriteData | FileSystemRights.CreateFiles | FileSystemRights.AppendData
                                               | FileSystemRights.CreateDirectories | FileSystemRights.WriteAttributes | FileSystemRights.Delete;
                var rules = new DirectoryInfo(dir).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));
                foreach (FileSystemAccessRule rule in rules)
                {
                    if (rule.AccessControlType != AccessControlType.Allow) continue;
                    if ((rule.FileSystemRights & write) == 0) continue;
                    if (users.Exists(u => u.Equals(rule.IdentityReference))) return true;
                }
                return false;
            }
            catch { return null; }
        }
    }
}
