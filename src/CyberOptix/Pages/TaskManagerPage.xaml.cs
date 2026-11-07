using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using CyberOptix.Services;

namespace CyberOptix.Pages
{
    public partial class TaskManagerPage : UserControl
    {
        private readonly ObservableCollection<ProcVm> _procs = new();
        private readonly Dictionary<int, ProcVm> _procMap = new();
        private readonly ListCollectionView _procView;
        private readonly ObservableCollection<StartupVm> _startup = new();
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

        private bool _running, _sampling, _startupLoaded, _pendingTree, _ready;

        public TaskManagerPage()
        {
            InitializeComponent();

            _procView = (ListCollectionView)CollectionViewSource.GetDefaultView(_procs);
            _procView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProcVm.Group)));
            _procView.IsLiveGrouping = true;
            _procView.LiveGroupingProperties.Add(nameof(ProcVm.Group));
            _procView.IsLiveSorting = true;
            _procView.LiveSortingProperties.Add(nameof(ProcVm.GroupOrder));
            _procView.LiveSortingProperties.Add(nameof(ProcVm.CpuPct));
            _procView.LiveSortingProperties.Add(nameof(ProcVm.MemMB));
            _procView.Filter = o => o is ProcVm p &&
                (string.IsNullOrWhiteSpace(txtProcSearch.Text) ||
                 p.Name.Contains(txtProcSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 p.Title.Contains(txtProcSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 p.Pid.ToString() == txtProcSearch.Text.Trim());
            lstProcs.ItemsSource = _procView;
            lstStartup.ItemsSource = _startup;
            ApplySort();

            _timer.Tick += (_, _) => RefreshProcesses();
            _ready = true;
        }

        // ---------- only work while the page is on screen ----------

        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateActivity();

        /// <summary>Switches between the three tabs: Processes (with the live monitors), Start-up apps and Free up resources.</summary>
        private void TabChanged(object sender, RoutedEventArgs e)
        {
            if (!_ready) return; // Checked fires while XAML is still loading
            secProcs.Visibility = rbTabProcs.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            secStartup.Visibility = rbTabStartup.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            secClean.Visibility = rbTabClean.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (rbTabClean.IsChecked == true)
            {
                // Always open on the clean-up list, not on the Never kill list.
                secNever.Visibility = Visibility.Collapsed;
                cleanMain.Visibility = Visibility.Visible;
            }
            scrollMain.ScrollToTop();
            UpdateActivity();
        }

        private void NeverOpenClicked(object sender, RoutedEventArgs e)
        {
            cleanMain.Visibility = Visibility.Collapsed;
            secNever.Visibility = Visibility.Visible;
            scrollMain.ScrollToTop();
            if (!_neverLoaded) StartNeverLoad();
        }

        private void NeverBackClicked(object sender, RoutedEventArgs e)
        {
            secNever.Visibility = Visibility.Collapsed;
            cleanMain.Visibility = Visibility.Visible;
            scrollMain.ScrollToTop();
            if (!_cleanScanned) StartCleanScan();
        }

        /// <summary>Process sampling only runs while the Processes tab is on screen; the start-up list loads the first time its tab opens.
        /// The Overview starts and stops its own sensors from its own visibility.</summary>
        private void UpdateActivity()
        {
            if (!_ready) return;
            bool procsOn = IsVisible && rbTabProcs.IsChecked == true;
            if (procsOn && !_running)
            {
                _running = true;
                _timer.Start();
                RefreshProcesses();
            }
            else if (!procsOn && _running)
            {
                _running = false;
                _timer.Stop();
            }
            if (IsVisible && rbTabStartup.IsChecked == true) LoadStartup();
            if (IsVisible && rbTabClean.IsChecked == true && !_cleanScanned && secNever.Visibility != Visibility.Visible) StartCleanScan();
        }

        // ---------- processes ----------

        private void RefreshProcesses()
        {
            if (_sampling) return;
            _sampling = true;
            Task.Run(ProcessService.Sample).ContinueWith(t =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _sampling = false;
                    if (!_running || t.IsFaulted) return;
                    Merge(t.Result);
                }));
            });
        }

        private void Merge(List<ProcSample> samples)
        {
            var seen = new HashSet<int>();
            foreach (var s in samples)
            {
                // Windows, driver and security processes are never listed at all.
                if (ProcessClassifier.Classify(s.Name).Group == ProcessClassifier.Unkillable) continue;
                seen.Add(s.Pid);
                if (_procMap.TryGetValue(s.Pid, out var vm))
                {
                    vm.CpuPct = s.CpuPct;
                    vm.MemMB = s.MemMB;
                    vm.IsApp = s.IsApp;
                    vm.Title = s.Title;
                }
                else
                {
                    vm = new ProcVm
                    {
                        Pid = s.Pid, Name = s.Name,
                        CpuPct = s.CpuPct, MemMB = s.MemMB, IsApp = s.IsApp, Title = s.Title,
                    };
                    _procMap[s.Pid] = vm;
                    _procs.Add(vm);
                }
            }
            foreach (var vm in _procMap.Values) vm.CheckClass();
            foreach (var gone in _procMap.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _procs.Remove(_procMap[gone]);
                _procMap.Remove(gone);
            }
        }

        private void ApplySort()
        {
            if (_procView == null) return;
            _procView.SortDescriptions.Clear();
            // Killable, then Bloat; inside each, the chosen order.
            _procView.SortDescriptions.Add(new SortDescription(nameof(ProcVm.GroupOrder), ListSortDirection.Ascending));
            if (rbSortName.IsChecked == true)
                _procView.SortDescriptions.Add(new SortDescription(nameof(ProcVm.Name), ListSortDirection.Ascending));
            else if (rbSortMem.IsChecked == true)
                _procView.SortDescriptions.Add(new SortDescription(nameof(ProcVm.MemMB), ListSortDirection.Descending));
            else
                _procView.SortDescriptions.Add(new SortDescription(nameof(ProcVm.CpuPct), ListSortDirection.Descending));
        }

        private void ProcSortChanged(object sender, RoutedEventArgs e) => ApplySort();
        private void ProcSearchChanged(object sender, TextChangedEventArgs e) => _procView?.Refresh();

        private ProcVm? Selected => lstProcs.SelectedItem as ProcVm;

        private void ProcSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            panelConfirm.Visibility = Visibility.Collapsed;
            var p = Selected;
            bool canEnd = p != null && !p.Locked;
            btnEndTask.IsEnabled = canEnd;
            btnEndTree.IsEnabled = canEnd;
            txtProcStatus.Text = p is { Locked: true } ? $"{p.Name} is a core Windows process and can't be ended here." : "";
        }

        private void EndTaskClicked(object sender, RoutedEventArgs e) => AskToEnd(false);
        private void EndTreeClicked(object sender, RoutedEventArgs e) => AskToEnd(true);

        private void AskToEnd(bool tree)
        {
            var p = Selected;
            if (p == null || p.Locked) return;
            _pendingTree = tree;
            txtConfirm.Text = tree
                ? $"End \"{p.Name}\" (PID {p.Pid}) and every process it started? Unsaved work in them will be lost."
                : $"End \"{p.Name}\" (PID {p.Pid})? Unsaved work in it will be lost.";
            btnConfirmEnd.Content = tree ? "End task and children" : "End task";
            panelConfirm.Visibility = Visibility.Visible;
        }

        private void CancelEndClicked(object sender, RoutedEventArgs e) => panelConfirm.Visibility = Visibility.Collapsed;

        private void ConfirmEndClicked(object sender, RoutedEventArgs e)
        {
            panelConfirm.Visibility = Visibility.Collapsed;
            var p = Selected;
            if (p == null) return;

            var error = ProcessService.Kill(p.Pid, _pendingTree);
            txtProcStatus.Text = error ?? $"Ended {p.Name}.";
            RefreshProcesses();
        }

        // ---------- start-up apps ----------

        private async void LoadStartup()
        {
            if (_startupLoaded) return;
            _startupLoaded = true;

            var entries = await Task.Run(StartupService.List);
            _startup.Clear();
            foreach (var e in entries) _startup.Add(new StartupVm { Entry = e, Enabled = e.Enabled });

            txtNoStartup.Visibility = _startup.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateStartupSummary();
        }

        private void UpdateStartupSummary() =>
            txtStartupSummary.Text = $"{_startup.Count} app{(_startup.Count == 1 ? "" : "s")} start with Windows, {_startup.Count(s => s.Enabled)} switched on.";

        private void StartupClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox box || box.DataContext is not StartupVm vm) return;

            bool want = box.IsChecked == true;
            var error = StartupService.SetEnabled(vm.Entry, want);
            if (error != null)
            {
                box.IsChecked = !want; // put the switch back
                txtStartupStatus.Text = $"Could not change {vm.Name}: {error}";
                return;
            }

            vm.Enabled = want;
            txtStartupStatus.Text = $"{vm.Name} will {(want ? "" : "no longer ")}start the next time you sign in.";
            UpdateStartupSummary();
        }

        // ---------- never kill ----------

        private bool _neverLoaded, _neverLoading;
        private readonly List<(CheckBox Box, string Name, string Text)> _neverRows = new();

        private static string FormatMem(double mb) =>
            mb < 1 ? "" : mb >= 1024 ? (mb / 1024).ToString("0.0") + " GB" : Math.Round(mb) + " MB";

        private async void StartNeverLoad()
        {
            if (_neverLoading) return;
            _neverLoading = true;
            btnNeverRefresh.IsEnabled = false;
            txtNeverStatus.Text = "Looking at what is running...";
            List<CleanupScanner.Candidate> running;
            try { running = await Task.Run(CleanupScanner.Candidates); }
            catch { running = new List<CleanupScanner.Candidate>(); }
            _neverLoading = false;
            _neverLoaded = true;
            btnNeverRefresh.IsEnabled = true;
            BuildNever(running);
        }

        private void NeverRefreshClicked(object sender, RoutedEventArgs e) => StartNeverLoad();

        private void BuildNever(List<CleanupScanner.Candidate> running)
        {
            var saved = new HashSet<string>(Models.AppSettings.Current.NeverKill, StringComparer.OrdinalIgnoreCase);
            panelNever.Children.Clear();
            _neverRows.Clear();

            var rows = new List<(string Name, string Desc, string Mem, bool On)>();
            foreach (var name in saved.Where(n => !running.Any(r => r.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                rows.Add((name, "Not running right now", "", true));
            foreach (var r in running.OrderByDescending(r => saved.Contains(r.Name)).ThenByDescending(r => r.MemMB))
                rows.Add((r.Name, (r.HasWindow ? "Open app. " : "") + (r.Description.Length > 0 ? r.Description : "Running program"), FormatMem(r.MemMB), saved.Contains(r.Name)));

            var stack = new StackPanel();
            bool first = true;
            foreach (var row in rows)
            {
                if (!first) stack.Children.Add(new Border { Style = (Style)FindResource("Divider") });
                first = false;
                var box = new CheckBox { Content = row.Name, Tag = row.Desc, IsChecked = row.On, Style = (Style)FindResource("UsageSwitch") };
                Controls.Usage.SetText(box, row.Mem);
                var name = row.Name;
                box.Checked += (_, _) => NeverToggled(name, true);
                box.Unchecked += (_, _) => NeverToggled(name, false);
                stack.Children.Add(box);
                _neverRows.Add((box, row.Name, (row.Name + " " + row.Desc).ToLowerInvariant()));
            }
            if (rows.Count == 0)
                stack.Children.Add(new TextBlock { Text = "No programs to show right now.", Margin = new Thickness(16, 14, 16, 14), Foreground = (System.Windows.Media.Brush)FindResource("TextDimBrush") });
            panelNever.Children.Add(new Border { Style = (Style)FindResource("Card"), Child = stack });

            NeverSearchChanged(txtNeverSearch, null!);
            UpdateNeverStatus();
        }

        private void UpdateNeverStatus()
        {
            int n = Models.AppSettings.Current.NeverKill.Count;
            txtNeverStatus.Text = n == 0
                ? "Nothing is marked yet. Programs you switch on stay out of Free up resources."
                : $"{n} program{(n == 1 ? "" : "s")} will never be stopped by Free up resources.";
        }

        private void NeverToggled(string name, bool on)
        {
            var list = Models.AppSettings.Current.NeverKill;
            list.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (on) list.Add(name);
            Models.AppSettings.Save();
            ProcessClassifier.Changed();
            _cleanScanned = false; // the Free up list is out of date; it rescans next time it is opened
            UpdateNeverStatus();
        }

        private void NeverSearchChanged(object sender, TextChangedEventArgs e)
        {
            var q = (txtNeverSearch.Text ?? "").Trim().ToLowerInvariant();
            foreach (var r in _neverRows)
                r.Box.Visibility = q.Length == 0 || r.Text.Contains(q) ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------- free up resources (one press = one run) ----------

        private readonly Dictionary<string, CheckBox> _cleanBoxes = new();
        private List<CleanTarget> _cleanTargets = new();
        private bool _cleanBusy, _cleanScanned, _cleanScanning;
        private List<CleanTarget> _pendingClean = new();

        /// <summary>Looks at what is running on this PC and lists, individually, what can be stopped. Runs the first time the tab opens, and on demand.</summary>
        private async void StartCleanScan()
        {
            if (_cleanScanning) return;
            _cleanScanning = true;
            btnCleanScan.IsEnabled = false;
            txtCleanScan.Text = "Scanning this PC for software you can safely stop...";
            List<CleanTarget> found;
            try { found = await Task.Run(CleanupScanner.Scan); }
            catch { found = new List<CleanTarget>(); }
            _cleanScanning = false;
            _cleanScanned = true;
            btnCleanScan.IsEnabled = true;
            _cleanTargets = found;
            BuildClean();
        }

        private void CleanScanClicked(object sender, RoutedEventArgs e) => StartCleanScan();

        private void CleanUntickClicked(object sender, RoutedEventArgs e)
        {
            foreach (var box in _cleanBoxes.Values) box.IsChecked = false;
            SaveCleanChoice();
        }

        private void BuildClean()
        {
            var saved = Models.AppSettings.Current.CleanTargets;
            panelClean.Children.Clear();
            _cleanBoxes.Clear();

            foreach (var group in new[] { RamCleaner.ThirdParty, RamCleaner.Microsoft })
            {
                var items = _cleanTargets.Where(x => x.Group == group).ToList();
                double mb = items.Sum(x => x.MemMB);
                panelClean.Children.Add(new TextBlock
                {
                    Text = items.Count == 0 ? group : $"{group}  ({items.Count})",
                    Style = (Style)FindResource("GroupTitle"),
                    Margin = new Thickness(4, group == RamCleaner.ThirdParty ? 0 : 20, 0, 10),
                });
                var stack = new StackPanel();
                if (items.Count == 0)
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = "Nothing safe to stop was found here right now.", Margin = new Thickness(16, 14, 16, 14),
                        Foreground = (System.Windows.Media.Brush)FindResource("TextDimBrush"),
                    });
                }
                bool first = true;
                foreach (var t in items)
                {
                    if (!first) stack.Children.Add(new Border { Style = (Style)FindResource("Divider") });
                    first = false;

                    var box = new CheckBox { Content = t.Name, Tag = t.Short, IsChecked = saved.Contains(t.Id), Style = (Style)FindResource("UsageSwitch") };
                    Controls.Usage.SetText(box, FormatMem(t.MemMB));
                    box.ToolTip = new TextBlock { Text = t.Hint, TextWrapping = TextWrapping.Wrap, MaxWidth = 360, FontSize = 12.5 };
                    ToolTipService.SetShowDuration(box, 60000);
                    ToolTipService.SetInitialShowDelay(box, 250);
                    box.Click += (_, _) => SaveCleanChoice();
                    _cleanBoxes[t.Id] = box;
                    stack.Children.Add(box);
                }
                panelClean.Children.Add(new Border { Style = (Style)FindResource("Card"), Child = stack });
            }

            double total = _cleanTargets.Sum(x => x.MemMB);
            txtCleanScan.Text = _cleanTargets.Count == 0
                ? "Scan finished. Nothing safe to stop was found."
                : $"Found {_cleanTargets.Count} things you can stop, using about {(total >= 1024 ? (total / 1024).ToString("0.0") + " GB" : Math.Round(total) + " MB")} between them. Hover an item to see exactly what stopping it does.";
            UpdateCleanButtons();
        }

        private List<CleanTarget> Chosen() =>
            _cleanTargets.Where(t => _cleanBoxes.TryGetValue(t.Id, out var b) && b.IsChecked == true).ToList();

        private void SaveCleanChoice()
        {
            Models.AppSettings.Current.CleanTargets = Chosen().Select(t => t.Id).ToList();
            Models.AppSettings.Save();
            panelCleanConfirm.Visibility = Visibility.Collapsed;
            UpdateCleanButtons();
        }

        private void UpdateCleanButtons()
        {
            btnCleanRun.IsEnabled = !_cleanBusy && Chosen().Count > 0;
            btnCleanRestore.IsEnabled = !_cleanBusy && RamCleaner.HasRestorePoint;
        }

        private void CleanRunClicked(object sender, RoutedEventArgs e)
        {
            _pendingClean = Chosen();
            if (_pendingClean.Count == 0) return;
            bool caution = _pendingClean.Any(t => t.Caution);
            txtCleanConfirm.Text = "Stop now: " + string.Join(", ", _pendingClean.Select(t => t.Name)) + "."
                + (caution ? " Some of these are marked Caution, so check you do not need them right now." : "")
                + " This runs one time. You can bring services back with Restore.";
            panelCleanConfirm.Visibility = Visibility.Visible;
        }

        private void CleanCancelClicked(object sender, RoutedEventArgs e) => panelCleanConfirm.Visibility = Visibility.Collapsed;

        private async void CleanConfirmClicked(object sender, RoutedEventArgs e)
        {
            panelCleanConfirm.Visibility = Visibility.Collapsed;
            var run = _pendingClean;
            await RunCleanAsync("Stopping...", () => RamCleaner.Run(run));
        }

        private async void CleanRestoreClicked(object sender, RoutedEventArgs e) =>
            await RunCleanAsync("Restoring...", RamCleaner.Restore);

        private async Task RunCleanAsync(string busyText, Func<string> work)
        {
            if (_cleanBusy) return;
            _cleanBusy = true;
            txtCleanStatus.Text = busyText;
            UpdateCleanButtons();
            try
            {
                txtCleanStatus.Text = await Task.Run(work);
            }
            finally
            {
                _cleanBusy = false;
                UpdateCleanButtons();
            }
            RefreshProcesses();
            StartCleanScan(); // what was stopped no longer shows as running, so show the new picture
        }
    }
}
