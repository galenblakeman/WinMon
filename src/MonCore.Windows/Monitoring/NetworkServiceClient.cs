// Close handles and cancel callbacks synchronously before releasing ownership or continuing shutdown.
// ReSharper disable MethodHasAsyncOverload
// Deserialized payloads can contain null despite non-nullable model annotations.
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract

using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;

namespace WinMon.Monitoring;

/// <summary>Client for the fixed local network-monitor service endpoint.</summary>
public sealed class NetworkServiceClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private long _lastRequest;
    private bool _subscribed;
    private int _disposed;

    internal NetworkServiceClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
    }

    public static async Task<NetworkServiceClient> ConnectAsync(CancellationToken token)
    {
        NamedPipeClientStream? pipe = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await NetworkServiceIdentity.EnsureRunningAsync(timeout.Token).ConfigureAwait(false);
            pipe = await ConnectProtectedPipeAsync(NetworkServiceProtocol.PipeName, timeout.Token)
                .ConfigureAwait(false);
            NetworkServiceIdentity.Verify(pipe.SafePipeHandle);
            // The service authenticates the connected Windows token and enforces its provisioned SID list.
            return new NetworkServiceClient(pipe);
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
            await SendCoreAsync("subscribe", token).ConfigureAwait(false);
            _subscribed = true;
        }
        finally
        {
            _requests.Release();
        }
    }

    public async Task<ProcessNetworkSnapshot> SnapshotAsync(CancellationToken token)
    {
        await _requests.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!_subscribed) throw new InvalidOperationException("Subscribe before requesting network readings.");
            if (_lastRequest != 0)
                // Timer delays can finish before the monotonic one-second server limit.
                while (true)
                {
                    var wait = TimeSpan.FromSeconds(1) - Stopwatch.GetElapsedTime(_lastRequest);
                    if (wait <= TimeSpan.Zero) break;
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(wait.TotalMilliseconds)), token)
                        .ConfigureAwait(false);
                }

            var response = await SendCoreAsync("snapshot", token).ConfigureAwait(false);
            _lastRequest = Stopwatch.GetTimestamp();
            var snapshot = response.Snapshot ?? throw new InvalidDataException("Network service returned no snapshot.");
            ValidateSnapshot(snapshot);
            return snapshot;
        }
        catch (InvalidDataException)
        {
            _subscribed = false;
            _pipe.Dispose();
            throw;
        }
        finally
        {
            _requests.Release();
        }
    }

    internal static void ValidateSnapshot(ProcessNetworkSnapshot snapshot)
    {
        const ProcessNetworkIncompleteReason knownReasons = ProcessNetworkIncompleteReason.OwnershipUnavailable |
                                                            ProcessNetworkIncompleteReason.InvalidEvent |
                                                            ProcessNetworkIncompleteReason.QueueOverflow |
                                                            ProcessNetworkIncompleteReason.NativeLoss |
                                                            ProcessNetworkIncompleteReason.AmbiguousLifetime |
                                                            ProcessNetworkIncompleteReason.CapacityExceeded |
                                                            ProcessNetworkIncompleteReason.EventTimestamp |
                                                            ProcessNetworkIncompleteReason.ClockDiscontinuity;
        if (snapshot.SubscriptionId == Guid.Empty || snapshot.Frequency <= 0 || snapshot.TimestampQpc < 0 ||
            snapshot.WindowStartQpc < 0 ||
            (snapshot.IncompleteReasons & ~knownReasons) != 0 ||
            (snapshot.IncompleteReasons != ProcessNetworkIncompleteReason.None &&
             !snapshot.Quality.HasFlag(ProcessNetworkQuality.Incomplete)) ||
            !Enum.IsDefined(snapshot.Failure) || (snapshot.Failure != ProcessNetworkFailure.None &&
                                                  !snapshot.Quality.HasFlag(ProcessNetworkQuality.Unavailable)) ||
            snapshot.WindowStartQpc > snapshot.WindowEndQpc || snapshot.WindowEndQpc > snapshot.TimestampQpc ||
            snapshot.Sequence < 0 || snapshot.EventsLost < 0 || snapshot.LateEvents < 0 || snapshot.DroppedEvents < 0 ||
            (!snapshot.Quality.HasFlag(ProcessNetworkQuality.Unavailable) &&
             (snapshot.Generation == Guid.Empty || snapshot.Sequence == 0)) ||
            snapshot.Processes is null || snapshot.Processes.Count > 8192 ||
            snapshot.Processes.Any(reading => reading is null || reading.ProcessId <= 0 || reading.CreationTicks <= 0 ||
                                              reading.ReceivedBytes < 0 || reading.SentBytes < 0 ||
                                              (reading.ReceiveBytesPerSecond is { } receive &&
                                               (!double.IsFinite(receive) || receive < 0)) ||
                                              (reading.SendBytesPerSecond is { } send &&
                                               (!double.IsFinite(send) || send < 0))))
            throw new InvalidDataException("Network service snapshot is invalid.");
    }

    private async Task<NetworkServiceResponse> SendCoreAsync(string operation, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await NetworkServiceProtocol.WriteAsync(_pipe,
                    new NetworkServiceRequest(NetworkServiceProtocol.Version, operation), timeout.Token)
                .ConfigureAwait(false);
            var response = await NetworkServiceProtocol.ReadAsync<NetworkServiceResponse>(_pipe, timeout.Token)
                .ConfigureAwait(false);
            ValidateResponse(response);
            return response;
        }
        catch (Exception exception)
        {
            _subscribed = false;
            _pipe.Dispose(); // A timed out/invalid length-prefixed exchange cannot be safely resumed.
            if (!token.IsCancellationRequested)
                Trace.TraceWarning("Network.RequestFailed operation={0} elapsedMs={1}: {2}",
                    operation switch
                    {
                        "subscribe" => RequestOperation.Subscribe, "snapshot" => RequestOperation.Snapshot,
                        "unsubscribe" => RequestOperation.Unsubscribe, _ => RequestOperation.Unknown
                    },
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds, exception);
            throw;
        }
    }

    internal static void ValidateResponse(NetworkServiceResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Version != NetworkServiceProtocol.Version)
            throw new NetworkServiceProtocolMismatchException(response.Version, NetworkServiceProtocol.Version);
        if (response.Status != "ok") throw new InvalidOperationException("Network service rejected the request.");
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
                if (_subscribed && _pipe.IsConnected)
                    await SendCoreAsync("unsubscribe", timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                _requests.Release();
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException
                                              or InvalidOperationException or InvalidDataException)
        {
        }
        finally
        {
            _pipe.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(NetworkServiceClient));
    }

    private enum RequestOperation
    {
        Unknown,
        Subscribe,
        Snapshot,
        Unsubscribe
    }

    // Request data rights explicitly; GENERIC_WRITE can include FILE_CREATE_PIPE_INSTANCE.
    internal static async Task<NamedPipeClientStream> ConnectProtectedPipeAsync(string pipeName,
        CancellationToken token)
    {
        var pipe = new NamedPipeClientStream(".", pipeName,
            PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.ReadPermissions |
            PipeAccessRights.Synchronize,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification, HandleInheritability.None);
        try
        {
            await pipe.ConnectAsync(token).ConfigureAwait(false);
            return pipe;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }
}

/// <summary>The authenticated service does not implement the client's protocol version.</summary>
public sealed class NetworkServiceProtocolMismatchException(int actualVersion, int expectedVersion)
    : InvalidOperationException(
        $"Network service protocol {actualVersion} does not match required version {expectedVersion}.")
{
    public int ActualVersion { get; } = actualVersion;
    public int ExpectedVersion { get; } = expectedVersion;
}