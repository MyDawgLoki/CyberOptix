using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using CyberOptix.Models;
using CyberOptix.Services;
using CyberOptix.Widgets;

namespace CyberOptix.Pages
{
    public class PinnedVm
    {
        public string Path { get; init; } = "";
        public string Name { get; init; } = "";
        public ImageSource? Icon { get; init; }
    }

    public partial class DockPage : UserControl
    {
        private bool _loading = true;

        public DockPage()
        {
            InitializeComponent();
            wheelGlow.ColorCommitted += hex => { if (!_loading) SetGlowColor(hex); };   // the glow color is saved when a choice is finished
        }

        public void Initialize()
        {
            _loading = true;
            var s = AppSettings.Current;
            chkDock.IsChecked = s.DockEnabled;
            cmbPosition.SelectedIndex = s.DockPosition == "Top" ? 1 : 0;
            cmbLayer.SelectedIndex = s.DockLayer == "Fall" ? 1 : s.DockLayer == "Stick" ? 2 : 0;
            chkStartBottom.IsChecked = s.DockStartAtBottom;
            chkSearchBottom.IsChecked = s.DockSearchAtBottom;
            sldSize.Value = s.DockIconSize;
            chkWidgetAuto.IsChecked = s.DockWidgetAuto;
            sldWidget.Value = s.DockWidgetAuto ? AppSettings.AutoWidgetPx(s.DockIconSize) : s.DockWidgetSize;
            sldWidget.IsEnabled = !s.DockWidgetAuto;
            chkClockAuto.IsChecked = s.DockClockAuto;
            sldClock.Value = s.DockClockAuto ? DockSystemStrip.AutoClockSize(WidgetPx(s)) : s.DockClockSize;
            sldClock.IsEnabled = !s.DockClockAuto;
            chkRunning.IsChecked = s.DockShowRunning;
            chkFullscreen.IsChecked = s.DockHideFullscreen;
            chkSystem.IsChecked = s.DockShowSystem;
            chkRecycle.IsChecked = s.DockShowRecycleBin;
            chkGlow.IsChecked = s.DockGlow;
            chkGlowMedia.IsChecked = s.DockMatchMedia;
            BuildGlowColors();
            chkBadges.IsChecked = s.DockShowBadges;
            chkFlashing.IsChecked = s.DockShowFlashing;
            chkAllDisplays.IsChecked = s.DockAllDisplays;
            cmbAppsOn.SelectedIndex = s.DockAppsOn == "All" ? 0 : s.DockAppsOn == "Window" ? 2 : 1;
            cmbAppsOn.IsEnabled = s.DockAllDisplays;
            chkReplace.IsChecked = s.DockReplaceTaskbar;
            chkFloat.IsChecked = s.DockFloatOverWindows;
            _loading = false;

            BuildExtras();
            BuildPanels();
            DockService.ReplaceChanged += () => Dispatcher.Invoke(() =>
            {
                _loading = true;
                chkReplace.IsChecked = false;
                _loading = false;
                txtStatus.Text = "The Windows taskbar is back.";
            });

            RefreshPinned();
            DockService.PinnedChanged += () => Dispatcher.Invoke(RefreshPinned);

            if (s.DockEnabled) DockService.Apply(); // bring the dock back if it was on last time
        }

