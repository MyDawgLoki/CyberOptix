using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CyberOptix.Services
{
    /// <summary>
    /// Apps that Windows tells apart by their AppUserModelID rather than by the program file: Store-style apps such as Settings, which all
    /// draw their windows through one shared host program, and Control Panel, which runs inside Explorer. The Windows taskbar gives each of
    /// these its own button; this is what lets the dock do the same.
    /// Such an app is identified by the address Windows uses for it in the Start menu, "shell:AppsFolder\&lt;AppUserModelID&gt;". It works as
    /// a path everywhere in the dock: it can be pinned, launched through Explorer, and it has its own name and icon.
    /// </summary>
    public static class ShellApps
    {
        public const string Prefix = @"shell:AppsFolder\";

        public static bool IsShellApp(string path) => path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

        public static string PathFor(string appId) => Prefix + appId;

        // ---------- which app a window belongs to ----------

        [DllImport("shell32.dll")]
        private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

        /// <summary>The AppUserModelID Windows has for a window (what the taskbar groups by), or null when it has none.</summary>
        public static string? AppIdOf(IntPtr hwnd)
        {
            try
            {
                var iid = typeof(IPropertyStore).GUID;
                if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0 || store == null) return null;
                var key = new PropertyKey { FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = 5 };   // System.AppUserModel.ID
                store.GetValue(ref key, out var value);
                try { return value.Type == 31 && value.Value != IntPtr.Zero ? Marshal.PtrToStringUni(value.Value) : null; }   // 31 = text
                finally { PropVariantClear(ref value); }
            }
            catch { return null; }
        }

        // ---------- its name and icon ----------

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem parent);
            void GetDisplayName(int sigdn, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Size { public int Width, Height; }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(Size size, int flags, out IntPtr bitmap);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);

        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct Bitmap { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public IntPtr Bits; }

        [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(IntPtr handle, int size, out Bitmap bitmap);

        /// <summary>The name Windows shows for the app, like "Settings" or "Control Panel". Null if Windows does not know it.</summary>
        public static string? NameOf(string path)
        {
            try
            {
                var iid = typeof(IShellItem).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var o);
                ((IShellItem)o).GetDisplayName(0 /* the normal name */, out var text);
                try { return Marshal.PtrToStringUni(text); }
                finally { Marshal.FreeCoTaskMem(text); }
            }
            catch { return null; }
        }

        /// <summary>The app's icon as Windows draws it in the Start menu, or null.</summary>
        public static ImageSource? IconOf(string path, int pixels = 64)
        {
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var o);
                if (((IShellItemImageFactory)o).GetImage(new Size { Width = pixels, Height = pixels }, 0x4 /* icon only */, out var bitmap) != 0 || bitmap == IntPtr.Zero) return null;
                try
                {
                    // Copy the pixels out with their transparency (the simple converters turn transparent into black).
                    if (GetObject(bitmap, Marshal.SizeOf<Bitmap>(), out var info) > 0 && info.Bits != IntPtr.Zero && info.BitsPixel == 32)
                    {
                        var image = BitmapSource.Create(info.Width, Math.Abs(info.Height), 96, 96, PixelFormats.Pbgra32, null, info.Bits, info.WidthBytes * Math.Abs(info.Height), info.WidthBytes);
                        image.Freeze();
                        return image;
                    }
                    var plain = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    plain.Freeze();
                    return plain;
                }
                finally { DeleteObject(bitmap); }
            }
            catch { return null; }
        }
    }
}
