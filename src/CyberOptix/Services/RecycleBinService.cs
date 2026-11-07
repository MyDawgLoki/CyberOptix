using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CyberOptix.Services
{
    /// <summary>
    /// The Windows Recycle Bin, for the dock icon: how many items it holds, its icon (empty or full), opening and emptying it, and sending files
    /// and folders to it the normal way (so they can be restored, and Windows asks first when something cannot go to the bin).
    /// </summary>
    public static class RecycleBinService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHSTOCKICONINFO
        {
            public int cbSize;
            public IntPtr hIcon;
            public int iSysImageIndex;
            public int iIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szPath;
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetStockIconInfo(int id, uint flags, ref SHSTOCKICONINFO info);

        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

        private const int SIID_RECYCLER = 31, SIID_RECYCLERFULL = 32;
        private const uint FO_DELETE = 3;
        private const ushort FOF_ALLOWUNDO = 0x40, FOF_WANTNUKEWARNING = 0x4000;

        /// <summary>How many items the Recycle Bin holds across all drives.</summary>
        public static long Count()
        {
            try
            {
                var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
                return SHQueryRecycleBin(null, ref info) == 0 ? info.i64NumItems : 0;
            }
            catch { return 0; }
        }

        private static ImageSource? _empty, _full;

        /// <summary>The Recycle Bin's own icon, empty or full, as Windows draws it.</summary>
        public static ImageSource? Icon(bool full)
        {
            var cached = full ? _full : _empty;
            if (cached != null) return cached;
            try
            {
                var info = new SHSTOCKICONINFO { cbSize = Marshal.SizeOf<SHSTOCKICONINFO>() };
                // SHGSI_ICON (0x100) with the large size (0)
                if (SHGetStockIconInfo(full ? SIID_RECYCLERFULL : SIID_RECYCLER, 0x100, ref info) != 0 || info.hIcon == IntPtr.Zero) return null;
                try
                {
                    var image = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    image.Freeze();
                    if (full) _full = image; else _empty = image;
                    return image;
                }
                finally { DestroyIcon(info.hIcon); }
            }
            catch { return null; }
        }

        /// <summary>Opens the Recycle Bin in a File Explorer window.</summary>
        public static void Open() => DockService.Launch("shell:RecycleBinFolder");

        /// <summary>Empties the bin. Windows shows its own "are you sure" and progress.</summary>
        public static void Empty(IntPtr owner)
        {
            try { SHEmptyRecycleBin(owner, null, 0); } catch { }
        }

        /// <summary>
        /// The paths that are safe to hand to the shell for deleting: real, full paths of things that exist. Nothing else is passed on, so a
        /// stray or odd entry in a drop cannot become part of the delete.
        /// </summary>
        public static List<string> Usable(IEnumerable<string?>? paths)
        {
            var list = new List<string>();
            if (paths == null) return list;
            foreach (var p in paths)
            {
                if (string.IsNullOrWhiteSpace(p) || p.IndexOfAny(new[] { '*', '?', '\0' }) >= 0) continue;
                string full;
                try { full = System.IO.Path.GetFullPath(p); } catch { continue; }
                if (!System.IO.File.Exists(full) && !System.IO.Directory.Exists(full)) continue;
                if (System.IO.Path.GetPathRoot(full)?.TrimEnd('\\').Equals(full.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) == true) continue;   // never a whole drive
                if (IsProtected(full)) continue;
                list.Add(full);
            }
            return list;
        }

        /// <summary>
        /// Places an elevated program must never delete because somebody dropped them on the dock: Windows itself, installed programs, shared
        /// program data, the Recycle Bin and other people's profiles. CyberOptix runs as administrator, so without this a drop made by some
        /// other program could remove things its own user could not.
        /// </summary>
        public static bool IsProtected(string fullPath)
        {
            string p = fullPath.TrimEnd('\\') + "\\";
            bool Under(string? folder) => !string.IsNullOrEmpty(folder) && p.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

            if (Under(Environment.GetFolderPath(Environment.SpecialFolder.Windows))) return true;
            if (Under(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))) return true;
            if (Under(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86))) return true;
            if (Under(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))) return true;
            string? root = System.IO.Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root))
            {
                if (Under(root + "$Recycle.Bin") || Under(root + "System Volume Information") || Under(root + "Recovery")) return true;
                // the folder that holds every profile: only the signed-in user's own profile (and the shared Public folder) is fair game
                string users = System.IO.Path.Combine(root, "Users");
                string mine = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (Under(users) && !Under(mine) && !Under(System.IO.Path.Combine(users, "Public"))) return true;
            }
            return false;
        }

        /// <summary>
        /// Sends files, folders and shortcuts to the Recycle Bin, exactly as pressing Delete in File Explorer would: they can be restored, and Windows
        /// asks before deleting anything that cannot go to the bin. Returns how many were handed over.
        /// </summary>
        public static int Recycle(IEnumerable<string?>? paths, IntPtr owner)
        {
            var list = Usable(paths);
            if (list.Count == 0) return 0;
            try
            {
                var op = new SHFILEOPSTRUCT
                {
                    hwnd = owner,
                    wFunc = FO_DELETE,
                    pFrom = string.Join("\0", list) + "\0\0",
                    fFlags = (ushort)(FOF_ALLOWUNDO | FOF_WANTNUKEWARNING),
                };
                int result = SHFileOperationW(ref op);
                return result == 0 && !op.fAnyOperationsAborted ? list.Count : 0;
            }
            catch { return 0; }
        }
    }
}
