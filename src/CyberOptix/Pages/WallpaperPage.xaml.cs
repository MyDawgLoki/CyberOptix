using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Pages
{
    /// <summary>The slideshow section. Choosing a single image, per display, lives in the Screens section.</summary>
    public partial class WallpaperPage : UserControl
    {
        private List<string> _files = new();
        private int _index;
        private readonly DispatcherTimer _timer = new();
        private readonly Random _rng = new();

        public WallpaperPage()
        {
            InitializeComponent();
            _timer.Tick += (_, _) => NextImage();
        }

        public void Initialize()
        {
            var folder = AppSettings.Current.SlideshowFolder;
            if (folder != null && Directory.Exists(folder))
                txtFolder.Text = folder;
        }

        private void Apply(string path)
        {
            try
            {
                WallpaperService.Set(path, AppSettings.Current.WallpaperFit);
                txtStatus.Text = $"Wallpaper set: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Could not set wallpaper: {ex.Message}";
            }
        }

        private void ChooseFolderClicked(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog();
            if (dlg.ShowDialog() != true) return;
            AppSettings.Current.SlideshowFolder = dlg.FolderName;
            AppSettings.Save();
            txtFolder.Text = dlg.FolderName;
        }

        private void SlideshowClicked(object sender, RoutedEventArgs e)
        {
            if (_timer.IsEnabled)
            {
                _timer.Stop();
                btnSlideshow.Content = "Start slideshow";
                txtStatus.Text = "Slideshow stopped.";
                return;
            }

            var folder = AppSettings.Current.SlideshowFolder;
            if (folder == null || !Directory.Exists(folder)) { txtStatus.Text = "Choose a folder first."; return; }

            _files = Directory.EnumerateFiles(folder)
                .Where(f => WallpaperService.ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
            if (_files.Count == 0) { txtStatus.Text = "No images found in that folder."; return; }

            var minutes = int.Parse(((ComboBoxItem)cmbInterval.SelectedItem).Content.ToString()!.Split(' ')[0]);
            _timer.Interval = TimeSpan.FromMinutes(minutes);
            _index = -1;
            NextImage();
            _timer.Start();
            btnSlideshow.Content = "Stop slideshow";
            txtStatus.Text = $"Slideshow running ({_files.Count} images, every {minutes} min).";
        }

        private void NextImage()
        {
            if (_files.Count == 0) return;
            _index = chkShuffle.IsChecked == true ? _rng.Next(_files.Count) : (_index + 1) % _files.Count;
            Apply(_files[_index]);
        }
    }
}
