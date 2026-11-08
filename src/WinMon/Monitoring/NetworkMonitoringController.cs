using System.ComponentModel;
using System.Diagnostics;

namespace WinMon.Monitoring;

internal interface INetworkMonitoringConnection : IAsyncDisposable
{
    Task SubscribeAsync(CancellationToken token);
    Task<ProcessNetworkSnapshot> SnapshotAsync(CancellationToken token);
}

internal sealed record NetworkMonitoringSample(Guid SessionId, ProcessNetworkSnapshot? Snapshot);

internal sealed class NetworkMonitoringController : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<INetworkMonitoringConnection>> _connect;
    private readonly Func<string, Func<CancellationToken, Task>, CancellationToken, Task> _setup;
    private readonly Func<bool> _canSetup;
    private readonly bool _supported;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _pollCancellation;
    private Task? _poll;
    private INetworkMonitoringConnection? _connection;
    private bool _enabled;
    private int _connectionFailures;
    private long _outageStarted;
    private ProcessNetworkSnapshot? _lastDiagnosticSnapshot;
    private long _lastDiagnosticAt;
    private NetworkMonitoringSample _sample = new(Guid.Empty, null);

    internal NetworkMonitoringController() : this(ConnectAsync, new NetworkSetupBroker().RunAsync,
        () => NetworkSetupBroker.IsAvailable, NetworkSetupBroker.Supported)
    {
    }

    internal NetworkMonitoringController(Func<CancellationToken, Task<INetworkMonitoringConnection>> connect,
        Func<string, Func<CancellationToken, Task>, CancellationToken, Task> setup, Func<bool> canSetup,
        bool supported = true)
    {
        _connect = connect;
        _setup = setup;
        _canSetup = canSetup;
        _supported = supported;
        State = supported ? NetworkMonitoringState.Disabled : NetworkMonitoringState.StoreUnavailable;
    }

    internal NetworkMonitoringState State { get; private set; }
    internal NetworkMonitoringSample Sample => Volatile.Read(ref _sample);
    internal ProcessNetworkSnapshot? LatestSnapshot => Sample.Snapshot;
    internal event Action<NetworkMonitoringState>? StateChanged;

    internal Task<NetworkMonitoringChangeResult> RestoreAsync(bool enabled, CancellationToken token)
    {
        return ChangeAsync(enabled, false, token);
    }

    internal Task<NetworkMonitoringChangeResult> ChangeAsync(bool enabled, CancellationToken token)
    {
        return ChangeAsync(enabled, true, token);
    }

    private async Task<NetworkMonitoringChangeResult> ChangeAsync(bool enabled, bool allowSetup,
        CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try
        {
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new NetworkMonitoringChangeResult(false, NetworkMonitoringState.Cancelled);
        }

        try
        {
            if (enabled && _connection is not null && _poll is { IsCompleted: false } &&
                State is NetworkMonitoringState.Active or NetworkMonitoringState.WarmingUp
                    or NetworkMonitoringState.Incomplete)
                return new NetworkMonitoringChangeResult(true, State);
            await DisconnectAsync().ConfigureAwait(false);
            if (!_supported) return Result(false, NetworkMonitoringState.StoreUnavailable);
            if (!enabled)
            {
                _enabled = false;
                _connectionFailures = 0;
                _outageStarted = 0;
                Volatile.Write(ref _sample, new NetworkMonitoringSample(Guid.Empty, null));
                return Result(true, NetworkMonitoringState.Disabled);
            }

            if (!_enabled)
            {
                _enabled = true;
                Volatile.Write(ref _sample, new NetworkMonitoringSample(Guid.NewGuid(), null));
            }

            SetState(NetworkMonitoringState.Enabling);
            try
            {
                try
                {
                    await OpenAsync(linked.Token).ConfigureAwait(false);
                }
                catch (NetworkServiceProtocolMismatchException)
                {
                    if (!allowSetup || !_canSetup()) return Result(false, NetworkMonitoringState.UpdateRequired);
                    await _setup("install", OpenAsync, linked.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException ||
                                                  !linked.IsCancellationRequested)
                {
                    ReportConnectionFailure(exception);
                    if (!allowSetup)
                    {
                        StartPolling();
                        return Result(false, NetworkMonitoringState.Unavailable);
                    }

                    if (!_canSetup()) return Result(false, NetworkMonitoringState.SetupUnavailable);
                    await _setup("install", OpenAsync, linked.Token).ConfigureAwait(false);
                }

                linked.Token.ThrowIfCancellationRequested();
                StartPolling();
                return new NetworkMonitoringChangeResult(true, State);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                await DisconnectAsync().ConfigureAwait(false);
                return Result(false, NetworkMonitoringState.Cancelled);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                await DisconnectAsync().ConfigureAwait(false);
                return Result(false, NetworkMonitoringState.Cancelled);
            }
            catch (NetworkServiceProtocolMismatchException)
            {
                await DisconnectAsync().ConfigureAwait(false);
                return Result(false, NetworkMonitoringState.UpdateRequired);
            }
            catch (Exception exception)
            {
                Trace.TraceError("WinMon network monitoring could not be enabled: {0}", exception);
                await DisconnectAsync().ConfigureAwait(false);
                return Result(false, NetworkMonitoringState.Failed);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<NetworkMonitoringChangeResult> RemoveAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try
        {
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new NetworkMonitoringChangeResult(false, NetworkMonitoringState.Cancelled);
        }

        try
        {
            if (!_supported) return Result(false, NetworkMonitoringState.StoreUnavailable);
            if (!_canSetup()) return Result(false, NetworkMonitoringState.SetupUnavailable);
            await DisconnectAsync().ConfigureAwait(false);
            try
            {
                SetState(NetworkMonitoringState.Enabling);
                await _setup("remove", _ => Task.CompletedTask, linked.Token).ConfigureAwait(false);
                _enabled = false;
                _connectionFailures = 0;
                _outageStarted = 0;
                Volatile.Write(ref _sample, new NetworkMonitoringSample(Guid.Empty, null));
                return Result(true, NetworkMonitoringState.Disabled);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                return Result(false, NetworkMonitoringState.Cancelled);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                return Result(false, NetworkMonitoringState.Cancelled);
            }
            catch (Exception exception)
            {
                Trace.TraceError("WinMon network service removal failed: {0}", exception);
                return Result(false, NetworkMonitoringState.Failed);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task OpenAsync(CancellationToken token)
    {
        var connection = await _connect(token).ConfigureAwait(false);
        try
        {
            await connection.SubscribeAsync(token).ConfigureAwait(false);
            var snapshot = await connection.SnapshotAsync(token).ConfigureAwait(false);
            ReportSnapshot(snapshot);
            if (snapshot.Quality.HasFlag(ProcessNetworkQuality.Unavailable))
                throw new InvalidOperationException("Network collection is unavailable.");
            token.ThrowIfCancellationRequested();
            _connection = connection;
            PublishSnapshot(snapshot);
            SetState(ToState(snapshot));
            Trace.TraceInformation("Network.Connected attempts={0} outageMs={1}", _connectionFailures,
                _outageStarted == 0 ? 0 : (long)Stopwatch.GetElapsedTime(_outageStarted).TotalMilliseconds);
            _connectionFailures = 0;
            _outageStarted = 0;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void StartPolling()
    {
        _pollCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _poll = PollAsync(_pollCancellation.Token);
    }

    private async Task PollAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(_connection is null ? 5 : 1), token).ConfigureAwait(false);
                try
                {
                    if (_connection is null)
                    {
                        await OpenAsync(token).ConfigureAwait(false);
                    }
                    else
                    {
                        var snapshot = await _connection.SnapshotAsync(token).ConfigureAwait(false);
                        ReportSnapshot(snapshot);
                        if ((snapshot.Quality & ProcessNetworkQuality.Unavailable) != 0)
                            throw new InvalidOperationException("Network collection is unavailable.");
                        PublishSnapshot(snapshot);
                        SetState(ToState(snapshot));
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    ReportConnectionFailure(exception);
                    PublishSnapshot(null);
                    SetState(exception is NetworkServiceProtocolMismatchException
                        ? NetworkMonitoringState.UpdateRequired
                        : NetworkMonitoringState.Unavailable);
                    if (_connection is { } failed)
                    {
                        _connection = null;
                        await failed.DisposeAsync().ConfigureAwait(false);
                    }

                    if (exception is NetworkServiceProtocolMismatchException) return;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            if (_connection is { } connection)
            {
                _connection = null;
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task DisconnectAsync()
    {
        if (_pollCancellation is { } cancellation)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            if (_poll is { } poll) await poll.ConfigureAwait(false);
            cancellation.Dispose();
        }
        else if (_connection is { } connection)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        _pollCancellation = null;
        _poll = null;
        _connection = null;
        PublishSnapshot(null);
    }

    private void PublishSnapshot(ProcessNetworkSnapshot? snapshot)
    {
        Volatile.Write(ref _sample, new NetworkMonitoringSample(_sample.SessionId, snapshot));
    }

    private NetworkMonitoringChangeResult Result(bool success, NetworkMonitoringState state)
    {
        SetState(state);
        return new NetworkMonitoringChangeResult(success, state);
    }

    private void SetState(NetworkMonitoringState state)
    {
        if (State == state) return;
        Trace.TraceInformation("Network.State from={0} to={1}", State, state);
        State = state;
        StateChanged?.Invoke(state);
    }

    private void ReportConnectionFailure(Exception exception)
    {
        if (_connectionFailures++ == 0) _outageStarted = Stopwatch.GetTimestamp();
        Trace.TraceWarning("Network.ConnectionFailed: {0}", exception);
    }

    private void ReportSnapshot(ProcessNetworkSnapshot snapshot)
    {
        var previous = _lastDiagnosticSnapshot;
        if (previous is not null && previous.Generation == snapshot.Generation &&
            previous.Quality == snapshot.Quality &&
            previous.IncompleteReasons == snapshot.IncompleteReasons && previous.Failure == snapshot.Failure &&
            Stopwatch.GetElapsedTime(_lastDiagnosticAt) < TimeSpan.FromMinutes(1)) return;
        _lastDiagnosticSnapshot = snapshot;
        _lastDiagnosticAt = Stopwatch.GetTimestamp();
        Trace.TraceInformation(
            "Network.Snapshot quality={0} reasons={1} failure={2} generationChanged={3} sequence={4} rows={5} rates={6} eventsLost={7} lateEvents={8} droppedEvents={9} windowMs={10} lagMs={11}",
            snapshot.Quality, snapshot.IncompleteReasons, snapshot.Failure, previous?.Generation != snapshot.Generation,
            snapshot.Sequence, snapshot.Processes.Count,
            snapshot.Processes.Count(row => row.ReceiveBytesPerSecond.HasValue && row.SendBytesPerSecond.HasValue),
            snapshot.EventsLost, snapshot.LateEvents, snapshot.DroppedEvents,
            (snapshot.WindowEndQpc - snapshot.WindowStartQpc) * 1000d / snapshot.Frequency,
            (snapshot.TimestampQpc - snapshot.WindowEndQpc) * 1000d / snapshot.Frequency);
    }

    internal static NetworkMonitoringState ToState(ProcessNetworkSnapshot snapshot)
    {
        return snapshot.Quality.HasFlag(ProcessNetworkQuality.Unavailable) ? NetworkMonitoringState.Unavailable :
            snapshot.Quality.HasFlag(ProcessNetworkQuality.WarmingUp) ? NetworkMonitoringState.WarmingUp :
            (snapshot.Quality & (ProcessNetworkQuality.Incomplete | ProcessNetworkQuality.CapacityExceeded |
                                 ProcessNetworkQuality.Discontinuity)) != 0 ? NetworkMonitoringState.Incomplete :
            NetworkMonitoringState.Active;
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        _lifetime.Dispose();
    }

    private static async Task<INetworkMonitoringConnection> ConnectAsync(CancellationToken token)
    {
        return new Connection(await NetworkServiceClient.ConnectAsync(token).ConfigureAwait(false));
    }

    private sealed class Connection(NetworkServiceClient client) : INetworkMonitoringConnection
    {
        public Task SubscribeAsync(CancellationToken token)
        {
            return client.SubscribeAsync(token);
        }

        public Task<ProcessNetworkSnapshot> SnapshotAsync(CancellationToken token)
        {
            return client.SnapshotAsync(token);
        }

        public ValueTask DisposeAsync()
        {
            return client.DisposeAsync();
        }
    }
}