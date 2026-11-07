using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace CyberOptix.Widgets
{
    /// <summary>
    /// A "Now Playing" widget on the desktop. The contents are the shared <see cref="MediaPanel"/>, which follows Windows' system
    /// media session, so it works with Spotify, Apple Music, or a browser tab playing YouTube, SoundCloud or Suno.
    /// </summary>
    public class MediaWidget : DesktopWidget
    {
        public MediaWidget() : base("media", 700, 640)
        {
            Card.Padding = new Thickness(20, 12, 20, 6);   // snug above the album cover and below the platform icons
            var panel = new MediaPanel();
            Card.Child = WithClose(panel);
            Closed += (_, _) => panel.Stop();   // let go of the media session as soon as the widget is closed
        }
    }
}
