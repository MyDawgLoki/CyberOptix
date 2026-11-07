using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace CyberOptix.Services
{
    /// <summary>
    /// The small part of NVIDIA's driver interface (NVAPI) that controls per-display digital vibrance and hue.
    /// Everything fails soft: on a PC without an NVIDIA driver <see cref="Available"/> is false and nothing is called.
    /// </summary>
    public static class NvApi
    {
        public record NvDisplay(IntPtr Handle, string GdiName);

        // NVAPI is looked up by numeric function id through a single exported entry point.
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr QueryInterface(uint id);

        private delegate int InitializeFn();
        private delegate int EnumDisplayFn(uint index, out IntPtr handle);
        private delegate int DisplayNameFn(IntPtr handle, byte[] name);
        private delegate int GetDvcFn(IntPtr handle, uint outputId, ref DvcInfo info);
        private delegate int SetDvcFn(IntPtr handle, uint outputId, ref DvcInfo info);
        private delegate int GetHueFn(IntPtr handle, uint outputId, ref HueInfo info);
        private delegate int SetHueFn(IntPtr handle, uint outputId, int angle);

        [StructLayout(LayoutKind.Sequential)]
        private struct DvcInfo { public uint Version; public int Current, Min, Max, Default; }

        [StructLayout(LayoutKind.Sequential)]
        private struct HueInfo { public uint Version; public int Current, Default; }

        private static bool _tried;
        private static EnumDisplayFn? _enum;
        private static DisplayNameFn? _name;
        private static GetDvcFn? _getDvc;
        private static SetDvcFn? _setDvc;
        private static GetHueFn? _getHue;
        private static SetHueFn? _setHue;

        public static bool Available { get; private set; }

        private static T? Fn<T>(uint id) where T : Delegate
        {
            var p = QueryInterface(id);
            return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
        }

        public static void EnsureInit()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                var init = Fn<InitializeFn>(0x0150E828);
                if (init == null || init() != 0) return;

                _enum = Fn<EnumDisplayFn>(0x9ABDD40D);
                _name = Fn<DisplayNameFn>(0x22A78B05);
                _getDvc = Fn<GetDvcFn>(0x0E45002D);
                _setDvc = Fn<SetDvcFn>(0x4A82C2B1);
                _getHue = Fn<GetHueFn>(0x95B64341);
                _setHue = Fn<SetHueFn>(0xF5A0F22C);
                Available = _enum != null && _name != null && _getDvc != null && _setDvc != null;
            }
            catch
            {
                Available = false; // no NVIDIA driver, or an incompatible one
            }
        }

        public static List<NvDisplay> Displays()
        {
            EnsureInit();
            var list = new List<NvDisplay>();
            if (!Available) return list;

            for (uint i = 0; i < 16; i++)
            {
                if (_enum!(i, out var handle) != 0) break;
                var raw = new byte[64];
                if (_name!(handle, raw) != 0) continue;
                list.Add(new NvDisplay(handle, Encoding.ASCII.GetString(raw).TrimEnd('\0')));
            }
            return list;
        }

        private static NvDisplay? Find(string gdiName) =>
            Displays().Find(d => string.Equals(d.GdiName, gdiName, StringComparison.OrdinalIgnoreCase));

        private static uint Version<T>(int sizeOverride = 0) =>
            (uint)((sizeOverride > 0 ? sizeOverride : Marshal.SizeOf<T>()) | (1 << 16));

        /// <summary>Digital vibrance in percent (0 to 100, 50 is the driver default).</summary>
        public static bool TryGetVibrance(string gdiName, out int percent)
        {
            percent = 50;
            try
            {
                var d = Find(gdiName);
                if (d == null) return false;
                var info = new DvcInfo { Version = Version<DvcInfo>() };
                if (_getDvc!(d.Handle, 0, ref info) != 0) return false;
                percent = info.Current;
                return true;
            }
            catch { return false; }
        }

        public static bool SetVibrance(string gdiName, int percent)
        {
            try
            {
                var d = Find(gdiName);
                if (d == null) return false;
                var info = new DvcInfo { Version = Version<DvcInfo>() };
                if (_getDvc!(d.Handle, 0, ref info) != 0) return false;
                info.Current = Math.Clamp(percent, info.Min, info.Max);
                return _setDvc!(d.Handle, 0, ref info) == 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Puts digital vibrance and hue back to what the NVIDIA driver itself reports as its default for one display.
        /// These two are stored by the driver (the same place NVIDIA Control Panel keeps them), so they stay until changed back.
        /// </summary>
        public static bool ResetToDefaults(string gdiName)
        {
            try
            {
                var d = Find(gdiName);
                if (d == null) return false;
                bool any = false;

                var dvc = new DvcInfo { Version = Version<DvcInfo>() };
                if (_getDvc!(d.Handle, 0, ref dvc) == 0)
                {
                    // Trust the driver's own default when it makes sense; 50 % is NVIDIA's normal default.
                    int wanted = dvc.Default >= dvc.Min && dvc.Default <= dvc.Max ? dvc.Default : 50;
                    dvc.Current = Math.Clamp(wanted, dvc.Min, dvc.Max);
                    if (_setDvc!(d.Handle, 0, ref dvc) == 0) any = true;
                }

                if (_getHue != null && _setHue != null)
                {
                    var hue = new HueInfo { Version = Version<HueInfo>() };
                    int wanted = _getHue(d.Handle, 0, ref hue) == 0 ? hue.Default : 0;
                    if (_setHue(d.Handle, 0, Math.Clamp(wanted, 0, 359)) == 0) any = true;
                }
                return any;
            }
            catch { return false; }
        }

        /// <summary>Resets vibrance and hue to the driver defaults on every NVIDIA display. Used by the uninstaller.</summary>
        public static bool ResetAllDisplays()
        {
            bool any = false;
            foreach (var d in Displays())
                if (ResetToDefaults(d.GdiName)) any = true;
            return any;
        }

        /// <summary>Hue angle in degrees (0 to 359).</summary>
        public static bool TryGetHue(string gdiName, out int degrees)
        {
            degrees = 0;
            try
            {
                var d = Find(gdiName);
                if (d == null || _getHue == null) return false;
                var info = new HueInfo { Version = Version<HueInfo>() };
                if (_getHue(d.Handle, 0, ref info) != 0) return false;
                degrees = info.Current;
                return true;
            }
            catch { return false; }
        }

        public static bool SetHue(string gdiName, int degrees)
        {
            try
            {
                var d = Find(gdiName);
                if (d == null || _setHue == null) return false;
                return _setHue(d.Handle, 0, Math.Clamp(degrees, 0, 359)) == 0;
            }
            catch { return false; }
        }
    }
}
