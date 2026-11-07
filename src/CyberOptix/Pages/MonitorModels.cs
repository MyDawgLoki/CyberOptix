using CyberOptix.Services;
using System.Windows.Media;
using CyberOptix.Models;

namespace CyberOptix.Pages
{
    /// <summary>A labelled value with a bar: usage, temperature, memory.</summary>
    public class MeterRow : Observable
    {
        private string _label = "";
        private string _valueText = "";
        private double _fraction;
        private Brush? _fill;

        public string Label { get => _label; set => Set(ref _label, value); }
        public string ValueText { get => _valueText; set => Set(ref _valueText, value); }
        /// <summary>0 to 100.</summary>
        public double Fraction { get => _fraction; set => Set(ref _fraction, value); }
        public Brush? Fill { get => _fill; set => Set(ref _fill, value); }
    }

    public class CoreVm : Observable
    {
        private double _load;
        private Brush? _fill;

        public string Name { get; init; } = "";
        public double Load { get => _load; set => Set(ref _load, value); }
        public Brush? Fill { get => _fill; set => Set(ref _fill, value); }
    }

    public class FanVm : Observable
    {
        private string _rpmText = "";
        private double _percent;
        private bool _isManual;

        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public bool Controllable { get; init; }
        public double Min { get; init; }
        public double Max { get; init; } = 100;

        public string RpmText { get => _rpmText; set => Set(ref _rpmText, value); }

        public double Percent
        {
            get => _percent;
            set { if (Set(ref _percent, value)) Raise(nameof(ModeText)); }
        }

        public bool IsManual
        {
            get => _isManual;
            set { if (Set(ref _isManual, value)) Raise(nameof(ModeText)); }
        }

        public string ModeText => !Controllable ? "Read only" : IsManual ? $"Manual {Percent:0}%" : $"Automatic {Percent:0}%";
    }

    /// <summary>One foreground app's CPU, GPU and RAM usage, shown on the Task Manager Overview tab.</summary>
    public class AppUsageVm : Observable
    {
        private double _cpu, _gpu, _mem;

        public int Pid { get; init; }
        public string Name { get; init; } = "";

        public double CpuPct
        {
            get => _cpu;
            set { if (Set(ref _cpu, value)) Raise(nameof(CpuText)); }
        }

        public double GpuPct
        {
            get => _gpu;
            set { if (Set(ref _gpu, value)) Raise(nameof(GpuText)); }
        }

        public double MemMB
        {
            get => _mem;
            set { if (Set(ref _mem, value)) Raise(nameof(MemText)); }
        }

        public string CpuText => _cpu < 0.05 ? "0%" : $"{_cpu:0.#}%";
        public string GpuText => _gpu < 0.05 ? "0%" : $"{_gpu:0.#}%";
        public string MemText => _mem >= 1024 ? $"{_mem / 1024:0.0} GB" : $"{_mem:0} MB";
    }

    public class ProcVm : Observable
    {
        private double _cpu;
        private double _mem;
        private bool _isApp;
        private string _title = "";

        public int Pid { get; init; }
        public string Name { get; init; } = "";
        private ProcGroup _group = ProcessClassifier.Killable;
        private string _reason = "";
        private int _classVersion = -1;
        private int _announcedVersion = -1;

        /// <summary>True when Windows, a driver or security software depends on this process, so it can't be ended.</summary>
        public bool Locked => Group == ProcessClassifier.Unkillable;

        public double CpuPct
        {
            get => _cpu;
            set { if (Set(ref _cpu, value)) Raise(nameof(CpuText)); }
        }

        public double MemMB
        {
            get => _mem;
            set { if (Set(ref _mem, value)) Raise(nameof(MemText)); }
        }

        /// <summary>True when the process owns a normal visible window.</summary>
        public bool IsApp
        {
            get => _isApp;
            set => Set(ref _isApp, value);
        }

        public string Title
        {
            get => _title;
            set { if (Set(ref _title, value)) Raise(nameof(TitleSuffix)); }
        }

        /// <summary>Which divider this process sits under: Killable, Bloat or Unkillable.</summary>
        public ProcGroup Group
        {
            get { Classify(); return _group; }
        }

        public int GroupOrder => Group.Order;

        /// <summary>Why it sits there (for Bloat and Unkillable), then the window title when it has one.</summary>
        public string TitleSuffix
        {
            get
            {
                Classify();
                var title = string.IsNullOrWhiteSpace(_title) ? "" : _title;
                if (_reason.Length == 0) return title.Length == 0 ? "" : "   " + title;
                return "   " + _reason + (title.Length == 0 ? "" : "  -  " + title);
            }
        }

        private void Classify()
        {
            if (_classVersion == ProcessClassifier.Version) return;
            (_group, _reason) = ProcessClassifier.Classify(Name);
            _classVersion = ProcessClassifier.Version;
        }

        /// <summary>Tells the list to move this row if the Never kill list changed what it should show.</summary>
        public void CheckClass()
        {
            if (_announcedVersion == ProcessClassifier.Version) return;
            var before = _group;
            var reason = _reason;
            _classVersion = -1;
            Classify();
            _announcedVersion = ProcessClassifier.Version;
            if (before != _group)
            {
                Raise(nameof(Group));
                Raise(nameof(GroupOrder));
                Raise(nameof(Locked));
            }
            if (before != _group || reason != _reason) Raise(nameof(TitleSuffix));
        }

        public string CpuText => _cpu < 0.05 ? "0%" : $"{_cpu:0.#}%";
        public string MemText => _mem >= 1024 ? $"{_mem / 1024:0.0} GB" : $"{_mem:0} MB";
    }

    public class StartupVm : Observable
    {
        private bool _enabled;

        public required CyberOptix.Services.StartupEntry Entry { get; init; }
        public string Name => Entry.Name;
        public string Detail => Entry.Detail;

        public bool Enabled
        {
            get => _enabled;
            set => Set(ref _enabled, value);
        }
    }
}