using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using CyberOptix.Models;
using CyberOptix.Services;
using CyberOptix.Widgets;

namespace CyberOptix.Pages
{
    public partial class DisplayPage : UserControl
    {
        private List<DisplayInfo> _displays = new();
        private DisplayInfo? _selected;
        private string? _picked;
        private bool _loading = true;
        private readonly Dictionary<string, BitmapImage?> _thumbs = new();

        public DisplayPage() => InitializeComponent();

        public void Initialize()
        {
            colorSection.LayoutChanged += Reload; // a rotated display changes the screen map
            cmbFit.ItemsSource = WallpaperService.Styles;
            cmbFit.SelectedItem = AppSettings.Current.WallpaperFit;
            if (cmbFit.SelectedItem == null) cmbFit.SelectedIndex = 0;
            _loading = false;
        }

        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible) Reload();
        }

        private Brush Res(string key) => (Brush)FindResource(key);

        // ---------- loading ----------

        private void Reload()
        {
            _thumbs.Clear();
            _displays = DisplayService.GetDisplays();
            _selected = _displays.FirstOrDefault(d => d.Id == _selected?.Id) ?? _displays.FirstOrDefault();
            BuildMap();
            UpdateWallpaperCard();
            txtStatus.Text = _displays.Count == 0 ? "Windows did not report any displays." : "";
        }

        private void MapSizeChanged(object sender, SizeChangedEventArgs e) => BuildMap();

        private BitmapImage? Thumb(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            if (_thumbs.TryGetValue(path, out var cached)) return cached;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 480;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                return _thumbs[path] = bmp;
            }
            catch
            {
                return _thumbs[path] = null;
            }
        }

        // ---------- the screen map ----------

        private void BuildMap()
        {
            mapCanvas.Children.Clear();
            double w = mapHost.ActualWidth, h = mapHost.ActualHeight;
            if (_displays.Count == 0 || w < 50 || h < 50) return;

            int minX = _displays.Min(d => d.X), minY = _displays.Min(d => d.Y);
            int maxX = _displays.Max(d => d.X + d.Width), maxY = _displays.Max(d => d.Y + d.Height);
            const double pad = 28;
            double scale = Math.Min((w - 2 * pad) / (maxX - minX), (h - 2 * pad) / (maxY - minY));
            double offX = (w - (maxX - minX) * scale) / 2, offY = (h - (maxY - minY) * scale) / 2;

            foreach (var d in _displays)
            {
                bool selected = d.Id == _selected?.Id;
                var image = Thumb(DisplayService.GetWallpaper(d.Id));

                var tile = new Border
                {
                    Width = d.Width * scale,
                    Height = d.Height * scale,
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(2),
                    BorderBrush = selected ? Res("AccentBrush") : Res("StrokeBrush"),
                    Background = image != null ? new ImageBrush(image) { Stretch = Stretch.UniformToFill } : Res("InputBrush"),
                    ClipToBounds = true,
                    AllowDrop = true,
                    Cursor = Cursors.Hand,
                    Tag = d,
                    ToolTip = $"Display {d.Number}: {d.Resolution}{(d.IsPrimary ? " (main)" : "")}",
                };
                Canvas.SetLeft(tile, offX + (d.X - minX) * scale);
                Canvas.SetTop(tile, offY + (d.Y - minY) * scale);

                // A dark scrim keeps the labels readable on any wallpaper.
                var grid = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)) };
                var label = new StackPanel { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(10, 8, 0, 0) };
                label.Children.Add(new TextBlock { Text = $"Display {d.Number}" + (d.IsPrimary ? "  ·  main" : ""), FontWeight = FontWeights.SemiBold });
                label.Children.Add(new TextBlock { Text = d.Resolution, FontSize = 12, Opacity = 0.8 });
                grid.Children.Add(label);


                tile.Child = grid;
                tile.MouseLeftButtonUp += (_, _) => Select(d);
                mapCanvas.Children.Add(tile);
            }
        }

        private void Select(DisplayInfo d)
        {
            _selected = d;
            _picked = null;
            BuildMap();
            UpdateWallpaperCard();
        }

        // ---------- wallpaper for the selected display ----------

        private void UpdateWallpaperCard()
        {
            if (_selected == null)
            {
                titleWallpaper.Text = "Wallpaper";
                btnApplyThis.IsEnabled = false;
                return;
            }
            colorSection.SetDisplay(_selected);
            titleWallpaper.Text = $"Wallpaper for Display {_selected.Number}";
            btnApplyThis.Content = $"Set on Display {_selected.Number}";
            btnApplyThis.IsEnabled = true;

            var image = _picked != null ? Thumb(_picked) : Thumb(DisplayService.GetWallpaper(_selected.Id));
            previewBrush.ImageSource = image;
            txtNoImage.Visibility = image == null ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ChooseClicked(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp" };
            if (dlg.ShowDialog() != true) return;
            _picked = dlg.FileName;
            UpdateWallpaperCard();
        }

        private void ApplyThisClicked(object sender, RoutedEventArgs e) => Apply(_selected);
        private void ApplyAllClicked(object sender, RoutedEventArgs e) => Apply(null);

        private void Apply(DisplayInfo? target)
        {
            if (_picked == null) { txtStatus.Text = "Choose an image first."; return; }
            try
            {
                DisplayService.SetFit(cmbFit.SelectedItem as string ?? "Fill");
                DisplayService.SetWallpaper(target?.Id, _picked);
                txtStatus.Text = target == null
                    ? "Wallpaper set on every display."
                    : $"Wallpaper set on Display {target.Number}. The other displays keep theirs.";
                _thumbs.Clear();
                BuildMap();
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Could not set the wallpaper: {ex.Message}";
            }
        }

        private void FitChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            AppSettings.Current.WallpaperFit = cmbFit.SelectedItem as string ?? "Fill";
            AppSettings.Save();
            try { DisplayService.SetFit(AppSettings.Current.WallpaperFit); }
            catch (Exception ex) { txtStatus.Text = $"Could not change the fit: {ex.Message}"; }
        }
    }
}
