using System;
using System.Runtime.InteropServices;

namespace CyberOptix.Services
{
    // The Windows property-store declarations that ShellApps (which app a window belongs to) and SystemStatusService (audio devices) both read through.

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey { public Guid FormatId; public uint PropertyId; }

    // A PROPVARIANT is 24 bytes on 64-bit Windows: a 2-byte type, 6 bytes of padding and a 16-byte value.
    [StructLayout(LayoutKind.Sequential)]
    internal struct PropVariant { public ushort Type; public ushort R1, R2, R3; public IntPtr Value; public IntPtr Value2; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }
}
