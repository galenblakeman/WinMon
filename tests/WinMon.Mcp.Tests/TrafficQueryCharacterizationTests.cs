using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol.Protocol;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace WinMon.Mcp.Tests;

[TestFixture]
public sealed class TrafficQueryCharacterizationTests
{
    private static readonly DateTimeOffset Origin = new(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);

    [TestCase("sort-null")]
    [TestCase("sort-case")]
    [TestCase("sort-unknown")]
    [TestCase("limit-zero")]
    [TestCase("limit-negative")]
    [TestCase("limit-high")]
    [TestCase("pid-zero")]
    [TestCase("pid-negative")]
    [TestCase("token-zero")]
    [TestCase("token-leading-zero")]
    [TestCase("token-negative")]
    [TestCase("token-overflow")]
    [TestCase("domain-case")]
    [TestCase("domain-unknown")]
    [TestCase("token-only")]
    [TestCase("domain-only")]
    [TestCase("orphan-token")]
    [TestCase("orphan-domain")]
    public async Task InvalidRequestsPrecedeCancellationAndNeverAcquireOrSubscribe(string kind)
    {
        var sort = kind == "sort-null" ? null! : kind == "sort-case" ? "RECEIVE" : kind == "sort-unknown" ? "unknown" : "receive";
        var limit = kind == "limit-zero" ? 0 : kind == "limit-negative" ? -1 : kind == "limit-high" ? 201 : 20;
        int? pid = kind.StartsWith("pid-", StringComparison.Ordinal) || kind.StartsWith("token-", StringComparison.Ordinal) || kind.StartsWith("domain-", StringComparison.Ordinal) ? 10 : null;
        string? token = pid is null ? null : "100", domain = pid is null ? null : "WindowsFileTime";
        switch (kind)
        {
            case "pid-zero": pid = 0; break;
            case "pid-negative": pid = -1; break;
            case "token-zero": token = "0"; break;
            case "token-leading-zero": token = "0100"; break;
            case "token-negative": token = "-1"; break;
            case "token-overflow": token = "9223372036854775808"; break;
            case "domain-case": domain = "windowsfiletime"; break;
            case "domain-unknown": domain = "unknown"; break;
            case "token-only": domain = null; break;
            case "domain-only": token = null; break;
            case "orphan-token": token = "100"; break;
            case "orphan-domain": domain = "WindowsFileTime"; break;
        }
        await using var owner = GuardedOwner();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var tools = new DiagnosticTools(owner);
        var result = await tools.GetProcessNetworkTraffic(sort, limit, pid, token, domain, cancelled.Token);
        Assert.That(result.IsError, Is.True);
        Assert.That(result.StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo("invalidInput"));
        Assert.That(owner.ReadAfter(0), Is.Empty);
        Assert.That(owner.TrafficState, Is.EqualTo("NotStarted"));
    }

