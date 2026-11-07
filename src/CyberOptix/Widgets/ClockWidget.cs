using System;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CyberOptix.Widgets
{
    public class ClockWidget : DesktopWidget
    {
        private readonly TextBlock _time = Text(46, weight: System.Windows.FontWeights.SemiBold);
        private readonly TextBlock _date = Text(14, opacity: 0.7);
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

        public ClockWidget() : base("clock", 40)
        {
            var panel = new StackPanel { MinWidth = 240 };
            panel.Children.Add(_time);
            panel.Children.Add(_date);
            Card.Child = panel;

            Tick();
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
            Closed += (_, _) => _timer.Stop();
        }

        private void Tick()
        {
            var now = DateTime.Now;
            _time.Text = now.ToString("h:mm tt");
            _date.Text = now.ToString("dddd, MMMM d");
        }
    }
}
