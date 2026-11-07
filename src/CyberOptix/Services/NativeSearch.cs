using System.Runtime.InteropServices;

// CyberOptix runs as administrator, so a look-alike DLL dropped next to it must never be picked up in place of a Windows one.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
