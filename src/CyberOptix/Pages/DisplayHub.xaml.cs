using System.Windows.Controls;

namespace CyberOptix.Pages
{
    /// <summary>The Display page: screens, wallpaper and live wallpaper as sections of one scrolling page.</summary>
    public partial class DisplayHub : UserControl
    {
        public DisplayHub() => InitializeComponent();

        /// <summary>Called once by MainWindow when it has loaded. Also restores a running live wallpaper.</summary>
        public void Initialize()
        {
            pgScreens.Initialize();
            pgSleep.Initialize();
            pgWallpaper.Initialize();
            pgClock.Initialize();
            pgLive.Initialize();
        }
    }
}
