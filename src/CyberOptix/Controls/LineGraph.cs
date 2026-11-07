using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace CyberOptix.Controls
{
    /// <summary>A small live usage graph: a line with a soft fill under it, newest sample on the right. Values are 0 to 100.</summary>
    public class LineGraph : FrameworkElement
    {
        private double[] _values = System.Array.Empty<double>();

        /// <summary>How many samples fit across the full width (one per second).</summary>
        public int Capacity { get; set; } = 60;

        public void SetValues(IEnumerable<double> values)
        {
            _values = values.TakeLast(Capacity).ToArray();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 4 || h < 4) return;

            var accent = TryFindResource("AccentBrush") as SolidColorBrush ?? Brushes.MediumPurple;
            var grid = new Pen(new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)), 1);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0)), null, new Rect(0, 0, w, h));
            foreach (var f in new[] { 0.25, 0.5, 0.75 })
                dc.DrawLine(grid, new Point(0, h * f), new Point(w, h * f));

            if (_values.Length < 2) return;

            double step = w / (Capacity - 1);
            Point P(int i) => new(w - (_values.Length - 1 - i) * step, h - 2 - System.Math.Clamp(_values[i], 0, 100) / 100 * (h - 4));

            var line = new StreamGeometry();
            using (var ctx = line.Open())
            {
                ctx.BeginFigure(P(0), false, false);
                for (int i = 1; i < _values.Length; i++) ctx.LineTo(P(i), true, true);
            }
            line.Freeze();

            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(P(0).X, h), true, true);
                for (int i = 0; i < _values.Length; i++) ctx.LineTo(P(i), true, false);
                ctx.LineTo(new Point(P(_values.Length - 1).X, h), true, false);
            }
            area.Freeze();

            var fill = new LinearGradientBrush(
                Color.FromArgb(0x70, accent.Color.R, accent.Color.G, accent.Color.B),
                Color.FromArgb(0x05, accent.Color.R, accent.Color.G, accent.Color.B), 90);
            dc.DrawGeometry(fill, null, area);
            dc.DrawGeometry(null, new Pen(accent, 2) { LineJoin = PenLineJoin.Round }, line);
        }
    }
}
