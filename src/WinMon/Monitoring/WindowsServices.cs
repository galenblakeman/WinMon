using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

internal sealed record ServiceObservation(string Name, string DisplayName, uint State, int ProcessId, uint ControlsAccepted)
{
    internal bool HasHost => ProcessId > 0 && State is 4 or 7 or 5 or 6;
}

internal sealed record ServiceCatalog(long ObservedFileTime, IReadOnlyList<ServiceObservation> Services);

/// <summary>Local SCM enumeration and explicit service control. No driver services or host termination.</summary>
internal static class WindowsServices
{
    internal static ServiceCatalog Capture()
    {
        // Processes born after enumeration began cannot safely inherit an enumerated PID.
        var observed = DateTime.UtcNow.ToFileTimeUtc();
        using var manager = OpenSCManagerW(null, null, 4);
        CheckHandle(manager);
        var buffer = Marshal.AllocHGlobal(256 * 1024);
        try
        {
            var services = new List<ServiceObservation>();
            uint resume = 0;
            for (var page = 0; page < 32; page++)
            {
                var success = EnumServicesStatusExW(manager, 0, 0x30, 3, buffer, 256 * 1024,
                    out _, out var count, ref resume, null);
                var error = Marshal.GetLastPInvokeError();
                if (!success && error != 234) throw new Win32Exception(error);
                var size = Marshal.SizeOf<EnumStatus>();
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<EnumStatus>(buffer + i * size);
                    services.Add(new ServiceObservation(Marshal.PtrToStringUni(item.Name)!,
                        Marshal.PtrToStringUni(item.DisplayName)!, item.Status.State,
                        checked((int)item.Status.ProcessId), item.Status.Controls));
                }
                if (success) return new ServiceCatalog(observed, services);
                if (count == 0) throw new Win32Exception(234);
            }
            throw new Win32Exception(234);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static async Task ExecuteAsync(string name, string action, int expectedPid, long expectedCreation,
        CancellationToken cancellationToken)
    {
        if (action is not ("start" or "stop" or "restart")) throw new ArgumentException(nameof(action));
        using var manager = OpenSCManagerW(null, null, 1);
        CheckHandle(manager);
        using var service = OpenServiceW(manager, name, 4u | (action == "start" ? 16u : action == "stop" ? 32u : 48u));
        CheckHandle(service);
        var status = Query(service);
        if (action == "start")
        {
            if (status.State != 1) throw new Win32Exception(1237);
        }
        else
        {
            if (status.State is not (4 or 7) || status.ProcessId != expectedPid || expectedCreation <= 0)
                throw new Win32Exception(1237);
            using var process = Process.GetProcessById(expectedPid);
            if (process.StartTime.ToUniversalTime().ToFileTimeUtc() != expectedCreation)
                throw new Win32Exception(1237);
            // SCM control targets a named service, not an atomic compare-and-stop process operation.
            // Recheck after opening the process so ordinary restarts during validation are rejected.
            status = Query(service);
            if (process.HasExited || status.State is not (4 or 7) || status.ProcessId != expectedPid)
                throw new Win32Exception(1237);
            if (!ControlService(service, 1, out _)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            await WaitAsync(service, 1, cancellationToken);
        }
        if (action is "start" or "restart")
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!StartServiceW(service, 0, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            await WaitAsync(service, 4, cancellationToken);
        }
    }

    private static async Task WaitAsync(ServiceHandle service, uint state, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        while (Query(service).State != state)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new Win32Exception(1053);
            await Task.Delay(250, cancellationToken);
        }
    }

    private static Status Query(ServiceHandle service)
    {
        if (!QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<Status>(), out _))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return status;
    }

    private static void CheckHandle(ServiceHandle handle)
    {
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Status
    {
        public uint Type, State, Controls, ExitCode, ServiceExitCode, Checkpoint, WaitHint, ProcessId, Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumStatus
    {
        public nint Name, DisplayName;
        public Status Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ControlStatus
    {
        public uint Type, State, Controls, ExitCode, ServiceExitCode, Checkpoint, WaitHint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenSCManagerW(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenServiceW(ServiceHandle manager, string name, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumServicesStatusExW(ServiceHandle manager, int level, uint type, uint state,
        nint buffer, int bufferSize, out int needed, out int count, ref uint resume, string? group);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(ServiceHandle service, int level, out Status status, int size, out int needed);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(ServiceHandle service, uint control, out ControlStatus status);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceW(ServiceHandle service, int count, nint arguments);
    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(nint handle);
}