        private void RefreshPinned()
        {
            var items = AppSettings.Current.DockPinned
                .Select(p => new PinnedVm { Path = p, Name = DockService.DisplayName(p), Icon = DockService.IconFor(p) })
                .ToList();
            lstPinned.ItemsSource = items;
            txtNoPinned.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The system icon size in pixels, whether it follows the app icons or was picked by hand.</summary>
        private static int WidgetPx(AppSettings s) => s.DockWidgetAuto ? (int)AppSettings.AutoWidgetPx(s.DockIconSize) : s.DockWidgetSize;

        // CheckBox.Click, ComboBox.SelectionChanged and Slider.ValueChanged all pass a RoutedEventArgs.
        private void Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var s = AppSettings.Current;
            s.DockEnabled = chkDock.IsChecked == true;
            s.DockPosition = cmbPosition.SelectedIndex == 1 ? "Top" : "Bottom";
            s.DockLayer = cmbLayer.SelectedIndex == 1 ? "Fall" : cmbLayer.SelectedIndex == 2 ? "Stick" : "Top";
            s.DockStartAtBottom = chkStartBottom.IsChecked == true;
            s.DockSearchAtBottom = chkSearchBottom.IsChecked == true;
            s.DockIconSize = (int)sldSize.Value;
            s.DockWidgetAuto = chkWidgetAuto.IsChecked == true;
            if (s.DockWidgetAuto)
            {
                // Following the app icons: show the size that gives, and keep the slider out of the way.
                _loading = true;
                sldWidget.Value = AppSettings.AutoWidgetPx(s.DockIconSize);
                _loading = false;
            }
            else
            {
                s.DockWidgetSize = (int)sldWidget.Value;
            }
            sldWidget.IsEnabled = !s.DockWidgetAuto;

            s.DockClockAuto = chkClockAuto.IsChecked == true;
            if (s.DockClockAuto)
            {
                // Following the icons: show the size that gives, and keep the slider out of the way.
                _loading = true;
                sldClock.Value = DockSystemStrip.AutoClockSize(WidgetPx(s));
                _loading = false;
            }
            else
            {
                s.DockClockSize = Math.Round(sldClock.Value * 2) / 2;   // half-pixel steps
            }
            sldClock.IsEnabled = !s.DockClockAuto;
            s.DockShowRunning = chkRunning.IsChecked == true;
            s.DockHideFullscreen = chkFullscreen.IsChecked == true;
            s.DockShowSystem = chkSystem.IsChecked == true;
            s.DockShowRecycleBin = chkRecycle.IsChecked == true;
            s.DockShowBadges = chkBadges.IsChecked == true;
            s.DockShowFlashing = chkFlashing.IsChecked == true;
            s.DockAllDisplays = chkAllDisplays.IsChecked == true;
            s.DockAppsOn = cmbAppsOn.SelectedIndex == 0 ? "All" : cmbAppsOn.SelectedIndex == 2 ? "Window" : "MainAndWindow";
            cmbAppsOn.IsEnabled = s.DockAllDisplays;
            s.DockReplaceTaskbar = chkReplace.IsChecked == true;
            s.DockFloatOverWindows = chkFloat.IsChecked == true;
            if (s.DockReplaceTaskbar && !s.DockEnabled)
            {
                s.DockEnabled = true; // the dock has to be on to stand in for the taskbar
                _loading = true; chkDock.IsChecked = true; _loading = false;
            }
            AppSettings.Save();
            DockService.Apply();
        }

        // ---------- border glow ----------

        private void BuildGlowColors()
        {
            var s = AppSettings.Current;
            wheelGlow.Hex = s.DockGlowColor;
            panelGlow.IsEnabled = s.DockGlow;
            panelGlow.Opacity = s.DockGlow ? 1 : 0.5;
        }

        private void SetGlowColor(string hex)
        {
            AppSettings.Current.DockGlowColor = hex.ToUpperInvariant();
            AppSettings.Save();
            DockService.Apply();
        }
        private void GlowMediaChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.DockMatchMedia = chkGlowMedia.IsChecked == true;
            AppSettings.Save();
            MediaColorService.Sync();   // starts or stops following the media player
            DockService.Apply();
        }

