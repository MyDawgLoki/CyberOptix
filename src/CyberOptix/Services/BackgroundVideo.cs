using System;
using System.IO;
using System.Reflection;

namespace CyberOptix.Services
{
    /// <summary>The silent looping video that is the app's default wallpaper. It is built into CyberOptix and copied out to the (administrator-only) data folder, because Windows plays video from a file.</summary>
    public static class BackgroundVideo
    {
        private const string Resource = "Assets/CyberOptix-Wallpaper-Loop.mp4";

        /// <summary>Adds a line to a small log next to the settings (kept under 20 KB), so a video that will not play can be diagnosed. Holds no personal information.</summary>
        public static void Log(string message)
        {
            try
            {
                var path = AppPaths.Secure("wallpaper-video.log");
                if (File.Exists(path) && new FileInfo(path).Length > 20_000) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>The path of the video file, copied out on first use. Null if it is not available.</summary>
        public static string? EnsureFile()
        {
            try
            {
                using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource);
                if (source == null) return null;

                var destination = AppPaths.Secure("wallpaper-loop.mp4");
                if (File.Exists(destination) && new FileInfo(destination).Length == source.Length) return destination;

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var file = File.Create(destination);
                source.CopyTo(file);
                return destination;
            }
            catch { return null; }
        }
    }
}
