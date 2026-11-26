using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace MonCore.Windows.Monitoring;

/// <summary>Starts and authenticates a fixed LocalSystem own-process service before using its pipe.</summary>
internal static class ProtectedServiceIdentity
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryConfig = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceWin32OwnProcess = 0x10;
    private const uint ServiceRunning = 4;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int ErrorServiceAlreadyRunning = 1056;

    internal static Task EnsureRunningAsync(ProtectedServiceEndpoint endpoint, CancellationToken token)
        => EnsureRunningAsync(endpoint, token, null);

    internal static async Task EnsureRunningAsync(ProtectedServiceEndpoint endpoint, CancellationToken token,
        Func<uint, IServiceStartupScope>? open)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        token.ThrowIfCancellationRequested();
        using var service = open is null
            ? new NativeStartupScope(endpoint, ServiceQueryStatus | ServiceQueryConfig | ServiceStart)
            : open(ServiceQueryStatus | ServiceQueryConfig | ServiceStart);
        if (!service.IsTrusted)
            throw new UnauthorizedAccessException("Registered service configuration is not trusted.");
        if (service.State == ServiceRunning) return;
        token.ThrowIfCancellationRequested();
        if (!service.TryStart(out var error) && error != ErrorServiceAlreadyRunning)
            throw new Win32Exception(error, "Cannot start privileged WinMon service.");
        var deadline = service.Clock.GetTimestamp() + (long)(service.Clock.TimestampFrequency * 10d);
        do
        {
            token.ThrowIfCancellationRequested();
            if (service.State == ServiceRunning) return;
            await Task.Delay(TimeSpan.FromMilliseconds(50), service.Clock, token).ConfigureAwait(false);
        } while (service.Clock.GetTimestamp() < deadline);
        throw new TimeoutException("Privileged WinMon service did not start in time.");
    }

    internal static void Verify(ProtectedServiceEndpoint endpoint, SafePipeHandle pipe)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!GetNamedPipeServerProcessId(pipe, out var processId) || processId == 0)
            throw new UnauthorizedAccessException("Cannot identify privileged service pipe server.");
        using var service = Open(endpoint, ServiceQueryStatus | ServiceQueryConfig);
        if (!ConfigurationMatches(endpoint, QueryConfiguration(service)))
            throw new UnauthorizedAccessException("Registered service configuration is not trusted.");
        var status = QueryStatus(service);
        if (status.CurrentState != ServiceRunning || status.ProcessId != processId)
            throw new UnauthorizedAccessException("Service pipe is not owned by the registered service.");
        using var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot inspect privileged service process.");
        if (!GetProcessTimes(process, out var created, out var exited, out _, out _) || exited != 0)
            throw new UnauthorizedAccessException("Service process is no longer running.");
        var localSystem = IsLocalSystem(process, endpoint, service);
        var recheck = QueryStatus(service);
        if (!localSystem || !GetProcessTimes(process, out var recheckedCreated, out exited, out _, out _) || exited != 0 ||
            recheckedCreated != created || recheck.CurrentState != ServiceRunning || recheck.ProcessId != processId)
            throw new UnauthorizedAccessException("Service identity changed.");
    }

    internal static bool ConfigurationMatches(ProtectedServiceEndpoint endpoint, uint serviceType, string? account,
        string? command)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return serviceType == ServiceWin32OwnProcess &&
               string.Equals(account, "LocalSystem", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(command?.Trim(), $"\"{endpoint.InstalledServiceExecutable}\"",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool ConfigurationMatches(ProtectedServiceEndpoint endpoint, ServiceConfiguration config)
    {
        return ConfigurationMatches(endpoint, config.ServiceType, config.Account, config.Command);
    }

    private static bool IsLocalSystem(SafeProcessHandle process, ProtectedServiceEndpoint endpoint,
        SafeServiceHandle service)
    {
        if (OpenProcessToken(process, TokenQuery, out var token) && !token.IsInvalid)
        {
            using (token)
            {
                using var identity = new WindowsIdentity(token.DangerousGetHandle());
                return identity.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) == true;
            }
        }
        token.Dispose();
        return Marshal.GetLastWin32Error() is 5 or 1314 && ConfigurationMatches(endpoint, QueryConfiguration(service));
    }

    private static SafeServiceHandle Open(ProtectedServiceEndpoint endpoint, uint access)
    {
        using var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager.IsInvalid) throw new UnauthorizedAccessException("Cannot inspect service manager.");
        var service = OpenService(manager, endpoint.ServiceName, access);
        if (!service.IsInvalid) return service;
        service.Dispose();
        throw new InvalidOperationException("Privileged WinMon service is not installed or accessible.");
    }

    private sealed class NativeStartupScope(ProtectedServiceEndpoint endpoint, uint access) : IServiceStartupScope
    {
        private readonly SafeServiceHandle _service = Open(endpoint, access);
        public bool IsTrusted => ConfigurationMatches(endpoint, QueryConfiguration(_service));
        public uint State => QueryStatus(_service).CurrentState;
        public TimeProvider Clock => TimeProvider.System;
        public bool TryStart(out int error)
        {
            var started = StartService(_service, 0, IntPtr.Zero);
            error = Marshal.GetLastWin32Error();
            return started;
        }
        public void Dispose() => _service.Dispose();
    }

    private static ServiceStatusProcess QueryStatus(SafeServiceHandle service)
    {
        _ = QueryServiceStatusEx(service, 0, IntPtr.Zero, 0, out var size);
        if (size < Marshal.SizeOf<ServiceStatusProcess>()) throw new UnauthorizedAccessException("Cannot query service.");
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryServiceStatusEx(service, 0, memory, size, out _)) throw new UnauthorizedAccessException("Cannot query service.");
            return Marshal.PtrToStructure<ServiceStatusProcess>(memory);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static ServiceConfiguration QueryConfiguration(SafeServiceHandle service)
    {
        _ = QueryServiceConfig(service, IntPtr.Zero, 0, out var bytes);
        if (bytes is 0 or > 8192) throw new UnauthorizedAccessException("Cannot query service configuration.");
        var memory = Marshal.AllocHGlobal(checked((int)bytes));
        try
        {
            if (!QueryServiceConfig(service, memory, bytes, out _)) throw new UnauthorizedAccessException("Cannot query service configuration.");
            var configuration = Marshal.PtrToStructure<QueryServiceConfigData>(memory);
            return new ServiceConfiguration(
                configuration.ServiceType,
                Marshal.PtrToStringUni(configuration.ServiceStartName),
                Marshal.PtrToStringUni(configuration.BinaryPathName));
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatusProcess { public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint, ProcessId, ServiceFlags; }
    private sealed record ServiceConfiguration(uint ServiceType, string? Account, string? Command);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct QueryServiceConfigData { public uint ServiceType, StartType, ErrorControl; public IntPtr BinaryPathName, LoadOrderGroup; public uint TagId; public IntPtr Dependencies, ServiceStartName, DisplayName; }
    private sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid { public SafeServiceHandle() : base(true) { } protected override bool ReleaseHandle() => CloseServiceHandle(handle); }
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeServiceHandle OpenService(SafeServiceHandle manager, string serviceName, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool StartService(SafeServiceHandle service, int arguments, IntPtr vectors);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceStatusEx(SafeServiceHandle service, int level, IntPtr memory, int size, out int needed);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceConfig(SafeServiceHandle service, IntPtr memory, uint size, out uint needed);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(IntPtr service);
}
