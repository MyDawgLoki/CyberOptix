using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    /// <summary>
    /// Polls CPU, GPU, memory and fan sensors once a second while at least one page is watching.
    /// Fan speeds set by software are always returned to automatic when the app exits.
    /// </summary>
    public sealed class HardwareService
    {
        public static HardwareService Instance { get; } = new();

        private Computer? _computer;
        private CancellationTokenSource? _cts;
        private int _watchers;
        private readonly object _lock = new();
        private readonly Dictionary<string, ISensor> _controls = new();
        private readonly HashSet<string> _manual = new();
        private readonly List<double> _cpuHist = new(), _gpuHist = new();

        /// <summary>Raised on a background thread; marshal to the UI thread before touching controls.</summary>
        public event Action<HardwareSnapshot>? Updated;

        /// <summary>Set when the sensors could not be opened at all.</summary>
        public string? Problem { get; private set; }

        public void Acquire()
        {
            if (Interlocked.Increment(ref _watchers) == 1) StartLoop();
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _watchers) == 0) StopLoop();
        }

        private void StartLoop()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            Task.Run(async () =>
            {
                try
                {
                    if (_computer == null) Open();
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var snapshot = Poll();
                            if (snapshot != null)
                            {
                                Problem = null;
                                Updated?.Invoke(snapshot);
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            // One bad reading must not stop the monitor; report it and try again next second.
                            Problem = "A sensor read failed: " + ex.Message;
                        }
                        await Task.Delay(1000, token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Problem = ex.Message; }
            });
        }

        private void StopLoop() => _cts?.Cancel();

        /// <summary>Opens the sensors again, so a driver installed while CyberOptix is running (PawnIO) is picked up without restarting the app.</summary>
        public void ReloadSensors()
        {
            var old = _computer;
            _computer = null;
            Open();
            if (old != null)
                Task.Run(async () => { await Task.Delay(2500); try { old.Close(); } catch { } });   // after any reading in progress is done with it
        }

        private void Open()
        {
            try
            {
                var pc = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    // The library's memory module probes the SMBus and can hang forever without a driver.
                    // Memory usage comes from Windows directly (see ReadMemory) and RAM speed from WMI.
                    IsMemoryEnabled = false,
                    IsMotherboardEnabled = true,
                    IsControllerEnabled = true,
                    IsStorageEnabled = false,
                    IsNetworkEnabled = false,
                    IsPsuEnabled = false,
                    IsBatteryEnabled = false,
                };
                pc.Open();
                _computer = pc;
            }
            catch (Exception ex)
            {
                Problem = "Could not open hardware sensors: " + ex.Message;
            }
        }

        private static IEnumerable<IHardware> Flatten(IEnumerable<IHardware> roots)
        {
            foreach (var hw in roots)
            {
                yield return hw;
                foreach (var sub in Flatten(hw.SubHardware)) yield return sub;
            }
        }

        private HardwareSnapshot? Poll()
        {
            var pc = _computer;
            if (pc == null) return null;

            var all = Flatten(pc.Hardware).ToList();
            foreach (var hw in all) hw.Update();

            // CPU
            var cpu = all.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            double? cpuLoad = null, cpuClock = null, cpuTemp = null, cpuPower = null;
            var cores = new List<double>();
            if (cpu != null)
            {
                var s = cpu.Sensors;
                cpuLoad = s.FirstOrDefault(x => x.SensorType == SensorType.Load && x.Name == "CPU Total")?.Value;
                cores = s.Where(x => x.SensorType == SensorType.Load && x.Name.StartsWith("CPU Core #") && x.Value.HasValue)
                         .OrderBy(x => x.Name.Length).ThenBy(x => x.Name)
                         .Select(x => (double)x.Value!.Value).ToList();
                var clocks = s.Where(x => x.SensorType == SensorType.Clock && x.Name.StartsWith("Core #") && x.Value.HasValue)
                              .Select(x => (double)x.Value!.Value).ToList();
                if (clocks.Count > 0) cpuClock = clocks.Average();

                var temps = s.Where(x => x.SensorType == SensorType.Temperature && x.Value.HasValue).ToList();
                cpuTemp = (temps.FirstOrDefault(x => x.Name is "CPU Package" or "Core (Tctl/Tdie)" or "Core Max" or "Core Average")
                           ?? temps.FirstOrDefault())?.Value;
                cpuPower = s.FirstOrDefault(x => x.SensorType == SensorType.Power && x.Name == "CPU Package")?.Value;
            }

            // GPUs
            var gpus = new List<GpuInfo>();
            foreach (var g in all.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel))
            {
                var s = g.Sensors;
                gpus.Add(new GpuInfo(
                    g.Name,
                    s.FirstOrDefault(x => x.SensorType == SensorType.Temperature && x.Name == "GPU Core")?.Value
                        ?? s.FirstOrDefault(x => x.SensorType == SensorType.Temperature)?.Value,
                    s.FirstOrDefault(x => x.SensorType == SensorType.Load && x.Name == "GPU Core")?.Value,
                    s.FirstOrDefault(x => x.SensorType == SensorType.Clock && x.Name == "GPU Core")?.Value,
                    s.FirstOrDefault(x => x.SensorType is SensorType.SmallData && x.Name == "GPU Memory Used")?.Value,
                    s.FirstOrDefault(x => x.SensorType is SensorType.SmallData && x.Name == "GPU Memory Total")?.Value,
                    s.FirstOrDefault(x => x.SensorType == SensorType.Fan)?.Value));
            }

            // Memory
            ReadMemory(out var used, out var total, out var memLoad);

            // Fans: pair each RPM sensor with its control sensor (same hardware, same name minus "Control").
            var fans = new List<FanInfo>();
            var newControls = new Dictionary<string, ISensor>();
            foreach (var hw in all)
            {
                // Graphics-card fans are shown read-only under Graphics; only case/CPU/pump fans get controls.
                if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel) continue;
                var rpmSensors = hw.Sensors.Where(x => x.SensorType == SensorType.Fan).ToList();
                var controlSensors = hw.Sensors.Where(x => x.SensorType == SensorType.Control).ToList();
                static string Norm(string n) => n.Replace("Control", "").Replace("  ", " ").Trim();

                var seen = new HashSet<string>();
                foreach (var f in rpmSensors)
                {
                    var key = Norm(f.Name);
                    seen.Add(key);
                    var ctl = controlSensors.FirstOrDefault(c => Norm(c.Name) == key);
                    AddFan(hw, f.Name, f.Value, ctl);
                }
                foreach (var c in controlSensors.Where(c => !seen.Contains(Norm(c.Name))))
                    AddFan(hw, c.Name, null, c);
            }

            void AddFan(IHardware hw, string name, float? rpm, ISensor? control)
            {
                var id = hw.Identifier + "|" + name;
                bool controllable = control?.Control != null;
                if (controllable) newControls[id] = control!;
                fans.Add(new FanInfo(
                    id,
                    name,
                    rpm,
                    control?.Value,
                    controllable,
                    controllable ? Math.Max(20, control!.Control!.MinSoftwareValue) : 0,
                    controllable ? control!.Control!.MaxSoftwareValue : 100));
            }

            int primary = Math.Max(0, gpus.FindIndex(g => !g.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase)));
            List<double> cpuHist, gpuHist;
            lock (_lock)
            {
                _cpuHist.Add(cpuLoad ?? 0);
                _gpuHist.Add(gpus.Count > primary ? gpus[primary].Load ?? 0 : 0);
                if (_cpuHist.Count > 90) _cpuHist.RemoveAt(0);
                if (_gpuHist.Count > 90) _gpuHist.RemoveAt(0);
                cpuHist = _cpuHist.ToList();
                gpuHist = _gpuHist.ToList();
            }

            lock (_lock)
            {
                _controls.Clear();
                foreach (var kv in newControls) _controls[kv.Key] = kv.Value;
            }

            return new HardwareSnapshot
            {
                CpuName = cpu?.Name ?? "CPU",
                CpuLoad = cpuLoad,
                CpuClockMHz = cpuClock,
                CpuTempC = cpuTemp,
                CpuPowerW = cpuPower,
                CoreLoads = cores,
                Gpus = gpus,
                MemUsedGB = used,
                MemTotalGB = total,
                MemLoad = memLoad,
                Fans = fans,
                CpuHistory = cpuHist,
                GpuHistory = gpuHist,
                PrimaryGpu = primary,
            };
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

        private static void ReadMemory(out double? usedGB, out double? totalGB, out double? loadPct)
        {
            usedGB = totalGB = loadPct = null;
            var m = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref m)) return;
            const double gb = 1024.0 * 1024 * 1024;
            totalGB = m.ullTotalPhys / gb;
            usedGB = (m.ullTotalPhys - m.ullAvailPhys) / gb;
            loadPct = m.dwMemoryLoad;
        }

        /// <summary>Takes manual control of a fan. Returns false if the hardware does not allow it.</summary>
        public bool SetFan(string id, double percent)
        {
            lock (_lock)
            {
                if (!_controls.TryGetValue(id, out var sensor) || sensor.Control == null) return false;
                var c = sensor.Control;
                percent = Math.Clamp(percent, Math.Max(20, c.MinSoftwareValue), c.MaxSoftwareValue);
                c.SetSoftware((float)percent);
                _manual.Add(id);
                return true;
            }
        }

        public void SetFanAuto(string id)
        {
            lock (_lock)
            {
                if (_controls.TryGetValue(id, out var sensor)) sensor.Control?.SetDefault();
                _manual.Remove(id);
            }
        }

        public bool IsManual(string id)
        {
            lock (_lock) return _manual.Contains(id);
        }

        /// <summary>Hands every fan back to the motherboard/driver. Called on exit so a slow fan never stays slow.</summary>
        public void ResetAllFans()
        {
            lock (_lock)
            {
                foreach (var id in _manual.ToList())
                {
                    try { if (_controls.TryGetValue(id, out var s)) s.Control?.SetDefault(); } catch { }
                }
                _manual.Clear();
            }
        }

        public void Shutdown()
        {
            _cts?.Cancel();
            ResetAllFans();
            try { _computer?.Close(); } catch { }
            _computer = null;
        }
    }
}
