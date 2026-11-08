using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CyberOptix.Models;
using CyberOptix.Services;
using CyberOptix.Widgets;

namespace CyberOptix
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<Game> _allGames = new();

        // Each Optimizer tab edits its own draft; "Save" copies the draft into that tab's saved profile and applies it.
        private SettingsProfile _gamingDraft = AppSettings.Current.GamingProfile.Clone();
        private SettingsProfile _generalDraft = AppSettings.Current.GeneralProfile.Clone();

        private bool _ready;       // false while XAML is still building the window
        private bool _loadingUi;   // true while we push a profile into the controls (so that is not seen as an edit)
        private bool _applying;
        private string _lastNote = "Ready";

        public MainWindow()
        {
            InitializeComponent();

            // The glowing edge: a ring just inside the window's border. The grid clips it, so only the glow falling inwards shows.
            var glowRing = new Border { IsHitTestVisible = false, BorderThickness = new Thickness(2), Visibility = Visibility.Collapsed };
            Root.ClipToBounds = true;
            Grid.SetColumnSpan(glowRing, 2);
            System.Windows.Controls.Panel.SetZIndex(glowRing, 1000);
            Root.Children.Add(glowRing);
            WindowGlow.Attach(this, c => WindowGlow.ApplyInner(glowRing, c));

            // Real Windows 11 blur-behind; fall back to a solid dark ground where unsupported.
            SourceInitialized += (_, _) =>
            {
                WindowEffects.RemoveNativeCaptionButtons(this);
                ThemeService.Apply(); // saved accent/tint/opacity and the backdrop (with a solid fallback before Windows 11)
            };
            StateChanged += (_, _) => glyphMax.Text = WindowState == WindowState.Maximized ? "" : "";

            StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) TrimMemory(); };
            StateChanged += (_, _) => UpdateBackgroundVideo();
            IsVisibleChanged += (_, _) => { if (IsVisible) ThemeService.RetryVideo(); UpdateBackgroundVideo(); };

            // Minimizing can send the window to the dock or the system tray instead of the taskbar.
            StateChanged += (_, _) =>
            {
                if (WindowState != WindowState.Minimized) return;
                var to = AppSettings.Current.MinimizeTo;
                bool toTray = to == "Tray" && GlassCursor.GlassCursorHost.IsRunning;
                bool toDock = to == "Dock" && DockService.IsRunning;
                if (!toTray && !toDock) return;   // the dock or the tray is not there, so minimize the usual way
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DockService.MainHiddenInDock = toDock;
                    Hide();
                    DockService.RefreshMain();
                }), DispatcherPriority.Background);
            };

            // Closing the window keeps CyberOptix running in the tray (Glass Cursor, the dock, widgets and hotkeys keep working).
            Closing += (_, e) =>
            {
                if (_exiting || !AppSettings.Current.CloseToTray || !GlassCursor.GlassCursorHost.IsRunning) return;
                e.Cancel = true;
                Hide();
                Dispatcher.BeginInvoke(TrimMemory, DispatcherPriority.ApplicationIdle);
            };
            Application.Current.SessionEnding += (_, _) => _exiting = true; // never hold up a Windows shutdown
            Deactivated += (_, _) => Dispatcher.BeginInvoke(TrimMemory, DispatcherPriority.ApplicationIdle);

            BuildCatalog();
            LoadGames();
            lstGames.ItemsSource = _allGames;
            _ready = true;
            ShowProfile();

            Loaded += (_, _) =>
            {
                OriginalSettingsService.Start(); // remember how Windows is set up now, before CyberOptix changes anything
                pgTaskbar.Initialize();
                pgWindows.Initialize();
                pgSettings.Initialize();
                if (!AppSettings.Current.KeepWindowsSettingsOnRelaunch) DisplayColorService.ApplySaved(); // put back the colors the user set on each display (unless asked to leave Windows alone)
                if (AppSettings.Current.SyncWindowsAccent && !AppSettings.Current.KeepWindowsSettingsOnRelaunch) WindowsAccentService.Apply(AppSettings.Current.AccentHex);
                TaskbarTakeoverService.RecoverIfStranded(); // an earlier crash must never leave the Windows taskbar hidden
                pgHardware.Initialize(); // pointer, cursor, lighting, hot-keys and remaps
                pgDock.Initialize();
                AppsPanelService.RestorePinned(); // the pinned Apps panel comes back where it was // brings the dock back if it was on last time
                pgWidgetsTab.Initialize();
                MediaColorService.Sync();   // follow the media player's color if that setting is on
                if (!AppSettings.Current.DockWidgetsIconOffered)
                {
                    // The Widgets icon is new: add it to the dock once. Removing it later stays removed.
                    AppSettings.Current.DockWidgetsIconOffered = true;
                    if (!AppSettings.Current.DockExtraIcons.Contains("widgets")) AppSettings.Current.DockExtraIcons.Add("widgets");
                    AppSettings.Save();
                    DockService.Apply();
                }
                WidgetLock.Init();                  // widgets start locked; the layout they are locked in is remembered
                DesktopMenuService.Sync();          // "Unlock Widgets" and "Restore Widgets Placing" in the desktop right-click menu
                WidgetCommandPipe.Start();          // ...which reach the app through this
                HardwareSoftwareService.Warm();     // reads the Start menu in the background, for the hardware app icons
                pgDisplay.Initialize(); // screens, wallpaper, live wallpaper (restores if it was running) and widgets
                WidgetManager.Apply(); // restore widgets that were on last time
                WidgetManager.StartGameWatch(); // close them while a game has the screen
                GlassCursorService.Begin(ShowFromTray, ExitApp); // Glass Cursor and the tray icon
                AddResetButtons();
                ShowPage("Home");
                _ = System.Threading.Tasks.Task.Run(AutoStartService.Sync);   // make Windows' start-up task match the setting (and this copy of CyberOptix)
                if (_startHidden) FinishHiddenStart();
            };
            // Safety net: if the process dies without Closed running, still give fans back to the firmware.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => HardwareService.Instance.ResetAllFans();
            Closed += (_, _) =>
            {
                HardwareService.Instance.Shutdown(); // also returns any manually set fan to automatic
                DockService.Stop();
                AppsPanelService.StopAll();
                HotkeyService.Stop();
                LightingService.Stop();
                LiveWallpaperService.Stop();
                WindowManagerService.Disable();
                GlassCursorService.End();
                AppSettings.Save();
            };
        }

        /// <summary>The wallpaper video plays only while the window can actually be seen (not minimized, not hidden in the tray).</summary>
        public void UpdateBackgroundVideo() =>
            bgVideo.SetActive(bgVideo.Visibility == Visibility.Visible && IsVisible && WindowState != WindowState.Minimized);

        // ---------- navigation ----------

        private void NavChecked(object sender, RoutedEventArgs e)
        {
            if (!_ready) return; // Checked fires while XAML is still loading
            ShowPage((string)((RadioButton)sender).Tag);
        }

        private void ShowPage(string name)
        {
            FrameworkElement[] pages = { pgHome, pgOptimizer, pgTasks, pgDisplay, pgWidgetsTab, pgHardware, pgDock, pgTaskbar, pgWindows, pgSettings };
            string[] names = { "Home", "Optimizer", "Tasks", "Display", "Widgets", "Hardware", "Dock", "Taskbar", "Windows", "Settings" };

            modeSwitch.Visibility = name == "Optimizer" ? Visibility.Visible : Visibility.Collapsed;

            for (int i = 0; i < pages.Length; i++)
            {
                bool show = names[i] == name;
                pages[i].Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show)
                {
                    FadeIn(pages[i]);
                    // Always open a page at its top, even if something inside it asked to be scrolled into view.
                    if (pages[i] is UserControl { Content: ScrollViewer sv })
                        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(sv.ScrollToTop));
                }
            }
        }

        /// <summary>One authored motion: the page fades in while rising 10px.</summary>
        private static void FadeIn(FrameworkElement page)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var move = new TranslateTransform(0, 10);
            page.RenderTransform = move;
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        }

        private void MinClicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaxClicked(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private bool _exiting;

        private void CloseClicked(object sender, RoutedEventArgs e) => Close();

        private bool _startHidden;

        /// <summary>
        /// For a start by Windows at sign-in: the window loads far off screen with no taskbar button (everything CyberOptix sets up happens
        /// when the window loads), then <see cref="FinishHiddenStart"/> hides it. Call before Show.
        /// </summary>
        public void StartHidden()
        {
            _startHidden = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;
        }

        private void FinishHiddenStart()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Hide();
                // Where it will appear when opened from the tray: the middle of the main display.
                var area = SystemParameters.WorkArea;
                Left = area.Left + Math.Max(0, (area.Width - ActualWidth) / 2);
                Top = area.Top + Math.Max(0, (area.Height - ActualHeight) / 2);
                ShowInTaskbar = true;
                ShowActivated = true;
                _startHidden = false;

                // The tray icon is the way back in. If it did not come up, show the window instead of leaving nothing on screen.
                var check = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                check.Tick += (_, _) =>
                {
                    check.Stop();
                    if (!GlassCursor.GlassCursorHost.IsRunning) ShowFromTray();
                };
                check.Start();
            }), DispatcherPriority.ApplicationIdle);
        }

        /// <summary>Brings the window back from the tray.</summary>
        public void ShowFromTray()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                DockService.MainHiddenInDock = false;
                if (!IsVisible) Show();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
                DockService.RefreshMain();
            }));
        }

        /// <summary>Really quits CyberOptix (the tray's "Exit CyberOptix").</summary>
        public void ExitApp()
        {
            Dispatcher.BeginInvoke(new Action(() => { _exiting = true; Close(); }));
        }

        // ---------- per-tab profiles ----------

        private bool GamingTab => rbGaming.IsChecked == true;
        private SettingsProfile Draft => GamingTab ? _gamingDraft : _generalDraft;
        private SettingsProfile Saved => GamingTab ? AppSettings.Current.GamingProfile : AppSettings.Current.GeneralProfile;
        private string TabName => GamingTab ? "Gaming" : "Universal";   // what the user sees
        private string TabId => GamingTab ? "Gaming" : "General";        // what is saved (kept so existing settings still load)

        private readonly System.Collections.Generic.Dictionary<string, CheckBox> _catalogBoxes = new();

        /// <summary>Builds the extra Windows tweaks (with hover hints) from <see cref="TweakCatalog"/>.</summary>
        private void BuildCatalog()
        {
            foreach (var group in TweakCatalog.Groups)
            {
                panelCatalog.Children.Add(new TextBlock { Text = group, Style = (Style)FindResource("GroupTitle") });
                var stack = new StackPanel();
                bool first = true;
                foreach (var t in TweakCatalog.Available.Where(x => x.Group == group))
                {
                    if (!first) stack.Children.Add(new Border { Style = (Style)FindResource("Divider") });
                    first = false;

                    var box = new CheckBox { Content = t.Title, Tag = t.Short };
                    box.ToolTip = new TextBlock { Text = t.Hint, TextWrapping = TextWrapping.Wrap, MaxWidth = 330, FontSize = 12.5 };
                    ToolTipService.SetShowDuration(box, 60000);
                    ToolTipService.SetInitialShowDelay(box, 250);
                    box.Checked += TweakChanged;
                    box.Unchecked += TweakChanged;
                    _catalogBoxes[t.Id] = box;
                    stack.Children.Add(box);
                }
                panelCatalog.Children.Add(new Border { Style = (Style)FindResource("Card"), Child = stack });
            }
        }

        private void LoadGames()
        {
            // The list starts empty. It fills when the user presses Scan Steam or Add Game, and is remembered after that.
            foreach (var g in AppSettings.Current.CustomGames.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                _allGames.Add(g);
            UpdateGamesEmpty();
        }

        private void UpdateGamesEmpty() =>
            txtNoGames.Visibility = _allGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Pushes the current tab's draft into the controls. Does not apply anything.</summary>
        private void ShowProfile()
        {
            _loadingUi = true;
            try
            {
                var p = Draft;
                chkUltimate.IsChecked = p.UltimatePerformance;
                chkGameDvr.IsChecked = p.DisableGameDVR;
                chkFso.IsChecked = p.DisableFullscreenOptimizations;
                chkHags.IsChecked = p.HardwareAcceleratedGPUScheduling;
                chkVisual.IsChecked = p.DisableVisualEffects;
                chkSysMain.IsChecked = p.DisableSysMain;
                chkNagle.IsChecked = p.DisableNagle;
                foreach (var t in TweakCatalog.Available) _catalogBoxes[t.Id].IsChecked = p.IsOn(t);
                sldVibrance.Value = p.DigitalVibrance;
                sldBrightness.Value = p.Brightness;
                sldContrast.Value = p.Contrast;
                sldHue.Value = p.Hue;
                sldGamma.Value = p.Gamma;
                sldMaxFps.Value = p.MaxFrameRate;
                SelectItem(cmbLatency, p.LowLatencyMode);
                SelectItem(cmbPower, p.PowerManagementMode);

                // Games only matter for the Gaming tab.
                panelGames.Visibility = GamingTab ? Visibility.Visible : Visibility.Collapsed;
                colGames.Width = GamingTab ? new GridLength(290) : new GridLength(0); // the settings use the whole page in Universal
                panelGameButtons.Visibility = GamingTab ? Visibility.Visible : Visibility.Collapsed;
                foreach (var prof in new[] { _gamingDraft, AppSettings.Current.GamingProfile })
                    if (prof.SelectedGame != null && !_allGames.Any(g => g.Name == prof.SelectedGame)) prof.SelectedGame = null;
                lstGames.IsEnabled = GamingTab;
                txtSearch.IsEnabled = GamingTab;
                lstGames.Opacity = GamingTab ? 1 : 0.4;
                SelectGame(GamingTab ? p.SelectedGame : null);

                btnSaveGaming.Visibility = GamingTab ? Visibility.Visible : Visibility.Collapsed;
                btnSaveGeneral.Visibility = GamingTab ? Visibility.Collapsed : Visibility.Visible;
            }
            finally
            {
                _loadingUi = false;
            }
            _lastNote = "Ready";
            UpdateDirty();
        }

        private static void SelectItem(ComboBox box, string text)
        {
            foreach (ComboBoxItem item in box.Items)
            {
                if (string.Equals(item.Content?.ToString(), text, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedItem = item;
                    return;
                }
            }
        }

        private void SelectGame(string? name)
        {
            lstGames.SelectedItem = name == null ? null : _allGames.FirstOrDefault(g => g.Name == name);
        }

        /// <summary>Copies the controls into the current tab's draft.</summary>
        private void ReadControls()
        {
            var p = Draft;
            p.Mode = TabId;
            p.UltimatePerformance = chkUltimate.IsChecked == true;
            p.DisableGameDVR = chkGameDvr.IsChecked == true;
            p.DisableFullscreenOptimizations = chkFso.IsChecked == true;
            p.HardwareAcceleratedGPUScheduling = chkHags.IsChecked == true;
            p.DisableVisualEffects = chkVisual.IsChecked == true;
            p.DisableSysMain = chkSysMain.IsChecked == true;
            p.DisableNagle = chkNagle.IsChecked == true;
            foreach (var t in TweakCatalog.Available) p.Tweaks[t.Id] = _catalogBoxes[t.Id].IsChecked == true;
            p.DigitalVibrance = (int)sldVibrance.Value;
            p.Brightness = (int)sldBrightness.Value;
            p.Contrast = (int)sldContrast.Value;
            p.Hue = (int)sldHue.Value;
            p.Gamma = Math.Round(sldGamma.Value, 2);
            p.MaxFrameRate = (int)sldMaxFps.Value;
            p.LowLatencyMode = (cmbLatency.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? p.LowLatencyMode;
            p.PowerManagementMode = (cmbPower.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? p.PowerManagementMode;
        }

        private void UpdateDirty()
        {
            bool dirty = !Draft.SameAs(Saved);
            var button = GamingTab ? btnSaveGaming : btnSaveGeneral;
            button.IsEnabled = dirty && !_applying;

            if (dirty)
            {
                txtStatus.Text = $"Unsaved changes in the {TabName} tab";
                dotStatus.Fill = (Brush)FindResource("AccentBrush");
            }
            else
            {
                txtStatus.Text = _lastNote;
                dotStatus.Fill = (Brush)FindResource("TextFaintBrush");
            }
        }

        // ---------- events ----------

        private void ModeChanged(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            ShowProfile(); // show the other tab's own settings; nothing is applied by switching
        }

        private void GameSelected(object sender, SelectionChangedEventArgs e)
        {
            btnRemoveGame.IsEnabled = lstGames.SelectedItem is Game;
            if (!_ready || _loadingUi || !GamingTab) return;
            if (lstGames.SelectedItem is Game g)
            {
                _gamingDraft.SelectedGame = g.Name;
                UpdateDirty();
            }
        }

        // ---------- adding games ----------

        private void ShowGameStatus(string text)
        {
            txtGameStatus.Text = text;
            txtGameStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Puts new games into the list (kept in name order after the built-in ones) and refreshes the search filter.</summary>
        private int AddToList(System.Collections.Generic.IEnumerable<Game> games, bool fromScan)
        {
            var fresh = GameLibraryService.AddCustom(games, fromScan);
            foreach (var g in fresh.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) _allGames.Add(g);
            UpdateGamesEmpty();
            SearchChanged(txtSearch, null!);
            return fresh.Count;
        }

        private async void ScanSteamClicked(object sender, RoutedEventArgs e)
        {
            var steam = GameLibraryService.FindSteam();
            if (steam == null)
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Find your Steam folder (the one that contains steamapps)" };
                if (dlg.ShowDialog() != true) return;
                if (!System.IO.Directory.Exists(System.IO.Path.Combine(dlg.FolderName, "steamapps")))
                {
                    ShowGameStatus("That folder has no steamapps inside it, so it isn't a Steam folder.");
                    return;
                }
                steam = dlg.FolderName;
            }

            btnScanSteam.IsEnabled = false;
            ShowGameStatus("Scanning Steam...");
            try
            {
                var found = await Task.Run(() => GameLibraryService.ScanSteam(steam));
                int added = AddToList(found, fromScan: true);
                int skipped = found.Count - added;
                ShowGameStatus(found.Count == 0
                    ? "No installed Steam games were found."
                    : $"Found {found.Count} Steam game{(found.Count == 1 ? "" : "s")}. {added} added to the list" +
                      (skipped > 0 ? $", {skipped} already there or removed by you." : "."));
            }
            catch (Exception ex)
            {
                ShowGameStatus($"Could not read Steam: {ex.Message}");
            }
            finally
            {
                btnScanSteam.IsEnabled = true;
            }
        }

        private void AddGameFolderClicked(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a game's install folder" };
            if (dlg.ShowDialog() != true) return;

            var path = dlg.FolderName.TrimEnd('\\', '/');
            var name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name)) { ShowGameStatus("Pick the game's own folder, not a whole drive."); return; }

            int added = AddToList(new[] { new Game { Name = name, Path = path, Source = "Folder" } }, fromScan: false);
            if (added > 0) lstGames.SelectedItem = _allGames.FirstOrDefault(g => g.Name == name);
            ShowGameStatus(added > 0 ? $"Added {name}." : $"{name} is already in the list.");
        }

        private void GameMenuOpened(object sender, RoutedEventArgs e) =>
            menuRemoveGame.IsEnabled = lstGames.SelectedItem is Game;

        /// <summary>Takes a game out of this list only. Its files are never touched.</summary>
        private void RemoveGameClicked(object sender, RoutedEventArgs e)
        {
            if (lstGames.SelectedItem is not Game g) return;
            GameLibraryService.RemoveCustom(g.Name);
            foreach (var prof in new[] { _gamingDraft, AppSettings.Current.GamingProfile })
                if (prof.SelectedGame == g.Name) prof.SelectedGame = null;
            _allGames.Remove(g);
            UpdateGamesEmpty();
            SearchChanged(txtSearch, null!);
            ShowGameStatus($"Removed {g.Name} from the list. The game itself was not touched.");
            btnRemoveGame.IsEnabled = false;
            UpdateDirty();
        }

        private void SearchChanged(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            var q = txtSearch.Text.ToLower();
            _loadingUi = true; // refiltering clears the selection; that is not an edit
            lstGames.ItemsSource = string.IsNullOrWhiteSpace(q)
                ? _allGames
                : _allGames.Where(g => g.Name.ToLower().Contains(q)).ToList();
            SelectGame(GamingTab ? _gamingDraft.SelectedGame : null);
            _loadingUi = false;
        }

        // Checkboxes, sliders and combo boxes all land here (each derives from RoutedEventArgs).
        private void TweakChanged(object sender, RoutedEventArgs e) => ControlEdited();
        private void NvidiaChanged(object sender, RoutedEventArgs e) => ControlEdited();

        private void ControlEdited()
        {
            if (!_ready || _loadingUi) return;
            ReadControls();
            UpdateDirty();
        }

        private void OpenNvidiaClicked(object sender, RoutedEventArgs e) => DisplayColorService.OpenNvidiaControlPanel();

        private async void SaveClicked(object sender, RoutedEventArgs e)
        {
            if (_applying) return;
            ReadControls();

            var snapshot = Draft.Clone();
            if (GamingTab) AppSettings.Current.GamingProfile = snapshot;
            else AppSettings.Current.GeneralProfile = snapshot;
            AppSettings.Save();

            var tab = TabName;
            _applying = true;
            txtStatus.Text = $"Saving {tab} profile and applying it...";
            ((Button)sender).IsEnabled = false;
            try
            {
                await Task.Run(() => TweakService.ApplyProfile(snapshot));

                // Display color goes to the main display (on the UI thread: it talks to Windows' display COM objects).
                var colorNote = DisplayColorService.ApplyProfileToMainDisplay(snapshot);

                _lastNote = $"{tab} profile saved and applied" +
                            (snapshot.SelectedGame != null ? $" ({snapshot.SelectedGame})" : "") +
                            (colorNote != null ? $". Not applied: {colorNote}" : "");
            }
            catch (Exception ex)
            {
                _lastNote = $"{tab} profile saved, but applying failed: {ex.Message}";
            }
            finally
            {
                _applying = false;
            }
            UpdateDirty();
        }
    
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

        /// <summary>Hands unused memory back to Windows when the window is minimized or in the background.</summary>
        private static void TrimMemory()
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
        }
}
}
