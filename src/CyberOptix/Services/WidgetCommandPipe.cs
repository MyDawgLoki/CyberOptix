using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using CyberOptix.Widgets;

namespace CyberOptix.Services
{
    /// <summary>
    /// How the desktop's right-click items reach the running app. They run a tiny script that writes one word to this pipe, so choosing
    /// "Unlock Widgets" needs no administrator prompt even though CyberOptix itself runs as administrator.
    /// Only the signed-in user can open the pipe, and only four words are understood (unlock, lock, toggle, restore).
    /// </summary>
    public static class WidgetCommandPipe
    {
        public const string PipeName = "CyberOptix.Widgets";
        private static Thread? _thread;
        private static System.Windows.Threading.Dispatcher? _dispatcher;
        private static volatile bool _stop;

        /// <summary>The last word received and the last problem, for diagnosing.</summary>
        public static string? LastLine, LastError;

        public static void Start()
        {
            if (_thread != null) return;
            _dispatcher = Application.Current?.Dispatcher;   // started from the UI thread: commands are handed back to it
            _stop = false;
            _thread = new Thread(Loop) { IsBackground = true, Name = "CyberOptix widget commands" };
            _thread.Start();
        }

        public static void Stop() => _stop = true;

        private static NamedPipeServerStream Create()
        {
            var security = new PipeSecurity();
            var me = WindowsIdentity.GetCurrent().User!;
            security.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.ReadWrite, AccessControlType.Allow));
            return NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.None, 0, 0, security);
        }

        private static void Loop()
        {
            while (!_stop)
            {
                try
                {
                    using var server = Create();
                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    var line = reader.ReadLine();
                    LastLine = line;
                    if (line != null) Handle(line);
                }
                catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message; Thread.Sleep(500); }
            }
        }

        /// <summary>Runs one command word. Anything that is not exactly one of the known words is ignored.</summary>
        public static void Handle(string line)
        {
            var word = line.Trim().ToLowerInvariant();
            if (word.Length > 10 || (word != "unlock" && word != "lock" && word != "toggle" && word != "restore")) return;
            (_dispatcher ?? Application.Current?.Dispatcher)?.BeginInvoke(new Action(() => WidgetLock.Run(word)));
        }
    }
}
