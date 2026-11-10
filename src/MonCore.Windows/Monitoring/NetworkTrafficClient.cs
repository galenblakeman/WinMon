namespace WinMon.Monitoring;

/// <summary>Read-only connection to the optional local network-monitor service.</summary>
public interface INetworkTrafficConnection : IAsyncDisposable
{
    Task SubscribeAsync(CancellationToken token);
    Task<ProcessNetworkSnapshot> SnapshotAsync(CancellationToken token);
}

/// <summary>Availability of the optional network-monitor service.</summary>
public enum NetworkTrafficState
{
    NotStarted,
    Connecting,
    WarmingUp,
    Active,
    Incomplete,
    Unavailable,
    UpdateRequired
}

/// <summary>Latest traffic observation for one logical monitoring session.</summary>
public sealed record TrafficSample(Guid SessionId, ProcessNetworkSnapshot? Snapshot, NetworkTrafficState State);

/// <summary>
/// Polls an already-installed network-monitor service without changing its installation or configuration.
/// </summary>
public sealed class NetworkTrafficClient : IAsyncDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);

    private readonly Func<CancellationToken, Task<INetworkTrafficConnection>> _connect;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _operationTimeout;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _lifecycle = new();
    private TrafficSample _sample = new(Guid.Empty, null, NetworkTrafficState.NotStarted);
    private TaskCompletionSource? _activeRun;
    private Task? _dispose;
    private bool _disposed;

    public NetworkTrafficClient()
        : this(ConnectAsync, Task.Delay, RetryDelay, OperationTimeout)
    {
    }

    /// <summary>Creates a client with replaceable I/O for hosts and deterministic tests.</summary>
    public NetworkTrafficClient(Func<CancellationToken, Task<INetworkTrafficConnection>> connect,
        Func<TimeSpan, CancellationToken, Task>? delay = null, TimeSpan? retryDelay = null,
        TimeSpan? operationTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(connect);
        _connect = connect;
        _delay = delay ?? Task.Delay;
        _retryDelay = retryDelay ?? RetryDelay;
        _operationTimeout = operationTimeout ?? OperationTimeout;
        if (_retryDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retryDelay));
        if (_operationTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(operationTimeout));
    }

    /// <summary>Latest atomically-published observation. A null snapshot represents an unavailable gap.</summary>
    public TrafficSample Sample => Volatile.Read(ref _sample);

    /// <summary>Runs until cancelled, retrying ordinary service failures after five seconds.</summary>
    public async Task Run(CancellationToken token)
    {
        TaskCompletionSource completion;
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_activeRun is not null) throw new InvalidOperationException("Network traffic polling is already running.");
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeRun = completion;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var sessionId = Guid.NewGuid();
        try
        {
            while (!linked.IsCancellationRequested)
            {
                Publish(sessionId, null, NetworkTrafficState.Connecting);
                INetworkTrafficConnection? connection = null;
                try
                {
                    connection = await InvokeAsync(_connect, linked.Token).ConfigureAwait(false);
                    await InvokeAsync(connection.SubscribeAsync, linked.Token).ConfigureAwait(false);
                    Publish(sessionId, null, NetworkTrafficState.WarmingUp);

                    while (true)
                    {
                        var snapshot = await InvokeAsync(connection.SnapshotAsync, linked.Token).ConfigureAwait(false);
                        if (snapshot.Quality.HasFlag(ProcessNetworkQuality.Unavailable))
                            throw new InvalidOperationException("Network collection is unavailable.");
                        Publish(sessionId, snapshot, ToState(snapshot));
                    }
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    return;
                }
                catch (NetworkServiceProtocolMismatchException)
                {
                    Publish(sessionId, null, NetworkTrafficState.UpdateRequired);
                    return;
                }
                catch
                {
                    Publish(sessionId, null, NetworkTrafficState.Unavailable);
                }
                finally
                {
                    if (connection is not null) await DisposeConnectionAsync(connection).ConfigureAwait(false);
                }

                try
                {
                    await _delay(_retryDelay, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    return;
                }
            }
        }
        finally
        {
            lock (_lifecycle)
            {
                if (ReferenceEquals(_activeRun, completion)) _activeRun = null;
            }

            completion.TrySetResult();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycle)
        {
            _dispose ??= DisposeCoreAsync(_activeRun?.Task);
            return new ValueTask(_dispose);
        }
    }

    private async Task DisposeCoreAsync(Task? activeRun)
    {
        _disposed = true;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        if (activeRun is not null) await activeRun.ConfigureAwait(false);
        _lifetime.Dispose();
    }

    private async Task<T> InvokeAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(_operationTimeout);
        return await operation(timeout.Token).ConfigureAwait(false);
    }

    private async Task InvokeAsync(Func<CancellationToken, Task> operation, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(_operationTimeout);
        await operation(timeout.Token).ConfigureAwait(false);
    }

    private void Publish(Guid sessionId, ProcessNetworkSnapshot? snapshot, NetworkTrafficState state)
    {
        Volatile.Write(ref _sample, new TrafficSample(sessionId, snapshot, state));
    }

    private static NetworkTrafficState ToState(ProcessNetworkSnapshot snapshot)
    {
        return snapshot.Quality.HasFlag(ProcessNetworkQuality.WarmingUp) ? NetworkTrafficState.WarmingUp :
            (snapshot.Quality & (ProcessNetworkQuality.Incomplete | ProcessNetworkQuality.CapacityExceeded |
                                 ProcessNetworkQuality.Discontinuity)) != 0 ? NetworkTrafficState.Incomplete :
            NetworkTrafficState.Active;
    }

    private static async Task DisposeConnectionAsync(INetworkTrafficConnection connection)
    {
        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // The connection already failed; disposal must not prevent a retry or shutdown.
        }
    }

    private static async Task<INetworkTrafficConnection> ConnectAsync(CancellationToken token)
    {
        return new Connection(await NetworkServiceClient.ConnectAsync(token).ConfigureAwait(false));
    }

    private sealed class Connection(NetworkServiceClient client) : INetworkTrafficConnection
    {
        public Task SubscribeAsync(CancellationToken token) => client.SubscribeAsync(token);

        public Task<ProcessNetworkSnapshot> SnapshotAsync(CancellationToken token) => client.SnapshotAsync(token);

        public ValueTask DisposeAsync() => client.DisposeAsync();
    }
}
