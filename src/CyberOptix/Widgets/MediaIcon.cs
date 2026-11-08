using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CyberOptix.Widgets
{
    /// <summary>
    /// The logos on the media player's buttons (YouTube, Spotify, SoundCloud, Apple Music, Suno), drawn as vectors on a 24 x 24 board
    /// so they stay sharp at any size.
    /// </summary>
    public static class MediaIcon
    {
        private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

        private static Path Fill(string data, Brush fill) => new() { Data = Geometry.Parse(data), Fill = fill };

        private static Path Stroke(string data, Brush stroke, double width) => new()
        {
            Data = Geometry.Parse(data), Stroke = stroke, StrokeThickness = width,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };

        private static Border Tile(Brush background, double radius) => new() { Width = 24, Height = 24, CornerRadius = new CornerRadius(radius), Background = background };

        public static FrameworkElement Create(string id, double size)
        {
            var board = new Canvas { Width = 24, Height = 24 };
            switch (id)
            {
                case "youtube":
                    board.Children.Add(Tile(Brush("#FF0000"), 6));
                    board.Children.Add(Fill("M9.6,7.6 L9.6,16.4 L17.2,12 Z", Brushes.White));
                    break;

                case "spotify":
                    board.Children.Add(Tile(Brushes.Black, 12));
                    board.Children.Add(Stroke("M6.1,9.0 Q12,7.2 18,10.1", Brushes.White, 2.5));
                    board.Children.Add(Stroke("M6.9,12.7 Q12,11.2 17.1,13.6", Brushes.White, 2.0));
                    board.Children.Add(Stroke("M7.7,16.1 Q12,14.9 16.2,16.9", Brushes.White, 1.6));
                    break;

                case "soundcloud":
                    board.Children.Add(Tile(Brushes.Black, 3.6));
                    // seven rounded bars that rise to the cloud, then the cloud with its big round lobe on the right (about two thirds of the tile wide)
                    double[] xs = { 4.9, 6.2, 7.5, 8.8, 10.1, 11.4 };
                    double[] tops = { 11.4, 10.4, 9.5, 8.8, 8.4, 8.2 };
                    for (int i = 0; i < xs.Length; i++)
                        board.Children.Add(Stroke($"M{xs[i]},{tops[i]} L{xs[i]},15.4", Brushes.White, 0.8));
                    board.Children.Add(Fill("M12.4,15.7 L12.4,9.3 C13.2,8.6 14.2,8.2 15.3,8.2 C17.0,8.2 18.4,9.3 18.8,10.9 C19.7,11.0 20.2,12.0 20.2,13.4 A2.3,2.3 0 0 1 17.9,15.7 Z", Brushes.White));
                    break;

                case "itunes":   // Apple Music
                    board.Children.Add(Tile(new LinearGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xE4, 0xE4, 0xE8), 90), 5.4));
                    board.Children.Add(Fill("M16.9,4.6 L9.3,6.2 C8.9,6.3 8.7,6.6 8.7,7.0 L8.7,15.3 C8.2,15.1 7.6,15.0 7.0,15.1 C5.5,15.4 4.6,16.5 4.8,17.8 C5.1,19.1 6.5,19.9 8.0,19.6 C9.5,19.3 10.4,18.2 10.4,16.9 L10.4,9.8 L15.5,8.7 L15.5,13.7 C15.0,13.5 14.4,13.4 13.8,13.5 C12.3,13.8 11.4,14.9 11.6,16.2 C11.9,17.5 13.3,18.3 14.8,18.0 C16.3,17.7 17.2,16.6 17.2,15.3 L17.2,5.4 C17.2,4.9 17.1,4.5 16.9,4.6 Z", Brushes.Black));
                    break;

                case "suno":
                    board.Children.Add(new Border
                    {
                        Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
                        Background = new LinearGradientBrush(new GradientStopCollection
                        {
                            new GradientStop(Color.FromRgb(0xFF, 0xC1, 0x07), 0.0), new GradientStop(Color.FromRgb(0xFF, 0x7A, 0x1A), 0.5), new GradientStop(Color.FromRgb(0xFF, 0x2D, 0x8E), 1.0),
                        }, new Point(0.15, 0.0), new Point(0.85, 1.0)),
                    });
                    var word = new TextBlock { Text = "SUNO", Foreground = Brushes.White, FontWeight = FontWeights.ExtraBold, FontSize = 6.6, Width = 24, TextAlignment = TextAlignment.Center };
                    Canvas.SetTop(word, 8.2);
                    board.Children.Add(word);
                    break;

                default:
                    board.Children.Add(Tile(Brushes.Gray, 12));
                    break;
            }
            var icon = new Viewbox { Child = board, Width = size, Height = size, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            return icon;
        }
    }
}
