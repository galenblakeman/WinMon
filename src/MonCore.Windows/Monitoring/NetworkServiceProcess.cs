using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

/// <summary>Reads the installed network service PID without treating an unreadable service as stopped.</summary>
public static class NetworkServiceProcess
{
    public static NetworkServiceProcessStatus Read()
    {
        using var manager = OpenSCManagerW(null, null, 1);
        if (manager.IsInvalid) return new(null, true);
        using var service = OpenServiceW(manager, NetworkServiceProtocol.ServiceName, 4);
        if (service.IsInvalid) return new(null, Marshal.GetLastWin32Error() != 1060);
        return QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<Status>(), out _) ?
            new(status.State != 1 && status.ProcessId != 0 ? checked((int)status.ProcessId) : null, false) : new(null, true);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Status { public uint Type, State, Controls, ExitCode, ServiceExitCode, Checkpoint, WaitHint, ProcessId, Flags; }
    private sealed class Handle() : SafeHandleZeroOrMinusOneIsInvalid(true) { protected override bool ReleaseHandle() => CloseServiceHandle(handle); }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern Handle OpenSCManagerW(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern Handle OpenServiceW(Handle manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceStatusEx(Handle service, int level, out Status status, int size, out int needed);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(IntPtr service);
}

/// <param name="ProcessId">The running own-process service PID; null if stopped or unavailable.</param>
/// <param name="Unreadable">True when access or an SCM error prevented a reliable answer.</param>
public readonly record struct NetworkServiceProcessStatus(int? ProcessId, bool Unreadable);
