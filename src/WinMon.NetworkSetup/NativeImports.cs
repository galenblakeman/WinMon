using System.Runtime.InteropServices;

// Staging is user-writable: never resolve an elevated native import beside the helper.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
