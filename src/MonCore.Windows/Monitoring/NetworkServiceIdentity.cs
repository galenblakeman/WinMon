using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

/// <summary>Rejects a same-named pipe unless its server is the running LocalSystem SCM service.</summary>
internal static class NetworkServiceIdentity
{
    internal static async Task EnsureRunningAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var service = OpenRegisteredService(ServiceQueryStatus | ServiceQueryConfig | ServiceStart);
        if (!IsConfiguredAsOwnProcessLocalSystem(service))
            throw new UnauthorizedAccessException("Registered network service configuration is not trusted.");
        var status = QueryStatus(service);
        if (status.CurrentState == ServiceRunning) return;
        token.ThrowIfCancellationRequested();
        if (!StartService(service, 0, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorServiceAlreadyRunning) throw new Win32Exception(error, "Cannot start network monitoring service.");
        }
        var deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 10d);
        do
        {
            token.ThrowIfCancellationRequested();
            if (QueryStatus(service).CurrentState == ServiceRunning) return;
            await Task.Delay(50, token).ConfigureAwait(false);
        }
        while (Stopwatch.GetTimestamp() < deadline);
        throw new TimeoutException("Network monitoring service did not start in time.");
    }

    internal static void Verify(SafePipeHandle pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe, out var pipeProcessId) || pipeProcessId == 0)
            throw new UnauthorizedAccessException("Cannot identify network service pipe server.");
        using var service = OpenRegisteredService(ServiceQueryStatus | ServiceQueryConfig);
        if (!IsConfiguredAsOwnProcessLocalSystem(service))
            throw new UnauthorizedAccessException("Registered network service configuration is not trusted.");
        var status = QueryStatus(service);
        if (status.CurrentState != ServiceRunning || status.ProcessId != pipeProcessId)
            throw new UnauthorizedAccessException("Network service pipe is not owned by the registered service.");

        using var processHandle = OpenProcess(ProcessQueryLimitedInformation, false, pipeProcessId);
        if (processHandle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open network service process for limited inspection.");
        if (!GetProcessTimes(processHandle, out var created, out var exited, out _, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot query network service process times.");
        if (exited != 0)
            throw new UnauthorizedAccessException("Network service process has already exited.");

        var localSystem = false;
        if (OpenProcessToken(processHandle, TokenQuery, out var token) && !token.IsInvalid)
        {
            using (token)
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
                localSystem = identity.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) == true;
        }
        else if (Marshal.GetLastWin32Error() is ErrorAccessDenied or ErrorPrivilegeNotHeld)
        {
            // Normal users often cannot query a LocalSystem token. SCM configuration remains authoritative.
            localSystem = IsConfiguredAsOwnProcessLocalSystem(service);
        }
        token?.Dispose();

        var recheck = QueryStatus(service);
        if (!localSystem || !GetProcessTimes(processHandle, out var recheckedCreation, out exited, out _, out _) ||
            exited != 0 || recheckedCreation != created ||
            recheck.CurrentState != ServiceRunning || recheck.ProcessId != pipeProcessId)
            throw new UnauthorizedAccessException("Network service identity changed.");
    }

    private static SafeNativeHandle OpenRegisteredService(uint access)
    {
        using var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager.IsInvalid) throw new UnauthorizedAccessException("Cannot inspect service manager.");
        var service = OpenService(manager, NetworkServiceProtocol.ServiceName, access);
        if (service.IsInvalid)
        {
            service.Dispose();
            throw new InvalidOperationException("Network monitoring service is not installed or accessible.");
        }
        return service;
    }

    private static ServiceStatusProcess QueryStatus(SafeNativeHandle service)
    {
        _ = QueryServiceStatusEx(service, 0, IntPtr.Zero, 0, out var size);
        if (size < Marshal.SizeOf<ServiceStatusProcess>()) throw new UnauthorizedAccessException("Cannot query network service.");
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryServiceStatusEx(service, 0, buffer, size, out _)) throw new UnauthorizedAccessException("Cannot query network service.");
            return Marshal.PtrToStructure<ServiceStatusProcess>(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool IsConfiguredAsOwnProcessLocalSystem(SafeNativeHandle service)
    {
        _ = QueryServiceConfig(service, IntPtr.Zero, 0, out var bytes);
        if (bytes == 0) return false;
        var buffer = Marshal.AllocHGlobal(checked((int)bytes));
        try
        {
            if (!QueryServiceConfig(service, buffer, bytes, out _)) return false;
            var config = Marshal.PtrToStructure<QueryServiceConfigData>(buffer);
            return ConfigurationMatches(config.ServiceType, Marshal.PtrToStringUni(config.ServiceStartName),
                Marshal.PtrToStringUni(config.BinaryPathName));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static bool ConfigurationMatches(uint serviceType, string? account, string? command) =>
        serviceType == ServiceWin32OwnProcess && string.Equals(account, "LocalSystem", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(command?.Trim(), $"\"{NetworkServiceProtocol.InstalledExecutable}\"", StringComparison.OrdinalIgnoreCase);

    private const uint ScManagerConnect = 0x0001, ServiceQueryConfig = 0x0001, ServiceQueryStatus = 0x0004, ServiceStart = 0x0010;
    private const uint TokenQuery = 0x0008, ProcessQueryLimitedInformation = 0x1000, ServiceWin32OwnProcess = 0x10, ServiceRunning = 4;
    private const int ErrorAccessDenied = 5, ErrorPrivilegeNotHeld = 1314, ErrorServiceAlreadyRunning = 1056;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeNativeHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeNativeHandle OpenService(SafeNativeHandle manager, string serviceName, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool StartService(SafeNativeHandle service, int arguments, IntPtr argumentVectors);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceStatusEx(SafeNativeHandle service, int infoLevel, IntPtr buffer, int size, out int needed);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceConfig(SafeNativeHandle service, IntPtr buffer, uint size, out uint needed);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatusProcess { public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint, ProcessId, ServiceFlags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct QueryServiceConfigData { public uint ServiceType, StartType, ErrorControl; public IntPtr BinaryPathName, LoadOrderGroup; public uint TagId; public IntPtr Dependencies, ServiceStartName, DisplayName; }
    private sealed class SafeNativeHandle : SafeHandleZeroOrMinusOneIsInvalid { public SafeNativeHandle() : base(true) { } protected override bool ReleaseHandle() => CloseServiceHandle(handle); }
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(IntPtr service);
}
