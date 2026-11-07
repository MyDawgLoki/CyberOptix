using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using CyberOptix.Models;

namespace CyberOptix.Services
{
    public record GdiDisplay(string DeviceName, int X, int Y, int Width, int Height, uint Orientation);

    /// <summary>
    /// Per-display orientation and color. Brightness, contrast and gamma use Windows gamma ramps (they work on any graphics card);
    /// digital vibrance and hue go through the NVIDIA driver and only exist on NVIDIA cards.
    /// </summary>
    public static class DisplayColorService
    {
        // ---------- native ----------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public uint dmFields;
            public int dmPositionX, dmPositionY;
            public uint dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent,
                        dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE dd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string deviceName, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsExW")]
        private static extern int ResetDisplaySettings(string deviceName, IntPtr dm, IntPtr hwnd, uint flags, IntPtr lParam);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateDC(string driver, string device, string? output, IntPtr init);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool SetDeviceGammaRamp(IntPtr hdc, ushort[] ramp);
        [DllImport("gdi32.dll")] private static extern bool GetDeviceGammaRamp(IntPtr hdc, [Out] ushort[] ramp);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const uint CDS_UPDATEREGISTRY = 0x1;
        private const uint DM_DISPLAYORIENTATION = 0x80, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000;

        private static DEVMODE NewDevMode() => new() { dmSize = (ushort)Marshal.SizeOf<DEVMODE>(), dmDeviceName = "", dmFormName = "" };

        // ---------- displays ----------

        public static List<GdiDisplay> GdiDisplays()
        {
            var list = new List<GdiDisplay>();
            for (uint i = 0; ; i++)
            {
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>(), DeviceName = "", DeviceString = "", DeviceID = "", DeviceKey = "" };
                if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
                if ((dd.StateFlags & 0x1) == 0) continue; // not attached to the desktop

                var dm = NewDevMode();
                if (EnumDisplaySettings(dd.DeviceName, ENUM_CURRENT_SETTINGS, ref dm))
                    list.Add(new GdiDisplay(dd.DeviceName, dm.dmPositionX, dm.dmPositionY, (int)dm.dmPelsWidth, (int)dm.dmPelsHeight, dm.dmDisplayOrientation));
            }
            return list;
        }

        // ---------- orientation ----------

        public record OrientationOption(string Name, uint Value)
        {
            public override string ToString() => Name;
        }

        public static readonly OrientationOption[] Orientations =
        {
            new("Landscape", 0), new("Portrait", 3), new("Landscape (flipped)", 2), new("Portrait (flipped)", 1),
        };

