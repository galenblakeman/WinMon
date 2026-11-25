using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using MonCore.Monitoring;
using NUnit.Framework;

namespace WinMon.Mcp.Tests;

[TestFixture]
public sealed class ConnectionQueryCharacterizationTests
{
    private static readonly DateTimeOffset Origin = new(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);

    [TestCase("limit-zero")]
    [TestCase("limit-negative")]
    [TestCase("limit-high")]
    [TestCase("pid-zero")]
    [TestCase("pid-negative")]
    [TestCase("orphan-token")]
    [TestCase("orphan-domain")]
    [TestCase("orphan-both")]
    [TestCase("token-empty")]
    [TestCase("token-zero")]
    [TestCase("token-negative")]
    [TestCase("token-signed")]
    [TestCase("token-leading-zero")]
    [TestCase("token-space")]
    [TestCase("token-overflow")]
    [TestCase("domain-unknown")]
    [TestCase("domain-case")]
    [TestCase("domain-missing")]
    [TestCase("token-missing")]
    public async Task InvalidConnectionRequestsPrecedeCancellationAndCollection(string kind)
    {
        int? pid = kind.StartsWith("orphan", StringComparison.Ordinal) ? null : kind == "pid-zero" ? 0 : kind == "pid-negative" ? -1 : 10;
        var token = kind switch { "orphan-domain" or "token-missing" => null, "token-empty" => "", "token-zero" => "0", "token-negative" => "-1", "token-signed" => "+100", "token-leading-zero" => "0100", "token-space" => "100 ", "token-overflow" => "9223372036854775808", _ => "100" };
        var domain = kind switch { "orphan-token" or "domain-missing" => null, "domain-unknown" => "unknown", "domain-case" => "windowsfiletime", _ => "WindowsFileTime" };
        var limit = kind == "limit-zero" ? 0 : kind == "limit-negative" ? -1 : kind == "limit-high" ? 201 : 50;
        await using var owner = GuardedOwner(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var result = await new DiagnosticTools(owner).GetNetworkConnections(pid, token, domain, limit, cancellationToken: cancellation.Token);
        Assert.That(result.IsError, Is.True);
        Assert.That(result.StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo("invalidInput"));
        Assert.That(owner.ReadAfter(0), Is.Empty);
        Assert.That(owner.TrafficState, Is.EqualTo("NotStarted"));
    }

    [TestCase("all")]
    [TestCase("pid")]
    [TestCase("windows")]
    [TestCase("unix")]
    public async Task ValidCancelledConnectionRequestsNeverCreateCollectors(string kind)
    {
        await using var owner = GuardedOwner(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var exception = Assert.ThrowsAsync<TaskCanceledException>(async () => await new DiagnosticTools(owner).GetNetworkConnections(
            kind == "all" ? null : 10, kind is "windows" or "unix" ? "100" : null,
            kind == "windows" ? "WindowsFileTime" : kind == "unix" ? "UnixMicroseconds" : null,
            limit: 200, cancellationToken: cancellation.Token));
        Assert.That(exception!.CancellationToken.IsCancellationRequested, Is.True);
        Assert.That(owner.ReadAfter(0), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EndpointOptInPreservesRowsProtocolsAndOpaqueLifetimes(bool endpoints)
    {
        await using var harness = await Harness.Create(); await harness.Publish(Processes(), Connections());
        var json = (await harness.Tools.GetNetworkConnections(includeEndpoints: endpoints)).StructuredContent!.Value;
        var data = json.GetProperty("data"); var entries = data.GetProperty("connections").EnumerateArray().ToArray();
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("ok"));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(4));
        Assert.That(data.GetProperty("endpointsIncluded").GetBoolean(), Is.EqualTo(endpoints));
        Assert.That(data.GetProperty("partiallyAvailable").GetBoolean(), Is.True);
        Assert.That(entries.Select(e => e.GetProperty("pid").GetInt32()), Is.EqualTo(new[] { 10, 10, 20, 30 }));
        Assert.That(entries.Select(e => e.GetProperty("protocol").GetString()), Is.EqualTo(new[] { "Tcp", "Tcp", "Udp", "Tcp" }));
        Assert.That(entries[0].GetProperty("state").GetString(), Is.EqualTo("Established"));
        Assert.That(entries[2].GetProperty("state").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(entries[0].GetProperty("identity").GetProperty("startToken").GetString(), Is.EqualTo("100"));
        Assert.That(entries[2].GetProperty("identity").GetProperty("domain").GetString(), Is.EqualTo("WindowsFileTime"));
        Assert.That(entries[3].GetProperty("identity").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(entries[0].GetProperty("name").GetString(), Is.EqualTo("owner"));
        Assert.That(entries.Skip(1).Select(e => e.GetProperty("name").ValueKind), Is.All.EqualTo(JsonValueKind.Null));
        Assert.That(json.GetRawText(), Does.Not.Contain("private-path").And.Not.Contain("unix-owner"));
        if (endpoints)
        {
            Assert.That(entries[0].GetProperty("localAddress").GetString(), Is.EqualTo("10.1.2.3"));
            Assert.That(entries[0].GetProperty("localPort").GetInt32(), Is.EqualTo(1234));
            Assert.That(entries[0].GetProperty("remoteAddress").GetString(), Is.EqualTo("192.168.1.2"));
            Assert.That(entries[0].GetProperty("remotePort").GetInt32(), Is.EqualTo(443));
            Assert.That(entries[1].GetProperty("localAddress").GetString(), Is.EqualTo("fe80::1%3"));
            Assert.That(entries[1].GetProperty("remoteAddress").GetString(), Is.EqualTo("2001:db8::2"));
            Assert.That(entries[2].GetProperty("remoteAddress").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(entries[2].GetProperty("remotePort").ValueKind, Is.EqualTo(JsonValueKind.Null));
        }
        else
        {
            foreach (var entry in entries) foreach (var field in new[] { "localAddress", "localPort", "remoteAddress", "remotePort" })
                Assert.That(entry.GetProperty(field).ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(json.GetRawText(), Does.Not.Contain("10.1.2.3").And.Not.Contain("192.168.1.2").And.Not.Contain("2001:db8"));
        }
    }

    [TestCase("all", "ok", "10,10,20,30")]
    [TestCase("pid", "ok", "10")]
    [TestCase("exact", "ok", "10")]
    [TestCase("reused", "identityMismatch", "")]
    [TestCase("domain", "identityMismatch", "")]
    [TestCase("missing", "notObserved", "")]
    [TestCase("unix-pid", "ok", "")]
    [TestCase("unix-pin", "ok", "")]
    [TestCase("unknown-token", "ok", "")]
    public async Task ConnectionPinsNeverFollowReusedOrCrossDomainOwners(string kind, string status, string expected)
    {
        await using var harness = await Harness.Create(); await harness.Publish(Processes(), Connections());
        int? pid = kind == "all" ? null : kind.StartsWith("unix", StringComparison.Ordinal) ? 20 : kind == "missing" ? 99 : kind == "unknown-token" ? 30 : 10;
        var token = kind is "exact" or "domain" ? "100" : kind == "reused" ? "99" : kind == "unix-pin" ? "200" : null;
        var domain = kind is "domain" or "unix-pin" ? "UnixMicroseconds" : token is null ? null : "WindowsFileTime";
        var json = (await harness.Tools.GetNetworkConnections(pid, token, domain)).StructuredContent!.Value;
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo(status));
        if (status != "ok")
        {
            Assert.That(json.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.Zero);
        }
        else
        {
            var entries = json.GetProperty("data").GetProperty("connections").EnumerateArray().ToArray();
            Assert.That(entries.Select(e => e.GetProperty("pid").GetInt32().ToString()), Is.EqualTo(expected.Length == 0 ? [] : expected.Split(',')));
            if (kind is "pid" or "exact") Assert.That(entries[0].GetProperty("identity").GetProperty("startToken").GetString(), Is.EqualTo("100"));
        }
    }

    [TestCase(1L)]
    [TestCase(9007199254740993L)]
    [TestCase(long.MaxValue)]
    public async Task ConnectionStartTokensStayOpaqueAtAcceptedBoundaries(long token)
    {
        await using var harness = await Harness.Create();
        await harness.Publish([Process(1, token, "owner")], new NetworkConnectionsFrame(
            [new NetworkConnection(1, token, TransportProtocol.Udp, new(IPAddress.Loopback, 1234))],
            NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success));
        var canonical = token.ToString(CultureInfo.InvariantCulture);
        var json = (await harness.Tools.GetNetworkConnections(1, canonical, "WindowsFileTime")).StructuredContent!.Value;
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("ok"));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(1));
        var value = json.GetProperty("data").GetProperty("connections")[0].GetProperty("identity").GetProperty("startToken");
        Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(value.GetString(), Is.EqualTo(canonical));
    }

    [TestCase("reused", "identityMismatch")]
    [TestCase("domain", "identityMismatch")]
    [TestCase("missing", "notObserved")]
    public async Task ConnectionIdentityFailuresPrecedeFamilyUnavailability(string kind, string expected)
    {
        await using var harness = await Harness.Create();
        await harness.Publish(Processes(), Connections() with { Ipv4 = new(false, 50) });
        var json = (await harness.Tools.GetNetworkConnections(kind == "missing" ? 99 : 10,
            kind == "reused" ? "99" : "100", kind == "domain" ? "UnixMicroseconds" : "WindowsFileTime")).StructuredContent!.Value;
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo(expected));
        Assert.That(json.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.Zero);
    }

    [TestCase(1, 0, false)]
    [TestCase(1, 1, false)]
    [TestCase(1, 2, true)]
    [TestCase(200, 199, false)]
    [TestCase(200, 200, false)]
    [TestCase(200, 201, true)]
    public async Task ConnectionLimitsPreserveInputOrderAndExactTruncation(int limit, int count, bool truncated)
    {
        await using var harness = await Harness.Create(); var rows = ManyRows(count);
        await harness.Publish([], new NetworkConnectionsFrame(rows, NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success));
        var json = (await harness.Tools.GetNetworkConnections(limit: limit)).StructuredContent!.Value;
        Assert.That(json.GetProperty("data").GetProperty("connections").EnumerateArray().Select(e => e.GetProperty("pid").GetInt32()), Is.EqualTo(rows.Take(limit).Select(r => r.ProcessId)));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(Math.Min(count, limit)));
        Assert.That(json.GetProperty("truncated").GetBoolean(), Is.EqualTo(truncated));
    }

    [TestCase("boundary", false)]
    [TestCase("long", true)]
    [TestCase("surrogate", true)]
    [TestCase("control", false)]
    [TestCase("empty", false)]
    public async Task ConnectionOwnerNamesKeepTextAndTruncationContracts(string kind, bool truncated)
    {
        var name = kind == "empty" ? "" : new string('n', kind is "control" or "surrogate" ? 511 : kind == "boundary" ? 512 : 513) + (kind == "surrogate" ? "😀" : kind == "control" ? "\n" : "");
        var expected = kind is "control" or "surrogate" ? new string('n', 511) : name[..Math.Min(name.Length, 512)];
        await using var harness = await Harness.Create(); await harness.Publish([Process(10, 100, name)], Connections());
        var row = (await harness.Tools.GetNetworkConnections(pid: 10)).StructuredContent!.Value.GetProperty("data").GetProperty("connections")[0];
        Assert.That(row.GetProperty("name").GetString(), Is.EqualTo(expected));
        Assert.That(row.GetProperty("nameTruncated").GetBoolean(), Is.EqualTo(truncated));
    }

    [TestCase("missing")]
    [TestCase("empty")]
    [TestCase("partial")]
    [TestCase("partial-v6")]
    [TestCase("failed-with-rows")]
    public async Task ConnectionFamilyAvailabilityDoesNotDiscardRetainedRows(string kind)
    {
        var frame = kind == "missing" ? null : kind == "empty" ? new NetworkConnectionsFrame([], NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success)
            : kind == "partial" ? Connections() : kind == "partial-v6" ? Connections() with { Ipv4 = new(false, 50), Ipv6 = NetworkAddressFamilyStatus.Success }
            : Connections() with { Ipv4 = new(false, 50) };
        await using var harness = await Harness.Create(); await harness.Publish(Processes(), frame);
        var json = (await harness.Tools.GetNetworkConnections()).StructuredContent!.Value; var data = json.GetProperty("data");
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo(kind is "missing" or "failed-with-rows" ? "unavailable" : "ok"));
        Assert.That(data.GetProperty("available").GetBoolean(), Is.EqualTo(kind is "empty" or "partial" or "partial-v6"));
        Assert.That(data.GetProperty("partiallyAvailable").GetBoolean(), Is.EqualTo(kind is "partial" or "partial-v6"));
        Assert.That(data.GetProperty("connections").GetArrayLength(), Is.EqualTo(kind is "partial" or "partial-v6" or "failed-with-rows" ? 4 : 0));
        if (kind == "missing") Assert.That(data.GetProperty("ipv4").ValueKind, Is.EqualTo(JsonValueKind.Null));
        else
        {
            Assert.That(data.GetProperty("ipv4").GetProperty("available").GetBoolean(), Is.EqualTo(kind is "empty" or "partial"));
            Assert.That(data.GetProperty("ipv6").GetProperty("available").GetBoolean(), Is.EqualTo(kind is "empty" or "partial-v6"));
            if (kind is "partial" or "failed-with-rows") Assert.That(data.GetProperty("ipv6").GetProperty("errorCode").GetInt32(), Is.EqualTo(5));
            if (kind is "partial-v6" or "failed-with-rows") Assert.That(data.GetProperty("ipv4").GetProperty("errorCode").GetInt32(), Is.EqualTo(50));
        }
    }

