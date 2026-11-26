// Close handles and cancel callbacks synchronously before releasing ownership or continuing shutdown.
// ReSharper disable MethodHasAsyncOverload

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;

namespace WinMon.NetworkService;

/// <summary>Bounded, local-only pipe server. Authentication always uses the connected token, never request data.</summary>
internal sealed class NetworkPipeServer(
    IReadOnlyCollection<string> allowedSids,
    Func<string, CancellationToken, Task<IAsyncDisposable>> subscribe,
    Func<string, ProcessNetworkSnapshot> snapshot,
    string pipeName = NetworkServiceProtocol.PipeName)
{
    private const int MaximumClients = 8;
    private readonly SemaphoreSlim _clients = new(MaximumClients, MaximumClients);
    private readonly ConcurrentDictionary<Task, byte> _handlers = new();
    private int _firstInstance = 1;
    private readonly string _serverSid = ProtectedServicePipe.GetCurrentSid();

    internal async Task RunAsync(CancellationToken token)
    {
        NamedPipeServerStream? pending = null;
        try
        {
            pending = CreatePipe(); // FIRST_PIPE_INSTANCE anchor: never leave the name unowned while running.
            while (!token.IsCancellationRequested)
            {
                await _clients.WaitAsync(token).ConfigureAwait(false);
                var pipe = pending ?? throw new InvalidOperationException("Network service listener was lost.");
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
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    pipe?.Dispose();
                    _clients.Release();
                    break;
                }
                catch
                {
                    pipe?.Dispose();
                    _clients.Release();
                    throw;
                }
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
            var sid = ProtectedServicePipe.AuthenticateClient(pipe, allowedSids);
            await ServeAsync(pipe, sid, stopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
        catch (EndOfStreamException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException
                                       or InvalidDataException)
        {
            Trace.TraceWarning("Network.PipeConnectionRejected: {0}", ex);
        }
        catch (Exception ex)
        {
            Trace.TraceError("Network pipe request failed: {0}", ex);
        }
        finally
        {
            pipe.Dispose();
            _clients.Release();
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        return ProtectedServicePipe.CreateServer(pipeName, allowedSids, _serverSid, MaximumClients,
            NetworkServiceProtocol.MaximumMessageBytes, Interlocked.Exchange(ref _firstInstance, 0) == 1);
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
        var operation = PipeOperation.None;
        var stage = PipeStage.Read;
        var started = Stopwatch.GetTimestamp();
        try
        {
            while (!stopping.IsCancellationRequested && pipe.IsConnected)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                stage = PipeStage.Read;
                started = Stopwatch.GetTimestamp();
                var request = await NetworkServiceProtocol.ReadAsync<NetworkServiceRequest>(pipe, timeout.Token,
                    NetworkServiceProtocol.MaximumRequestBytes).ConfigureAwait(false);
                operation = request.Operation switch
                {
                    "status" => PipeOperation.Status, "subscribe" => PipeOperation.Subscribe,
                    "snapshot" => PipeOperation.Snapshot, "unsubscribe" => PipeOperation.Unsubscribe,
                    _ => PipeOperation.Unknown
                };
                var receivedAt = Stopwatch.GetTimestamp();
                if (Stopwatch.GetElapsedTime(requestWindow, receivedAt).TotalSeconds >= 1)
                {
                    requestWindow = receivedAt;
                    requests = 0;
                }

                if (++requests > 16)
                {
                    Trace.TraceWarning("Network.PipeControlRateExceeded requests={0}", requests);
                    throw new InvalidDataException("Control request rate exceeded.");
                }

                stage = PipeStage.Write;
                if (request.Version != NetworkServiceProtocol.Version)
                {
                    await NetworkServiceProtocol.WriteAsync(pipe,
                            new NetworkServiceResponse(NetworkServiceProtocol.Version, "version-mismatch"),
                            timeout.Token)
                        .ConfigureAwait(false);
                    return;
                }

                switch (request.Operation)
                {
                    case "status":
                        await NetworkServiceProtocol.WriteAsync(pipe,
                                new NetworkServiceResponse(NetworkServiceProtocol.Version, "ok"), timeout.Token)
                            .ConfigureAwait(false);
                        break;
                    case "subscribe" when lease is null:
                        stage = PipeStage.Subscribe;
                        lease = await subscribe(sid, timeout.Token).ConfigureAwait(false);
                        stage = PipeStage.Write;
                        await NetworkServiceProtocol.WriteAsync(pipe,
                                new NetworkServiceResponse(NetworkServiceProtocol.Version, "ok"), timeout.Token)
                            .ConfigureAwait(false);
                        break;
                    case "snapshot" when lease is not null:
                        stage = PipeStage.Snapshot;
                        var current = PrepareSnapshot(sid, subscriptionId, ref lastSnapshot, ref generation, ref sequence);
                        stage = PipeStage.Write;
                        await NetworkServiceProtocol.WriteAsync(pipe,
                                new NetworkServiceResponse(NetworkServiceProtocol.Version, "ok", current),
                                timeout.Token)
                            .ConfigureAwait(false);
                        break;
                    case "unsubscribe" when lease is not null:
                        stage = PipeStage.Unsubscribe;
                        await lease.DisposeAsync().ConfigureAwait(false);
                        lease = null;
                        stage = PipeStage.Write;
                        await NetworkServiceProtocol.WriteAsync(pipe,
                                new NetworkServiceResponse(NetworkServiceProtocol.Version, "ok"), timeout.Token)
                            .ConfigureAwait(false);
                        return;
                    default:
                        throw new InvalidDataException("Unsupported network service operation.");
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
        catch (EndOfStreamException) when (stage == PipeStage.Read)
        {
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Network.PipeRequestFailed operation={0} stage={1} subscribed={2} elapsedMs={3}: {4}",
                operation, stage, lease is not null, Stopwatch.GetElapsedTime(started).TotalMilliseconds, ex);
        }
        finally
        {
            if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private ProcessNetworkSnapshot PrepareSnapshot(string sid, Guid subscriptionId, ref long lastSnapshot,
        ref Guid generation, ref long sequence)
    {
        var now = Stopwatch.GetTimestamp();
        if (lastSnapshot != 0 && Stopwatch.GetElapsedTime(lastSnapshot, now) < TimeSpan.FromSeconds(1))
        {
            Trace.TraceWarning("Network.PipeSnapshotRateExceeded intervalMs={0}",
                Stopwatch.GetElapsedTime(lastSnapshot, now).TotalMilliseconds);
            throw new InvalidDataException("Snapshot request rate exceeded.");
        }

        lastSnapshot = now;
        var current = snapshot(sid);
        if (generation != current.Generation)
        {
            generation = current.Generation;
            sequence = 0;
        }

        return FitSnapshot(current with { SubscriptionId = subscriptionId, Sequence = checked(++sequence) });
    }

    internal static ProcessNetworkSnapshot FitSnapshot(ProcessNetworkSnapshot snapshot)
    {
        if (snapshot.Flows.Count == 0) return snapshot;
        var empty = snapshot with { Flows = [] };
        // Empty-array brackets remain; only element bytes and separators consume the remaining budget.
        var remaining = NetworkServiceProtocol.MaximumMessageBytes -
                        JsonSerializer.SerializeToUtf8Bytes(new NetworkServiceResponse(NetworkServiceProtocol.Version,
                            "ok", empty)).Length;
        var count = 0;
        foreach (var flow in snapshot.Flows)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(flow).Length + (count == 0 ? 0 : 1);
            if (bytes > remaining) break;
            remaining -= bytes;
            count++;
        }

        if (count == snapshot.Flows.Count) return snapshot;

        var trimmed = snapshot with
        {
            Flows = snapshot.Flows.Take(count).ToArray(),
            FlowQuality = snapshot.FlowQuality | ProcessNetworkQuality.Incomplete |
                          ProcessNetworkQuality.CapacityExceeded
        };
        while (trimmed.Flows.Count > 0 && !Fits(trimmed))
            trimmed = trimmed with { Flows = trimmed.Flows.Take(trimmed.Flows.Count - 1).ToArray() };
        return trimmed;

        static bool Fits(ProcessNetworkSnapshot value)
        {
            return JsonSerializer.SerializeToUtf8Bytes(new NetworkServiceResponse(NetworkServiceProtocol.Version,
                "ok", value)).Length <= NetworkServiceProtocol.MaximumMessageBytes;
        }
    }

    private enum PipeOperation
    {
        None,
        Status,
        Subscribe,
        Snapshot,
        Unsubscribe,
        Unknown
    }

    private enum PipeStage
    {
        Read,
        Subscribe,
        Snapshot,
        Write,
        Unsubscribe
    }
}
