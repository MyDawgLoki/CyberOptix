using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CyberOptix.Models;
using CyberOptix.Services;
using CyberOptix.Widgets;

namespace CyberOptix.Pages
{
    public partial class MonitorPage : UserControl
    {
        private static readonly string[] PopKeys = { "temps", "cpu", "gpu" };

        private readonly ObservableCollection<MeterRow> _temps = new(), _cpuRows = new(), _gpuRows = new(), _memRows = new();
        private readonly ObservableCollection<CoreVm> _cores = new();
        private readonly ObservableCollection<FanVm> _fans = new();
        private readonly ObservableCollection<string> _modules = new();
        private readonly ObservableCollection<AppUsageVm> _apps = new();
        private readonly Dictionary<int, AppUsageVm> _appMap = new();
        private readonly DispatcherTimer _appTimer = new() { Interval = TimeSpan.FromSeconds(2) };

        private bool _watching, _ramLoaded, _applyingFans, _samplingApps;
        private double _maxClock = 1, _maxPower = 1;

        public MonitorPage()
        {
            InitializeComponent();

            lstTemps.ItemsSource = _temps;
            lstCpu.ItemsSource = _cpuRows;
            lstCores.ItemsSource = _cores;
            lstGpu.ItemsSource = _gpuRows;
            lstMem.ItemsSource = _memRows;
            lstRamModules.ItemsSource = _modules;
            lstFans.ItemsSource = _fans;
            lstApps.ItemsSource = _apps;

            _appTimer.Tick += (_, _) => RefreshApps();
        }

