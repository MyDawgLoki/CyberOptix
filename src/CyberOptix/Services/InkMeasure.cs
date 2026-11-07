using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CyberOptix.Services
{
    /// <summary>
    /// Where the visible part of a piece of text starts. A text box has empty space above its letters (more for some fonts, and for emoji),
    /// so lining two pieces of text up by their boxes looks wrong. This draws the text and finds the first row that has ink in it.
    /// </summary>
    public static class InkMeasure
    {
        private static readonly Dictionary<string, double> Cache = new();

        /// <summary>The distance from the top of the text box to the top of the visible text, in device-independent units.</summary>
        public static double TopOf(string text, FontFamily family, double size, FontWeight weight, double lineHeight = double.NaN)
        {
            string key = $"{text}|{family.Source}|{Math.Round(size, 1)}|{weight}|{lineHeight}";
            if (Cache.TryGetValue(key, out var known)) return known;
            double result;
            try
            {
                var box = new TextBlock { Text = text, FontFamily = family, FontSize = size, FontWeight = weight, Foreground = Brushes.White };
                if (!double.IsNaN(lineHeight)) { box.LineHeight = lineHeight; box.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; }
                box.Measure(new Size(10000, 10000));
                box.Arrange(new Rect(box.DesiredSize));
                const double zoom = 2;   // measure at twice the size for a finer answer
                int w = Math.Max(1, (int)Math.Ceiling(box.DesiredSize.Width * zoom)), h = Math.Max(1, (int)Math.Ceiling(box.DesiredSize.Height * zoom));
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                    dc.DrawRectangle(new VisualBrush(box) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, box.DesiredSize.Width, box.DesiredSize.Height), Stretch = Stretch.Fill }, null, new Rect(0, 0, box.DesiredSize.Width, box.DesiredSize.Height));
                var bitmap = new RenderTargetBitmap(w, h, 96 * zoom, 96 * zoom, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var pixels = new byte[w * h * 4];
                bitmap.CopyPixels(pixels, w * 4, 0);
                int firstRow = -1;
                for (int y = 0; y < h && firstRow < 0; y++)
                    for (int x = 0; x < w; x++)
                        if (pixels[(y * w + x) * 4 + 3] > 48) { firstRow = y; break; }
                result = firstRow < 0 ? size * 0.2 : firstRow / zoom;
            }
            catch { result = size * 0.2; }
            Cache[key] = result;
            return result;
        }
    }
}
