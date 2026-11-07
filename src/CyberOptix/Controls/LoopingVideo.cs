using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CyberOptix.Services;

namespace CyberOptix.Controls
{
    /// <summary>
    /// A silent video that plays forever with no visible seam. Two players take turns: shortly before the one in front reaches its end, the other
    /// starts from the beginning and fades in over it, so the end melts into the start instead of jumping back. It plays no sound.
    /// </summary>
    public sealed class LoopingVideo : Grid
    {
        /// <summary>How long the end of the video dissolves into its start.</summary>
        public static readonly TimeSpan Blend = TimeSpan.FromSeconds(1.2);

        private readonly MediaElement _a = NewPlayer();
        private readonly MediaElement _b = NewPlayer();
        private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(40) };
        private MediaElement _front;
        private MediaElement _back;
        private TimeSpan _length = TimeSpan.Zero;
        private bool _blending;
        private bool _active;
        private string? _path;

        /// <summary>Raised when the video cannot be played (for example Windows has no video codec); the caller shows a still picture instead.</summary>
        public event Action? Failed;

        public LoopingVideo()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
            _front = _a;
            _back = _b;
            Children.Add(_a);
            Children.Add(_b);
            _a.Opacity = 1;
            _b.Opacity = 0;
            _a.MediaOpened += (_, _) => { _failures = 0; ReadLength(_a); };
            _b.MediaOpened += (_, _) => { _failures = 0; ReadLength(_b); };
            _a.MediaFailed += (_, e) => OnFailed(e);
            _b.MediaFailed += (_, e) => OnFailed(e);
            _watch.Tick += (_, _) => Watch();
            // a video that reached its end without the blend (or a player that stopped for any reason) starts over instead of freezing on its last picture
            _a.MediaEnded += (_, _) => Ended(_a);
            _b.MediaEnded += (_, _) => Ended(_b);
            // the picture can stop after the PC wakes, is unlocked, or a display changes: start the video again then
            Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode == Microsoft.Win32.PowerModes.Resume) Dispatcher.BeginInvoke(new Action(() => RecoverSoon())); };
            Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) => { if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.SessionLogon or Microsoft.Win32.SessionSwitchReason.ConsoleConnect or Microsoft.Win32.SessionSwitchReason.RemoteConnect) Dispatcher.BeginInvoke(new Action(() => RecoverSoon())); };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => RecoverSoon()));
        }

        private DateTime _lastMove = DateTime.UtcNow;
        private TimeSpan _lastPosition = TimeSpan.Zero;
        private DateTime _graceUntil = DateTime.MinValue;

        private void Ended(MediaElement player)
        {
            if (!_active || _blending && player == _back) return;
            if (player == _front)
            {
                player.Position = TimeSpan.Zero;   // the blend did not get to it in time: loop plainly (a small jump, but never a frozen picture)
                player.Play();
            }
        }

        /// <summary>Reopens the video after a short wait (the graphics driver needs a moment after wake, unlock or a display change).</summary>
        private void RecoverSoon()
        {
            var wait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            wait.Tick += (_, _) => { wait.Stop(); Recover("system event"); };
            wait.Start();
        }

        /// <summary>Opens the video again from scratch and plays it. Used when the picture has stopped moving.</summary>
        private void Recover(string why)
        {
            var path = _path;
            if (path == null) return;
            BackgroundVideo.Log("restarting the video: " + why);
            _path = null;
            Load(path);
            _graceUntil = DateTime.UtcNow.AddSeconds(5);
        }

        private static MediaElement NewPlayer() => new()
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            IsMuted = true,                    // never any sound
            Volume = 0,
            Stretch = Stretch.UniformToFill,   // fills the whole window: a window shaped differently from the video crops the edges instead of leaving bars
            ScrubbingEnabled = false,
            IsHitTestVisible = false,
        };

        /// <summary>Opens the video (once; asking again for the same file does nothing). Playing starts when <see cref="SetActive"/> says so.</summary>
        public void Load(string path)
        {
            if (_path == path) return;
            _path = path;
            _length = TimeSpan.Zero;
            _blending = false;
            _a.BeginAnimation(OpacityProperty, null); _b.BeginAnimation(OpacityProperty, null);   // a fade left over from before must not touch the new start
            _front = _a; _back = _b;
            _graceUntil = DateTime.UtcNow.AddSeconds(5);
            _lastMove = DateTime.UtcNow;
            _lastPosition = TimeSpan.Zero;
            _a.Opacity = 1; _b.Opacity = 0;
            SetZIndex(_a, 0); SetZIndex(_b, 1);
            var uri = new Uri(path);
            _a.Source = uri;
            _b.Source = uri;
            // Both are opened and parked on the first picture, so the second is ready the moment it is needed.
            _a.Play(); _a.Pause(); _b.Play(); _b.Pause();
            if (_active) _a.Play();
        }

        /// <summary>Plays while true, and holds still (using no CPU or GPU) while false, for example when the window is minimized.</summary>
        public void SetActive(bool active)
        {
            if (_active == active) return;
            _active = active;
            if (active)
            {
                if (_path == null) return;
                _lastMove = DateTime.UtcNow; _graceUntil = DateTime.UtcNow.AddSeconds(3);
                _front.Play();
                if (_blending) _back.Play();
                _watch.Start();
            }
            else
            {
                _watch.Stop();
                _a.Pause(); _b.Pause();
            }
        }

        private int _failures;

        /// <summary>A player that will not start is tried again (a few times, a moment apart) before the caller is told to show a still picture instead.</summary>
        private void OnFailed(ExceptionRoutedEventArgs e)
        {
            BackgroundVideo.Log("player failed: " + (e.ErrorException?.GetType().Name ?? "?") + ": " + e.ErrorException?.Message);
            if (++_failures >= 3) { Failed?.Invoke(); return; }
            var path = _path;
            var retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            retry.Tick += (_, _) =>
            {
                retry.Stop();
                if (path == null || _path != path) return;
                _path = null;                    // forget it, so Load opens it from scratch
                Load(path);
            };
            retry.Start();
        }

        private void ReadLength(MediaElement player)
        {
            if (player.NaturalDuration.HasTimeSpan && player.NaturalDuration.TimeSpan > Blend * 2)
                _length = player.NaturalDuration.TimeSpan;
        }

        private void Watch()
        {
            // watchdog: the playing picture has not moved for a while, so it has frozen: start it again
            var pos = _front.Position;
            if (pos != _lastPosition) { _lastPosition = pos; _lastMove = DateTime.UtcNow; }
            else if (DateTime.UtcNow > _graceUntil && DateTime.UtcNow - _lastMove > TimeSpan.FromSeconds(2.5)) { _lastMove = DateTime.UtcNow; Recover("the picture stopped moving"); return; }
            // a blend that never finished (its fade stopped while the window was hidden) must not stop the next one
            if (_blending && _back.Opacity >= 1 && _front.Opacity <= 0) _blending = false;
            if (_blending || _length == TimeSpan.Zero) return;
            if (_front.Position >= _length - Blend) StartBlend();
        }

        private void StartBlend()
        {
            _blending = true;
            var incoming = _back;
            var outgoing = _front;
            SetZIndex(outgoing, 0);
            SetZIndex(incoming, 1);          // the new one sits on top, and only it fades: no dip in brightness halfway
            incoming.Position = TimeSpan.Zero;
            incoming.Opacity = 0;
            incoming.Play();

            var fade = new DoubleAnimation(0, 1, new Duration(Blend)) { FillBehavior = FillBehavior.Stop };
            fade.Completed += (_, _) =>
            {
                incoming.Opacity = 1;
                outgoing.Pause();
                outgoing.Opacity = 0;
                outgoing.Position = TimeSpan.Zero;   // parked on the first picture, ready for the next turn
                _front = incoming;
                _back = outgoing;
                _blending = false;
            };
            incoming.BeginAnimation(OpacityProperty, fade);
        }
    }
}