        // ---------- start/stop with visibility so a hidden page costs nothing ----------

        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible) Start(); else Stop();
        }

        private void Start()
        {
            if (_watching) return;
            _watching = true;
            HardwareService.Instance.Updated += OnHardware;
            HardwareService.Instance.Acquire();
            UpdatePopButtons();
            LoadRam();
            RefreshApps();
            _appTimer.Start();
        }

        private void Stop()
        {
            if (!_watching) return;
            _watching = false;
            HardwareService.Instance.Updated -= OnHardware;
            HardwareService.Instance.Release();
            _appTimer.Stop();
        }

        private Brush Res(string key) => (Brush)FindResource(key);
        private Brush Tone(double value, double warm, double hot) =>
            value >= hot ? Res("HotBrush") : value >= warm ? Res("WarmBrush") : Res("AccentBrush");

        // ---------- pop-out widgets ----------

        private void UpdatePopButtons()
        {
            lblPopTemps.Text = WidgetManager.IsEnabled("temps") ? "Close pop-out" : "Pop out";
            lblPopCpu.Text = WidgetManager.IsEnabled("cpu") ? "Close pop-out" : "Pop out";
            lblPopGpu.Text = WidgetManager.IsEnabled("gpu") ? "Close pop-out" : "Pop out";
            lblPopAll.Text = PopKeys.All(WidgetManager.IsEnabled) ? "Close all pop-outs" : "Pop out all";
        }

        private void PopClicked(object sender, RoutedEventArgs e)
        {
            var key = (string)((FrameworkElement)sender).Tag;
            WidgetManager.SetEnabled(key, !WidgetManager.IsEnabled(key));
            WidgetManager.Apply();
            UpdatePopButtons();
        }

        private void PopAllClicked(object sender, RoutedEventArgs e)
        {
            bool turnOn = !PopKeys.All(WidgetManager.IsEnabled);
            foreach (var key in PopKeys) WidgetManager.SetEnabled(key, turnOn);
            WidgetManager.Apply();
            UpdatePopButtons();
        }

        // ---------- hardware ----------

        private void OnHardware(HardwareSnapshot snap) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (_watching) Render(snap); }));

        private static void Sync(ObservableCollection<MeterRow> rows, List<(string Label, string Text, double Fraction, Brush Fill)> items)
        {
            bool sameShape = rows.Count == items.Count && rows.Select(r => r.Label).SequenceEqual(items.Select(i => i.Label));
            if (!sameShape)
            {
                rows.Clear();
                foreach (var i in items) rows.Add(new MeterRow { Label = i.Label, ValueText = i.Text, Fraction = i.Fraction, Fill = i.Fill });
                return;
            }
            for (int n = 0; n < items.Count; n++)
            {
                rows[n].ValueText = items[n].Text;
                rows[n].Fraction = items[n].Fraction;
                rows[n].Fill = items[n].Fill;
            }
        }

        private void Render(HardwareSnapshot s)
        {
            UpdatePopButtons(); // a widget closed with its x button should flip the label back

            // Temperatures
            var temps = new List<(string, string, double, Brush)>
            {
                ("CPU", s.CpuTempC.HasValue ? $"{s.CpuTempC:0} °C" : "Not available", Math.Clamp(s.CpuTempC ?? 0, 0, 100), Tone(s.CpuTempC ?? 0, 70, 85)),
            };
            foreach (var g in s.Gpus)
                temps.Add(($"GPU  {g.Name}", g.TempC.HasValue ? $"{g.TempC:0} °C" : "Not available",
                           Math.Clamp(g.TempC ?? 0, 0, 100), Tone(g.TempC ?? 0, 70, 85)));
            Sync(_temps, temps);

            // CPU
            txtCpuName.Text = s.CpuName;
            graphCpu.SetValues(s.CpuHistory);
            var cpu = new List<(string, string, double, Brush)>
            {
                ("Usage", s.CpuLoad.HasValue ? $"{s.CpuLoad:0}%" : "Not available", s.CpuLoad ?? 0, Tone(s.CpuLoad ?? 0, 70, 90)),
            };
            if (s.CpuClockMHz.HasValue)
            {
                _maxClock = Math.Max(_maxClock, s.CpuClockMHz.Value);
                cpu.Add(("Clock speed", $"{s.CpuClockMHz / 1000:0.00} GHz", s.CpuClockMHz.Value / _maxClock * 100, Res("AccentBrush")));
            }
            if (s.CpuPowerW is > 0)
            {
                _maxPower = Math.Max(_maxPower, s.CpuPowerW.Value);
                cpu.Add(("Power draw", $"{s.CpuPowerW:0} W", s.CpuPowerW.Value / _maxPower * 100, Res("AccentBrush")));
            }
            Sync(_cpuRows, cpu);

            if (_cores.Count != s.CoreLoads.Count)
            {
                _cores.Clear();
                for (int i = 0; i < s.CoreLoads.Count; i++) _cores.Add(new CoreVm { Name = $"Core {i + 1}" });
            }
            for (int i = 0; i < s.CoreLoads.Count; i++)
            {
                _cores[i].Load = s.CoreLoads[i];
                _cores[i].Fill = Tone(s.CoreLoads[i], 70, 90);
            }

            // Graphics
            var gpu = new List<(string, string, double, Brush)>();
            foreach (var g in s.Gpus)
            {
                if (g.Load.HasValue) gpu.Add(($"{g.Name}  load", $"{g.Load:0}%", g.Load.Value, Tone(g.Load.Value, 70, 90)));
                if (g.ClockMHz.HasValue) gpu.Add(($"{g.Name}  clock", $"{g.ClockMHz:0} MHz", Math.Clamp(g.ClockMHz.Value / 30, 0, 100), Res("AccentBrush")));
                if (g.VramUsedMB.HasValue && g.VramTotalMB is > 0)
                    gpu.Add(($"{g.Name}  video memory", $"{g.VramUsedMB / 1024:0.0} / {g.VramTotalMB / 1024:0.0} GB",
                             g.VramUsedMB.Value / g.VramTotalMB.Value * 100, Tone(g.VramUsedMB.Value / g.VramTotalMB.Value * 100, 80, 95)));
                if (g.FanRpm.HasValue)
                    gpu.Add(($"{g.Name}  fan (read only)", g.FanRpm > 0 ? $"{g.FanRpm:0} RPM" : "Stopped", Math.Clamp(g.FanRpm.Value / 30, 0, 100), Res("AccentBrush")));
            }
            Sync(_gpuRows, gpu);
            txtNoGpu.Visibility = s.Gpus.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (s.Gpus.Count > 0)
            {
                txtGpuName.Text = s.Gpus[Math.Min(s.PrimaryGpu, s.Gpus.Count - 1)].Name;
                graphGpu.SetValues(s.GpuHistory);
            }
            txtGpuName.Visibility = graphGpu.Visibility = s.Gpus.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

            // Memory
            var mem = new List<(string, string, double, Brush)>();
            if (s.MemLoad.HasValue)
                mem.Add(("In use", s.MemUsedGB.HasValue && s.MemTotalGB.HasValue ? $"{s.MemUsedGB:0.0} / {s.MemTotalGB:0.0} GB" : $"{s.MemLoad:0}%",
                         s.MemLoad.Value, Tone(s.MemLoad.Value, 75, 90)));
            Sync(_memRows, mem);

            RenderFans(s);

            var missing = new List<string>();
            if (!s.CpuTempC.HasValue) missing.Add("CPU temperature");
            if (s.Fans.Count == 0) missing.Add("case fans");
            noteSensors.Visibility = missing.Count > 0 || HardwareService.Instance.Problem != null ? Visibility.Visible : Visibility.Collapsed;
            bool pawn = PawnIoService.IsInstalled();
            txtSensorNote.Text = HardwareService.Instance.Problem ??
                $"Your PC isn't reporting {string.Join(" or ", missing)}. Reading it can need a low-level sensor driver (PawnIO) in addition to running as administrator. " +
                (pawn ? "PawnIO is installed here; if it still does not show, restart Windows once. " : "") +
                "Everything that is available is shown below.";
            // The install button is offered while the driver is missing, and not while an install is running.
            if (!_installingPawnIo) panelPawnIo.Visibility = !pawn && missing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            rowPawnIoLink.Visibility = !pawn && !s.CpuTempC.HasValue && !_installingPawnIo ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool _installingPawnIo;

        private async void InstallPawnIoClicked(object sender, RoutedEventArgs e)
        {
            if (_installingPawnIo) return;
            var ask = MessageBox.Show(Window.GetWindow(this),
                "PawnIO is a small signed driver (freeware, by namazso, pawnio.eu) that lets CyberOptix read your CPU temperature and some fan sensors.\n\n" +
                "It is installed for the whole PC, not just for CyberOptix, so other programs that use PawnIO will share it. CyberOptix installs it with Windows' own package manager (winget), " +
                "which downloads it from its official source and checks its checksum. Nothing else is downloaded.\n\n" +
                "You can remove it later in Windows Settings > Installed apps, or with: winget uninstall namazso.PawnIO\n\nInstall it now?",
                "Install the PawnIO sensor driver", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (ask != MessageBoxResult.Yes) return;

            _installingPawnIo = true;
            rowPawnIoLink.Visibility = Visibility.Collapsed;
            btnInstallPawnIo.IsEnabled = false;
            panelPawnIo.Visibility = Visibility.Visible;
            txtPawnIoStatus.Text = "Installing... this can take a minute.";
            var result = await PawnIoService.InstallAsync();
            _installingPawnIo = false;
            btnInstallPawnIo.IsEnabled = true;

            if (result.Installed)
            {
                HardwareService.Instance.ReloadSensors();
                txtPawnIoStatus.Text = result.Message + " The temperature should appear within a few seconds; if it does not, restart Windows once.";
                panelPawnIo.Visibility = Visibility.Visible;
                btnInstallPawnIo.Visibility = Visibility.Collapsed;
            }
            else if (result.WingetMissing)
            {
                txtPawnIoStatus.Text = result.Message + " Opening the PawnIO website, where you can download the installer yourself.";
                PawnIoService.OpenWebsite();
            }
            else
            {
                txtPawnIoStatus.Text = result.Message;
            }
        }

        private void RenderFans(HardwareSnapshot s)
        {
            _applyingFans = true;
            try
            {
                var ids = s.Fans.Select(f => f.Id).ToList();
                if (!_fans.Select(f => f.Id).SequenceEqual(ids))
                {
                    _fans.Clear();
                    foreach (var f in s.Fans)
                        _fans.Add(new FanVm
                        {
                            Id = f.Id, Name = f.Name, Controllable = f.Controllable,
                            Min = f.Controllable ? f.MinPercent : 0, Max = f.MaxPercent > 0 ? f.MaxPercent : 100,
                            IsManual = HardwareService.Instance.IsManual(f.Id),
                        });
                }
                foreach (var f in s.Fans)
                {
                    var vm = _fans.First(x => x.Id == f.Id);
                    vm.RpmText = f.Rpm.HasValue ? $"{f.Rpm:0} RPM" : "No RPM reading";
                    if (!vm.IsManual && f.Percent.HasValue) vm.Percent = f.Percent.Value;
                }
            }
            finally
            {
                _applyingFans = false;
            }
            txtNoFans.Visibility = _fans.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Only a real drag or key press counts as the user taking control; the binding also raises this event.
        private void FanSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_applyingFans || sender is not Slider slider) return;
            if (!(slider.IsMouseCaptureWithin || slider.IsKeyboardFocused)) return;
            if (slider.DataContext is not FanVm fan || !fan.Controllable) return;

            if (HardwareService.Instance.SetFan(fan.Id, e.NewValue)) fan.IsManual = true;
        }

        private void FanAutoClicked(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is not FanVm fan) return;
            HardwareService.Instance.SetFanAuto(fan.Id);
            fan.IsManual = false;
        }

        // ---------- apps: per-app CPU, GPU and RAM ----------

        private void RefreshApps()
        {
            if (_samplingApps) return;
            _samplingApps = true;
            Task.Run(ProcessService.Sample).ContinueWith(t =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _samplingApps = false;
                    if (!_watching || t.IsFaulted) return;
                    MergeApps(t.Result);
                }));
            });
        }

        private void MergeApps(List<ProcSample> samples)
        {
            var seen = new HashSet<int>();
            foreach (var s in samples.Where(s => s.IsApp))
            {
                seen.Add(s.Pid);
                if (_appMap.TryGetValue(s.Pid, out var vm))
                {
                    vm.CpuPct = s.CpuPct;
                    vm.GpuPct = s.GpuPct;
                    vm.MemMB = s.MemMB;
                }
                else
                {
                    vm = new AppUsageVm { Pid = s.Pid, Name = s.Name, CpuPct = s.CpuPct, GpuPct = s.GpuPct, MemMB = s.MemMB };
                    _appMap[s.Pid] = vm;
                    _apps.Add(vm);
                }
            }
            foreach (var gone in _appMap.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _apps.Remove(_appMap[gone]);
                _appMap.Remove(gone);
            }

            SortApps();
            txtNoApps.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SortApps()
        {
            // Checked fires while the page is still being built, before every named control exists.
            if (rbAppGpu == null || rbAppMem == null || _apps == null) return;

            List<AppUsageVm> ordered = rbAppGpu.IsChecked == true ? _apps.OrderByDescending(a => a.GpuPct).ToList()
                : rbAppMem.IsChecked == true ? _apps.OrderByDescending(a => a.MemMB).ToList()
                : _apps.OrderByDescending(a => a.CpuPct).ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                int current = _apps.IndexOf(ordered[i]);
                if (current != i) _apps.Move(current, i);
            }
        }

        private void AppSortChanged(object sender, RoutedEventArgs e) => SortApps();

        // ---------- RAM speed (static: what the memory is configured to run at) ----------

        private async void LoadRam()
        {
            if (_ramLoaded) return;
            _ramLoaded = true;
            var modules = await Task.Run(RamInfoService.Read);

            _modules.Clear();
            if (modules.Count == 0)
            {
                txtRamSpeed.Text = "Not available";
                rowRamSpeed.Description = "Windows did not report the installed memory modules.";
                return;
            }

            foreach (var m in modules)
            {
                var rated = m.RatedMTs > 0 && m.RatedMTs != m.ConfiguredMTs ? $" (rated {m.RatedMTs})" : "";
                _modules.Add($"{m.Slot}  ·  {m.CapacityGB:0} GB {m.Type}  ·  {m.ConfiguredMTs} MT/s{rated}" +
                             (string.IsNullOrWhiteSpace(m.Maker) ? "" : $"  ·  {m.Maker}"));
            }

            int running = modules.Min(m => m.ConfiguredMTs);
            int rating = modules.Max(m => m.RatedMTs);
            txtRamSpeed.Text = $"{running} MT/s";
            rowRamSpeed.Description = $"{modules[0].Type}, {modules.Count} module{(modules.Count == 1 ? "" : "s")}, {modules.Sum(m => m.CapacityGB):0} GB total. " +
                                      "This is the speed the memory is set to run at, not live bandwidth.";
            txtRamNote.Text = rating > running + 100
                ? $"Your memory is running at {running} MT/s but is rated for {rating} MT/s. Turning on XMP or EXPO in the BIOS usually fixes this."
                : "";
        }
    }
}
