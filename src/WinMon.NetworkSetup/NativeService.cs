using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace WinMon.NetworkSetup;

/// <summary>All SCM mutations are restricted to the fixed, verified WinMon service configuration.</summary>
internal static class NativeService
{
    private const uint AllAccess = 0xF01FF;
    internal sealed record ServiceState(bool Exists, bool WasRunning, byte[]? SecurityDescriptor);

    internal static bool ConfigurationMatches(uint serviceType, uint startType, string? account, string? binaryPath) =>
        serviceType == 0x10 && startType == 3 && string.Equals(account, "LocalSystem", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(binaryPath, $"\"{SetupPaths.ServiceExecutable}\"", StringComparison.OrdinalIgnoreCase);

    internal static ServiceState CaptureState()
    {
        using var scm = Manager();
        using var service = OpenOwned(scm);
        if (service is null) return new(false, false, null);
        var status = Status(service);
        if (status.State is not (1 or 4)) throw new InvalidOperationException("Service is changing state; retry setup after it settles.");
        return new(true, status.State == 4, QuerySecurity(service));
    }

    internal static void EnsureOwnedOrAbsent()
    {
        using var scm = Manager();
        using var service = OpenOwned(scm);
    }

    internal static void StopOwnedIfRunning(CancellationToken token)
    {
        using var scm = Manager();
        using var service = OpenOwned(scm);
        if (service is not null) Stop(service, token);
    }

    internal static void CreateOrUpdateOwned(IReadOnlyCollection<string> users)
    {
        using var scm = Manager(create: true);
        using var existing = OpenOwned(scm);
        using var created = existing is null ? CreateService(scm, SetupPaths.ServiceName, SetupPaths.ServiceName,
            AllAccess, 0x10, 3, 1, $"\"{SetupPaths.ServiceExecutable}\"", null, IntPtr.Zero, null, "LocalSystem", null) : null;
        var service = existing ?? created!;
        if (service.IsInvalid) Throw();
        try
        {
            if (created is not null)
            {
                var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var owner = new byte[administrators.BinaryLength];
                administrators.GetBinaryForm(owner, 0);
                var error = SetOwner(service, 2, 1, owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (error != 0) throw new Win32Exception((int)error);
            }
            SetSecurity(service, Descriptor(users));
            ValidateSecurity(QuerySecurity(service));
        }
        catch
        {
            if (created is not null)
            {
                if (!DeleteService(created)) Throw();
                created.Dispose();
                WaitRemoved(scm, CancellationToken.None);
            }
            throw;
        }
    }

    internal static byte[] Descriptor(IReadOnlyCollection<string> users)
    {
        if (users.Count is 0 or > AuthorizationPolicy.MaximumUsers) throw new InvalidDataException("Invalid service users.");
        var sddl = "D:P(A;;0xF01FF;;;SY)(A;;0xF01FF;;;BA)" +
                   string.Concat(users.Select(sid => $"(A;;0x15;;;{AuthorizationPolicy.ValidateSid(sid)})"));
        var descriptor = new RawSecurityDescriptor(sddl);
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        return bytes;
    }

    internal static void StartAndVerify(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var scm = Manager();
        using var service = OpenOwned(scm) ?? throw new InvalidOperationException("Service is missing.");
        if (!StartService(service, 0, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1056) throw new Win32Exception(error);
        }
        Wait(service, 4, token);
    }

    internal static void Restore(ServiceState state, CancellationToken token)
    {
        if (!state.Exists) { StopAndDeleteOwned(token); return; }
        using (var scm = Manager())
        using (var service = OpenOwned(scm) ?? throw new InvalidOperationException("Original service disappeared during setup."))
        {
            if (state.SecurityDescriptor is null) throw new InvalidDataException("Missing original service permissions.");
            SetSecurity(service, state.SecurityDescriptor);
        }
        if (state.WasRunning) StartAndVerify(token); else StopOwnedIfRunning(token);
    }

    internal static void StopAndDeleteOwned(CancellationToken token)
    {
        using var scm = Manager();
        using (var service = OpenOwned(scm))
        {
            if (service is null) return;
            Stop(service, token);
            if (!DeleteService(service)) Throw();
        }
        WaitRemoved(scm, token);
    }

    private static void WaitRemoved(SafeScHandle scm, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            using var service = OpenService(scm, SetupPaths.ServiceName, 1);
            if (service.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 1060) return;
                if (error != 1072) throw new Win32Exception(error);
            }
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(20)) throw new TimeoutException("Service deletion is pending another open handle.");
            Thread.Sleep(50);
        }
    }

    private static SafeScHandle Manager(bool create = false)
    {
        var handle = OpenScManager(null, null, create ? 3u : 1u);
        if (!handle.IsInvalid) return handle;
        handle.Dispose();
        Throw();
        throw new InvalidOperationException();
    }

    private static SafeScHandle? OpenOwned(SafeScHandle scm)
    {
        var service = OpenService(scm, SetupPaths.ServiceName, AllAccess);
        if (service.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            service.Dispose();
            if (error == 1060) return null;
            throw new Win32Exception(error);
        }
        try { EnsureExactConfiguration(service); ValidateSecurity(QuerySecurity(service)); return service; }
        catch { service.Dispose(); throw; }
    }

    private static void EnsureExactConfiguration(SafeScHandle service)
    {
        QueryServiceConfig(service, IntPtr.Zero, 0, out var bytes);
        if (bytes is 0 or > 8192) throw new InvalidDataException("Invalid service configuration size.");
        var memory = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (!QueryServiceConfig(service, memory, bytes, out _)) Throw();
            var config = Marshal.PtrToStructure<QueryServiceConfigData>(memory);
            if (!ConfigurationMatches(config.ServiceType, config.StartType, Marshal.PtrToStringUni(config.ServiceStartName), Marshal.PtrToStringUni(config.BinaryPathName)) ||
                config.Dependencies != IntPtr.Zero && Marshal.ReadInt16(config.Dependencies) != 0)
                throw new UnauthorizedAccessException("Service name belongs to a different configuration.");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static ServiceStatusProcess Status(SafeScHandle service)
    {
        if (!QueryServiceStatusEx(service, 0, out var status, (uint)Marshal.SizeOf<ServiceStatusProcess>(), out _)) Throw();
        return status;
    }

    private static void Stop(SafeScHandle service, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var status = Status(service);
        if (status.State == 1) return;
        if (status.State != 3 && !ControlService(service, 1, out _))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1062) throw new Win32Exception(error);
        }
        Wait(service, 1, token);
    }

    private static void Wait(SafeScHandle service, uint target, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var status = Status(service);
            if (status.State == target) return;
            if (target == 4 && status.State == 1) throw new Win32Exception((int)(status.Win32Exit == 0 ? 1067 : status.Win32Exit));
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(20)) throw new TimeoutException("Service did not reach the expected state.");
            Thread.Sleep(50);
        }
    }

    private static byte[] QuerySecurity(SafeScHandle service)
    {
        QueryServiceObjectSecurity(service, 5, null, 0, out var bytes);
        if (bytes is 0 or > 64 * 1024) throw new InvalidDataException("Invalid service permissions size.");
        var result = new byte[bytes];
        if (!QueryServiceObjectSecurity(service, 5, result, bytes, out _)) Throw();
        return result;
    }

    internal static void ValidateSecurity(byte[] bytes)
    {
        var descriptor = new RawSecurityDescriptor(bytes, 0);
        if (descriptor.Owner is null || !TrustedPrincipal(descriptor.Owner) || descriptor.DiscretionaryAcl is null)
            throw new UnauthorizedAccessException("Untrusted service security owner or DACL.");
        foreach (GenericAce entry in descriptor.DiscretionaryAcl)
        {
            if (entry is not CommonAce ace || ace.IsCallback)
                throw new UnauthorizedAccessException("Unsupported service permission.");
            if (ace.AceQualifier == AceQualifier.AccessAllowed && !TrustedPrincipal(ace.SecurityIdentifier) && (ace.AccessMask & ~0x15) != 0)
                throw new UnauthorizedAccessException("An untrusted account can control the service.");
        }
    }

    private static bool TrustedPrincipal(SecurityIdentifier sid) => sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) ||
        sid.IsWellKnown(WellKnownSidType.LocalSystemSid);
    private static void SetSecurity(SafeScHandle service, byte[] descriptor)
    {
        var security = new RawSecurityDescriptor(descriptor, 0);
        var acl = security.DiscretionaryAcl ?? throw new InvalidDataException("Missing service DACL.");
        var bytes = new byte[acl.BinaryLength];
        acl.GetBinaryForm(bytes, 0);
        var protection = security.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected) ? 0x80000000u : 0x20000000u;
        var error = SetSecurityInfo(service, 2, 4 | protection, IntPtr.Zero, IntPtr.Zero, bytes, IntPtr.Zero);
        if (error != 0) throw new Win32Exception((int)error);
    }
    private static void Throw() => throw new Win32Exception(Marshal.GetLastWin32Error());

    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeScHandle OpenScManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeScHandle OpenService(SafeScHandle manager, string name, uint access);
    [DllImport("advapi32.dll", EntryPoint = "CreateServiceW", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeScHandle CreateService(SafeScHandle manager, string name, string displayName, uint access, uint type, uint start, uint error, string path, string? group, IntPtr tag, string? dependencies, string account, string? password);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool StartService(SafeScHandle service, uint count, IntPtr arguments);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteService(SafeScHandle service);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ControlService(SafeScHandle service, uint control, out ServiceStatus status);
    [DllImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceConfig(SafeScHandle service, IntPtr buffer, uint size, out uint needed);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceStatusEx(SafeScHandle service, int level, out ServiceStatusProcess status, uint size, out uint needed);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceObjectSecurity(SafeScHandle service, uint info, [Out] byte[]? descriptor, uint size, out uint needed);
    [DllImport("advapi32.dll")] private static extern uint SetSecurityInfo(SafeScHandle service, int type, uint info, IntPtr owner, IntPtr group, byte[] dacl, IntPtr sacl);
    [DllImport("advapi32.dll", EntryPoint = "SetSecurityInfo")] private static extern uint SetOwner(SafeScHandle service, int type, uint info, byte[] owner, IntPtr group, IntPtr dacl, IntPtr sacl);
}

[StructLayout(LayoutKind.Sequential)] internal struct ServiceStatusProcess { internal uint Type, State, Controls, Win32Exit, SpecificExit, CheckPoint, WaitHint, ProcessId, ServiceFlags; }
[StructLayout(LayoutKind.Sequential)] internal struct QueryServiceConfigData { internal uint ServiceType, StartType, ErrorControl; internal IntPtr BinaryPathName, LoadOrderGroup; internal uint TagId; internal IntPtr Dependencies, ServiceStartName, DisplayName; }
[StructLayout(LayoutKind.Sequential)] internal struct ServiceStatus { internal uint Type, State, Controls, Win32Exit, SpecificExit, CheckPoint, WaitHint; }
internal sealed class SafeScHandle : SafeHandle
{
    internal SafeScHandle() : base(IntPtr.Zero, true) { }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(IntPtr handle);
}
