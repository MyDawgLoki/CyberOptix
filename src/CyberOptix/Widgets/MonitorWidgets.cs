using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CyberOptix.Controls;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>A desktop widget fed by the hardware sensors. Sensors only run while at least one such widget (or Task Manager's Overview tab) is open.</summary>
    public abstract class MonitorWidget : DesktopWidget
    {
        private bool _watching;

        protected MonitorWidget(string key, double top) : base(key, top, 640)
        {
            Loaded += (_, _) =>
            {
                _watching = true;
                HardwareService.Instance.Updated += OnUpdated;
                HardwareService.Instance.Acquire();
            };
            Closed += (_, _) =>
            {
                if (!_watching) return;
                _watching = false;
                HardwareService.Instance.Updated -= OnUpdated;
                HardwareService.Instance.Release();
            };
        }

        private void OnUpdated(HardwareSnapshot s) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (_watching) Render(s); }));

        protected abstract void Render(HardwareSnapshot s);

        protected static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
        protected static Brush Tone(double value, double warm, double hot) =>
            value >= hot ? Res("HotBrush") : value >= warm ? Res("WarmBrush") : Res("AccentBrush");

        protected static TextBlock Dim(string text = "", double size = 12) =>
            Text(size, text, 0.65);

        /// <summary>A label, a value and a thin bar.</summary>
        protected sealed class BarRow
        {
            public readonly TextBlock Label = Text(12, "", 0.7), Value = Text(12, "", 1, FontWeights.SemiBold);
            public readonly ProgressBar Bar = new() { Style = (Style)Application.Current.FindResource("Meter"), Height = 5, Margin = new Thickness(0, 4, 0, 0) };
            public readonly StackPanel Root = new() { Margin = new Thickness(0, 0, 0, 10) };

            public BarRow()
            {
                var top = new DockPanel();
                DockPanel.SetDock(Value, Dock.Right);
                top.Children.Add(Value);
                top.Children.Add(Label);
                Root.Children.Add(top);
                Root.Children.Add(Bar);
            }

            public void Set(string label, string value, double percent, Brush fill)
            {
                Label.Text = label;
                Value.Text = value;
                Bar.Value = Math.Clamp(percent, 0, 100);
                Bar.Foreground = fill;
            }
        }
    }

    public class TempsWidget : MonitorWidget
    {
        private readonly StackPanel _rows = new();
        private readonly List<BarRow> _bars = new();

        public TempsWidget() : base("temps", 40)
        {
            var root = new StackPanel { MinWidth = 280 };
            root.Children.Add(Text(14, "Temperatures", 1, FontWeights.SemiBold));
            _rows.Margin = new Thickness(0, 12, 0, 0);
            root.Children.Add(_rows);
            Card.Child = WithClose(root);
        }

        protected override void Render(HardwareSnapshot s)
        {
            var items = new List<(string Label, double? Temp)> { ("CPU", s.CpuTempC) };
            items.AddRange(s.Gpus.Select(g => ($"GPU  {g.Name}", g.TempC)));

            while (_bars.Count < items.Count) { var r = new BarRow(); _bars.Add(r); _rows.Children.Add(r.Root); }
            while (_bars.Count > items.Count) { _rows.Children.Remove(_bars[^1].Root); _bars.RemoveAt(_bars.Count - 1); }

            for (int i = 0; i < items.Count; i++)
            {
                var t = items[i].Temp;
                _bars[i].Set(items[i].Label, t.HasValue ? $"{t:0} °C" : "Not available", t ?? 0, Tone(t ?? 0, 70, 85));
            }
        }
    }

    public class CpuWidget : MonitorWidget
    {
        private readonly TextBlock _name = Dim(), _usage = Text(40, "--", 1, FontWeights.SemiBold), _detail = Dim();
        private readonly LineGraph _graph = new() { Height = 70, Width = 280, Margin = new Thickness(0, 12, 0, 0) };

        public CpuWidget() : base("cpu", 260)
        {
            var root = new StackPanel { MinWidth = 280 };
            root.Children.Add(Text(14, "CPU", 1, FontWeights.SemiBold));
            root.Children.Add(_name);
            root.Children.Add(_usage);
            root.Children.Add(_detail);
            root.Children.Add(_graph);
            Card.Child = WithClose(root);
        }

        protected override void Render(HardwareSnapshot s)
        {
            _name.Text = s.CpuName;
            _usage.Text = s.CpuLoad.HasValue ? $"{s.CpuLoad:0}%" : "--";
            var parts = new List<string>();
            if (s.CpuClockMHz.HasValue) parts.Add($"{s.CpuClockMHz / 1000:0.00} GHz");
            if (s.CpuTempC.HasValue) parts.Add($"{s.CpuTempC:0} °C");
            if (s.CpuPowerW is > 0) parts.Add($"{s.CpuPowerW:0} W");
            _detail.Text = parts.Count > 0 ? string.Join("  ·  ", parts) : "Usage only";
            _graph.SetValues(s.CpuHistory);
        }
    }

    public class GpuWidget : MonitorWidget
    {
        private readonly TextBlock _name = Dim(), _usage = Text(40, "--", 1, FontWeights.SemiBold), _detail = Dim();
        private readonly LineGraph _graph = new() { Height = 70, Width = 280, Margin = new Thickness(0, 12, 0, 0) };

        public GpuWidget() : base("gpu", 480)
        {
            var root = new StackPanel { MinWidth = 280 };
            root.Children.Add(Text(14, "GPU", 1, FontWeights.SemiBold));
            root.Children.Add(_name);
            root.Children.Add(_usage);
            root.Children.Add(_detail);
            root.Children.Add(_graph);
            Card.Child = WithClose(root);
        }

        protected override void Render(HardwareSnapshot s)
        {
            if (s.Gpus.Count == 0) { _name.Text = "No graphics card found"; return; }
            var g = s.Gpus[Math.Min(s.PrimaryGpu, s.Gpus.Count - 1)];
            _name.Text = g.Name;
            _usage.Text = g.Load.HasValue ? $"{g.Load:0}%" : "--";
            var parts = new List<string>();
            if (g.TempC.HasValue) parts.Add($"{g.TempC:0} °C");
            if (g.ClockMHz.HasValue) parts.Add($"{g.ClockMHz:0} MHz");
            if (g.VramUsedMB.HasValue && g.VramTotalMB is > 0) parts.Add($"{g.VramUsedMB / 1024:0.0} / {g.VramTotalMB / 1024:0.0} GB");
            _detail.Text = string.Join("  ·  ", parts);
            _graph.SetValues(s.GpuHistory);
        }
    }
}