        public static uint GetOrientation(string deviceName)
        {
            var dm = NewDevMode();
            return EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm) ? dm.dmDisplayOrientation : 0;
        }

        /// <summary>
        /// Tries an orientation without saving it. Call <see cref="CommitOrientation"/> to keep it, or <see cref="RevertOrientation"/>
        /// (or just let the user's countdown expire) to go back. Returns null on success, otherwise a reason.
        /// </summary>
        public static string? TryOrientation(string deviceName, uint orientation)
        {
            var dm = NewDevMode();
            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm)) return "Windows could not read that display's current mode.";

            if ((dm.dmDisplayOrientation % 2) != (orientation % 2)) // landscape <-> portrait swaps width and height
                (dm.dmPelsWidth, dm.dmPelsHeight) = (dm.dmPelsHeight, dm.dmPelsWidth);
            dm.dmDisplayOrientation = orientation;
            dm.dmFields = DM_DISPLAYORIENTATION | DM_PELSWIDTH | DM_PELSHEIGHT;

            int rc = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, 0, IntPtr.Zero); // 0 = temporary, not saved
            return rc == 0 ? null : $"Windows could not rotate that display (code {rc}).";
        }

        public static string? CommitOrientation(string deviceName, uint orientation)
        {
            var dm = NewDevMode();
            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm)) return "Windows could not read the display mode.";
            dm.dmDisplayOrientation = orientation;
            dm.dmFields = DM_DISPLAYORIENTATION | DM_PELSWIDTH | DM_PELSHEIGHT;
            int rc = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            return rc == 0 ? null : $"Windows could not save the orientation (code {rc}).";
        }

        /// <summary>Goes back to the saved mode (what was in the registry before the trial).</summary>
        public static void RevertOrientation(string deviceName) =>
            ResetDisplaySettings(deviceName, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);

        // ---------- brightness / contrast / gamma ----------

        private static double Curve(double x, ChannelColor c)
        {
            double contrast = 0.5 + c.Contrast / 100.0;           // 50 -> 1.0
            double offset = (c.Brightness - 50) / 100.0 * 0.5;    // 50 -> 0
            double y = Math.Clamp((x - 0.5) * contrast + 0.5 + offset, 0, 1);
            return Math.Pow(y, 1.0 / Math.Max(0.05, c.Gamma));
        }

        private static ushort[] BuildRamp(DisplayColor c)
        {
            var ramp = new ushort[768]; // 256 red, 256 green, 256 blue
            for (int i = 0; i < 256; i++)
            {
                double x = Curve(i / 255.0, c.All); // "All channels" first, then each channel on top of it
                ramp[i] = (ushort)Math.Round(Curve(x, c.Red) * 65535);
                ramp[256 + i] = (ushort)Math.Round(Curve(x, c.Green) * 65535);
                ramp[512 + i] = (ushort)Math.Round(Curve(x, c.Blue) * 65535);
            }
            return ramp;
        }

        /// <summary>The ramp a display has right now (768 values), or null if Windows will not say.</summary>
        public static ushort[]? ReadRamp(string deviceName)
        {
            var dc = CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;
            try
            {
                var ramp = new ushort[768];
                return GetDeviceGammaRamp(dc, ramp) ? ramp : null;
            }
            finally { DeleteDC(dc); }
        }

        /// <summary>The untouched, straight-line ramp.</summary>
        public static ushort[] IdentityRamp()
        {
            var ramp = new ushort[768];
            for (int i = 0; i < 256; i++) ramp[i] = ramp[256 + i] = ramp[512 + i] = (ushort)(i * 257);
            return ramp;
        }

        public static bool WriteRamp(string deviceName, ushort[] ramp)
        {
            var dc = CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) return false;
            try { return SetDeviceGammaRamp(dc, ramp); }
            finally { DeleteDC(dc); }
        }

        /// <summary>Returns null on success, otherwise a short reason (Windows refuses very extreme ramps).</summary>
        public static string? ApplyRamp(string deviceName, DisplayColor color)
        {
            var dc = CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) return "Windows would not open that display.";
            try
            {
                return SetDeviceGammaRamp(dc, BuildRamp(color))
                    ? null
                    : "Windows refused that combination. Try less extreme brightness, contrast or gamma values.";
            }
            finally
            {
                DeleteDC(dc);
            }
        }

        // ---------- saved settings ----------

        public static DisplayColor Get(string monitorId)
        {
            var map = AppSettings.Current.DisplayColors;
            if (!map.TryGetValue(monitorId, out var c)) map[monitorId] = c = new DisplayColor();
            return c;
        }

        /// <summary>Re-applies whatever the user changed on each display. Called at launch.</summary>
        public static void ApplySaved()
        {
            var displays = DisplayService.GetDisplays();
            foreach (var d in displays)
            {
                if (string.IsNullOrEmpty(d.DeviceName) || !AppSettings.Current.DisplayColors.TryGetValue(d.Id, out var c) || c.IsDefault) continue;
                if (c.RampTouched) ApplyRamp(d.DeviceName, c);
                if (c.VibranceTouched) NvApi.SetVibrance(d.DeviceName, c.Vibrance);
                if (c.HueTouched) NvApi.SetHue(d.DeviceName, c.Hue);
            }
        }

        /// <summary>Applies an Optimizer profile's display values to the main display. Returns a short note about anything that could not be applied.</summary>
        public static string? ApplyProfileToMainDisplay(SettingsProfile p)
        {
            var main = DisplayService.GetDisplays().FirstOrDefault(d => d.IsPrimary);
            if (main == null || string.IsNullOrEmpty(main.DeviceName)) return "main display not found";

            var notes = new List<string>();
            var color = new DisplayColor { RampTouched = true };
            color.All.Brightness = p.Brightness;
            color.All.Contrast = p.Contrast;
            color.All.Gamma = p.Gamma;

            var ramp = ApplyRamp(main.DeviceName, color);
            if (ramp != null) notes.Add(ramp);

            if (NvApi.Available || NvApiProbe())
            {
                if (!NvApi.SetVibrance(main.DeviceName, p.DigitalVibrance)) notes.Add("digital vibrance");
                if (!NvApi.SetHue(main.DeviceName, p.Hue)) notes.Add("hue");
            }
            else
            {
                notes.Add("digital vibrance and hue need an NVIDIA graphics card");
            }

            // Remember it so the Display page and the next launch agree with what is on screen.
            var saved = Get(main.Id);
            saved.All = color.All;
            saved.RampTouched = true;
            saved.Vibrance = p.DigitalVibrance; saved.VibranceTouched = true;
            saved.Hue = p.Hue; saved.HueTouched = true;
            AppSettings.Save();

            return notes.Count == 0 ? null : string.Join("; ", notes);
        }

        private static bool NvApiProbe()
        {
            NvApi.EnsureInit();
            return NvApi.Available;
        }

        /// <summary>Opens the NVIDIA Control Panel as the normal user (through Explorer), by its exact Store address, so nothing in the app's own folder can stand in for it.</summary>
        public static void OpenNvidiaControlPanel() =>
            DockService.Launch(@"shell:AppsFolder\NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIACorp.NVIDIAControlPanel");
    }
}
