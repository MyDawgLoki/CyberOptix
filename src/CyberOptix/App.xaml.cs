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

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
            _single = new Mutex(true, @"Local\CyberOptix.SingleInstance", out var first);
            if (!first)
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
            new Thread(() =>
            {
                while (show.WaitOne()) window.ShowFromTray();
            }) { IsBackground = true, Name = "CyberOptix show" }.Start();
        }
    }
}
