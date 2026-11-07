using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace CyberOptix.Services
{
    public class StartupEntry
    {
        public string Name { get; init; } = "";
        public string Command { get; init; } = "";
        public string Location { get; init; } = "";
        public string Publisher { get; init; } = "";
        public bool Enabled { get; set; }

        // Where Windows stores whether this entry is switched off (what Task Manager's Start-up tab writes).
        public RegistryHive ApprovedHive { get; init; }
        public string ApprovedSubKey { get; init; } = "";
        public string ValueName { get; init; } = "";

        public string Detail => string.IsNullOrEmpty(Publisher) ? Location : $"{Publisher}  ·  {Location}";
    }

    /// <summary>Lists the apps that start with Windows and switches them on or off the same way Task Manager does.</summary>
    public static class StartupService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        public static List<StartupEntry> List()
        {
            var list = new List<StartupEntry>();
            AddRegistry(list, RegistryHive.CurrentUser, RegistryView.Registry64, RunKey, "Run", "Registry, this user");
            AddRegistry(list, RegistryHive.LocalMachine, RegistryView.Registry64, RunKey, "Run", "Registry, all users");
            AddRegistry(list, RegistryHive.LocalMachine, RegistryView.Registry32, RunKey, "Run32", "Registry, all users (32-bit)");
            AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, "Startup folder, this user");
            AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, "Startup folder, all users");
            return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void AddRegistry(List<StartupEntry> list, RegistryHive hive, RegistryView view, string runSubKey, string approvedName, string location)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var run = root.OpenSubKey(runSubKey);
                if (run == null) return;

                foreach (var name in run.GetValueNames())
                {
                    var command = run.GetValue(name)?.ToString() ?? "";
                    list.Add(new StartupEntry
                    {
                        Name = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name,
                        Command = command,
                        Location = location,
                        Publisher = Publisher(command),
                        ApprovedHive = hive,
                        ApprovedSubKey = Approved + approvedName,
                        ValueName = name,
                        Enabled = ReadEnabled(hive, Approved + approvedName, name),
                    });
                }
            }
            catch
            {
                // A key we cannot read simply contributes nothing.
            }
        }

        private static void AddFolder(List<StartupEntry> list, string folder, RegistryHive hive, string location)
        {
            try
            {
                if (!Directory.Exists(folder)) return;
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new StartupEntry
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Command = file,
                        Location = location,
                        Publisher = "",
                        ApprovedHive = hive,
                        ApprovedSubKey = Approved + "StartupFolder",
                        ValueName = fileName,
                        Enabled = ReadEnabled(hive, Approved + "StartupFolder", fileName),
                    });
                }
            }
            catch
            {
            }
        }

        /// <summary>Windows marks an entry disabled by setting the lowest bit of the first byte (02 = on, 03 = off).</summary>
        private static bool ReadEnabled(RegistryHive hive, string subKey, string valueName)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = root.OpenSubKey(subKey);
                return key?.GetValue(valueName) is not byte[] data || data.Length == 0 || (data[0] & 1) == 0;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>Returns null on success, or a short reason it failed. Takes effect the next time you sign in.</summary>
        public static string? SetEnabled(StartupEntry entry, bool enabled)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(entry.ApprovedHive, RegistryView.Registry64);
                using var key = root.CreateSubKey(entry.ApprovedSubKey, true);

                var data = new byte[12];
                if (enabled) data[0] = 2;
                else
                {
                    data[0] = 3;
                    BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
                }
                key.SetValue(entry.ValueName, data, RegistryValueKind.Binary);
                entry.Enabled = enabled;
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static string Publisher(string command)
        {
            try
            {
                var path = ExePath(command);
                if (path == null || !File.Exists(path)) return "";
                var info = FileVersionInfo.GetVersionInfo(path);
                return (info.CompanyName ?? info.ProductName ?? "").Trim();
            }
            catch
            {
                return "";
            }
        }

        private static string? ExePath(string command)
        {
            command = Environment.ExpandEnvironmentVariables(command.Trim());
            if (command.StartsWith('"'))
            {
                int end = command.IndexOf('"', 1);
                return end > 1 ? command[1..end] : null;
            }
            int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe > 0 ? command[..(exe + 4)] : null;
        }
    }
}
