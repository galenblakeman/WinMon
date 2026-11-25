using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// ReSharper disable ClassNeverInstantiated.Local

namespace WinMon.Monitoring;

/// <summary>Local SCM enumeration and explicit service control. No driver services or host termination.</summary>
public static class WindowsServices
{
    public static ServiceCatalog Capture()
    {
        return new Reader().Read();
    }

    /// <summary>Enumerates services once per sample, reusing its buffer and every unchanged observation.</summary>
    /// <remarks>
    /// Service history keeps two minutes of catalogs. Nearly every service is unchanged from one second to the
    /// next, so reusing the previous observation keeps one copy of its name strings instead of one per frame.
    /// </remarks>
    public sealed class Reader
    {
        private const int BufferBytes = 256 * 1024;
        private readonly byte[] _buffer = GC.AllocateUninitializedArray<byte>(BufferBytes, true);
        private Dictionary<string, ServiceObservation> _previous = new(StringComparer.Ordinal);
        private Dictionary<string, ServiceObservation> _next = new(StringComparer.Ordinal);
        private readonly IServiceCatalogOperations _operations;
        private readonly TimeProvider _timeProvider;

        public Reader() : this(NativeCatalogOperations.Instance, TimeProvider.System) { }

        internal Reader(IServiceCatalogOperations operations, TimeProvider timeProvider)
        {
            _operations = operations;
            _timeProvider = timeProvider;
        }

        public ServiceCatalog Read()
        {
            // Processes born after enumeration began cannot safely inherit an enumerated PID.
            var observed = _timeProvider.GetUtcNow().UtcDateTime.ToFileTimeUtc();
            using var manager = _operations.OpenManager();
            _operations.CheckHandle(manager);
            var address = Marshal.UnsafeAddrOfPinnedArrayElement(_buffer, 0);
            var services = new List<ServiceObservation>(_previous.Count);
            var previous = _previous.GetAlternateLookup<ReadOnlySpan<char>>();
            _next.Clear();
            uint resume = 0;
            for (var page = 0; page < 32; page++)
            {
                var success = _operations.Enumerate(manager, address, BufferBytes, out var count, ref resume,
                    out var error);
                if (!success && error != 234) throw new Win32Exception(error);
                var size = Marshal.SizeOf<EnumStatus>();
                for (var i = 0; i < count; i++)
                {
                    var item = MemoryMarshal.Read<EnumStatus>(_buffer.AsSpan(i * size, size));
                    var name = Text(item.Name, address);
                    var displayName = Text(item.DisplayName, address);
                    var processId = checked((int)item.Status.ProcessId);
                    if (!previous.TryGetValue(name, out var service) || service.State != item.Status.State ||
                        service.ProcessId != processId || service.ControlsAccepted != item.Status.Controls ||
                        !displayName.SequenceEqual(service.DisplayName))
                        service = new ServiceObservation(service?.Name ?? name.ToString(),
                            service is not null && displayName.SequenceEqual(service.DisplayName)
                                ? service.DisplayName
                                : displayName.ToString(),
                            item.Status.State, processId, item.Status.Controls);
                    _next[service.Name] = service;
                    services.Add(service);
                }

                if (success)
                {
                    (_previous, _next) = (_next, _previous);
                    return new ServiceCatalog(observed, services);
                }

                if (count == 0) throw new Win32Exception(234);
            }

            throw new Win32Exception(234);
        }

        // The SCM writes strings after the fixed entries and points at them with absolute addresses.
        private ReadOnlySpan<char> Text(nint pointer, nint address)
        {
            var offset = pointer - address;
            if (pointer == 0 || offset < 0 || offset >= BufferBytes || offset % 2 != 0) throw new Win32Exception(13);
            var chars = MemoryMarshal.Cast<byte, char>(_buffer.AsSpan((int)offset));
            var end = chars.IndexOf('\0');
            return end < 0 ? throw new Win32Exception(13) : chars[..end];
        }
    }

    internal interface IServiceCatalogOperations
    {
        IServiceHandle OpenManager();
        void CheckHandle(IServiceHandle manager);
        bool Enumerate(IServiceHandle manager, nint address, int length, out int count, ref uint resume, out int error);
    }

    private sealed class NativeCatalogOperations : IServiceCatalogOperations
    {
        internal static NativeCatalogOperations Instance { get; } = new();
        public IServiceHandle OpenManager() => OpenSCManagerW(null, null, 4);
        public void CheckHandle(IServiceHandle manager) => WindowsServices.CheckHandle((ServiceHandle)manager);
        public bool Enumerate(IServiceHandle manager, nint address, int length, out int count, ref uint resume, out int error)
        {
            var success = EnumServicesStatusExW((ServiceHandle)manager, 0, 0x30, 3, address, length,
                out _, out count, ref resume, null);
            error = Marshal.GetLastPInvokeError();
            return success;
        }
    }