    [Test]
    public async Task StaleConnectionsPrecedeIdentityMismatchAndWithholdEndpoints()
    {
        await using var harness = await Harness.Create(); await harness.Publish(Processes(), Connections()); harness.Clock.Advance(TimeSpan.FromSeconds(3));
        var json = (await harness.Tools.GetNetworkConnections(10, "99", "WindowsFileTime", includeEndpoints: true)).StructuredContent!.Value;
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("unavailable"));
        Assert.That(json.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(json.GetRawText(), Does.Not.Contain("10.1.2.3"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingOrCancelledConnectionWarmupReleasesRequests(bool cancel)
    {
        await using var harness = await Harness.Create(false); using var token = new CancellationTokenSource();
        var request = harness.Tools.GetNetworkConnections(cancellationToken: token.Token);
        await harness.Clock.CurrentCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancel) { token.Cancel(); Assert.That(async () => await request, Throws.InstanceOf<OperationCanceledException>()); }
        else { harness.Clock.Advance(TimeSpan.FromSeconds(5)); var json = (await request.WaitAsync(TimeSpan.FromSeconds(5))).StructuredContent!.Value; Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("unavailable")); Assert.That(json.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null)); }
        await harness.Publish(Processes(), Connections()); await harness.Publish(Processes(), Connections());
        Assert.That((await harness.Tools.GetNetworkConnections()).StructuredContent!.Value.GetProperty("returnedCount").GetInt32(), Is.EqualTo(4));
        await harness.ReleasePin(); await harness.Clock.IdleCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    internal static async Task<(Func<object> Invoke, IAsyncDisposable Owner)> CreateBenchmarkRequest(string scenario)
    {
        if (scenario == "invalid") { var owner = GuardedOwner(); return (() => new DiagnosticTools(owner).GetNetworkConnections(limit: 0).GetAwaiter().GetResult(), owner); }
        var harness = await Harness.Create();
        try
        {
            await harness.Publish(Processes(), scenario == "limited" ? new NetworkConnectionsFrame(ManyRows(201), NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success)
                : scenario == "unavailable" ? Connections() with { Ipv4 = new(false, 50) } : Connections());
            harness.Clock.SystemTimers = true;
            return (() => harness.Tools.GetNetworkConnections(scenario is "pinned" or "mismatch" ? 10 : null,
                scenario == "pinned" ? "100" : scenario == "mismatch" ? "99" : null,
                scenario is "pinned" or "mismatch" ? "WindowsFileTime" : null, limit: 200,
                includeEndpoints: scenario == "disclosed").GetAwaiter().GetResult(), harness);
        }
        catch { await harness.DisposeAsync(); throw; }
    }

    private static SamplingOwner GuardedOwner() => new(() => throw new AssertionException("Unexpected native collector."), new Clock(), createTraffic: () => throw new AssertionException("Unexpected traffic."));
    private static ProcessUsage Process(int pid, long token, string name, ProcessLifetimeDomain domain = ProcessLifetimeDomain.WindowsFileTime) => new(pid, token, name, 1) { ImagePath = @"C:\private-path\secret.exe", LifetimeDomain = domain };
    private static ProcessUsage[] Processes() => [Process(10, 100, "owner"), Process(20, 200, "unix-owner", ProcessLifetimeDomain.UnixMicroseconds), Process(30, 300, "other")];
    private static NetworkConnectionsFrame Connections() => new([
        new(10, 100, TransportProtocol.Tcp, new(IPAddress.Parse("10.1.2.3"), 1234), new(IPAddress.Parse("192.168.1.2"), 443), TcpState.Established),
        new(10, 99, TransportProtocol.Tcp, new(IPAddress.Parse("fe80::1%3"), 2345), new(IPAddress.Parse("2001:db8::2"), 443), TcpState.Established),
        new(20, 200, TransportProtocol.Udp, new(IPAddress.Parse("2001:db8::1"), 5353)),
        new(30, null, TransportProtocol.Tcp, new(IPAddress.Loopback, 80), State: TcpState.Listen)
    ], NetworkAddressFamilyStatus.Success, new(false, 5));
    private static NetworkConnection[] ManyRows(int count) => Enumerable.Range(0, count).Select(i => new NetworkConnection(1000 - i, i + 1, TransportProtocol.Udp, new(IPAddress.Loopback, 1234))).ToArray();

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
    private sealed class Harness : IAsyncDisposable
    {
        private readonly Source _source = new();
        private IAsyncDisposable? _lease;
        internal Clock Clock { get; } = new();
        internal SamplingOwner Owner { get; }
        internal DiagnosticTools Tools { get; }
        private Harness() { Owner = new SamplingOwner(() => _source, Clock, TimeSpan.FromMilliseconds(1)); Tools = new DiagnosticTools(Owner); }
        internal static async Task<Harness> Create(bool warm = true)
        {
            var result = new Harness();
            try { result._lease = await result.Owner.AcquireAsync(default); if (warm) { await result.Publish([], null); await result.Publish([], null); } return result; }
            catch { await result.DisposeAsync(); throw; }
        }
        internal async Task Publish(ProcessUsage[] processes, NetworkConnectionsFrame? connections)
        {
            var sequence = Owner.ReadAfter(0).LastOrDefault()?.Sequence ?? 0;
            Clock.Advance(TimeSpan.FromSeconds(1));
            var sample = new MonitorSample(Clock.GetUtcNow(), 100, processes, 0) { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 };
            _source.Frames.Add(new CapturedFrame(sample) { Connections = connections });
            await Owner.WaitForChangeAsync(sequence, default).WaitAsync(TimeSpan.FromSeconds(5));
        }
        internal async Task ReleasePin() { if (_lease is not null) { await _lease.DisposeAsync(); _lease = null; } }
        public async ValueTask DisposeAsync()
        {
            var stop = Owner.StopAsync(default); _source.Frames.CompleteAdding(); await stop.WaitAsync(TimeSpan.FromSeconds(5));
            if (_lease is not null) await _lease.DisposeAsync(); await Owner.DisposeAsync();
            _source.Frames.Dispose();
        }
    }
}
