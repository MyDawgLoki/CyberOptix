using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace CyberOptix.Services
{
    /// <summary>One music or video service the media panel can open: its app if this PC has it, otherwise its website in the default browser.</summary>
    public record MediaLauncher(string Id, string Name, string Glyph, string ColorHex, string[] Protocols, string Url, string? StartMenuName = null);

    public static class MediaLaunchers
    {
        public static readonly IReadOnlyList<MediaLauncher> All = new List<MediaLauncher>
        {
            new("youtube", "YouTube", "", "#E62117", Array.Empty<string>(), "https://www.youtube.com"),
            new("spotify", "Spotify", "", "#1DB954", new[] { "spotify" }, "https://open.spotify.com", "Spotify"),
            new("soundcloud", "SoundCloud", "", "#FF5500", Array.Empty<string>(), "https://soundcloud.com"),
            new("itunes", "Apple Music", "", "#F2559E", new[] { "itunes", "applemusic" }, "https://music.apple.com", "iTunes|Apple Music"),
            new("suno", "Suno", "", "#7C5CFF", Array.Empty<string>(), "https://suno.com"),
        };

        /// <summary>Raised when the user changes which buttons the media player shows, so open players can redraw.</summary>
        public static event Action? SelectionChanged;

        public static void RaiseSelectionChanged() => SelectionChanged?.Invoke();

        /// <summary>The services the user chose to show, in the usual order.</summary>
        public static IReadOnlyList<MediaLauncher> Selected()
        {
            var chosen = new HashSet<string>(CyberOptix.Models.AppSettings.Current.MediaIcons);
            return All.Where(l => chosen.Contains(l.Id)).ToList();
        }

        /// <summary>The URL-protocol this PC has registered for the service's app (like spotify:), or null if the app is not installed.</summary>
        public static string? InstalledProtocol(MediaLauncher l)
        {
            foreach (var p in l.Protocols)
            {
                try
                {
                    using var key = Registry.ClassesRoot.OpenSubKey(p);
                    if (key?.GetValue("URL Protocol") != null) return p;
                }
                catch { }
            }
            return null;
        }

        /// <summary>What a click opens: the app (<c>spotify:</c>) when it is installed, otherwise the website.</summary>
        public static string Target(MediaLauncher l, Func<MediaLauncher, string?>? protocolOf = null)
        {
            // the app in the Start menu first (a Store app or a program), then its protocol, then the website
            if (l.StartMenuName != null && protocolOf == null)
            {
                try
                {
                    var names = l.StartMenuName.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var (name, id) in HardwareSoftwareService.AllStartApps())
                        if (names.Any(n => string.Equals(name, n, StringComparison.OrdinalIgnoreCase))) return HardwareSoftwareService.Target(id);
                }
                catch { }
            }
            var protocol = (protocolOf ?? InstalledProtocol)(l);
            return protocol != null ? protocol + ":" : l.Url;
        }

        /// <summary>Opens the app or the site. Goes through Explorer, so the app starts as you (not as administrator) and a website opens in your default browser.</summary>
        public static void Open(MediaLauncher l) => DockService.Launch(Target(l));
    }
}
