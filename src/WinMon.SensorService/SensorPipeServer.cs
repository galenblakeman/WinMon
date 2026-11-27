// Secure, bounded local pipe server. Authentication is from the connected token, never the request.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using MonCore.Windows.Monitoring;

namespace WinMon.SensorService;

internal sealed class SensorPipeServer(
    IReadOnlyCollection<string> allowedSids,
    Func<string, CancellationToken, Task<IAsyncDisposable>> subscribe,
    Func<string, SensorCollectorSnapshot> snapshot,
    string pipeName = SensorServiceProtocol.PipeName)
{
    private const int MaximumClients = 8;
    private readonly SemaphoreSlim _clients = new(MaximumClients, MaximumClients);
    private readonly ConcurrentDictionary<Task, byte> _handlers = new();
    private readonly string _serverSid = ProtectedServicePipe.GetCurrentSid();
    private int _firstInstance = 1;

    internal async Task RunAsync(CancellationToken token)
    {
        NamedPipeServerStream? pending = null;
        try
        {
            pending = CreatePipe();
            while (!token.IsCancellationRequested)
            {
                await _clients.WaitAsync(token).ConfigureAwait(false);
                var pipe = pending ?? throw new InvalidOperationException("Sensor pipe listener was lost.");
                pending = null;
                try
                {
                    using var registration = token.Register(pipe.Dispose);
                    await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                    pending = CreatePipe();
                    var handler = HandleAsync(pipe, token);
                    pipe = null;
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
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (EndOfStreamException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Trace.TraceWarning("Sensor pipe connection rejected: {0}", ex);
        }
        catch (Exception ex)
        {
            Trace.TraceError("Sensor pipe request failed: {0}", ex);
        }
        finally
        {
            pipe.Dispose();
            _clients.Release();
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, string sid, CancellationToken stopping)
    {
        IAsyncDisposable? lease = null;
        var requests = 0;
        var requestWindow = Stopwatch.GetTimestamp();
        var lastSnapshot = 0L;
        try
        {
            while (!stopping.IsCancellationRequested && pipe.IsConnected)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var request = await SensorServiceProtocol.ReadAsync<SensorServiceRequest>(pipe, timeout.Token,
                    SensorServiceProtocol.MaximumRequestBytes).ConfigureAwait(false);
                var now = Stopwatch.GetTimestamp();
                if (Stopwatch.GetElapsedTime(requestWindow, now) >= TimeSpan.FromSeconds(1))
                {
                    requestWindow = now;
                    requests = 0;
                }
                if (++requests > 16) throw new InvalidDataException("Control request rate exceeded.");
                if (request.Version != SensorServiceProtocol.Version)
                {
                    await WriteAsync(pipe, new(SensorServiceProtocol.Version, "version-mismatch"), timeout.Token)
                        .ConfigureAwait(false);
                    return;
                }

                switch (request.Operation)
                {
                    case "status":
                        await WriteAsync(pipe, new(SensorServiceProtocol.Version, "ok"), timeout.Token).ConfigureAwait(false);
                        break;
                    case "subscribe" when lease is null:
                        lease = await subscribe(sid, timeout.Token).ConfigureAwait(false);
                        await WriteAsync(pipe, new(SensorServiceProtocol.Version, "ok"), timeout.Token).ConfigureAwait(false);
                        break;
                    case "snapshot" when lease is not null:
                        if (lastSnapshot != 0 && Stopwatch.GetElapsedTime(lastSnapshot, now) < TimeSpan.FromSeconds(1))
                            throw new InvalidDataException("Snapshot request rate exceeded.");
                        lastSnapshot = now;
                        await WriteAsync(pipe, new(SensorServiceProtocol.Version, "ok", ToWire(snapshot(sid))), timeout.Token)
                            .ConfigureAwait(false);
                        break;
                    case "unsubscribe" when lease is not null:
                        await lease.DisposeAsync().ConfigureAwait(false);
                        lease = null;
                        await WriteAsync(pipe, new(SensorServiceProtocol.Version, "ok"), timeout.Token).ConfigureAwait(false);
                        return;
                    default:
                        throw new InvalidDataException("Unsupported sensor service operation.");
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (EndOfStreamException) { }
        catch (Exception ex)
        {
            Trace.TraceWarning("Sensor pipe request rejected: {0}", ex);
        }
        finally
        {
            if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static Task WriteAsync(NamedPipeServerStream pipe, SensorServiceResponse response, CancellationToken token) =>
        SensorServiceProtocol.WriteAsync(pipe, response, token);

    private static SensorSnapshot ToWire(SensorCollectorSnapshot value) => new(value.Generation, value.Sequence,
        value.UpdatedAt, value.Frequency, value.Quality switch
        {
            SensorCollectorQuality.Idle => SensorServiceQuality.Idle,
            SensorCollectorQuality.Starting => SensorServiceQuality.Starting,
            SensorCollectorQuality.Complete => SensorServiceQuality.Complete,
            SensorCollectorQuality.Partial => SensorServiceQuality.Partial,
            SensorCollectorQuality.Failed => SensorServiceQuality.Failed,
            SensorCollectorQuality.Suspended => SensorServiceQuality.Suspended,
            _ => throw new InvalidDataException("Unknown collector quality.")
        }, value.CpuTemperature is { } reading ? new CpuSensorReading(reading.HardwareId, reading.HardwareName,
            reading.SensorId, reading.SensorName, reading.Celsius, reading.Source, reading.LastReadAt,
            reading.AvailabilityReason) : null, value.FailureReason)
        { Fans = value.Fans, CpuPackagePower = value.CpuPackagePower, Sensors = value.Sensors };

    private NamedPipeServerStream CreatePipe()
    {
        return ProtectedServicePipe.CreateServer(pipeName, allowedSids, _serverSid, MaximumClients,
            SensorServiceProtocol.MaximumMessageBytes, Interlocked.Exchange(ref _firstInstance, 0) == 1);
    }
}
