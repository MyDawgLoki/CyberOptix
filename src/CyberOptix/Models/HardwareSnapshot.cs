using System.Collections.Generic;

namespace CyberOptix.Models
{
    public record GpuInfo(string Name, double? TempC, double? Load, double? ClockMHz,
                          double? VramUsedMB, double? VramTotalMB, double? FanRpm = null);

    /// <summary>One fan. <see cref="Controllable"/> is true only when the hardware lets software set its speed.</summary>
    public record FanInfo(string Id, string Name, double? Rpm, double? Percent, bool Controllable, double MinPercent, double MaxPercent);

    public record RamModule(string Slot, double CapacityGB, int ConfiguredMTs, int RatedMTs, string Type, string Maker);

    public class HardwareSnapshot
    {
        public string CpuName { get; init; } = "CPU";
        public double? CpuLoad { get; init; }
        public double? CpuClockMHz { get; init; }
        public double? CpuTempC { get; init; }
        public double? CpuPowerW { get; init; }
        public List<double> CoreLoads { get; init; } = new();

        public List<GpuInfo> Gpus { get; init; } = new();

        public double? MemUsedGB { get; init; }
        public double? MemTotalGB { get; init; }
        public double? MemLoad { get; init; }

        /// <summary>Motherboard and cooler fans only. Graphics-card fans are read-only and live in <see cref="GpuInfo.FanRpm"/>.</summary>
        public List<FanInfo> Fans { get; init; } = new();

        /// <summary>Recent usage, one sample per second, oldest first (0 to 100).</summary>
        public List<double> CpuHistory { get; init; } = new();
        public List<double> GpuHistory { get; init; } = new();
        public int PrimaryGpu { get; init; }
    }
}
