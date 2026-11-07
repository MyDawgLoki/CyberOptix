using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CyberOptix.Services
{
    /// <summary>
    /// A PowerShell script on disk that is about to run with administrator rights. It gets an unpredictable name and is created exclusively,
    /// then stays open for reading (which blocks writers) from the moment its contents are verified until it is disposed. That way another
    /// program running as the same user cannot swap it for something else between "written" and "run".
    /// </summary>
    internal sealed class ScriptFile : IDisposable
    {
        private FileStream? _hold;

        public string Path { get; }

        private ScriptFile(string path, FileStream hold)
        {
            Path = path;
            _hold = hold;
        }

        /// <summary>An unpredictable file name in the user's temp folder, for output a script writes back.</summary>
        public static string TempName(string label, string extension) =>
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cyberoptix_{label}_{Guid.NewGuid():N}{extension}");

        public static ScriptFile Create(string content, string label)
        {
            var path = TempName(label, ".ps1");
            var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray();
            using (var w = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                w.Write(bytes, 0, bytes.Length);

            FileStream? hold = null;
            try
            {
                hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var back = new byte[hold.Length];
                int read = 0;
                while (read < back.Length)
                {
                    int n = hold.Read(back, read, back.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read != bytes.Length || !back.AsSpan().SequenceEqual(bytes))
                    throw new IOException("The script file changed while it was being prepared, so it was not run.");
                hold.Position = 0;
                return new ScriptFile(path, hold);
            }
            catch
            {
                hold?.Dispose();
                try { File.Delete(path); } catch { }
                throw;
            }
        }

        public void Dispose()
        {
            _hold?.Dispose();
            _hold = null;
            try { File.Delete(Path); } catch { }
        }
    }
}
