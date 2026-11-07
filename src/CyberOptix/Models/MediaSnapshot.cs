using System;
using System.Windows.Media;

namespace CyberOptix.Models
{
    /// <summary>What's currently playing, from whichever app (Spotify, Apple Music, a browser tab, etc.) owns the system's "Now Playing" session.</summary>
    public record MediaSnapshot
    {
        public string Title { get; init; } = "";
        public string Artist { get; init; } = "";
        public string AlbumTitle { get; init; } = "";
        public string SourceApp { get; init; } = "";
        public ImageSource? Thumbnail { get; init; }
        public bool IsPlaying { get; init; }
        public bool CanPlay { get; init; }
        public bool CanPause { get; init; }
        public bool CanNext { get; init; }
        public bool CanPrevious { get; init; }
        public bool CanSeek { get; init; }
        public TimeSpan? Position { get; init; }
        public TimeSpan? Duration { get; init; }
    }
}
