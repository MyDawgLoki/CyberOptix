using System.Runtime.InteropServices;

// Native libraries are only ever loaded from System32, never from the program's own (or the current) folder.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
