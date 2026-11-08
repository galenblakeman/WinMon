using System.Buffers.Binary;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace WinMon.NetworkProbe;

internal sealed class NetworkCapture : IDisposable
{
    private readonly object _gate = new();
    private readonly NetworkLedger _ledger = new();
    private readonly TraceEventSession _session;
    private readonly int _workerId;
    private readonly HashSet<(bool Tcp, bool Ipv6, bool Send)> _checkedPayloads = [];
    private Task? _consumer;
    private long _networkEvents;
    private long _unattributedWorkerEvents;
    private long _zeroSizeWorkerEvents;
    private long _unsupportedWorkerEvents;
    private long _payloadPidMismatches;
    private long _processStarts;
    private long _processStops;
    private bool _disposed;

    internal NetworkCapture(int workerId, long creationTicks, DateTime startedAtUtc)
    {
        _workerId = workerId;
        _ledger.Start(workerId, creationTicks, startedAtUtc);
        SessionName = $"WinMon-NetworkProbe-{Environment.ProcessId}-{Guid.NewGuid():N}";
        _session = new TraceEventSession(SessionName,
            TraceEventSessionOptions.Create | TraceEventSessionOptions.NoRestartOnCreate)
        {
            StopOnDispose = true,
            BufferSizeMB = 16
        };
    }

    internal string SessionName { get; }

    internal void Start()
    {
        // Accessing Source starts a session, so enable the kernel provider before opening the consumer.
        _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP | KernelTraceEventParser.Keywords.Process);
        var kernel = _session.Source.Kernel;
        kernel.TcpIpSend += e => Add(e, e.size, true, false, true);
        kernel.TcpIpRecv += e => Add(e, e.size, true, false, false);
        kernel.TcpIpSendIPV6 += e => Add(e, e.size, true, true, true);
        kernel.TcpIpRecvIPV6 += e => Add(e, e.size, true, true, false);
        kernel.UdpIpSend += e => Add(e, e.size, false, false, true);
        kernel.UdpIpRecv += e => Add(e, e.size, false, false, false);
        kernel.UdpIpSendIPV6 += e => Add(e, e.size, false, true, true);
        kernel.UdpIpRecvIPV6 += e => Add(e, e.size, false, true, false);
        kernel.ProcessStart += e => { if (e.ProcessID == _workerId) Interlocked.Increment(ref _processStarts); };
        kernel.ProcessStop += e =>
        {
            if (e.ProcessID != _workerId) return;
            Interlocked.Increment(ref _processStops);
            lock (_gate) _ledger.Stop(e.ProcessID, e.TimeStamp.ToUniversalTime());
        };
        _consumer = Task.Run(() => _session.Source.Process());
    }

    private void Add(TraceEvent data, int size, bool tcp, bool ipv6, bool send)
    {
        Interlocked.Increment(ref _networkEvents);
        if (data.ProcessID != _workerId) return;
        lock (_gate)
        {
            if (data.Version < 1)
            {
                _unsupportedWorkerEvents++;
                return;
            }
            // Check the pinned parser's PID fixup against the documented payload, without retaining endpoints.
            if (_checkedPayloads.Add((tcp, ipv6, send)))
            {
                if (data.EventDataLength < sizeof(int) ||
                    BinaryPrimitives.ReadInt32LittleEndian(data.EventData(null!, 0, 0, sizeof(int))) != data.ProcessID)
                    _payloadPidMismatches++;
            }
            if (size == 0)
            {
                _zeroSizeWorkerEvents++;
                return;
            }
            if (!_ledger.Add(data.ProcessID, data.TimeStamp.ToUniversalTime(), tcp, ipv6, send, size))
                _unattributedWorkerEvents++;
        }
    }

    internal async Task SettleAsync(CancellationToken token)
    {
        _session.Flush();
        await Task.Delay(TimeSpan.FromSeconds(2), token);
        if (_consumer?.IsCompleted == true)
        {
            await _consumer;
            throw new IOException("ETW consumer stopped before the capture completed.");
        }
    }

    internal async Task<CaptureSnapshot> DrainBytesAsync(CaptureSnapshot before, bool tcp, bool ipv6,
        long expectedSent, long expectedReceived, CancellationToken token)
    {
        var sentTarget = Count(before, tcp, ipv6, true) + expectedSent;
        var receivedTarget = Count(before, tcp, ipv6, false) + expectedReceived;
        var started = Stopwatch.GetTimestamp();
        CaptureSnapshot snapshot;
        do
        {
            _session.Flush();
            await Task.Delay(250, token);
            if (_consumer?.IsCompleted == true)
            {
                await _consumer;
                throw new IOException("ETW consumer stopped while draining workload bytes.");
            }
            snapshot = Snapshot();
            if (Count(snapshot, tcp, ipv6, true) >= sentTarget &&
                Count(snapshot, tcp, ipv6, false) >= receivedTarget) return snapshot;
        } while (Stopwatch.GetElapsedTime(started).TotalSeconds < 10);
        return snapshot;
    }

    internal static long Count(CaptureSnapshot snapshot, bool tcp, bool ipv6, bool send) =>
        snapshot.Rows.Where(row => row.Tcp == tcp && row.Ipv6 == ipv6 && row.Send == send).Sum(row => row.Bytes);

    internal CaptureSnapshot Snapshot()
    {
        lock (_gate)
            return new CaptureSnapshot(_ledger.Snapshot(DateTime.UtcNow), _session.EventsLost,
                Interlocked.Read(ref _networkEvents), _unattributedWorkerEvents, _payloadPidMismatches,
                _checkedPayloads.Count, Interlocked.Read(ref _processStarts), Interlocked.Read(ref _processStops),
                _zeroSizeWorkerEvents, _unsupportedWorkerEvents);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
        // StopTrace drains the consumer; never leave its exception unobserved.
        if (_consumer is not null && !_consumer.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("ETW consumer did not stop within ten seconds.");
    }
}

internal sealed record CaptureSnapshot(IReadOnlyList<NetworkLedgerRow> Rows, long EventsLost, long NetworkEvents,
    long UnattributedWorkerEvents, long PayloadPidMismatches, int PayloadKindsChecked, long WorkerStarts, long WorkerStops,
    long ZeroSizeWorkerEvents, long UnsupportedWorkerEvents);