    public static Task ExecuteAsync(string name, string action, int expectedPid, long expectedCreation,
        CancellationToken cancellationToken) =>
        ExecuteAsync(name, action, expectedPid, expectedCreation, cancellationToken, NativeOperations.Instance,
            TimeProvider.System);

    internal static async Task ExecuteAsync(string name, string action, int expectedPid, long expectedCreation,
        CancellationToken cancellationToken, IServiceOperations operations, TimeProvider timeProvider)
    {
        if (action is not ("start" or "stop" or "restart")) throw new ArgumentException(nameof(action));
        using var manager = operations.OpenManager();
        operations.CheckHandle(manager);
        using var service = operations.OpenService(manager, name, 4u | action switch { "start" => 16u, "stop" => 32u, _ => 48u });
        operations.CheckHandle(service);
        var status = operations.Query(service);
        if (action == "start")
        {
            if (status.State != 1) throw new Win32Exception(1237);
        }
        else
        {
            CheckHostStatus(in status, expectedPid, expectedCreation);
            using var process = operations.OpenProcess(expectedPid);
            CheckOpenedHost(service, process, expectedPid, expectedCreation, operations);
            operations.Stop(service);
            await WaitAsync(service, 1, cancellationToken, operations, timeProvider);
        }

        if (action is "start" or "restart")
        {
            cancellationToken.ThrowIfCancellationRequested();
            operations.Start(service);
            await WaitAsync(service, 4, cancellationToken, operations, timeProvider);
        }
    }

    private static void CheckHostStatus(in Status status, int expectedPid, long expectedCreation)
    {
        if (status.State is not (4 or 7) || status.ProcessId != expectedPid || expectedCreation <= 0)
            throw new Win32Exception(1237);
    }

    private static void CheckOpenedHost(IServiceHandle service, IServiceProcess process, int expectedPid,
        long expectedCreation, IServiceOperations operations)
    {
        if (process.CreationTicks != expectedCreation) throw new Win32Exception(1237);
        // SCM cannot compare-and-stop atomically; recheck after opening the expected host.
        var status = operations.Query(service);
        if (process.HasExited || status.State is not (4 or 7) || status.ProcessId != expectedPid)
            throw new Win32Exception(1237);
    }

    private static async Task WaitAsync(IServiceHandle service, uint state, CancellationToken cancellationToken,
        IServiceOperations operations, TimeProvider timeProvider)
    {
        var started = timeProvider.GetTimestamp();
        while (operations.Query(service).State != state)
        {
            if (timeProvider.GetElapsedTime(started) > TimeSpan.FromSeconds(30)) throw new Win32Exception(1053);
            await Task.Delay(TimeSpan.FromMilliseconds(250), timeProvider, cancellationToken);
        }
    }

    internal interface IServiceHandle : IDisposable;

    internal interface IServiceProcess : IDisposable
    {
        long CreationTicks { get; }
        bool HasExited { get; }
    }

    internal interface IServiceOperations
    {
        IServiceHandle OpenManager();
        IServiceHandle OpenService(IServiceHandle manager, string name, uint access);
        void CheckHandle(IServiceHandle handle);
        Status Query(IServiceHandle service);
        IServiceProcess OpenProcess(int processId);
        void Stop(IServiceHandle service);
        void Start(IServiceHandle service);
    }

    private sealed class NativeOperations : IServiceOperations
    {
        internal static NativeOperations Instance { get; } = new();
        public IServiceHandle OpenManager() => OpenSCManagerW(null, null, 1);
        public IServiceHandle OpenService(IServiceHandle manager, string name, uint access) =>
            OpenServiceW((ServiceHandle)manager, name, access);
        public void CheckHandle(IServiceHandle handle) => WindowsServices.CheckHandle((ServiceHandle)handle);
        public Status Query(IServiceHandle service) => WindowsServices.Query((ServiceHandle)service);
        public IServiceProcess OpenProcess(int processId) => new NativeProcess(processId);
        public void Stop(IServiceHandle service)
        {
            if (!ControlService((ServiceHandle)service, 1, out _)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        public void Start(IServiceHandle service)
        {
            if (!StartServiceW((ServiceHandle)service, 0, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private sealed class NativeProcess(int processId) : IServiceProcess
    {
        private readonly Process _process = Process.GetProcessById(processId);
        public long CreationTicks => _process.StartTime.ToUniversalTime().ToFileTimeUtc();
        public bool HasExited => _process.HasExited;
        public void Dispose() => _process.Dispose();
    }

    private static Status Query(ServiceHandle service)
    {
        return QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<Status>(), out _)
            ? status
            : throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    private static void CheckHandle(ServiceHandle handle)
    {
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    private sealed class ServiceHandle() : SafeHandleZeroOrMinusOneIsInvalid(true), IServiceHandle
    {
        protected override bool ReleaseHandle()
        {
            return CloseServiceHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Status
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
    private static extern bool QueryServiceStatusEx(ServiceHandle service, int level, out Status status, int size,
        out int needed);

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