    [TestCase("receive")]
    [TestCase("send")]
    [TestCase("received")]
    [TestCase("sent")]
    public async Task ValidCancellationNeverCreatesCollectorOrConnection(string sort)
    {
        await using var owner = GuardedOwner();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var tools = new DiagnosticTools(owner);
        var exception = Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await tools.GetProcessNetworkTraffic(sort, cancellationToken: cancelled.Token));
        Assert.That(exception!.CancellationToken, Is.EqualTo(cancelled.Token));
        Assert.That(owner.ReadAfter(0), Is.Empty);
        Assert.That(owner.TrafficState, Is.EqualTo("NotStarted"));
    }

    [TestCase("receive", "10,40,30,20")]
    [TestCase("send", "30,20,10,40")]
    [TestCase("received", "30,10,40,20")]
    [TestCase("sent", "10,40,20,30")]
    public async Task AllTrafficSortsPreserveNullOrderingPidTiesAndSessionIdentity(string sort, string expected)
    {
        await using var harness = await Harness.Create();
        await harness.Prepare(Rows());
        var response = await harness.Tools.GetProcessNetworkTraffic(sort);
        var json = response.StructuredContent!.Value;
        var data = json.GetProperty("data");
        Assert.That(data.GetProperty("processes").EnumerateArray().Select(Pid), Is.EqualTo(expected.Split(',').Select(int.Parse)));
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("ok"));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(4));
        Assert.That(json.GetProperty("truncated").GetBoolean(), Is.False);
        Assert.That(data.GetProperty("sessionId").GetGuid(), Is.EqualTo(harness.Client.Sample.SessionId).And.Not.EqualTo(Guid.Empty));
        Assert.That(data.GetProperty("serviceState").GetString(), Is.EqualTo("Active"));
        Assert.That(data.GetProperty("scope").GetString(), Is.EqualTo("All adapters; observed bytes since this server subscribed; delayed five-second rates"));
        var byPid = data.GetProperty("processes").EnumerateArray().ToDictionary(Pid);
        Assert.That(byPid[30].GetProperty("receivedBytes").GetInt64(), Is.EqualTo(300));
        Assert.That(byPid[10].GetProperty("sentBytes").GetInt64(), Is.EqualTo(300));
        Assert.That(byPid[30].GetProperty("receiveBytesPerSecond").GetDouble(), Is.Zero);
        Assert.That(byPid[20].GetProperty("receiveBytesPerSecond").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(byPid[40].GetProperty("sendBytesPerSecond").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(json.GetRawText(), Does.Not.Contain("private-secret").And.Not.Contain("Endpoint").And.Not.Contain("imagePath"));
        Assert.That(harness.Connects, Is.EqualTo(1));
        Assert.That(harness.Connection.Subscriptions, Is.EqualTo(1));
    }

    [Test]
    public async Task LimitBoundariesAndPidOnlySelectionPreserveTruncation()
    {
        await using var harness = await Harness.Create();
        await harness.Prepare(Enumerable.Range(1, 201).Select(i => new ProcessNetworkReading(i, i * 10, i, i * 2, i, i * 2)).ToArray());
        var json = (await harness.Tools.GetProcessNetworkTraffic(limit: 200)).StructuredContent!.Value;
        var rows = json.GetProperty("data").GetProperty("processes");
        Assert.That(rows.GetArrayLength(), Is.EqualTo(200));
        Assert.That(rows.EnumerateArray().Select(Pid), Is.EqualTo(Enumerable.Range(2, 200).Reverse()));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(200));
        Assert.That(json.GetProperty("truncated").GetBoolean(), Is.True);
        var first = (await harness.Tools.GetProcessNetworkTraffic(limit: 1)).StructuredContent!.Value;
        Assert.That(Pid(first.GetProperty("data").GetProperty("processes")[0]), Is.EqualTo(201));
        Assert.That(first.GetProperty("truncated").GetBoolean(), Is.True);
        var pinned = (await harness.Tools.GetProcessNetworkTraffic(pid: 10)).StructuredContent!.Value;
        Assert.That(pinned.GetProperty("returnedCount").GetInt32(), Is.EqualTo(1));
        Assert.That(Pid(pinned.GetProperty("data").GetProperty("processes")[0]), Is.EqualTo(10));
        Assert.That(pinned.GetProperty("truncated").GetBoolean(), Is.False);
    }

    [Test]
    public async Task ExactPinsMismatchBeforeUnavailableAndMissingPidRemainsUnobserved()
    {
        await using var harness = await Harness.Create();
        await harness.Prepare([new(10, 100, 200, 300, 20, 30), new(10, 99, 50, 60, 5, 6)]);
        var pinned = (await harness.Tools.GetProcessNetworkTraffic(pid: 10, expectedStartToken: "100", expectedDomain: "WindowsFileTime")).StructuredContent!.Value;
        Assert.That(pinned.GetProperty("returnedCount").GetInt32(), Is.EqualTo(1));
        Assert.That(pinned.GetProperty("data").GetProperty("processes")[0].GetProperty("identity").GetProperty("startToken").GetString(), Is.EqualTo("100"));
        var unqualified = (await harness.Tools.GetProcessNetworkTraffic(pid: 10)).StructuredContent!.Value;
        Assert.That(unqualified.GetProperty("returnedCount").GetInt32(), Is.EqualTo(2));
        foreach (var domain in new[] { "WindowsFileTime", "UnixMicroseconds" })
        {
            var result = await harness.Tools.GetProcessNetworkTraffic(pid: 10, expectedStartToken: domain == "WindowsFileTime" ? "999" : "100", expectedDomain: domain);
            Assert.That(result.IsError, Is.True);
            Assert.That(result.StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo("identityMismatch"));
            Assert.That(result.StructuredContent.Value.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
        }
        var missing = (await harness.Tools.GetProcessNetworkTraffic(pid: 999, expectedStartToken: "100", expectedDomain: "WindowsFileTime")).StructuredContent!.Value;
        Assert.That(missing.GetProperty("status").GetString(), Is.EqualTo("notObserved"));
        Assert.That(missing.GetProperty("returnedCount").GetInt32(), Is.Zero);
        await harness.FailService();
        var mismatch = (await harness.Tools.GetProcessNetworkTraffic(pid: 10, expectedStartToken: "999", expectedDomain: "WindowsFileTime")).StructuredContent!.Value;
        Assert.That(mismatch.GetProperty("status").GetString(), Is.EqualTo("identityMismatch"));
        var unavailable = (await harness.Tools.GetProcessNetworkTraffic()).StructuredContent!.Value;
        Assert.That(unavailable.GetProperty("status").GetString(), Is.EqualTo("unavailable"));
        Assert.That(unavailable.GetProperty("data").GetProperty("processes").EnumerateArray().Sum(row => row.GetProperty("receivedBytes").GetInt64()), Is.EqualTo(250));
        Assert.That(unavailable.GetProperty("data").GetProperty("processes").EnumerateArray().All(row => row.GetProperty("receiveBytesPerSecond").ValueKind == JsonValueKind.Null && row.GetProperty("sendBytesPerSecond").ValueKind == JsonValueKind.Null), Is.True);
    }

    [TestCase(ProcessNetworkQuality.None, "Active", true)]
    [TestCase(ProcessNetworkQuality.Incomplete, "Incomplete", true)]
    [TestCase(ProcessNetworkQuality.Provisional, "Active", true)]
    [TestCase(ProcessNetworkQuality.CapacityExceeded, "Incomplete", true)]
    [TestCase(ProcessNetworkQuality.WarmingUp, "WarmingUp", false)]
    public async Task QualityFlagsKeepStateTotalsAndRateAvailability(ProcessNetworkQuality quality, string state, bool rates)
    {
        await using var harness = await Harness.Create();
        await harness.Prepare([new(10, 100, 200, 300, 20, 30)], quality);
        var json = (await harness.Tools.GetProcessNetworkTraffic()).StructuredContent!.Value;
        var data = json.GetProperty("data"); var row = data.GetProperty("processes")[0];
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("ok"));
        Assert.That(data.GetProperty("serviceState").GetString(), Is.EqualTo(state));
        Assert.That((ProcessNetworkQuality)data.GetProperty("quality").GetInt32() & quality, Is.EqualTo(quality));
        Assert.That(row.GetProperty("receivedBytes").GetInt64(), Is.EqualTo(200));
        Assert.That(row.GetProperty("sentBytes").GetInt64(), Is.EqualTo(300));
        if (rates) Assert.That(row.GetProperty("receiveBytesPerSecond").GetDouble(), Is.EqualTo(20));
        else Assert.That(row.GetProperty("receiveBytesPerSecond").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [TestCase("cached")]
    [TestCase("unknown")]
    [TestCase("other-domain")]
    [TestCase("name-512")]
    [TestCase("name-513")]
    public async Task OwnerMetadataKeepsRichCachePrecedenceDomainAndTextBounds(string mode)
    {
        var name = mode == "name-512" ? new string('n', 512) : mode == "name-513" ? new string('n', 513) : "cached owner";
        var owner = new ProcessUsage(10, 100, name, 1) { ImagePath = @"C:\private-secret\worker.exe",
            LifetimeDomain = mode == "other-domain" ? ProcessLifetimeDomain.UnixMicroseconds : ProcessLifetimeDomain.WindowsFileTime };
        await using var harness = await Harness.Create(mode == "unknown" ? [] : [owner]);
        await harness.Prepare([new(10, 100, 200, 300, 20, 30)], owners: mode == "cached" ? [owner with { Name = "current incomplete name", ImagePath = "" }] : mode == "unknown" ? [] : [owner]);
        var json = (await harness.Tools.GetProcessNetworkTraffic()).StructuredContent!.Value;
        var row = json.GetProperty("data").GetProperty("processes")[0];
        if (mode is "unknown" or "other-domain") Assert.That(row.GetProperty("name").ValueKind, Is.EqualTo(JsonValueKind.Null));
        else Assert.That(row.GetProperty("name").GetString(), Is.EqualTo(name[..Math.Min(name.Length, 512)]));
        Assert.That(row.GetProperty("nameTruncated").GetBoolean(), Is.EqualTo(mode == "name-513"));
        Assert.That(row.GetProperty("identity").GetProperty("domain").GetString(), Is.EqualTo("WindowsFileTime"));
        Assert.That(json.GetRawText(), Does.Not.Contain("private-secret").And.Not.Contain("imagePath"));
    }

    [Test]
    public async Task FullFrameFallbackWorksWhenOwnerCacheIsFull()
    {
        var owners = Enumerable.Range(1, 8192).Select(i => new ProcessUsage(i, i, "cached", 1)).ToArray();
        await using var harness = await Harness.Create(owners);
        await harness.Prepare([new(9001, 90010, 2, 3, 4, 5)], owners: [new ProcessUsage(9001, 90010, "full frame fallback", 1)]);
        var json = (await harness.Tools.GetProcessNetworkTraffic()).StructuredContent!.Value;
        Assert.That(json.GetProperty("data").GetProperty("processes")[0].GetProperty("name").GetString(), Is.EqualTo("full frame fallback"));
        Assert.That((ProcessNetworkQuality)json.GetProperty("data").GetProperty("quality").GetInt32() & ProcessNetworkQuality.CapacityExceeded, Is.EqualTo(ProcessNetworkQuality.CapacityExceeded));
    }

    [Test]
    public async Task StalePublicationDoesNotReturnTrafficData()
    {
        await using var harness = await Harness.Create();
        await harness.Prepare(Rows());
        harness.Clock.Advance(TimeSpan.FromSeconds(3));
        var json = (await harness.Tools.GetProcessNetworkTraffic()).StructuredContent!.Value;
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("unavailable"));
        Assert.That(json.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(harness.Connects, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingOrCancelledWarmupReleasesRequestsForTheNextCall(bool cancel)
    {
        await using var harness = await Harness.Create(warm: false);
        using var cancellation = new CancellationTokenSource();
        var request = harness.Tools.GetProcessNetworkTraffic(cancellationToken: cancellation.Token);
        await harness.Clock.CurrentCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancel)
        {
            cancellation.Cancel();
            Assert.That(async () => await request, Throws.InstanceOf<OperationCanceledException>());
        }
        else
        {
            harness.Clock.Advance(TimeSpan.FromSeconds(5));
            var json = (await request.WaitAsync(TimeSpan.FromSeconds(5))).StructuredContent!.Value;
            Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("unavailable"));
            Assert.That(json.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
        }
        await harness.Publish([]); await harness.Publish([]);
        var next = (await harness.Tools.GetProcessNetworkTraffic()).StructuredContent!.Value;
        Assert.That(next.GetProperty("data").GetProperty("serviceState").GetString(), Is.EqualTo("WarmingUp"));
        Assert.That(harness.Connects, Is.EqualTo(1));
        await harness.ReleasePinnedLease();
        await harness.Clock.IdleCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static SamplingOwner GuardedOwner() => new(() => throw new AssertionException("Unexpected collector."), new Clock(),
        createTraffic: () => throw new AssertionException("Unexpected traffic subscription."));
    private static int Pid(JsonElement row) => row.GetProperty("identity").GetProperty("pid").GetInt32();
    private static ProcessNetworkReading[] Rows() => [new(30, 300, 300, 100, 0, 30), new(10, 100, 200, 300, 20, 0), new(20, 200, 100, 200, null, 20), new(40, 400, 200, 300, 20, null)];

    internal static async Task<(Func<object> Invoke, IAsyncDisposable Owner, string Session)> CreateBenchmarkRequest(string scenario)
    {
        if (scenario == "invalid")
        {
            var owner = GuardedOwner(); var tools = new DiagnosticTools(owner);
            return (() => tools.GetProcessNetworkTraffic(limit: 0).GetAwaiter().GetResult(), owner, Guid.Empty.ToString());
        }
        var harness = await Harness.Create();
        try
        {
            if (scenario != "warm")
                await harness.Prepare(scenario == "limited" ? Enumerable.Range(1, 201).Select(i => new ProcessNetworkReading(i, i * 10, i, i * 2, i, i * 2)).ToArray() : Rows());
            if (scenario == "unavailable") await harness.FailService();
            harness.Clock.SystemTimers = true;
            return (() => harness.Tools.GetProcessNetworkTraffic(limit: scenario == "limited" ? 200 : 20,
                pid: scenario == "mismatch" ? 10 : null, expectedStartToken: scenario == "mismatch" ? "999" : null,
                expectedDomain: scenario == "mismatch" ? "WindowsFileTime" : null).GetAwaiter().GetResult(), harness, harness.Client.Sample.SessionId.ToString());
        }
        catch { await harness.DisposeAsync(); throw; }
    }

    internal static string Fingerprint(object value, string expectedSession)
    {
        var result = (CallToolResult)value;
        var data = result.StructuredContent!.Value.GetProperty("data");
        var json = JsonSerializer.Serialize(result, Responses.Json);
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("sessionId", out var session))
        {
            Assert.That(session.GetGuid(), Is.EqualTo(Guid.Parse(expectedSession)).And.Not.EqualTo(Guid.Empty));
            Assert.That(json.Split(expectedSession, StringSplitOptions.None), Has.Length.EqualTo(3), "Session appears once in each protocol representation.");
            return json.Replace(expectedSession, Guid.Empty.ToString(), StringComparison.Ordinal);
        }
        return json;
    }

    private sealed class Clock : TimeProvider
    {
        private readonly SamplingOwnerTests.ManualClock _inner = new();
        internal bool SystemTimers { get; set; }
        internal TaskCompletionSource CurrentCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource IdleCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override long TimestampFrequency => _inner.TimestampFrequency;
        public override long GetTimestamp() => _inner.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => Origin.AddTicks(GetTimestamp());
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            // Timed repeated queries must release disposed timers instead of retaining the manual helper's timer history.
            if (SystemTimers) return System.CreateTimer(callback, state, dueTime, period);
            var timer = _inner.CreateTimer(callback, state, dueTime, period);
            if (dueTime == TimeSpan.FromSeconds(5)) CurrentCreated.TrySetResult();
            if (dueTime == TimeSpan.FromSeconds(30)) IdleCreated.TrySetResult();
            return timer;
        }
        internal void Advance(TimeSpan elapsed) => _inner.Advance(elapsed);
    }
    private sealed class Source : IFrameSource
    {
        internal readonly BlockingCollection<CapturedFrame> Frames = new();
        public CapturedFrame Capture(SamplingOptions options) => Frames.Take();
        public void Reset() { }
        // The harness owns the queue until sampling has joined.
        public void Dispose() { }
    }
    private sealed class Connection : INetworkTrafficConnection
    {
        internal readonly Channel<ProcessNetworkSnapshot> Snapshots = Channel.CreateUnbounded<ProcessNetworkSnapshot>();
        internal readonly SemaphoreSlim Requests = new(0);
        internal readonly TaskCompletionSource Disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Subscriptions;
        public Task SubscribeAsync(CancellationToken token) { Subscriptions++; return Task.CompletedTask; }
        public Task<ProcessNetworkSnapshot> SnapshotAsync(CancellationToken token) { Requests.Release(); return Snapshots.Reader.ReadAsync(token).AsTask(); }
        public ValueTask DisposeAsync() { Disposed.TrySetResult(); return ValueTask.CompletedTask; }
    }
    private sealed class Harness : IAsyncDisposable
    {
        private readonly Source _source = new();
        private readonly ProcessUsage[] _owners;
        private IAsyncDisposable? _lease;
        private long _sequence;
        internal Clock Clock { get; } = new();
        internal Connection Connection { get; } = new();
        internal NetworkTrafficClient Client { get; }
        internal SamplingOwner Owner { get; }
        internal DiagnosticTools Tools { get; }
        internal int Connects;
        private Harness(ProcessUsage[] owners)
        {
            _owners = owners;
            Client = new NetworkTrafficClient(_ => { Interlocked.Increment(ref Connects); return Task.FromResult<INetworkTrafficConnection>(Connection); },
                (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token), operationTimeout: TimeSpan.FromHours(1));
            Owner = new SamplingOwner(() => _source, Clock, TimeSpan.FromMilliseconds(1), () => Client);
            Tools = new DiagnosticTools(Owner);
        }
        internal static async Task<Harness> Create(ProcessUsage[]? owners = null, bool warm = true)
        {
            var result = new Harness(owners ?? Rows().Select(row => new ProcessUsage(row.ProcessId, row.CreationTicks, "owner-" + row.ProcessId, 1) { ImagePath = @"C:\private-secret\worker.exe" }).ToArray());
            try
            {
                result._lease = await result.Owner.AcquireAsync(default);
                if (warm)
                {
                    await result.Publish(result._owners); await result.Publish(result._owners);
                    result.Owner.EnableTraffic();
                    Assert.That(await result.Connection.Requests.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
                    await result.Publish(result._owners); await result.Publish(result._owners);
                }
                return result;
            }
            catch { await result.DisposeAsync(); throw; }
        }
        internal async Task Publish(ProcessUsage[] owners)
        {
            var sequence = Owner.ReadAfter(0).LastOrDefault()?.Sequence ?? 0;
            Clock.Advance(TimeSpan.FromTicks(1));
            _source.Frames.Add(new CapturedFrame(new MonitorSample(Clock.GetUtcNow(), 10, owners, 0) { IntervalSeconds = 1 }));
            await Owner.WaitForChangeAsync(sequence, default).WaitAsync(TimeSpan.FromSeconds(5));
        }
        internal async Task Prepare(ProcessNetworkReading[] rows, ProcessNetworkQuality quality = ProcessNetworkQuality.None, ProcessUsage[]? owners = null)
        {
            await Send(rows.Select(row => row with { ReceivedBytes = 1000, SentBytes = 2000 }).ToArray(), ProcessNetworkQuality.None, owners ?? _owners);
            await Send(rows.Select(row => row with { ReceivedBytes = row.ReceivedBytes + 1000, SentBytes = row.SentBytes + 2000 }).ToArray(), quality, owners ?? _owners);
        }
        private async Task Send(ProcessNetworkReading[] rows, ProcessNetworkQuality quality, ProcessUsage[] owners)
        {
            var now = Stopwatch.GetTimestamp();
            // Query-policy fixtures keep delivery jitter below the service-staleness boundary; clock policy has separate session tests.
            var snapshot = new ProcessNetworkSnapshot(Guid.Parse("11111111-2222-3333-4444-555555555555"), ++_sequence, now,
                long.MaxValue / 16, now - 1, now, quality, 0, 0, rows);
            await Connection.Snapshots.Writer.WriteAsync(snapshot);
            Assert.That(await Connection.Requests.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(Client.Sample.Snapshot, Is.SameAs(snapshot));
            await Publish(owners); await Publish(owners);
        }
        internal async Task FailService()
        {
            Connection.Snapshots.Writer.TryComplete(new IOException("controlled service loss"));
            await Connection.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Publish(_owners); await Publish(_owners);
        }
        internal async Task ReleasePinnedLease()
        {
            if (_lease is not null) { await _lease.DisposeAsync(); _lease = null; }
        }
        public async ValueTask DisposeAsync()
        {
            var stop = Owner.StopAsync(default); _source.Frames.CompleteAdding();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
            if (_lease is not null) await _lease.DisposeAsync();
            await Owner.DisposeAsync();
            _source.Frames.Dispose();
            Connection.Requests.Dispose();
        }
    }
}
