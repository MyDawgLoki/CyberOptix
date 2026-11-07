using System;
using System.Collections.Generic;
using System.Management;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    /// <summary>Installed memory modules and their speeds, read once from WMI. These are configured speeds, not live bandwidth.</summary>
    public static class RamInfoService
    {
        public static List<RamModule> Read()
        {
            var list = new List<RamModule>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT BankLabel, DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, Manufacturer FROM Win32_PhysicalMemory");
                foreach (ManagementObject m in searcher.Get())
                {
                    double gb = Convert.ToDouble(m["Capacity"] ?? 0) / (1024.0 * 1024 * 1024);
                    int rated = Convert.ToInt32(m["Speed"] ?? 0);
                    int configured = Convert.ToInt32(m["ConfiguredClockSpeed"] ?? 0);
                    var type = Convert.ToInt32(m["SMBIOSMemoryType"] ?? 0) switch
                    {
                        20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 34 => "DDR5", 35 => "LPDDR5", 30 => "LPDDR4", _ => "RAM",
                    };
                    list.Add(new RamModule(
                        (m["DeviceLocator"] as string)?.Trim() ?? (m["BankLabel"] as string)?.Trim() ?? "Slot",
                        Math.Round(gb), configured == 0 ? rated : configured, rated, type,
                        (m["Manufacturer"] as string)?.Trim() ?? ""));
                }
            }
            catch
            {
                // WMI can be unavailable or blocked; the page shows "not available".
            }
            return list;
        }
    }
}
