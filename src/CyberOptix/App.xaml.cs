using System;
using System.Linq;
using System.Threading;
using System.Windows;
namespace CyberOptix
{
    public partial class App : Application
    {
        private const string ShowEventName = @"Local\CyberOptix.ShowWindow";
        private static Mutex? _single;

        /// <summary>
        /// True when this is the only CyberOptix. Two copies would fight over the taskbar (hiding it, bringing it back, Windows complaining
        /// that "a toolbar is already hidden"), so the lock is checked twice: by name, and by looking for another CyberOptix in this sign-in session.
        /// A copy that is closing (a restart) gets a few seconds to let go before this one gives up.
        /// </summary>
        private static bool TakeSingleInstance()
        {
            bool owned = false;
            try
            {
                _single = new Mutex(true, @"Local\CyberOptix.SingleInstance", out owned);
                if (!owned)
                {
                    try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }   // wakes the running one, in case it is in the tray
                    try { owned = _single.WaitOne(TimeSpan.FromSeconds(4)); }
                    catch (AbandonedMutexException) { owned = true; }   // the other copy died without letting go
                }
            }
            catch (UnauthorizedAccessException) { owned = false; }   // the lock belongs to a copy we cannot open: it is running
            catch { owned = true; }                                   // could not make the lock at all: the check below decides

            if (!owned) return false;
            try
            {
                // belt and braces: another CyberOptix.exe in this session that still holds on after the lock was ours is a real second copy
                var me = System.Diagnostics.Process.GetCurrentProcess();
                for (int wait = 0; wait < 8; wait++)
                {
                    bool other = System.Diagnostics.Process.GetProcessesByName(me.ProcessName)
                        .Any(p => { try { return p.Id != me.Id && p.SessionId == me.SessionId && !p.HasExited && !Services.CrashGuard.IsWatchdog(p.Id); } catch { return false; } });
                    if (!other) return true;
                    Thread.Sleep(500);   // maybe a copy that is closing
                }
                return false;
            }
            catch { return true; }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Services.CrashGuard.StandDown();   // closed on purpose: the watchdog leaves things as they are
            base.OnExit(e);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // The watchdog: another copy of this program that only waits for the real one, and puts the taskbar and the desktop items back
            // if the real one is killed (see CrashGuard).
            var dog = e.Args.FirstOrDefault(a => a.StartsWith(Services.CrashGuard.Argument));
            if (dog != null)
            {
                if (int.TryParse(dog.Substring(Services.CrashGuard.Argument.Length), out var parent)) Services.CrashGuard.RunWatchdog(parent);
                Shutdown(0);
                return;
            }

            // The uninstaller also runs "CyberOptix.exe --restore-desktop": everything moved into the Apps panel goes back where it came from.
            if (e.Args.Any(a => a == "--restore-desktop"))
            {
                try { Services.DesktopItemsService.MoveAllBack(); } catch { }
                Shutdown(0);
                return;
            }

            // The uninstaller runs "CyberOptix.exe --restore-originals": put Windows back the way it was, show nothing, and exit.
            // An optional list picks which groups to put back: --restore-originals=tweaks,services,colors,rotation,remaps,pointer,scrolling,cursors
            var restore = e.Args.FirstOrDefault(a => a.StartsWith("--restore-originals"));
            if (restore != null)
            {
                var list = restore.Contains('=') ? restore.Substring(restore.IndexOf('=') + 1) : null;
                try { Services.OriginalSettingsService.RestoreAll(Services.RestoreOptions.Parse(list)); } catch { }
                Shutdown(0);
                return;
            }

            // One CyberOptix at a time: a second launch just brings the first one (which may be in the tray) to the front.
            if (!TakeSingleInstance())
            {
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                Shutdown(0);
                return;
            }
            var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

            var window = new MainWindow();
            // Started by Windows at sign-in: load everything (dock, widgets, Glass Cursor, hotkeys) but stay quietly in the tray.
            if (e.Args.Any(a => a == Services.AutoStartService.StartupArgument) && Models.AppSettings.Current.StartQuietly) window.StartHidden();
            window.Show();
            SessionEnding += (_, _) => Services.CrashGuard.StandDown();   // Windows shutting down is not a kill
            Services.CrashGuard.Start();
            new Thread(() =>
            {
                while (show.WaitOne()) window.ShowFromTray();
            }) { IsBackground = true, Name = "CyberOptix show" }.Start();
        }
    }
}
