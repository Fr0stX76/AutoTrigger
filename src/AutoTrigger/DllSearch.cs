using System.Runtime.InteropServices;

// Native DLLs load from System32 only: a DLL dropped next to the exe or in the current folder is never used.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