        private void GlowChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.DockGlow = chkGlow.IsChecked == true;
            panelGlow.IsEnabled = AppSettings.Current.DockGlow;
            panelGlow.Opacity = AppSettings.Current.DockGlow ? 1 : 0.5;
            AppSettings.Save();
            DockService.Apply();
        }

        // ---------- extra system icons ----------

        private readonly Dictionary<string, CheckBox> _extraBoxes = new();

        private void BuildExtras()
        {
            _loading = true;
            panelExtras.Children.Clear();
            _extraBoxes.Clear();
            var chosen = new HashSet<string>(AppSettings.Current.DockExtraIcons);

            foreach (var group in DockWidgetCatalog.Groups)
            {
                var members = DockWidgetCatalog.InGroup(group).Where(DockWidgetCatalog.Available).ToList();
                if (members.Count == 0 && !DockWidgetCatalog.DockableGroups.Contains(group)) continue;   // for example a hardware app that is not installed on this PC
                panelExtras.Children.Add(GroupHeader(group));
                var stack = new StackPanel();
                var dropGroup = group;
                if (group != DockWidgetCatalog.Live)
                {
                    stack.AllowDrop = true;
                    stack.Background = Brushes.Transparent;
                    stack.DragOver += (_, e) => DragOverIcon(e);
                    stack.Drop += (_, e) => DropIcon(e, dropGroup, null);
                    if (members.Count == 0)
                        stack.Children.Add(new TextBlock { Text = "Drag an icon here by its three lines to put it in this dock.", Style = (Style)FindResource("Note"), Margin = new Thickness(16, 12, 16, 12) });
                }
                bool first = true;
                foreach (var def in members)
                {
                    if (!first) stack.Children.Add(new Border { Style = (Style)FindResource("Divider") });
                    first = false;

                    var box = new CheckBox { Content = def.Title, Tag = def.Description, IsChecked = chosen.Contains(def.Id) };
                    System.Windows.Automation.AutomationProperties.SetAutomationId(box, "extra_" + def.Id);
                    // Checked/Unchecked rather than Click, so it also reacts to keyboard and screen-reader toggles.
                    box.Checked += ExtraChanged;
                    box.Unchecked += ExtraChanged;
                    _extraBoxes[def.Id] = box;
                    // each row: the three-line handle (drag it to another place or another dock), the switch, and (for programs and folders you added) a Remove button
                    var row = new Grid { Background = Brushes.Transparent };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var rowId = def.Id; var rowGroup = group;
                    Grid.SetColumn(box, 1);
                    row.Children.Add(box);
                    if (group != DockWidgetCatalog.Live)
                    {
                        var handle = new TextBlock
                        {
                            Text = "\uE700", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 16, Margin = new Thickness(14, 0, 4, 0), Padding = new Thickness(4, 8, 8, 8),
                            VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeAll, Background = Brushes.Transparent, Opacity = 0.75,
                            ToolTip = "Drag to move this icon: up or down in its dock, or into another dock",
                        };
                        handle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                        handle.MouseLeftButtonDown += (_, e) =>
                        {
                            e.Handled = true;
                            DragDrop.DoDragDrop(handle, new DataObject(IconFormat, rowId), DragDropEffects.Move);
                            row.Background = Brushes.Transparent;
                        };
                        row.Children.Add(handle);
                        row.AllowDrop = true;
                        row.DragOver += (_, e) => { DragOverIcon(e); if (e.Effects != DragDropEffects.None) row.Background = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0xB0, 0xFF)); };
                        row.DragLeave += (_, _) => row.Background = Brushes.Transparent;
                        row.Drop += (_, e) => { row.Background = Brushes.Transparent; DropIcon(e, rowGroup, rowId); };
                    }
                    if (def.Id.StartsWith("custom_", StringComparison.Ordinal))
                    {
                        var remove = new Button { Content = "Remove", MinHeight = 28, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(6, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
                        remove.Click += (_, _) => { DockWidgetCatalog.RemoveCustom(rowId); BuildExtras(); DockService.Apply(); };
                        Grid.SetColumn(remove, 2);
                        row.Children.Add(remove);
                    }
                    stack.Children.Add(row);
                }

                if (group is DockWidgetCatalog.SystemControls or DockWidgetCatalog.Software or DockWidgetCatalog.Hardware)
                {
                    stack.Children.Add(new Border { Style = (Style)FindResource("Divider") });
                    var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 12, 16, 12) };
                    var addGroup = group;
                    var add = new Button { Content = "Add a program or shortcut...", ToolTip = "Adds any program or shortcut as an icon in this group, for example a restart-to-BIOS tool. It opens that program when clicked." };
                    add.Click += (_, _) => AddProgramClicked(addGroup);
                    var addFolder = new Button { Content = "Add a folder...", Margin = new Thickness(10, 0, 0, 0), ToolTip = "Adds a folder as an icon in this group. It opens that folder when clicked." };
                    addFolder.Click += (_, _) => AddFolderClicked(addGroup);
                    buttons.Children.Add(add);
                    buttons.Children.Add(addFolder);
                    if (group is DockWidgetCatalog.Software or DockWidgetCatalog.Hardware)
                    {
                        var find = new Button { Content = "Find my software...", Margin = new Thickness(10, 0, 0, 0), ToolTip = "Looks through your Start menu for the programs installed on this PC and lets you pick which ones become icons here." };
                        find.Click += (_, _) => FindSoftwareClicked(addGroup);
                        buttons.Children.Add(find);
                    }
                    stack.Children.Add(buttons);
                }
                panelExtras.Children.Add(new Border { Style = (Style)FindResource("Card"), Child = stack });
            }
            _loading = false;
        }

        private const string IconFormat = "CyberOptix.DockIcon";

        private static void DragOverIcon(DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(IconFormat) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>An icon was dropped on a row (it goes just before that icon) or on the empty part of a group (it goes to the end of that group's dock).</summary>
        private void DropIcon(DragEventArgs e, string group, string? beforeId)
        {
            e.Handled = true;
            if (!e.Data.GetDataPresent(IconFormat) || e.Data.GetData(IconFormat) is not string id) return;
            if (!DockWidgetCatalog.MoveTo(id, group, beforeId)) return;
            // rebuild after the drop has finished, since the rows being rebuilt are the ones the drop came from
            Dispatcher.BeginInvoke(new Action(() => { BuildExtras(); DockService.Apply(); }));
        }

        /// <summary>"Find my software": lists the installed programs and adds the ticked ones as icons in this group.</summary>
        private void FindSoftwareClicked(string group)
        {
            var window = new SoftwareFinderWindow(group) { Owner = Window.GetWindow(this) };
            if (window.ShowDialog() != true || window.Added == 0) return;
            BuildExtras();
            DockService.Apply();
        }

        /// <summary>Picks a program or shortcut and adds it as an icon of its own (it opens that program when clicked).</summary>
        private void AddProgramClicked(string group)
        {
            var dlg = new OpenFileDialog { Filter = "Programs and shortcuts|*.exe;*.lnk;*.bat;*.cmd|All files|*.*", Title = "Choose a program or shortcut to add as a dock icon" };
            if (dlg.ShowDialog() != true) return;
            DockWidgetCatalog.AddCustom(dlg.FileName, System.IO.Path.GetFileNameWithoutExtension(dlg.FileName), group);
            BuildExtras();
            DockService.Apply();
        }

        /// <summary>Picks a folder and adds it as an icon of its own (it opens that folder when clicked).</summary>
        private void AddFolderClicked(string group)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder to add as a dock icon" };
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.FolderName)) return;
            var name = new System.IO.DirectoryInfo(dlg.FolderName).Name;
            DockWidgetCatalog.AddCustom(dlg.FolderName, string.IsNullOrWhiteSpace(name) ? dlg.FolderName : name, group);
            BuildExtras();
            DockService.Apply();
        }

        /// <summary>
        /// A group's name. The groups that can have a dock of their own (AI platforms, social, hardware software) also get an "Individual Dock"
        /// switch beside it, and, once it is on, the edge of the screen that dock sticks to.
        /// </summary>
        private FrameworkElement GroupHeader(string group)
        {
            var title = new TextBlock { Text = group, Style = (Style)FindResource("GroupTitle"), Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
            if (!DockWidgetCatalog.DockableGroups.Contains(group))
            {
                title.Margin = new Thickness(4, 18, 0, 10);
                return title;
            }

            var s = AppSettings.Current;
            var grid = new Grid { Margin = new Thickness(0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(title);

            var side = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var edge = new ComboBox { Width = 190, Margin = new Thickness(0, 0, 14, 0), ToolTip = "Where this dock sits: an edge of the screen, a corner, or to the left or right of the main dock." };
            string currentKey = s.IndividualDocks.TryGetValue(group, out var current) ? (current == "Beside" ? "BesideRight" : current) : "Top";
            foreach (var (key, name) in IconGroupDockWindow.Positions)
            {
                var item = new ComboBoxItem { Content = name, Tag = key };
                edge.Items.Add(item);
                if (key == currentKey) edge.SelectedItem = item;
            }
            if (edge.SelectedItem == null) edge.SelectedIndex = 0;
            edge.Visibility = s.IndividualDocks.ContainsKey(group) ? Visibility.Visible : Visibility.Collapsed;
            bool second = group == DockWidgetCatalog.SystemControls;
            var toggle = new CheckBox
            {
                Content = second ? "Second Dock" : "Individual Dock", IsChecked = s.IndividualDocks.ContainsKey(group),
                ToolTip = "Gives these icons a dock of their own, in the place you pick, instead of the main dock.",
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(toggle, "individual_" + group);
            toggle.Click += (_, _) =>
            {
                if (_loading) return;
                if (toggle.IsChecked == true) { AppSettings.Current.IndividualDocks[group] = (edge.SelectedItem as ComboBoxItem)?.Tag as string ?? "Top"; IconGroupDockService.NotePlaced(group); }
                else AppSettings.Current.IndividualDocks.Remove(group);
                edge.Visibility = toggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                AppSettings.Save();
                DockService.Apply();
            };
            edge.SelectionChanged += (_, _) =>
            {
                if (_loading || (edge.SelectedItem as ComboBoxItem)?.Tag is not string pos || !AppSettings.Current.IndividualDocks.ContainsKey(group)) return;
                AppSettings.Current.IndividualDocks[group] = pos;
                IconGroupDockService.NotePlaced(group);
                AppSettings.Save();
                DockService.Apply();
            };
            side.Children.Add(edge);
            side.Children.Add(toggle);
            Grid.SetColumn(side, 1);
            grid.Children.Add(side);

            // how this dock sits among the windows, the same three choices as the main dock
            var layerRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
            layerRow.Children.Add(new TextBlock { Text = "Dock and windows", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Foreground = (Brush)FindResource("TextDimBrush") });
            var layer = new ComboBox { Width = 210, ToolTip = "Stay on top keeps this dock in front. Fall behind (the default) lets windows cover it: move the pointer onto it (or the screen edge it sits on) to bring it forward. Stick behind covers it the same way, but only the Start menu brings it forward." };
            string[] layerKeys = { "Top", "Fall", "Stick" };
            foreach (var name in new[] { "Stay On Top Of Windows", "Fall Behind Windows (Default)", "Stick Behind Windows" }) layer.Items.Add(new ComboBoxItem { Content = name });
            layer.SelectedIndex = Array.IndexOf(layerKeys, IconGroupDockWindow.LayerFor(group));
            layer.SelectionChanged += (_, _) =>
            {
                if (_loading || layer.SelectedIndex < 0) return;
                AppSettings.Current.IndividualDockLayers[group] = layerKeys[layer.SelectedIndex];
                AppSettings.Save();   // the dock reads it on its own, within a moment
            };
            layerRow.Children.Add(layer);
            layerRow.Visibility = s.IndividualDocks.ContainsKey(group) ? Visibility.Visible : Visibility.Collapsed;
            toggle.Click += (_, _) => layerRow.Visibility = toggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            var both = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            both.Children.Add(grid);
            both.Children.Add(layerRow);
            return both;
        }

        private void ExtraChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            // Kept in catalog order, so the icons always appear in the same order.
            AppSettings.Current.DockExtraIcons = DockWidgetCatalog.All.Where(d => _extraBoxes.TryGetValue(d.Id, out var b) && b.IsChecked == true).Select(d => d.Id).ToList();
            AppSettings.Save();
            DockService.Apply();
        }

        private void AddClicked(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Programs and shortcuts|*.exe;*.lnk", Title = "Choose an app to pin" };
            if (dlg.ShowDialog() != true) return;

            DockService.Pin(dlg.FileName);
            txtStatus.Text = $"Pinned {DockService.DisplayName(DockService.ResolveShortcut(dlg.FileName))}.";
        }

        private void RemoveClicked(object sender, RoutedEventArgs e)
        {
            var path = (string)((FrameworkElement)sender).Tag;
            DockService.Unpin(path);
            txtStatus.Text = "Removed from the dock.";
        }

        private void BringBackClicked(object sender, RoutedEventArgs e)
        {
            AppSettings.Current.DockReplaceTaskbar = false;
            AppSettings.Save();
            _loading = true; chkReplace.IsChecked = false; _loading = false;
            DockService.Apply();
            txtStatus.Text = "The Windows taskbar is back.";
        }

        // ---------- app panels ----------

        private void BuildPanels()
        {
            panelPanels.Children.Clear();
            foreach (var panel in AppSettings.Current.AppPanels.ToList())
            {
                var stack = new StackPanel();
                string key = "panel:" + panel.Id;

                var title = new TextBox { Text = panel.Title, Tag = "Panel name", Margin = new Thickness(16, 14, 16, 8), MaxLength = 24 };
                title.LostFocus += (_, _) =>
                {
                    panel.Title = string.IsNullOrWhiteSpace(title.Text) ? "Apps" : title.Text.Trim();
                    AppSettings.Save();
                    RefreshPanelWidget(key);
                };
                stack.Children.Add(title);

                var show = new CheckBox { Content = "Show on the desktop", Tag = "Drag the panel anywhere. Use its x to hide it again.", IsChecked = WidgetManager.IsEnabled(key) };
                show.Click += (_, _) =>
                {
                    WidgetManager.SetEnabled(key, show.IsChecked == true);
                    WidgetManager.Apply();
                };
                stack.Children.Add(new Border { Style = (Style)FindResource("Divider") });
                stack.Children.Add(show);

                foreach (var path in panel.Apps.ToList())
                {
                    stack.Children.Add(new Border { Style = (Style)FindResource("Divider") });
                    var row = new Grid { Margin = new Thickness(16, 8, 16, 8) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(new Image { Source = DockService.IconFor(path), Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center });
                    var name = new TextBlock { Text = DockService.DisplayName(path), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                    Grid.SetColumn(name, 1);
                    row.Children.Add(name);
                    var remove = new Button { Content = "Remove", MinHeight = 30, Padding = new Thickness(14, 0, 14, 0) };
                    remove.Click += (_, _) => { panel.Apps.Remove(path); AppSettings.Save(); RefreshPanelWidget(key); BuildPanels(); };
                    Grid.SetColumn(remove, 2);
                    row.Children.Add(remove);
                    stack.Children.Add(row);
                }

                stack.Children.Add(new Border { Style = (Style)FindResource("Divider") });
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 12, 16, 14) };
                var add = new Button { Content = "Add an app" };
                add.Click += (_, _) =>
                {
                    var dlg = new OpenFileDialog { Filter = "Programs and shortcuts|*.exe;*.lnk", Title = "Choose an app for this panel" };
                    if (dlg.ShowDialog() != true) return;
                    var path = DockService.ResolveShortcut(dlg.FileName);
                    if (!panel.Apps.Contains(path, StringComparer.OrdinalIgnoreCase)) panel.Apps.Add(path);
                    AppSettings.Save();
                    RefreshPanelWidget(key);
                    BuildPanels();
                };
                var delete = new Button { Content = "Delete panel", Margin = new Thickness(10, 0, 0, 0) };
                delete.Click += (_, _) =>
                {
                    WidgetManager.Remove(key);
                    AppSettings.Current.AppPanels.Remove(panel);
                    AppSettings.Save();
                    BuildPanels();
                };
                buttons.Children.Add(add);
                buttons.Children.Add(delete);
                stack.Children.Add(buttons);

                panelPanels.Children.Add(new Border { Style = (Style)FindResource("Card"), Child = stack, Margin = new Thickness(0, 0, 0, 14) });
            }
        }

        private static void RefreshPanelWidget(string key)
        {
            if (!WidgetManager.IsEnabled(key)) return;
            WidgetManager.Remove(key);          // close it, then bring it back with the new tiles
            WidgetManager.SetEnabled(key, true);
            WidgetManager.Apply();
        }

        private void NewPanelClicked(object sender, RoutedEventArgs e)
        {
            AppSettings.Current.AppPanels.Add(new AppPanel { Title = $"Apps {AppSettings.Current.AppPanels.Count + 1}" });
            AppSettings.Save();
            BuildPanels();
            txtPanelStatus.Text = "Panel added. Add some apps, then switch on Show on the desktop.";
        }
    }
}
