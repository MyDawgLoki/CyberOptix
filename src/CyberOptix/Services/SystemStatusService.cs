using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace CyberOptix.Services
{
    /// <summary>Speaker and microphone volume and mute, through Windows' Core Audio.</summary>
    public static class AudioService
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            void EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            void RegisterEndpointNotificationCallback(IntPtr client);
            void UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            void GetCount(out uint count);
            void Item(uint index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            void OpenPropertyStore(int access, out IPropertyStore properties);
            void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            void GetState(out int state);
        }

        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

        // ----- switching the default device -----
        // IPolicyConfig is not an official Windows API, but it is what every sound switcher uses and it works on Windows 10 and 11.
        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] private class PolicyConfigClient { }

        [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
            [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
            [PreserveSig] int ResetDeviceFormat(IntPtr a);
            [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
            [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
            [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
            [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
            [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
            [PreserveSig] int SetEndpointVisibility(IntPtr a, int b);
        }

        // ----- the volume mixer: one session per app that is making sound -----
        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            void GetAudioSessionControl(IntPtr groupingId, uint flags, out IntPtr session);
            void GetSimpleAudioVolume(IntPtr groupingId, uint flags, out IntPtr volume);
            void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
            void RegisterSessionNotification(IntPtr n);
            void UnregisterSessionNotification(IntPtr n);
            void RegisterDuckNotification(IntPtr id, IntPtr n);
            void UnregisterDuckNotification(IntPtr n);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            void GetCount(out int count);
            void GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
        }

        [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            // IAudioSessionControl
            void GetState(out int state);
            void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
            void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
            void GetGroupingParam(out Guid grouping);
            void SetGroupingParam(ref Guid grouping, ref Guid context);
            void RegisterAudioSessionNotification(IntPtr n);
            void UnregisterAudioSessionNotification(IntPtr n);
            // IAudioSessionControl2
            void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            void GetProcessId(out uint pid);
            [PreserveSig] int IsSystemSoundsSession();
            void SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            void SetMasterVolume(float level, ref Guid context);
            void GetMasterVolume(out float level);
            void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            void RegisterControlChangeNotify(IntPtr notify);
            void UnregisterControlChangeNotify(IntPtr notify);
            void GetChannelCount(out uint count);
            void SetMasterVolumeLevel(float level, ref Guid context);
            void SetMasterVolumeLevelScalar(float level, ref Guid context);
            void GetMasterVolumeLevel(out float level);
            void GetMasterVolumeLevelScalar(out float level);
            void SetChannelVolumeLevel(uint channel, float level, ref Guid context);
            void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            void GetChannelVolumeLevel(uint channel, out float level);
            void GetChannelVolumeLevelScalar(uint channel, out float level);
            void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        public const int Speakers = 0, Microphone = 1;

        private static IAudioEndpointVolume? Endpoint(int flow)
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                enumerator.GetDefaultAudioEndpoint(flow, 0, out var device);
                var iid = typeof(IAudioEndpointVolume).GUID;
                device.Activate(ref iid, 23, IntPtr.Zero, out var volume);
                return (IAudioEndpointVolume)volume;
            }
            catch
            {
                return null; // no device of that kind
            }
        }

        public static bool Available(int flow) => Endpoint(flow) != null;

        /// <summary>0 to 100, or -1 when there is no device.</summary>
        public static int GetLevel(int flow)
        {
            var e = Endpoint(flow);
            if (e == null) return -1;
            try { e.GetMasterVolumeLevelScalar(out var v); return (int)Math.Round(v * 100); } catch { return -1; }
        }

        public static void SetLevel(int flow, int percent)
        {
            var e = Endpoint(flow);
            var ctx = Guid.Empty;
            try { e?.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref ctx); } catch { }
        }

        public static bool GetMute(int flow)
        {
            var e = Endpoint(flow);
            if (e == null) return false;
            try { e.GetMute(out var m); return m; } catch { return false; }
        }

        public static void SetMute(int flow, bool mute)
        {
            var e = Endpoint(flow);
            var ctx = Guid.Empty;
            try { e?.SetMute(mute, ref ctx); } catch { }
        }

        // ---------- devices ----------

        /// <summary>One sound output (speakers, headphones) or input (microphone) that is plugged in and switched on.</summary>
        public record AudioDevice(string Id, string Name, bool IsDefault);

        private static PropertyKey Key(string guid, uint pid) => new() { FormatId = new Guid(guid), PropertyId = pid };

        private static string ReadString(IPropertyStore store, PropertyKey key)
        {
            store.GetValue(ref key, out var value);
            try { return value.Type == 31 && value.Value != IntPtr.Zero ? Marshal.PtrToStringUni(value.Value) ?? "" : ""; }   // 31 = a text value
            finally { PropVariantClear(ref value); }
        }

        private static string? DefaultId(IMMDeviceEnumerator enumerator, int flow)
        {
            try { enumerator.GetDefaultAudioEndpoint(flow, 0, out var device); device.GetId(out var id); return id; }
            catch { return null; }
        }

        /// <summary>The name Windows shows for a device, like "Speakers (Realtek Audio)".</summary>
        private static string NameOf(IMMDevice device)
        {
            try
            {
                device.OpenPropertyStore(0, out var props);
                var name = ReadString(props, Key("a45c254e-df1c-4efd-8020-67d146a850e0", 14));
                return name.Length > 0 ? name : "Audio device";
            }
            catch { return "Audio device"; }
        }

        /// <summary>Every active device of that kind. Empty when there are none.</summary>
        public static List<AudioDevice> Devices(int flow)
        {
            var list = new List<AudioDevice>();
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                string? def = DefaultId(enumerator, flow);
                enumerator.EnumAudioEndpoints(flow, 1 /* only devices that are on */, out var devices);
                devices.GetCount(out var count);
                for (uint i = 0; i < count; i++)
                {
                    devices.Item(i, out var device);
                    device.GetId(out var id);
                    list.Add(new AudioDevice(id, NameOf(device), id == def));
                }
            }
            catch { }
            return list;
        }

        /// <summary>Makes a device the one Windows uses by default, for apps, media and calls.</summary>
        public static bool SetDefault(string deviceId)
        {
            try
            {
                var policy = (IPolicyConfig)new PolicyConfigClient();
                bool ok = true;
                for (int role = 0; role < 3; role++)   // console, multimedia, communications
                    if (policy.SetDefaultEndpoint(deviceId, role) != 0) ok = false;
                return ok;
            }
            catch { return false; }
        }

        // ---------- listen to this device (microphone monitoring) ----------

        private static PropertyKey ListenKey => Key("24dbb0fc-9311-4b3d-9cf0-18ff155639d4", 1);

        /// <summary>Whether "Listen to this device" is on for a microphone. Null when Windows would not say.</summary>
        public static bool? GetListen(string deviceId)
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                enumerator.GetDevice(deviceId, out var device);
                device.OpenPropertyStore(0, out var props);
                var key = ListenKey;
                props.GetValue(ref key, out var value);
                try { return value.Type == 11 ? (short)value.Value.ToInt64() != 0 : false; }   // 11 = yes/no; nothing stored means off
                finally { PropVariantClear(ref value); }
            }
            catch { return null; }
        }

        /// <summary>Turns "Listen to this device" on or off. Needs administrator rights, which CyberOptix has.</summary>
        public static bool SetListen(string deviceId, bool on)
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                enumerator.GetDevice(deviceId, out var device);
                device.OpenPropertyStore(1 /* write */, out var props);
                var key = ListenKey;
                var value = new PropVariant { Type = 11, Value = on ? new IntPtr(-1) : IntPtr.Zero };
                props.SetValue(ref key, ref value);
                props.Commit();
                return true;
            }
            catch { return false; }
        }

        /// <summary>The id of the device Windows uses by default, or null.</summary>
        public static string? DefaultDeviceId(int flow)
        {
            try { return DefaultId((IMMDeviceEnumerator)new MMDeviceEnumeratorCom(), flow); } catch { return null; }
        }

        // ---------- volume mixer ----------

        /// <summary>One app in the volume mixer. An app with several sound streams (a browser, say) counts as one.</summary>
        public sealed class AudioSession
        {
            private readonly List<ISimpleAudioVolume> _volumes = new();

            public string Name { get; }
            public bool IsSystem { get; }
            internal AudioSession(string name, bool system) { Name = name; IsSystem = system; }
            internal void Add(object volume) => _volumes.Add((ISimpleAudioVolume)volume);

            public int Level
            {
                get { try { _volumes[0].GetMasterVolume(out var v); return (int)Math.Round(v * 100); } catch { return 0; } }
            }

            public bool Muted
            {
                get { try { _volumes[0].GetMute(out var m); return m; } catch { return false; } }
            }

            public void SetLevel(int percent)
            {
                var ctx = Guid.Empty;
                foreach (var v in _volumes) { try { v.SetMasterVolume(Math.Clamp(percent, 0, 100) / 100f, ref ctx); } catch { } }
            }

            public void SetMuted(bool mute)
            {
                var ctx = Guid.Empty;
                foreach (var v in _volumes) { try { v.SetMute(mute, ref ctx); } catch { } }
            }
        }

        private static string AppName(uint pid)
        {
            try
            {
                using var p = Process.GetProcessById((int)pid);
                string? text = null;
                try { text = p.MainModule?.FileVersionInfo.FileDescription; } catch { }
                if (string.IsNullOrWhiteSpace(text)) text = p.ProcessName;
                return text!.Trim();
            }
            catch { return ""; }
        }

        /// <summary>The apps that are playing sound (or have recently) on the default output.</summary>
        public static List<AudioSession> Sessions()
        {
            var result = new Dictionary<string, AudioSession>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                enumerator.GetDefaultAudioEndpoint(Speakers, 0, out var device);
                var iid = typeof(IAudioSessionManager2).GUID;
                device.Activate(ref iid, 23, IntPtr.Zero, out var managerObject);
                var manager = (IAudioSessionManager2)managerObject;
                manager.GetSessionEnumerator(out var sessions);
                sessions.GetCount(out var count);
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        sessions.GetSession(i, out var o);
                        var control = (IAudioSessionControl2)o;
                        control.GetState(out var state);
                        if (state == 2) continue;   // already ended

                        bool system = control.IsSystemSoundsSession() == 0;
                        control.GetProcessId(out var pid);
                        string name = system ? "System sounds" : AppName(pid);
                        if (name.Length == 0) continue;   // not an app we can name

                        if (!result.TryGetValue(name, out var session)) result[name] = session = new AudioSession(name, system);
                        session.Add(o);
                    }
                    catch { }
                }
            }
            catch { }
            return result.Values.OrderBy(s => s.IsSystem ? 1 : 0).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Which network the PC is on right now.</summary>
    public static class NetworkStatusService
    {
        public record NetStatus(string Kind, string Name, bool Connected);

        public static NetStatus Read()
        {
            try
            {
                var up = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up
                                && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet
                                && n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any)))
                    .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 0) // wired wins
                    .FirstOrDefault();
                if (up == null) return new NetStatus("None", "Not connected", false);
                return up.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                    ? new NetStatus("Wi-Fi", up.Name, true)
                    : new NetStatus("Ethernet", up.Name, true);
            }
            catch
            {
                return new NetStatus("None", "Unknown", false);
            }
        }
    }

    /// <summary>The Bluetooth radio's on/off state, through Windows' radio API.</summary>
    public static class BluetoothService
    {
        private static Radio? _radio;
        private static bool _loaded;

        /// <summary>null when this PC has no Bluetooth radio.</summary>
        public static async Task<bool?> IsOnAsync()
        {
            try
            {
                if (!_loaded)
                {
                    _loaded = true;
                    var status = await Radio.RequestAccessAsync();
                    if (status == RadioAccessStatus.Allowed)
                        _radio = (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
                }
                return _radio == null ? null : _radio.State == RadioState.On;
            }
            catch
            {
                return null;
            }
        }

        public static async Task SetAsync(bool on)
        {
            try { if (_radio != null) await _radio.SetStateAsync(on ? RadioState.On : RadioState.Off); } catch { }
        }
    }

    /// <summary>Battery, when there is one. Desktops have none, so the dock hides the icon.</summary>
    public static class BatteryService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
            public int BatteryLifeTime, BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        /// <summary>Percent and whether it is charging, or null when this PC has no battery.</summary>
        public static (int Percent, bool Charging)? Read()
        {
            if (!GetSystemPowerStatus(out var s) || (s.BatteryFlag & 128) != 0 || s.BatteryLifePercent > 100) return null;
            return (s.BatteryLifePercent, s.ACLineStatus == 1);
        }
    }
}
