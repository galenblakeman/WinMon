using System.IO.Pipes;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using WinMon.Monitoring;

namespace WinMon.NetworkService;

/// <summary>Bounded, local-only pipe server. Authentication always uses the connected token, never request data.</summary>
internal sealed class NetworkPipeServer(IReadOnlyCollection<string> allowedSids,
    Func<string, CancellationToken, Task<IAsyncDisposable>> subscribe,
    Func<string, ProcessNetworkSnapshot> snapshot,
    string pipeName = NetworkServiceProtocol.PipeName)
{
    private const int MaximumClients = 8;
    private readonly SemaphoreSlim _clients = new(MaximumClients, MaximumClients);
    private readonly ConcurrentDictionary<Task, byte> _handlers = new();
    private int _firstInstance = 1;
    private readonly string _serverSid = GetServerSid();

    internal async Task RunAsync(CancellationToken token)
    {
        NamedPipeServerStream? pending = null;
        try
        {
            pending = CreatePipe(); // FIRST_PIPE_INSTANCE anchor: never leave the name unowned while running.
            while (!token.IsCancellationRequested)
            {
                await _clients.WaitAsync(token).ConfigureAwait(false);
                NamedPipeServerStream? pipe = pending ?? throw new InvalidOperationException("Network service listener was lost.");
                pending = null;
                try
                {
                    using var stopRegistration = token.Register(pipe.Dispose);
                    await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                    pending = CreatePipe(); // Create the replacement before giving up the connected instance.
                    var handler = HandleAsync(pipe, token);
                    pipe = null; // The handler owns it and the reserved slot.
                    _handlers.TryAdd(handler, 0);
                    _ = handler.ContinueWith(completed => _handlers.TryRemove(completed, out _),
                        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { pipe?.Dispose(); _clients.Release(); break; }
                catch { pipe?.Dispose(); _clients.Release(); throw; }
            }
        }
        finally
        {
            pending?.Dispose();
            var handlers = _handlers.Keys.ToArray();
            if (handlers.Length != 0) await Task.WhenAll(handlers).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken stopping)
    {
        try
        {
            string? sid = null;
            pipe.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
                sid = identity.User?.Value;
            });
            if (sid is null) throw new UnauthorizedAccessException("Pipe client has no SID.");
            if (!allowedSids.Contains(sid, StringComparer.Ordinal)) throw new UnauthorizedAccessException("Pipe client is not provisioned.");
            await ServeAsync(pipe, sid, stopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidDataException) { }
        catch (Exception ex) { Trace.TraceError("Network pipe request failed: {0}", ex); }
        finally { pipe.Dispose(); _clients.Release(); }
    }

    private NamedPipeServerStream CreatePipe()
    {
        var sddl = "D:P(A;;GA;;;SY)" + (_serverSid == "S-1-5-18" ? "" : $"(A;;GA;;;{_serverSid})") +
            string.Concat(allowedSids.Where(sid => !string.Equals(sid, _serverSid, StringComparison.Ordinal))
                .Select(sid => $"(D;;0x4;;;{sid})(A;;0x12019B;;;{sid})"));
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var descriptor, out _))
            throw new UnauthorizedAccessException("Cannot create network service pipe security descriptor.");
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), SecurityDescriptor = descriptor };
            var mode = PipeTypeByte | PipeReadmodeByte | PipeRejectRemoteClients;
            var openMode = PipeAccessDuplex | FileFlagOverlapped;
            if (Interlocked.Exchange(ref _firstInstance, 0) == 1) openMode |= PipeFirstInstance;
            var handle = CreateNamedPipe($"\\\\.\\pipe\\{pipeName}", openMode,
                mode, MaximumClients + 1,
                NetworkServiceProtocol.MaximumMessageBytes, NetworkServiceProtocol.MaximumMessageBytes, 10000, ref attributes);
            if (handle.IsInvalid) throw new UnauthorizedAccessException("Cannot create protected network service pipe.");
            return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
        }
        finally { _ = LocalFree(descriptor); }
    }

    private const uint PipeAccessDuplex = 0x00000003;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint PipeTypeByte = 0;
    private const uint PipeReadmodeByte = 0;
    private const uint PipeRejectRemoteClients = 0x00000008;
    private const uint PipeFirstInstance = 0x00080000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Length; public IntPtr SecurityDescriptor; public IntPtr InheritHandle; }
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maximumInstances,
        uint outputSize, uint inputSize, uint timeout, ref SecurityAttributes attributes);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static string GetServerSid()
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        return identity.User?.Value ?? throw new InvalidOperationException("Network service has no Windows SID.");
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, string sid, CancellationToken stopping)
    {
        IAsyncDisposable? lease = null;
        var lastSnapshot = 0L;
        var subscriptionId = Guid.NewGuid();
        var generation = Guid.Empty;
        var sequence = 0L;
        var requestWindow = Stopwatch.GetTimestamp();
        var requests = 0;
        try
        {
            while (!stopping.IsCancellationRequested && pipe.IsConnected)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var request = await NetworkServiceProtocol.ReadAsync<NetworkServiceRequest>(pipe, timeout.Token,
                    NetworkServiceProtocol.MaximumRequestBytes).ConfigureAwait(false);
                var receivedAt = Stopwatch.GetTimestamp();
                if (Stopwatch.GetElapsedTime(requestWindow, receivedAt).TotalSeconds >= 1)
                {
                    requestWindow = receivedAt;
                    requests = 0;
                }
                if (++requests > 16) throw new InvalidDataException("Control request rate exceeded.");
                if (request.Version != NetworkServiceProtocol.Version)
                {
                    await NetworkServiceProtocol.WriteAsync(pipe,
                        new NetworkServiceResponse(NetworkServiceProtocol.Version, "version-mismatch"), timeout.Token).ConfigureAwait(false);
                    return;
                }
                switch (request.Operation)
                {
                    case "status":
                        await NetworkServiceProtocol.WriteAsync(pipe, new NetworkServiceResponse(NetworkServiceProtocol.Version, "ok"), timeout.Token).ConfigureAwait(false);
                        break;
                    case "subscribe" when lease is null:
                        lease = await subscribe(sid, timeout.Token).ConfigureAwait(false);
                        await NetworkServiceProtocol.WriteAsync(pipe, new NetworkServiceResponse(NetworkServiceProtocol.Version, "ok"), timeout.Token).ConfigureAwait(false);
                        break;
                    case "snapshot" when lease is not null:
                        var now = Stopwatch.GetTimestamp();
                        if (lastSnapshot != 0 && Stopwatch.GetElapsedTime(lastSnapshot, now) < TimeSpan.FromSeconds(1))
                            throw new InvalidDataException("Snapshot request rate exceeded.");
                        lastSnapshot = now;
                        var current = snapshot(sid);
                        if (generation != current.Generation)
                        {
                            generation = current.Generation;
                            sequence = 0;
                        }
                        current = current with { SubscriptionId = subscriptionId, Sequence = checked(++sequence) };
                        await NetworkServiceProtocol.WriteAsync(pipe, new NetworkServiceResponse(NetworkServiceProtocol.Version, "ok", current), timeout.Token).ConfigureAwait(false);
                        break;
                    case "unsubscribe" when lease is not null:
                        await lease.DisposeAsync().ConfigureAwait(false);
                        lease = null;
                        await NetworkServiceProtocol.WriteAsync(pipe, new NetworkServiceResponse(NetworkServiceProtocol.Version, "ok"), timeout.Token).ConfigureAwait(false);
                        return;
                    default:
                        throw new InvalidDataException("Unsupported network service operation.");
                }
            }
        }
        finally { if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false); }
    }
}
