using System.Diagnostics;
using System.IO.Pipes;

namespace MonCore.Windows.Monitoring;

/// <summary>A read-only subscription to the optional sensor service.</summary>
public interface ISensorTemperatureConnection : IAsyncDisposable
{
    Task SubscribeAsync(CancellationToken token);
    Task<SensorSnapshot> SnapshotAsync(CancellationToken token);
}

/// <summary>Serializes bounded requests to a verified LocalSystem sensor-service pipe.</summary>
public sealed class SensorServiceClient : ISensorTemperatureConnection
{
    private readonly Stream _pipe;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private bool _subscribed;
    private long _lastSnapshot;
    private int _disposed;

    internal SensorServiceClient(Stream pipe) => _pipe = pipe;

    public static async Task<SensorServiceClient> ConnectAsync(CancellationToken token)
    {
        NamedPipeClientStream? pipe = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await ProtectedServiceIdentity.EnsureRunningAsync(ProtectedServiceEndpoints.Sensor, timeout.Token)
                .ConfigureAwait(false);
            pipe = await NetworkServiceClient.ConnectProtectedPipeAsync(SensorServiceProtocol.PipeName, timeout.Token)
                .ConfigureAwait(false);
            ProtectedServiceIdentity.Verify(ProtectedServiceEndpoints.Sensor, pipe.SafePipeHandle);
            return new(pipe);
        }
        catch
        {
            pipe?.Dispose();
            throw;
        }
    }

    public async Task SubscribeAsync(CancellationToken token)
    {
        await _requests.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_subscribed) return;
            await SendAsync("subscribe", token).ConfigureAwait(false);
            _subscribed = true;
        }
        finally { _requests.Release(); }
    }

    public async Task<SensorServiceResponse> StatusAsync(CancellationToken token)
    {
        await _requests.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var response = await SendAsync("status", token).ConfigureAwait(false);
            if (response.Snapshot is { } snapshot) SensorSnapshotValidation.Validate(snapshot);
            return response;
        }
        catch (InvalidDataException)
        {
            _subscribed = false;
            _pipe.Dispose();
            throw;
        }
        finally { _requests.Release(); }
    }

    public async Task<SensorSnapshot> SnapshotAsync(CancellationToken token)
    {
        await _requests.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!_subscribed) throw new InvalidOperationException("Subscribe before requesting CPU sensors.");
            if (_lastSnapshot != 0)
                while (TimeSpan.FromSeconds(1) - Stopwatch.GetElapsedTime(_lastSnapshot) is var remaining &&
                       remaining > TimeSpan.Zero)
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(remaining.TotalMilliseconds)), token)
                        .ConfigureAwait(false);
            var response = await SendAsync("snapshot", token).ConfigureAwait(false);
            _lastSnapshot = Stopwatch.GetTimestamp();
            var snapshot = response.Snapshot ?? throw new InvalidDataException("Sensor service returned no snapshot.");
            SensorSnapshotValidation.Validate(snapshot);
            return snapshot;
        }
        catch (InvalidDataException)
        {
            _subscribed = false;
            _pipe.Dispose();
            throw;
        }
        finally { _requests.Release(); }
    }

    private async Task<SensorServiceResponse> SendAsync(string operation, CancellationToken token)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await SensorServiceProtocol.WriteAsync(_pipe,
                new SensorServiceRequest(SensorServiceProtocol.Version, operation), timeout.Token).ConfigureAwait(false);
            var response = await SensorServiceProtocol.ReadAsync<SensorServiceResponse>(_pipe, timeout.Token)
                .ConfigureAwait(false);
            ValidateResponse(response);
            return response;
        }
        catch
        {
            _subscribed = false;
            _pipe.Dispose(); // Partial/timed-out framed exchanges cannot be resumed safely.
            throw;
        }
    }

    internal static void ValidateResponse(SensorServiceResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Version != SensorServiceProtocol.Version)
            throw new SensorServiceProtocolMismatchException(response.Version, SensorServiceProtocol.Version);
        if (response.Status != "ok") throw new InvalidOperationException("Sensor service rejected the request.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _requests.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                if (_subscribed) await SendAsync("unsubscribe", timeout.Token).ConfigureAwait(false);
            }
            finally { _requests.Release(); }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or InvalidOperationException
                                          or InvalidDataException)
        { }
        finally { _pipe.Dispose(); }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}

public sealed class SensorServiceProtocolMismatchException(int actualVersion, int expectedVersion)
    : InvalidOperationException($"Sensor service protocol {actualVersion} does not match required {expectedVersion}.")
{
    public int ActualVersion { get; } = actualVersion;
    public int ExpectedVersion { get; } = expectedVersion;
}
