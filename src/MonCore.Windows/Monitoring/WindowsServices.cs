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

        public ServiceCatalog Read()
        {
            // Processes born after enumeration began cannot safely inherit an enumerated PID.
            var observed = DateTime.UtcNow.ToFileTimeUtc();
            using var manager = OpenSCManagerW(null, null, 4);
            CheckHandle(manager);
            var address = Marshal.UnsafeAddrOfPinnedArrayElement(_buffer, 0);
            var services = new List<ServiceObservation>(_previous.Count);
            var previous = _previous.GetAlternateLookup<ReadOnlySpan<char>>();
            _next.Clear();
            uint resume = 0;
            for (var page = 0; page < 32; page++)
            {
                var success = EnumServicesStatusExW(manager, 0, 0x30, 3, address, BufferBytes,
                    out _, out var count, ref resume, null);
                var error = Marshal.GetLastPInvokeError();
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

    public static async Task ExecuteAsync(string name, string action, int expectedPid, long expectedCreation,
        CancellationToken cancellationToken)
    {
        if (action is not ("start" or "stop" or "restart")) throw new ArgumentException(nameof(action));
        using var manager = OpenSCManagerW(null, null, 1);
        CheckHandle(manager);
        using var service = OpenServiceW(manager, name, 4u | action switch { "start" => 16u, "stop" => 32u, _ => 48u });
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
        return QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<Status>(), out _)
            ? status
            : throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    private static void CheckHandle(ServiceHandle handle)
    {
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    private sealed class ServiceHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle()
        {
            return CloseServiceHandle(handle);
        }
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
