using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    /// <summary>
    /// Wraps Windows' System Media Transport Controls (the same "Now Playing" session used by the
    /// volume flyout) so widgets can show album art and send play/pause/skip to whichever app owns it:
    /// Spotify, Apple Music, or a browser tab playing YouTube, SoundCloud or Suno.
    /// Only connects while at least one widget is watching.
    /// </summary>
    public sealed class MediaService
    {
        public static MediaService Instance { get; } = new();

        /// <summary>Raised on a background thread; marshal to the UI thread before touching controls. Null means nothing is playing.</summary>
        public event Action<MediaSnapshot?>? Updated;

        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _session;
        private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(1) };
        private int _watchers;
        private bool _started;
        private MediaSnapshot? _last;

        public void Acquire()
        {
            if (Interlocked.Increment(ref _watchers) == 1) Start();
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _watchers) == 0) Stop();
        }

        private void Start()
        {
            if (_started) return;
            _started = true;
            _ticker.Tick += TickerTick;
            _ticker.Start();
            _ = InitAsync();
        }

        private void Stop()
        {
            if (!_started) return;
            _started = false;
            _ticker.Stop();
            _ticker.Tick -= TickerTick;
            Unhook();
            if (_manager != null) _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager = null;
            _last = null;
        }

        private async Task InitAsync()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += OnCurrentSessionChanged;
                Rehook();
            }
            catch
            {
                // No media session support on this build of Windows; the widget just shows "Nothing playing".
                Push(null);
            }
        }

        private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => Rehook();

        private void Rehook()
        {
            Unhook();
            _session = _manager?.GetCurrentSession();
            if (_session != null)
            {
                _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
            }
            _ = RefreshAsync();
        }

        private void Unhook()
        {
            if (_session == null) return;
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            _session = null;
        }

        private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => _ = RefreshAsync();
        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => _ = RefreshAsync();
        private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => _ = RefreshAsync();

        // Runs every second so the progress bar and elapsed time move smoothly between the sparser change events above.
        private void TickerTick(object? sender, EventArgs e)
        {
            var session = _session;
            var last = _last;
            if (session == null || last == null) return;
            try
            {
                var playback = session.GetPlaybackInfo();
                var timeline = session.GetTimelineProperties();
                // The buttons are read again every time too: a browser changes what Previous / Next can do (after a skip, Previous
                // switches on) without any change event we would hear.
                Push(last with
                {
                    IsPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    CanPlay = playback.Controls.IsPlayEnabled,
                    CanPause = playback.Controls.IsPauseEnabled,
                    CanNext = playback.Controls.IsNextEnabled,
                    CanPrevious = playback.Controls.IsPreviousEnabled,
                    CanSeek = playback.Controls.IsPlaybackPositionEnabled,
                    Position = timeline.EndTime > timeline.StartTime ? LivePosition(timeline, playback) : last.Position,
                });
            }
            catch
            {
                // The session can disappear between calls (app closed); the next CurrentSessionChanged will sort it out.
            }
        }

        /// <summary>
        /// Where the video or song is right now. A browser (YouTube, SoundCloud) reports its position only when something happens (play, pause, seek),
        /// together with the time it reported it. While it is playing, the time since that report is added, so the clock keeps moving.
        /// </summary>
        internal static TimeSpan LivePosition(GlobalSystemMediaTransportControlsSessionTimelineProperties timeline, GlobalSystemMediaTransportControlsSessionPlaybackInfo playback)
        {
            var position = timeline.Position;
            if (playback.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return position;
            return Extrapolate(position, timeline.LastUpdatedTime, DateTimeOffset.Now, playback.PlaybackRate ?? 1.0, timeline.EndTime - timeline.StartTime);
        }

        /// <summary>The position reported at <paramref name="reportedAt"/>, moved on to <paramref name="now"/> at the given speed, never past the end.</summary>
        public static TimeSpan Extrapolate(TimeSpan position, DateTimeOffset reportedAt, DateTimeOffset now, double rate, TimeSpan duration)
        {
            var elapsed = now - reportedAt;
            if (elapsed <= TimeSpan.Zero || elapsed > TimeSpan.FromHours(12)) return position;   // a report from the future, or an absurdly old one, is not trusted
            if (rate <= 0) rate = 1.0;
            var live = position + TimeSpan.FromTicks((long)(elapsed.Ticks * rate));
            return duration > TimeSpan.Zero && live > duration ? duration : live;
        }

        private async Task RefreshAsync()
        {
            var session = _session;
            if (session == null) { Push(null); return; }

            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                var playback = session.GetPlaybackInfo();
                GlobalSystemMediaTransportControlsSessionTimelineProperties? timeline = null;
                try { timeline = session.GetTimelineProperties(); } catch { /* not every app reports a timeline */ }

                System.Windows.Media.ImageSource? art = null;
                if (props.Thumbnail != null)
                {
                    try { art = await LoadThumbnailAsync(props.Thumbnail); } catch { /* no art, or it failed to decode */ }
                }

                bool hasTimeline = timeline != null && timeline.EndTime > timeline.StartTime;
                Push(new MediaSnapshot
                {
                    Title = props.Title ?? "",
                    Artist = props.Artist ?? "",
                    AlbumTitle = props.AlbumTitle ?? "",
                    Thumbnail = art,
                    SourceApp = FriendlyAppName(session.SourceAppUserModelId),
                    IsPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    CanPlay = playback.Controls.IsPlayEnabled,
                    CanPause = playback.Controls.IsPauseEnabled,
                    CanNext = playback.Controls.IsNextEnabled,
                    CanPrevious = playback.Controls.IsPreviousEnabled,
                    CanSeek = playback.Controls.IsPlaybackPositionEnabled,
                    Position = hasTimeline ? LivePosition(timeline!, playback) : null,
                    Duration = hasTimeline ? timeline!.EndTime - timeline!.StartTime : null,
                });
            }
            catch
            {
                Push(null);
            }
        }

        private void Push(MediaSnapshot? snap)
        {
            _last = snap;
            Updated?.Invoke(snap);
        }

        private static async Task<System.Windows.Media.ImageSource?> LoadThumbnailAsync(IRandomAccessStreamReference reference)
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0) return null;

            var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);

            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze(); // safe to hand to the UI thread from here
            return bmp;
        }

        /// <summary>Turns an AUMID / exe name into a short label for the apps this is built for; anything else is shown as-is.</summary>
        private static string FriendlyAppName(string aumid)
        {
            if (string.IsNullOrWhiteSpace(aumid)) return "";
            var name = aumid;
            int bang = name.IndexOf('!');
            if (bang >= 0) name = name[..bang];
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

            if (name.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
            if (name.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase) || name.Contains("AppleInc.AppleMusic", StringComparison.OrdinalIgnoreCase)) return "Apple Music";
            if (name.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
            if (name.Contains("msedge", StringComparison.OrdinalIgnoreCase)) return "Edge";
            if (name.Contains("firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
            return name;
        }

        /// <summary>Jumps forward (positive) or back (negative) in what is playing, from where it is right now.</summary>
        public void SeekBy(TimeSpan delta)
        {
            var session = _session;
            if (session != null) _ = SeekAsync(session, delta);
        }

        private static async Task SeekAsync(GlobalSystemMediaTransportControlsSession session, TimeSpan delta)
        {
            try
            {
                var timeline = session.GetTimelineProperties();
                var playback = session.GetPlaybackInfo();
                var target = SeekTarget(LivePosition(timeline, playback), delta, timeline.EndTime - timeline.StartTime);
                await session.TryChangePlaybackPositionAsync((timeline.StartTime + target).Ticks);
            }
            catch { /* the app does not allow seeking, or it just closed */ }
        }

        /// <summary>Where a jump of <paramref name="delta"/> from <paramref name="position"/> lands: never before the start, never past the end.</summary>
        public static TimeSpan SeekTarget(TimeSpan position, TimeSpan delta, TimeSpan duration)
        {
            var target = position + delta;
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            if (duration > TimeSpan.Zero && target > duration) target = duration;
            return target;
        }

        public void PlayPause() => _ = _session?.TryTogglePlayPauseAsync();
        public void Next() => _ = _session?.TrySkipNextAsync();
        public void Previous() => _ = _session?.TrySkipPreviousAsync();
    }
}
