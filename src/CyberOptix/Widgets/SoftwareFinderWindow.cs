using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CyberOptix.Models;
using CyberOptix.Services;

namespace CyberOptix.Widgets
{
    /// <summary>
    /// "Find my software": lists the programs installed on this PC (from the Start menu) with their icons, and adds the ticked ones as dock icons.
    /// Everything stays on this PC.
    /// </summary>
    public sealed class SoftwareFinderWindow : Window
    {
        private readonly string _group;
        private readonly StackPanel _list = new();
        private readonly TextBlock _status = new() { Margin = new Thickness(4, 8, 0, 8), TextWrapping = TextWrapping.Wrap };
        private readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 8), Tag = "Search" };
        private readonly Button _add = new() { Content = "Add the ticked programs", MinWidth = 190, IsEnabled = false };
        private readonly List<(CheckBox Box, FoundSoftware Item, Border Row)> _rows = new();

        /// <summary>The icons that were added, once the window is closed with "Add".</summary>
        public int Added { get; private set; }

        public SoftwareFinderWindow(string group)
        {
            _group = group;
            Title = "Find my software";
            Width = 560; Height = 640; MinWidth = 420; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
            SetResourceReference(ForegroundProperty, "TextBrush");
            ThemeService.TryParse(AppSettings.Current.TintHex, out var tint);
            Background = new SolidColorBrush(Color.FromRgb(tint.R, tint.G, tint.B));

            var root = new Grid { Margin = new Thickness(20, 16, 20, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new TextBlock { Text = "Find my software", FontSize = 22 };
            head.SetResourceReference(TextBlock.StyleProperty, "SectionTitle");
            root.Children.Add(head);

            var note = new TextBlock
            {
                Text = "These programs were found in your Start menu. Tick the ones you want as icons on the dock. Nothing leaves this PC, and nothing is started.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12), FontSize = 12,
            };
            note.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            var top = new StackPanel();
            top.Children.Add(note);
            top.Children.Add(_search);
            Grid.SetRow(top, 1);
            root.Children.Add(top);

            var body = new StackPanel();
            body.Children.Add(_status);
            body.Children.Add(_list);
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
            Grid.SetRow(scroll, 2);
            root.Children.Add(scroll);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var all = new Button { Content = "Tick all shown", Margin = new Thickness(0, 0, 10, 0) };
            all.Click += (_, _) => { foreach (var r in _rows.Where(r => r.Row.Visibility == Visibility.Visible)) r.Box.IsChecked = true; };
            var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 10, 0) };
            cancel.Click += (_, _) => Close();
            _add.SetResourceReference(StyleProperty, "PrimaryButton");
            _add.Click += (_, _) => AddTicked();
            buttons.Children.Add(all);
            buttons.Children.Add(cancel);
            buttons.Children.Add(_add);
            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);

            Content = root;
            _search.TextChanged += (_, _) => Filter();
            ContentRendered += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            _status.Text = "Looking through your Start menu...";
            List<FoundSoftware> found;
            try { found = await Task.Run(SoftwareScanner.Scan); }
            catch { found = new List<FoundSoftware>(); }

            _status.Text = found.Count == 0 ? "No more programs were found. Everything here is already on the dock, or has its own icon." : $"{found.Count} programs found.";
            foreach (var item in found)
            {
                var box = new CheckBox { Content = item.Name, ToolTip = item.Path, VerticalAlignment = VerticalAlignment.Center };
                box.Checked += (_, _) => UpdateButton();
                box.Unchecked += (_, _) => UpdateButton();
                var icon = new Image { Width = 24, Height = 24, Margin = new Thickness(4, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
                var row = new Border { Padding = new Thickness(6, 4, 6, 4), Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, box } } };
                _list.Children.Add(row);
                _rows.Add((box, item, row));
                // icons come in a few at a time, so the list appears at once and fills in
                var path = item.Path;
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => icon.Source = DockService.IconFor(path)));
            }
        }

        private void Filter()
        {
            string q = _search.Text.Trim();
            foreach (var r in _rows)
                r.Row.Visibility = q.Length == 0 || r.Item.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateButton()
        {
            int n = _rows.Count(r => r.Box.IsChecked == true);
            _add.IsEnabled = n > 0;
            _add.Content = n == 0 ? "Add the ticked programs" : n == 1 ? "Add 1 program" : $"Add {n} programs";
        }

        private void AddTicked()
        {
            foreach (var (box, item, _) in _rows)
                if (box.IsChecked == true) { DockWidgetCatalog.AddCustom(item.Path, item.Name, _group); Added++; }
            DialogResult = true;
        }
    }
}
