using System.Net;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using WinMon.Mcp;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class DiagnosticToolsTests
{
    private sealed class Source(CapturedFrame frame) : IFrameSource
    {
        internal CapturedFrame Frame = frame;
        public CapturedFrame Capture(SamplingOptions options) => Frame;
        public void Reset() { }
        public void Dispose() { }
    }

    private static ProcessUsage Process(int pid, long token, string name, double cpu) =>
        new(pid, token, name, cpu, 200, 300) { ImagePath = "C:\\private\\secret.exe", PrivateBytes = 100 };

    private static CapturedFrame Frame(params ProcessUsage[] processes) =>
        new(new MonitorSample(DateTimeOffset.UtcNow, 20, processes, 1)
        {
            IntervalSeconds = 1,
            Resources = new ResourceSample { UsedMemoryBytes = 100, TotalMemoryBytes = 200 }
        })
        {
            Connections = new NetworkConnectionsFrame(
                [new NetworkConnection(10, 9007199254740993, TransportProtocol.Tcp,
                    new IPEndPoint(IPAddress.Parse("10.1.2.3"), 1234), new IPEndPoint(IPAddress.Parse("192.168.1.2"), 443))],
                NetworkAddressFamilyStatus.Success, new NetworkAddressFamilyStatus(false, 5))
        };

    private static JsonElement Json(CallToolResult result) => result.StructuredContent!.Value;

    [Test]
    public async Task ListsFilteredSortedBoundedProcessesAndPreservesOpaqueIdentityWithoutSecrets()
    {
        var source = new Source(Frame(Process(10, 9007199254740993, "alpha", 15),
            Process(20, 2, "beta", 30), Process(30, 3, "alphabet", 25)));
        await using var owner = new SamplingOwner(() => source, TimeProvider.System, TimeSpan.FromMilliseconds(5));
        await owner.StartAsync(default);
        var tools = new DiagnosticTools(owner);
        var response = Json(await tools.ListProcesses(filter: "alpha", limit: 1));
        Assert.Multiple(() =>
        {
            Assert.That(response.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(response.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(response.GetProperty("data")[0].GetProperty("name").GetString(), Is.EqualTo("alphabet"));
            Assert.That(response.GetRawText(), Does.Not.Contain("secret.exe"));
        });
        var detail = Json(await tools.GetProcessDetails(10));
        Assert.That(detail.GetProperty("data").GetProperty("identity").GetProperty("startToken").GetString(),
            Is.EqualTo("9007199254740993"));
        Assert.That(Json(await tools.ListProcesses(filter: "absent")).GetProperty("data").GetArrayLength(), Is.Zero);
    }

    [Test]
    public async Task DetailsAndConnectionsRejectReusedPidAndMissingLifetimes()
    {
        await using var owner = new SamplingOwner(() => new Source(Frame(Process(10, 2, "new", 1))),
            TimeProvider.System, TimeSpan.FromMilliseconds(5));
        await owner.StartAsync(default);
        var tools = new DiagnosticTools(owner);
        Assert.That(Json(await tools.GetProcessDetails(10, "1", "WindowsFileTime")).GetProperty("status").GetString(), Is.EqualTo("identityMismatch"));
        Assert.That(Json(await tools.GetNetworkConnections(10, "1", "WindowsFileTime")).GetProperty("status").GetString(), Is.EqualTo("identityMismatch"));
        Assert.That(Json(await tools.GetProcessDetails(99)).GetProperty("status").GetString(), Is.EqualTo("notObserved"));
        Assert.That(Json(await tools.SamplePerformance(pid: 10, expectedStartToken: "1", expectedDomain: "WindowsFileTime"))
            .GetProperty("status").GetString(), Is.EqualTo("identityMismatch"));
    }

    [Test]
    public async Task ConnectionsRequireEndpointOptInAndPreservePartialFamilyFailure()
    {
        await using var owner = new SamplingOwner(() => new Source(Frame(Process(10, 9007199254740993, "alpha", 1))),
            TimeProvider.System, TimeSpan.FromMilliseconds(5));
        await owner.StartAsync(default);
        var tools = new DiagnosticTools(owner);
        var safe = Json(await tools.GetNetworkConnections());
        Assert.Multiple(() =>
        {
            Assert.That(safe.GetRawText(), Does.Not.Contain("10.1.2.3").And.Not.Contain("192.168.1.2"));
            Assert.That(safe.GetProperty("data").GetProperty("partiallyAvailable").GetBoolean(), Is.True);
        });
        var disclosed = Json(await tools.GetNetworkConnections(includeEndpoints: true));
        Assert.That(disclosed.GetRawText(), Does.Contain("10.1.2.3").And.Contain("192.168.1.2"));
    }

    [Test]
    public async Task PidOnlyConnectionQueryExcludesEndpointsFromOlderLifetimes()
    {
        await using var owner = new SamplingOwner(() => new Source(Frame(Process(10, 2, "new", 1))),
            TimeProvider.System, TimeSpan.FromMilliseconds(5));
        await owner.StartAsync(default);
        var tools = new DiagnosticTools(owner);
        var response = Json(await tools.GetNetworkConnections(pid: 10));
        Assert.That(response.GetProperty("data").GetProperty("connections").GetArrayLength(), Is.Zero);
        var unfiltered = Json(await tools.GetNetworkConnections());
        var old = unfiltered.GetProperty("data").GetProperty("connections")[0];
        Assert.That(old.GetProperty("name").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public void AvailabilityPreservesNullProcessCpuAndMissingSystemMetrics()
    {
        var process = ProcessMetrics.From(Process(1, 2, "unreadable", 0) with { CpuAvailable = false });
        Assert.Multiple(() =>
        {
            Assert.That(process.CpuPercent, Is.Null);
            Assert.That(process.MetricAvailability["cpuPercent"], Is.False);
            Assert.That(process.MetricAvailability["workingSetBytes"], Is.True);
            Assert.That(SystemMetrics.From(Frame().Full).MetricAvailability["gpuPercent"], Is.False);
        });
    }

    [Test]
    public async Task SlowCaptureDoesNotMakeAnOldObservationCurrent()
    {
        var frame = Frame() with { Full = Frame().Full with
        {
            MonotonicTimestamp = System.Diagnostics.Stopwatch.GetTimestamp() - System.Diagnostics.Stopwatch.Frequency * 3
        } };
        await using var owner = new SamplingOwner(() => new Source(frame), TimeProvider.System, TimeSpan.FromMilliseconds(5));
        await owner.StartAsync(default);
        var json = Json(await new DiagnosticTools(owner).GetSystemSnapshot(default));
        Assert.Multiple(() =>
        {
            Assert.That(json.GetProperty("freshness").GetString(), Is.EqualTo("stale"));
            Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("unavailable"));
            Assert.That(json.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task SamplingUsesSharedOneSecondFramesAndStopsAtThePinnedLifetime()
    {
        var clock = new AdvancingClock();
        var captures = 0;
        var source = new TimedSource(() =>
        {
            clock.Advance();
            var count = Interlocked.Increment(ref captures);
            return Frame(Process(10, count < 6 ? 1 : 2, "example", 10)) with
            {
                Full = Frame(Process(10, count < 6 ? 1 : 2, "example", 10)).Full with { Timestamp = clock.GetUtcNow() }
            };
        });
        await using var owner = new SamplingOwner(() => source, clock, TimeSpan.FromMilliseconds(20));
        await owner.StartAsync(default);
        var tools = new DiagnosticTools(owner);
        var response = Json(await tools.SamplePerformance(10, 10, 10).WaitAsync(TimeSpan.FromSeconds(3)));
        var data = response.GetProperty("data");
        Assert.Multiple(() =>
        {
            Assert.That(response.GetProperty("status").GetString(), Is.EqualTo("notObserved"));
            Assert.That(data.GetProperty("identity").GetProperty("startToken").GetString(), Is.EqualTo("1"));
            Assert.That(data.GetProperty("actualDurationSeconds").GetDouble(), Is.LessThan(10));
            Assert.That(data.GetProperty("metrics").GetProperty("processCpuPercent").GetProperty("average").GetDouble(), Is.EqualTo(10));
            Assert.That(data.GetProperty("trend").GetArrayLength(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SamplingCompletesTenSecondBucketWithOneSecondCadence()
    {
        var clock = new AdvancingClock();
        var source = new TimedSource(() =>
        {
            clock.Advance();
            return Frame() with { Full = Frame().Full with { Timestamp = clock.GetUtcNow() } };
        });
        await using var owner = new SamplingOwner(() => source, clock, TimeSpan.FromMilliseconds(10));
        await owner.StartAsync(default);
        var response = Json(await new DiagnosticTools(owner).SamplePerformance(10, 10).WaitAsync(TimeSpan.FromSeconds(3)));
        var data = response.GetProperty("data");
        Assert.Multiple(() =>
        {
            Assert.That(response.GetProperty("status").GetString(), Is.EqualTo("ok"));
            Assert.That(data.GetProperty("trend").GetArrayLength(), Is.EqualTo(1));
            Assert.That(data.GetProperty("metrics").GetProperty("cpuPercent").GetProperty("validSampleCount").GetInt32(), Is.EqualTo(10));
            Assert.That(data.GetProperty("metrics").GetProperty("cpuPercent").GetProperty("coverage").GetDouble(), Is.EqualTo(1));
        });
    }

    private sealed class AdvancingClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(GetTimestamp());
        internal void Advance() => Interlocked.Add(ref _ticks, 1000);
    }

    private sealed class TimedSource(Func<CapturedFrame> capture) : IFrameSource
    {
        public CapturedFrame Capture(SamplingOptions options) => capture();
        public void Reset() { }
        public void Dispose() { }
    }

    [Test]
    public async Task RejectsInvalidLimitsFiltersSortsAndIdentityPairsBeforeCollection()
    {
        await using var owner = new SamplingOwner(() => throw new AssertionException("Must not collect"), TimeProvider.System);
        var tools = new DiagnosticTools(owner);
        CallToolResult[] results =
        [
            await tools.ListProcesses(limit: 0), await tools.ListProcesses(limit: 201),
            await tools.ListProcesses(sort: "0"), await tools.ListProcesses(sort: "bad"),
            await tools.ListProcesses(sort: "Cpu,Memory"),
            await tools.ListProcesses(filter: new string('x', 257)),
            await tools.GetProcessDetails(-1), await tools.GetProcessDetails(1, "123"),
            await tools.GetProcessDetails(1, "001", "WindowsFileTime"),
            await tools.GetNetworkConnections(expectedStartToken: "1", expectedDomain: "WindowsFileTime"),
            await tools.SamplePerformance(61), await tools.SamplePerformance(3, 2)
        ];
        Assert.That(results.All(result => Json(result).GetProperty("status").GetString() == "invalidInput"), Is.True);
    }

    [Test]
    public async Task FourSamplingSlotsAreSharedAndCancellationReleasesThemWithoutStoppingSnapshots()
    {
        await using var owner = new SamplingOwner(() => new Source(Frame()), TimeProvider.System, TimeSpan.FromMilliseconds(5));
        await owner.StartAsync(default);
        await owner.CurrentAsync(default);
        var tools = new DiagnosticTools(owner);
        using var cancel = new CancellationTokenSource();
        var pending = Enumerable.Range(0, 4).Select(_ => tools.SamplePerformance(60, cancellationToken: cancel.Token)).ToArray();
        Assert.That(Json(await tools.SamplePerformance()).GetProperty("status").GetString(), Is.EqualTo("busy"));
        cancel.Cancel();
        foreach (var task in pending) Assert.That(async () => await task, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(Json(await tools.GetSystemSnapshot(default)).GetProperty("status").GetString(), Is.EqualTo("ok"));
        using var again = new CancellationTokenSource(30);
        Assert.That(async () => await tools.SamplePerformance(60, cancellationToken: again.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void BoundsCompleteWireResultIncludingDuplicatedTextAndUnicodeEscaping()
    {
        var entries = Enumerable.Range(1, 200).Select(pid => ProcessMetrics.From(Process(pid, pid, new string('界', 512), 1))).ToArray();
        var response = new DiagnosticResponse(1, DateTimeOffset.UtcNow, "ok", "current", 1, false, 0, null, null);
        var result = Responses.BoundedList(response, entries);
        Assert.Multiple(() =>
        {
            Assert.That(JsonSerializer.SerializeToUtf8Bytes(result, Responses.Json).Length, Is.LessThanOrEqualTo(Responses.MaximumBytes));
            Assert.That(Json(result).GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(Json(result).GetProperty("returnedCount").GetInt32(), Is.GreaterThan(0).And.LessThan(200));
        });
    }

    [Test]
    public void BoundsTrendEntriesWhilePreservingSummaryAndRequiredMetadata()
    {
        var aggregate = new PerformanceAggregate(60, 1);
        for (var second = 1; second <= 60; second++)
            aggregate.Add(Frame().Full, null, second, true);
        var targets = new PerformanceTargets("C:", "adapter", "gpu", false);
        var data = new PerformanceResult(DateTimeOffset.UtcNow, 60, 60, 1, null, targets, targets, false,
            aggregate.Summary(), aggregate.Trend(60), [], "No process traffic");
        var result = Responses.Result(new DiagnosticResponse(1, DateTimeOffset.UtcNow, "ok", "current", 1,
            false, 0, null, data, ReturnedCount: 60));
        var json = Json(result);
        Assert.Multiple(() =>
        {
            Assert.That(JsonSerializer.SerializeToUtf8Bytes(result, Responses.Json).Length, Is.LessThanOrEqualTo(Responses.MaximumBytes));
            Assert.That(json.GetProperty("data").GetProperty("metrics").GetProperty("cpuPercent").GetProperty("validSampleCount").GetInt32(), Is.EqualTo(60));
            Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(json.GetProperty("data").GetProperty("trend").GetArrayLength()));
            Assert.That(json.GetProperty("data").GetProperty("startingTargets").GetProperty("diskName").GetString(), Is.EqualTo("C:"));
        });
    }
}
