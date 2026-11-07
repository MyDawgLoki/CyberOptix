using System.Windows;

namespace CyberOptix.Controls
{
    /// <summary>Carries a usage figure (such as "312 MB") for a switch row, shown in its own column after a vertical divider.</summary>
    public static class Usage
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Usage), new PropertyMetadata(""));

        public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);
        public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);
    }
}
