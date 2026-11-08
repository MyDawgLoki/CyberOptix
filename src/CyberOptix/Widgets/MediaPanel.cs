using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>
    /// The "Now Playing" contents: album art, title, progress and previous / play / next. It follows Windows' system media session,
    /// so it works with Spotify, Apple Music, or a browser tab playing YouTube, SoundCloud or Suno.
    /// Used by the desktop media widget and by the music note on the dock. It only listens while it is on screen.
    /// </summary>
    public sealed class MediaPanel : StackPanel
    {
        private bool _watching;

        private readonly Border _art = new()
        {
            Width = 64,
            Height = 64,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            ClipToBounds = true,
        };
        private readonly Image _artImage = new() { Stretch = Stretch.UniformToFill };
        private readonly TextBlock _title = Label(14, "Nothing playing", 1, FontWeights.SemiBold);
        private readonly TextBlock _artist = Dim();
        private readonly TextBlock _source = Dim(9);
        private readonly TextBlock _elapsed = Dim(11);
        private readonly TextBlock _total = Dim(11);
        private readonly ProgressBar _progress = new() { Height = 4, Margin = new Thickness(0, 10, 0, 3) };
        private readonly ContentControl _launcherHost = new() { IsTabStop = false, Padding = new Thickness(0) };
        private readonly Grid _btnBack = SeekButton(false);
        private readonly Grid _btnForward = SeekButton(true);
        private readonly TextBlock _btnPrev = IconButton("", "Previous");
        private readonly TextBlock _btnPlay = IconButton("", "Play / pause", 20);
        private readonly TextBlock _btnNext = IconButton("", "Next");

        public MediaPanel()
        {
            Width = 400;   // a steady size: a long title is trimmed, it never stretches the widget
            _progress.Style = (Style)Application.Current.FindResource("Meter");
            _art.Child = _artImage;

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            info.Children.Add(_title);
            info.Children.Add(_artist);
            info.Children.Add(_source);

            _title.TextTrimming = _artist.TextTrimming = _source.TextTrimming = TextTrimming.CharacterEllipsis;
            _title.TextWrapping = _artist.TextWrapping = _source.TextWrapping = TextWrapping.NoWrap;
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(info, 1);
            head.Children.Add(_art);
            head.Children.Add(info);

            // One row: elapsed time on the left, the controls in the middle, the total on the right. The controls have a column of their
            // own, so the times can never run into the icons, and the row sits right under the bar.
            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            controls.Children.Add(_btnPrev);
            controls.Children.Add(_btnBack);
            controls.Children.Add(_btnPlay);
            controls.Children.Add(_btnForward);
            controls.Children.Add(_btnNext);

            var row = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), SharedSizeGroup = null });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _elapsed.VerticalAlignment = _total.VerticalAlignment = VerticalAlignment.Center;
            _elapsed.HorizontalAlignment = HorizontalAlignment.Left;
            _total.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(controls, 1);
            Grid.SetColumn(_total, 2);
            row.Children.Add(_elapsed);
            row.Children.Add(controls);
            row.Children.Add(_total);

            Children.Add(head);
            Children.Add(_progress);
            Children.Add(row);
            _launcherHost.Content = BuildLaunchers();
            Children.Add(_launcherHost);

            _btnPrev.MouseLeftButtonDown += (_, e) => { e.Handled = true; MediaService.Instance.Previous(); };
            _btnPlay.MouseLeftButtonDown += (_, e) => { e.Handled = true; MediaService.Instance.PlayPause(); };
            _btnNext.MouseLeftButtonDown += (_, e) => { e.Handled = true; MediaService.Instance.Next(); };
            _btnBack.MouseLeftButtonDown += (_, e) => { e.Handled = true; MediaService.Instance.SeekBy(TimeSpan.FromSeconds(-5)); };
            _btnForward.MouseLeftButtonDown += (_, e) => { e.Handled = true; MediaService.Instance.SeekBy(TimeSpan.FromSeconds(5)); };

            Render(null);

            Loaded += (_, _) =>
            {
                if (_watching) return;
                _watching = true;
                MediaService.Instance.Updated += OnUpdated;
                MediaService.Instance.Acquire();
            };
            Unloaded += (_, _) => Stop();

            // Redraw the service buttons when the choice changes in the widget settings.
            Action redraw = () => Dispatcher.BeginInvoke(new Action(() => _launcherHost.Content = BuildLaunchers()));
            Loaded += (_, _) => { MediaLaunchers.SelectionChanged -= redraw; MediaLaunchers.SelectionChanged += redraw; };
            Unloaded += (_, _) => MediaLaunchers.SelectionChanged -= redraw;
        }

        /// <summary>Stops listening to the media session. Safe to call more than once; it also happens by itself when the panel leaves the screen.</summary>
        public void Stop()
        {
            if (!_watching) return;
            _watching = false;
            MediaService.Instance.Updated -= OnUpdated;
            MediaService.Instance.Release();
        }

        private void OnUpdated(MediaSnapshot? s) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (_watching) Render(s); }));

        /// <summary>Shows what is playing. Null (or no title) shows "Nothing playing".</summary>
        public void Render(MediaSnapshot? s)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.Title))
            {
                _title.Text = "Nothing playing";
                _artist.Text = "";
                _source.Text = "";
                _artImage.Source = null;
                _progress.Value = 0;
                _elapsed.Text = _total.Text = "";
                SetPlayGlyph(true);
                SetEnabled(_btnPrev, false);
                SetEnabled(_btnNext, false);
                SetEnabled(_btnPlay, false);
                SetEnabled(_btnBack, false);
                SetEnabled(_btnForward, false);
                return;
            }

            _title.Text = s.Title;
            _artist.Text = s.Artist;
            _source.Text = s.SourceApp;
            _artImage.Source = s.Thumbnail;
            SetPlayGlyph(!s.IsPlaying);
            SetEnabled(_btnPrev, s.CanPrevious);
            SetEnabled(_btnNext, s.CanNext);
            SetEnabled(_btnPlay, s.CanPlay || s.CanPause);
            SetEnabled(_btnBack, s.CanSeek);
            SetEnabled(_btnForward, s.CanSeek);

            if (s.Duration is { } dur && dur > TimeSpan.Zero && s.Position is { } pos)
            {
                _progress.Value = Math.Clamp(pos.TotalSeconds / dur.TotalSeconds * 100, 0, 100);
                _elapsed.Text = Fmt(pos);
                _total.Text = Fmt(dur);
            }
            else
            {
                _progress.Value = 0;
                _elapsed.Text = _total.Text = "";
            }
        }

        // The glyph shows the action a click will perform, same as Spotify/Apple Music: a Play icon while paused, Pause while playing.
        private void SetPlayGlyph(bool showPlay) => _btnPlay.Text = showPlay ? "" : "";

        private static void SetEnabled(UIElement button, bool enabled)
        {
            button.IsHitTestVisible = enabled;
            button.Opacity = enabled ? 0.85 : 0.3;
        }

        private static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

        private static TextBlock Label(double size, string text = "", double opacity = 1, FontWeight? weight = null) => new()
        {
            FontSize = size,
            Text = text,
            Opacity = opacity,
            FontWeight = weight ?? FontWeights.Normal,
        };

        private static TextBlock Dim(double size = 12) => Label(size, "", 0.65);

        /// <summary>A row of buttons that open YouTube, Spotify, SoundCloud, iTunes and Suno: the app if it is installed, otherwise the website in the default browser.</summary>
        private static UIElement BuildLaunchers()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) };
            if (MediaLaunchers.Selected().Count == 0) return new Border { Height = 0 };   // none chosen: no row, no gap
            foreach (var l in MediaLaunchers.Selected())
            {
                var launcher = l;
                bool app = MediaLaunchers.InstalledProtocol(launcher) != null;

                var disc = MediaIcon.Create(launcher.Id, 38);   // the service's own logo
                disc.HorizontalAlignment = HorizontalAlignment.Center;
                var name = new TextBlock { Text = launcher.Name, FontSize = 10, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0), Opacity = 0.75 };
                var stack = new StackPanel();
                stack.Children.Add(disc);
                stack.Children.Add(name);

                var tile = new Border
                {
                    Child = stack, Width = 60, Padding = new Thickness(0, 3, 0, 4), Margin = new Thickness(1, 0, 1, 0), CornerRadius = new CornerRadius(10),
                    Background = Brushes.Transparent, Cursor = Cursors.Hand,
                    ToolTip = app ? $"Open {launcher.Name}" : $"Open {launcher.Name} in your browser",
                };
                tile.MouseEnter += (_, _) => tile.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
                tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
                tile.MouseLeftButtonDown += (_, e) => e.Handled = true;      // a click here must not drag the widget
                tile.MouseLeftButtonUp += (_, _) => MediaLaunchers.Open(launcher);
                row.Children.Add(tile);
            }
            return row;
        }

        /// <summary>A "skip 5 seconds" button: a curved arrow (back or forward) with a 5 in it.</summary>
        /// <summary>
        /// A skip button: a circular arrow that chases its own tail (clockwise to go forward, the mirror image to go back) with a 5 in the middle.
        /// It is drawn rather than taken from the icon font, so the arrow and the number are one steady shape at every size.
        /// </summary>
        private static Grid SeekButton(bool forward)
        {
            var grid = new Grid { Margin = new Thickness(6, 0, 6, 0), Width = 26, Height = 26, Cursor = Cursors.Hand, Opacity = 0.9, ToolTip = forward ? "Forward 5 seconds" : "Back 5 seconds", Background = Brushes.Transparent };

            // On a 24 x 24 board: almost a full circle, with the arrowhead coming round to meet the start of the line.
            var arrow = new Canvas { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5) };
            var ring = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 13.48,3.63 A 8.5,8.5 0 1 1 4.64,7.75"),
                StrokeThickness = 1.9, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Flat, Fill = null,
            };
            var head = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 2.0,6.2 L 7.3,9.3 L 6.8,3.9 Z"), StrokeThickness = 0.8, StrokeLineJoin = PenLineJoin.Round };
            ring.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "TextBrush");
            head.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "TextBrush");
            head.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "TextBrush");
            arrow.Children.Add(ring);
            arrow.Children.Add(head);
            if (!forward) arrow.RenderTransform = new ScaleTransform(-1, 1);   // the mirror image, so the arrow runs the other way round
            grid.Children.Add(arrow);

            var five = new TextBlock { Text = "5", FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) };
            five.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            grid.Children.Add(five);
            return grid;
        }

        private static TextBlock IconButton(string glyph, string tooltip, double size = 16) => new()
        {
            Text = glyph,
            FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
            FontSize = size,
            Margin = new Thickness(14, 0, 14, 0),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            Opacity = 0.85,
        };
    }
}
