using System.ComponentModel;
using System.Diagnostics;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;

namespace WinMon.Monitoring;

/// <summary>Owns optional CPU-sensor service polling; capture reads only its cached validated observation.</summary>
internal sealed class CpuTemperatureClient : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<ISensorTemperatureConnection>> _connect;
    private readonly Func<string, Func<CancellationToken, Task>, CancellationToken, Task> _setup;
    private readonly Func<bool> _canSetup;
    private readonly bool _supported;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _pollCancellation;
    private Task? _poll;
    private ISensorTemperatureConnection? _connection;
    private SensorSnapshot? _snapshot;
    private bool _enabled;
    private long _lastSequence;
    private Guid _generation;
    private readonly Queue<Guid> _retiredGenerations = new();
    private readonly HashSet<Guid> _retiredGenerationSet = [];

    internal CpuTemperatureClient() : this(async token => await SensorServiceClient.ConnectAsync(token)
            .ConfigureAwait(false), new SensorSetupBroker().RunAsync,
        () => SensorSetupBroker.IsAvailable, SensorSetupBroker.Supported)
    {
    }

    internal CpuTemperatureClient(Func<CancellationToken, Task<ISensorTemperatureConnection>> connect,
        Func<string, Func<CancellationToken, Task>, CancellationToken, Task> setup, Func<bool> canSetup,
        bool supported = true, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _connect = connect;
        _setup = setup;
        _canSetup = canSetup;
        _supported = supported;
        _delay = delay ?? Task.Delay;
        State = supported ? SensorMonitoringState.Disabled : SensorMonitoringState.StoreUnavailable;
    }

    internal SensorMonitoringState State { get; private set; }

    /// <summary>Fresh validated reading for the sampler, or null when disabled, stale, incomplete, or replaced.</summary>
    internal CpuTemperatureReading? Reading => _enabled
        ? SensorSnapshotValidation.TryGetReading(Volatile.Read(ref _snapshot), Stopwatch.GetTimestamp())
        : null;
    internal IReadOnlyList<HardwareFanReading> Fans => _enabled
        ? SensorSnapshotValidation.GetFreshFans(Volatile.Read(ref _snapshot), Stopwatch.GetTimestamp())
        : [];

    internal event Action<SensorMonitoringState>? StateChanged;

    internal Task<SensorMonitoringChangeResult> RestoreAsync(bool enabled, CancellationToken token) =>
        ChangeAsync(enabled, false, token);

    internal Task<SensorMonitoringChangeResult> ChangeAsync(bool enabled, CancellationToken token) =>
        ChangeAsync(enabled, true, token);

    private async Task<SensorMonitoringChangeResult> ChangeAsync(bool enabled, bool allowSetup, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try { await _gate.WaitAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return Result(false, SensorMonitoringState.Cancelled); }
        var previouslyEnabled = _enabled;
        var applied = false;
        try
        {
            if (!_supported) return Result(false, SensorMonitoringState.StoreUnavailable);
            Clear();
            await DisconnectAsync().ConfigureAwait(false);
            if (!enabled)
            {
                _enabled = false;
                applied = true;
                return Result(true, SensorMonitoringState.Disabled);
            }

            _enabled = true;
            SetState(SensorMonitoringState.Enabling);
            try
            {
                try { await OpenAsync(linked.Token).ConfigureAwait(false); }
                catch (SensorServiceProtocolMismatchException) when (!allowSetup || !_canSetup())
                {
                    return Result(false, SensorMonitoringState.UpdateRequired);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !linked.IsCancellationRequested)
                {
                    if (!allowSetup) { StartPolling(); return Result(false, SensorMonitoringState.Unavailable); }
                    if (!_canSetup()) return Result(false, SensorMonitoringState.SetupUnavailable);
                    await _setup("install", OpenAsync, linked.Token).ConfigureAwait(false);
                }
                StartPolling();
                applied = true;
                return new SensorMonitoringChangeResult(true, State);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                await DisconnectAsync().ConfigureAwait(false);
                return Result(false, SensorMonitoringState.Cancelled);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                await DisconnectAsync().ConfigureAwait(false);
                return Result(false, SensorMonitoringState.Cancelled);
            }
            catch (SensorSetupFailedException exception) when (exception.Phase == "pawnio")
            {
                Trace.TraceWarning("WinMon PawnIO setup returned {0}.", exception.ErrorCode);
                await DisconnectAsync().ConfigureAwait(false);
                return Result(false, exception.ErrorCode == 3010
                    ? SensorMonitoringState.RestartRequired : SensorMonitoringState.PawnIoSetupFailed);
            }
            catch (Exception exception)
            {
                Trace.TraceError("WinMon CPU temperature monitoring could not be enabled: {0}", exception);
                await DisconnectAsync().ConfigureAwait(false);
                return Result(false, SensorMonitoringState.Failed);
            }
        }
        finally
        {
            if (!applied && allowSetup)
            {
                _enabled = previouslyEnabled;
                if (_enabled && !_lifetime.IsCancellationRequested && _poll is null && _connection is null &&
                    State != SensorMonitoringState.UpdateRequired)
                    StartPolling();
            }
            _gate.Release();
        }
    }

    internal async Task<SensorMonitoringChangeResult> RemoveAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try { await _gate.WaitAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return Result(false, SensorMonitoringState.Cancelled); }
        try
        {
            if (!_supported) return Result(false, SensorMonitoringState.StoreUnavailable);
            if (!_canSetup()) return Result(false, SensorMonitoringState.SetupUnavailable);
            Clear();
            await DisconnectAsync().ConfigureAwait(false);
            try
            {
                SetState(SensorMonitoringState.Enabling);
                await _setup("remove", _ => Task.CompletedTask, linked.Token).ConfigureAwait(false);
                _enabled = false;
                return Result(true, SensorMonitoringState.Disabled);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { return Result(false, SensorMonitoringState.Cancelled); }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223) { return Result(false, SensorMonitoringState.Cancelled); }
            catch (Exception exception)
            {
                Trace.TraceError("WinMon CPU sensor service removal failed: {0}", exception);
                return Result(false, SensorMonitoringState.Failed);
            }
        }
        finally
        {
            // A declined/failed removal does not disable the saved monitoring choice.
            // Resume ordinary reconnects without another setup prompt once rollback can finish.
            if (_enabled && !_lifetime.IsCancellationRequested && _poll is null && _connection is null)
                StartPolling();
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
            var published = Publish(snapshot);
            _connection = connection;
            if (published) SetState(StateFor(snapshot));
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
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
                await _delay(TimeSpan.FromSeconds(_connection is null ? 5 : 1), token).ConfigureAwait(false);
                try
                {
                    if (_connection is null) await OpenAsync(token).ConfigureAwait(false);
                    else
                    {
                        var snapshot = await _connection.SnapshotAsync(token).ConfigureAwait(false);
                        if (Publish(snapshot)) SetState(StateFor(snapshot));
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (SensorServiceProtocolMismatchException)
                {
                    var connection = Interlocked.Exchange(ref _connection, null);
                    if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
                    Clear();
                    SetState(SensorMonitoringState.UpdateRequired);
                    return; // An incompatible service needs explicit repair, not automatic reconnect loops.
                }
                catch (Exception exception)
                {
                    Trace.TraceWarning("CPU sensor request failed: {0}", exception.Message);
                    var connection = Interlocked.Exchange(ref _connection, null);
                    if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
                    Clear();
                    SetState(SensorMonitoringState.Unavailable);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private bool Publish(SensorSnapshot snapshot)
    {
        SensorSnapshotValidation.Validate(snapshot);
        if (_retiredGenerationSet.Contains(snapshot.Generation)) return false;
        if (_generation != Guid.Empty && snapshot.Generation != _generation)
        {
            RetireGeneration(_generation);
            _lastSequence = 0;
        }
        if (_generation == snapshot.Generation && snapshot.Sequence < _lastSequence) return false;
        _generation = snapshot.Generation;
        _lastSequence = snapshot.Sequence;
        Volatile.Write(ref _snapshot, snapshot);
        return true;
    }

    private SensorMonitoringState StateFor(SensorSnapshot snapshot) => snapshot.Quality switch
    {
        SensorServiceQuality.Starting => SensorMonitoringState.Starting,
        SensorServiceQuality.Complete when Reading is not null => SensorMonitoringState.Active,
        _ => SensorMonitoringState.Unavailable
    };

    private async Task DisconnectAsync()
    {
        var cancellation = _pollCancellation;
        cancellation?.Cancel();
        var poll = _poll;
        _poll = null;
        if (poll is not null)
        {
            try { await poll.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        cancellation?.Dispose();
        _pollCancellation = null;
        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
    }

    private void Clear()
    {
        _generation = Guid.Empty;
        _lastSequence = 0;
        Volatile.Write(ref _snapshot, null);
    }

    private void RetireGeneration(Guid generation)
    {
        if (!_retiredGenerationSet.Add(generation)) return;
        _retiredGenerations.Enqueue(generation);
        if (_retiredGenerations.Count > 8)
            _retiredGenerationSet.Remove(_retiredGenerations.Dequeue());
    }

    private SensorMonitoringChangeResult Result(bool success, SensorMonitoringState state)
    {
        SetState(state);
        return new SensorMonitoringChangeResult(success, state);
    }

    private void SetState(SensorMonitoringState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await _gate.WaitAsync().ConfigureAwait(false);
        try { _enabled = false; await DisconnectAsync().ConfigureAwait(false); Clear(); }
        finally { _gate.Release(); _lifetime.Dispose(); _gate.Dispose(); }
    }
}
