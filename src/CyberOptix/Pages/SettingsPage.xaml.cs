using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Pages
{
    public partial class SettingsPage : UserControl
    {
        private bool _loading = true;

        private static readonly (string Name, string Hex)[] FontColors =
        {
            ("Soft white", "#F4EFF8"), ("White", "#FFFFFF"), ("Ice", "#BFF3FF"), ("Mint", "#C9F7DC"),
            ("Amber", "#FFE2A8"), ("Pink", "#FFD0E6"), ("Silver", "#D0D0D8"),
        };

        private static readonly (string Name, string Hex)[] CardColors =
        {
            ("White", "#FFFFFF"), ("Black", "#000000"), ("Blue", "#3B82F6"), ("Turquoise", "#14B8D4"),
            ("Violet", "#B146C2"), ("Green", "#22C55E"), ("Rose", "#F43F5E"),
        };

        public SettingsPage()
        {
            InitializeComponent();
            // The driver row shows only while PawnIO is installed, which can change while the app runs (installed from the Monitor page).
            IsVisibleChanged += (_, _) => { if (IsVisible) rowPawnIo.Visibility = PawnIoService.IsInstalled() ? Visibility.Visible : Visibility.Collapsed; };
            wheelAccent.ColorCommitted += hex => { if (_loading) return; AppSettings.Current.AccentHex = hex.ToUpperInvariant(); Commit(); };
            wheelWin.ColorCommitted += hex => { if (_loading) return; AppSettings.Current.WinColorHex = hex.ToUpperInvariant(); AppSettings.Save(); WindowsAccentService.Refresh(); };
            wheelWindowGlow.ColorCommitted += hex => { if (!_loading) SetWindowGlow(hex); };
        }

        public void Initialize()
        {
            _loading = true;
            var s = AppSettings.Current;

            wheelAccent.Hex = s.AccentHex;

            panelTints.Children.Clear();
            foreach (var (name, hex) in ThemeService.Tints)
                panelTints.Children.Add(MakeSwatch("Tint", name, hex, string.Equals(hex, s.TintHex, StringComparison.OrdinalIgnoreCase), TintChecked));

            panelFonts.Children.Clear();
            foreach (var (name, hex) in FontColors)
                panelFonts.Children.Add(MakeSwatch("Font", name, hex, string.Equals(hex, s.FontHex, StringComparison.OrdinalIgnoreCase), FontChecked));
            txtFont.Text = s.FontHex;

            panelCards.Children.Clear();
            foreach (var (name, hex) in CardColors)
                panelCards.Children.Add(MakeSwatch("Card", name, hex, string.Equals(hex, s.CardHex, StringComparison.OrdinalIgnoreCase), CardChecked));
            txtCard.Text = s.CardHex;
            sldCard.Value = s.CardOpacity;
            chkWindowGlow.IsChecked = s.WindowGlow;
            wheelWindowGlow.Hex = s.WindowGlowColor;
            rowPawnIo.Visibility = PawnIoService.IsInstalled() ? Visibility.Visible : Visibility.Collapsed;
            chkMatchMedia.IsChecked = s.MatchMediaColor;
            chkMatchAccent.IsChecked = s.MatchMediaAccent;
            chkMatchWindow.IsChecked = s.MatchMediaWindow;
            chkMatchText.IsChecked = s.MatchMediaText;
            chkMatchGlow.IsChecked = s.MatchMediaGlow;

            cmbBackdrop.ItemsSource = ThemeService.Backdrops;
            cmbBackdrop.SelectedItem = ThemeService.Backdrops.Contains(s.Backdrop) ? s.Backdrop : ThemeService.Backdrops[^1];
            sldGlass.Value = s.GlassTint;
            sldOpacity.Value = s.WindowOpacity;
            chkBackground.IsChecked = s.BackgroundImage;
            chkBackgroundVideo.IsChecked = s.BackgroundVideo;
            chkStartWithWindows.IsChecked = s.StartWithWindows;
            chkStartQuietly.IsChecked = s.StartQuietly;
            ShowStartupNote();
            chkCloseToTray.IsChecked = s.CloseToTray;
            chkKeepWindows.IsChecked = s.KeepWindowsSettingsOnRelaunch;
            chkWindowsAccent.IsChecked = s.SyncWindowsAccent;
            chkWinMatchMedia.IsChecked = s.WinMatchMedia;
            chkWinOwnColor.IsChecked = s.WinOwnColor;
            wheelWin.Hex = s.WinColorHex;
            cmbWinMode.SelectedIndex = s.WinSystemDark && s.WinAppsDark ? 1 : !s.WinSystemDark && !s.WinAppsDark ? 0 : 2;
            cmbWinSystem.SelectedIndex = s.WinSystemDark ? 1 : 0;
            cmbWinApp.SelectedIndex = s.WinAppsDark ? 1 : 0;
            chkWinTransparency.IsChecked = s.WinTransparency;
            chkWinAccentStart.IsChecked = s.WinAccentOnStart;
            chkWinAccentTitle.IsChecked = s.WinAccentOnTitle;
            ShowWindowsColorRows();
            cmbMinimizeTo.SelectedIndex = s.MinimizeTo == "Dock" ? 1 : s.MinimizeTo == "Tray" ? 2 : 0;
            sldPicture.Value = s.BackgroundImageOpacity;
            rowPicture.Description = string.IsNullOrWhiteSpace(s.BackgroundImagePath) ? "Using the CyberOptix wallpaper." : s.BackgroundImagePath;
            _loading = false;
        }

        // ---------- window glow ----------

        private void WindowGlowChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.WindowGlow = chkWindowGlow.IsChecked == true;
            AppSettings.Save();
            WindowGlow.Refresh();
        }

        private void SetWindowGlow(string hex)
        {
            AppSettings.Current.WindowGlowColor = hex.ToUpperInvariant();
            AppSettings.Save();
            WindowGlow.Refresh();
        }

        private static RadioButton MakeSwatch(string group, string name, string hex, bool isChecked, RoutedEventHandler onChecked)
        {
            ThemeService.TryParse(hex, out var color);
            var swatch = new RadioButton
            {
                GroupName = group,
                Style = (Style)Application.Current.FindResource("Swatch"),
                Background = new SolidColorBrush(color),
                Tag = hex,
                ToolTip = name,
                IsChecked = isChecked,
            };
            swatch.Checked += onChecked;
            return swatch;
        }

        private void TintChecked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.TintHex = (string)((RadioButton)sender).Tag;
            Commit();
        }

        private void BackdropChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || cmbBackdrop.SelectedItem is not string style) return;
            AppSettings.Current.Backdrop = style;
            Commit();
        }

        // Slider.ValueChanged passes a RoutedPropertyChangedEventArgs<double>, which is a RoutedEventArgs.
        private void SlidersChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.GlassTint = sldGlass.Value;
            AppSettings.Current.WindowOpacity = sldOpacity.Value;
            Commit();
        }

        private void FontChecked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var hex = (string)((RadioButton)sender).Tag;
            txtFont.Text = hex;
            AppSettings.Current.FontHex = hex;
            Commit();
        }

        private void FontKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyCustomFont();
        }

        private void FontLostFocus(object sender, RoutedEventArgs e) => ApplyCustomFont();

        private void ApplyCustomFont()
        {
            if (_loading) return;
            var text = txtFont.Text.Trim();
            if (!text.StartsWith('#')) text = "#" + text;
            if (!ThemeService.TryParse(text, out _) || text.Length is not (7 or 9))
            {
                txtStatus.Text = "That isn't a valid hex color. Use a value like #F4EFF8.";
                txtFont.Text = AppSettings.Current.FontHex;
                return;
            }
            AppSettings.Current.FontHex = text.ToUpperInvariant();
            txtFont.Text = AppSettings.Current.FontHex;
            _loading = true;
            foreach (RadioButton r in panelFonts.Children.OfType<RadioButton>())
                r.IsChecked = string.Equals((string)r.Tag, AppSettings.Current.FontHex, StringComparison.OrdinalIgnoreCase);
            _loading = false;
            Commit();
        }

        private void CardChecked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var hex = (string)((RadioButton)sender).Tag;
            txtCard.Text = hex;
            AppSettings.Current.CardHex = hex;
            Commit();
        }

        private void CardKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyCustomCard();
        }

        private void CardLostFocus(object sender, RoutedEventArgs e) => ApplyCustomCard();

        private void ApplyCustomCard()
        {
            if (_loading) return;
            var text = txtCard.Text.Trim();
            if (!text.StartsWith('#')) text = "#" + text;
            if (!ThemeService.TryParse(text, out _) || text.Length is not (7 or 9))
            {
                txtStatus.Text = "That isn't a valid hex color. Use a value like #3B82F6.";
                txtCard.Text = AppSettings.Current.CardHex;
                return;
            }
            AppSettings.Current.CardHex = text.ToUpperInvariant();
            txtCard.Text = AppSettings.Current.CardHex;
            _loading = true;
            foreach (RadioButton r in panelCards.Children.OfType<RadioButton>())
                r.IsChecked = string.Equals((string)r.Tag, AppSettings.Current.CardHex, StringComparison.OrdinalIgnoreCase);
            _loading = false;
            Commit();
        }

        private async void RemovePawnIoClicked(object sender, RoutedEventArgs e)
        {
            var ask = MessageBox.Show(Window.GetWindow(this),
                "Remove the PawnIO sensor driver?\n\nCyberOptix will no longer be able to read your CPU temperature, and other programs that read sensors through PawnIO will lose those readings too. " +
                "You can install it again from the Monitor page.",
                "Remove the PawnIO sensor driver", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (ask != MessageBoxResult.Yes) return;

            btnRemovePawnIo.IsEnabled = false;
            txtStatus.Text = "Removing the PawnIO driver... this can take a minute.";
            var result = await PawnIoService.UninstallAsync();
            btnRemovePawnIo.IsEnabled = true;
            if (result.Installed)   // for a removal, "Installed" in the result means the driver is gone
            {
                rowPawnIo.Visibility = Visibility.Collapsed;
                HardwareService.Instance.ReloadSensors();
                txtStatus.Text = result.Message;
            }
            else
            {
                txtStatus.Text = result.WingetMissing ? result.Message + " Remove it in Windows Settings, Installed apps (PawnIO)." : result.Message;
            }
        }

        private void ResetColorsClicked(object sender, RoutedEventArgs e)
        {
            var d = new AppSettings();
            var s = AppSettings.Current;
            s.AccentHex = d.AccentHex; s.TintHex = d.TintHex; s.FontHex = d.FontHex; s.CardHex = d.CardHex; s.CardOpacity = d.CardOpacity;
            s.MatchMediaAccent = s.MatchMediaWindow = s.MatchMediaText = s.MatchMediaColor = s.MatchMediaGlow = true;
            Initialize();
            AppSettings.Save();
            CyberOptix.Services.MediaColorService.Sync();
            txtStatus.Text = "Colors are back to the defaults.";
        }

        private void BackgroundVideoChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.BackgroundVideo = chkBackgroundVideo.IsChecked == true;
            Commit();
        }

        private void MatchMediaChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var s = AppSettings.Current;
            s.MatchMediaColor = chkMatchMedia.IsChecked == true;
            s.MatchMediaAccent = chkMatchAccent.IsChecked == true;
            s.MatchMediaWindow = chkMatchWindow.IsChecked == true;
            s.MatchMediaText = chkMatchText.IsChecked == true;
            s.MatchMediaGlow = chkMatchGlow.IsChecked == true;
            AppSettings.Save();
            MediaColorService.Sync();   // starts or stops following the media player, and re-colors the cards
        }

        private void CardOpacityChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.CardOpacity = sldCard.Value;
            Commit();
        }

        private void MinimizeToChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.MinimizeTo = cmbMinimizeTo.SelectedIndex == 1 ? "Dock" : cmbMinimizeTo.SelectedIndex == 2 ? "Tray" : "Taskbar";
            AppSettings.Save();
        }

        /// <summary>
        /// The start-up task starts CyberOptix with administrator rights, so if ordinary programs can change CyberOptix's own folder, one of them
        /// could swap the program and get those rights at the next sign-in. Say so plainly, and how to avoid it.
        /// </summary>
        private void ShowStartupNote()
        {
            bool risky = AppSettings.Current.StartWithWindows && AutoStartService.FolderOpenToUsers() == true;
            txtStartupNote.Visibility = risky ? Visibility.Visible : Visibility.Collapsed;
            if (risky)
                txtStartupNote.Text = "Heads up: the folder CyberOptix is in can be changed by ordinary programs. Because it starts with administrator rights, " +
                                      "a program could swap CyberOptix for something else and be run as administrator at your next sign-in. " +
                                      "Keeping CyberOptix in C:\\Program Files, or in a folder only administrators can change, avoids that.";
        }

        private async void StartWithWindowsChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = chkStartWithWindows.IsChecked == true;
            AppSettings.Current.StartWithWindows = on;
            AppSettings.Save();
            ShowStartupNote();

            txtStatus.Text = on ? "Setting up start with Windows..." : "Removing start with Windows...";
            bool ok = await System.Threading.Tasks.Task.Run(() => AutoStartService.Apply(on));
            txtStatus.Text = ok
                ? (on ? "CyberOptix will start when you sign in." : "CyberOptix will no longer start with Windows.")
                : "Windows would not change the start-up task. Try again, or check Task Scheduler for a task named CyberOptix.";
        }

        private void StartQuietlyChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.StartQuietly = chkStartQuietly.IsChecked == true;
            AppSettings.Save();
        }

        private void CloseToTrayChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.CloseToTray = chkCloseToTray.IsChecked == true;
            AppSettings.Save();
        }

        private void BackgroundChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.BackgroundImage = chkBackground.IsChecked == true;
            Commit();
        }

        private void PictureStrengthChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.BackgroundImageOpacity = sldPicture.Value;
            Commit();
        }

        private void ChoosePictureClicked(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Choose a background picture", Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dlg.ShowDialog() != true) return;
            AppSettings.Current.BackgroundImagePath = dlg.FileName;
            AppSettings.Current.BackgroundImage = true;
            _loading = true; chkBackground.IsChecked = true; _loading = false;
            rowPicture.Description = dlg.FileName;
            Commit();
        }

        private void DefaultPictureClicked(object sender, RoutedEventArgs e)
        {
            AppSettings.Current.BackgroundImagePath = null;
            AppSettings.Current.BackgroundImage = true;
            _loading = true; chkBackground.IsChecked = true; _loading = false;
            rowPicture.Description = "Using the CyberOptix wallpaper.";
            Commit();
        }

        private async void RestoreClicked(object sender, RoutedEventArgs e)
        {
            btnRestore.IsEnabled = false;
            var message = await RestoreFlow.RunAsync(System.Windows.Window.GetWindow(this));
            btnRestore.IsEnabled = true;
            if (message != null) txtStatus.Text = message;
        }

        /// <summary>The two separate mode choices show only when the mode is Custom; the other choices work only while the color match is on.</summary>
        private void ShowWindowsColorRows()
        {
            bool custom = cmbWinMode.SelectedIndex == 2;
            rowWinSystem.Visibility = rowWinApp.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            bool on = chkWindowsAccent.IsChecked == true;
            panelWinColor.Visibility = chkWinOwnColor.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            foreach (UIElement el in new UIElement[] { chkWinMatchMedia, chkWinOwnColor, panelWinColor, cmbWinMode, rowWinSystem, rowWinApp, chkWinTransparency, chkWinAccentStart, chkWinAccentTitle })
                el.IsEnabled = on;
        }

        private void WindowsAccentChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var s = AppSettings.Current;
            _loading = true;
            if (ReferenceEquals(sender, cmbWinMode) && cmbWinMode.SelectedIndex is 0 or 1)
            {
                cmbWinSystem.SelectedIndex = cmbWinApp.SelectedIndex = cmbWinMode.SelectedIndex;   // Light or Dark sets both
            }
            else if (ReferenceEquals(sender, cmbWinSystem) || ReferenceEquals(sender, cmbWinApp))
            {
                cmbWinMode.SelectedIndex = cmbWinSystem.SelectedIndex == cmbWinApp.SelectedIndex ? cmbWinSystem.SelectedIndex : 2;
            }
            _loading = false;

            s.SyncWindowsAccent = chkWindowsAccent.IsChecked == true;
            s.WinMatchMedia = chkWinMatchMedia.IsChecked == true;
            s.WinOwnColor = chkWinOwnColor.IsChecked == true;
            s.WinSystemDark = cmbWinSystem.SelectedIndex == 1;
            s.WinAppsDark = cmbWinApp.SelectedIndex == 1;
            s.WinTransparency = chkWinTransparency.IsChecked == true;
            s.WinAccentOnStart = chkWinAccentStart.IsChecked == true;
            s.WinAccentOnTitle = chkWinAccentTitle.IsChecked == true;
            AppSettings.Save();
            ShowWindowsColorRows();
            MediaColorService.Sync();   // starts or stops following the media player for the Windows color
            WindowsAccentService.Sync();
        }

        private void KeepWindowsChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.KeepWindowsSettingsOnRelaunch = chkKeepWindows.IsChecked == true;
            AppSettings.Save();
        }

        private void UninstallClicked(object sender, RoutedEventArgs e)
        {
            // Uninstall.exe sits next to CyberOptix.exe.
            var folder = System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            var uninstaller = System.IO.Path.Combine(folder, "Uninstall.exe");
            if (!System.IO.File.Exists(uninstaller))
            {
                txtStatus.Text = "Uninstall.exe was not found next to CyberOptix.exe.";
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uninstaller) { UseShellExecute = true, WorkingDirectory = folder });
                txtStatus.Text = "The uninstaller is open. It will ask before it removes anything.";
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Could not open the uninstaller: {ex.Message}";
            }
        }

        private void ResetClicked(object sender, RoutedEventArgs e)
        {
            ThemeService.Reset();
            Initialize();
            txtStatus.Text = "Back to the default look.";
        }

        private void Commit()
        {
            ThemeService.Apply();
            AppSettings.Save();
            WindowsAccentService.Refresh();
            txtStatus.Text = "";
        }
    }
}
