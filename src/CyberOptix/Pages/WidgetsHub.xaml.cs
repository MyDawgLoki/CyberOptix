using System.Windows.Controls;

namespace CyberOptix.Pages
{
    /// <summary>The Widgets page: the screen map with the widgets on it, then the widget settings.</summary>
    public partial class WidgetsHub : UserControl
    {
        public WidgetsHub() => InitializeComponent();

        public void Initialize() => pgWidgets.Initialize();
    }
}
