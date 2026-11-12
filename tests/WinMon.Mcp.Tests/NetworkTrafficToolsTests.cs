using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using NUnit.Framework;
using WinMon.Mcp;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class NetworkTrafficToolsTests
{
    [Test]
    public async Task InvalidAndCancelledRequestsDoNotSubscribeAndUnavailableServiceIsExplicit()
    {
        var connects = 0;
        await using var owner = new SamplingOwner(() => new Source(), TimeProvider.System, TimeSpan.FromMilliseconds(5),
            () => new NetworkTrafficClient(_ => { connects++; throw new IOException("not installed"); }));
        var tools = new DiagnosticTools(owner);
        Assert.That((await tools.GetProcessNetworkTraffic(limit: 0)).IsError, Is.True);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await tools.GetProcessNetworkTraffic(cancellationToken: cancelled.Token));
        Assert.That(connects, Is.Zero);
        await owner.StartAsync(default);
        var response = (await tools.GetProcessNetworkTraffic()).StructuredContent!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(response.GetProperty("status").GetString(), Is.EqualTo("unavailable"));
            Assert.That(response.GetProperty("data").GetProperty("serviceState").GetString(), Is.EqualTo("Unavailable"));
            Assert.That(response.GetProperty("data").GetProperty("processes").GetArrayLength(), Is.Zero);
        });
    }

    private sealed class Connection : INetworkTrafficConnection
    {
        internal readonly Channel<ProcessNetworkSnapshot> Snapshots = Channel.CreateUnbounded<ProcessNetworkSnapshot>();
        public Task SubscribeAsync(CancellationToken token) => Task.CompletedTask;
        public Task<ProcessNetworkSnapshot> SnapshotAsync(CancellationToken token) => Snapshots.Reader.ReadAsync(token).AsTask();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Source : IFrameSource
    {
        public CapturedFrame Capture(SamplingOptions options) => new(new MonitorSample(DateTimeOffset.UtcNow, 10,
            [new ProcessUsage(42, 100, "worker", 10) { ImagePath = "C:\\private\\secret.exe" }], 0));
        public void Reset() { }
        public void Dispose() { }
    }

    [Test]
    public async Task TrafficUsesSessionCountersPreservesIdentityAndQualityAndNeverDisclosesEndpointsOrPaths()
    {
        var connection = new Connection();
        var connects = 0;
        await using var owner = new SamplingOwner(() => new Source(), TimeProvider.System, TimeSpan.FromMilliseconds(5),
            () => new NetworkTrafficClient(_ => { connects++; return Task.FromResult<INetworkTrafficConnection>(connection); }));
        await owner.StartAsync(default);
        var tools = new DiagnosticTools(owner);
        await tools.GetSystemSnapshot(default);
        Assert.That(connects, Is.Zero, "Ordinary diagnostics do not subscribe to traffic.");
        await tools.GetProcessNetworkTraffic();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var generation = Guid.NewGuid();
        var qpc = Stopwatch.GetTimestamp() - 2 * Stopwatch.Frequency;
        ProcessNetworkSnapshot Snapshot(int sequence, long received) => new(generation, sequence,
            qpc + sequence * Stopwatch.Frequency / 10, Stopwatch.Frequency, qpc - 5 * Stopwatch.Frequency, qpc,
            ProcessNetworkQuality.Incomplete, 0, 0,
            [new ProcessNetworkReading(42, 100, received, 200, 100, 50),
                new ProcessNetworkReading(42, 99, received / 2, 100, null, null)]);
        await connection.Snapshots.Writer.WriteAsync(Snapshot(1, 100), timeout.Token);
        await WaitUntil(frame => frame.Network?.Processes.Count > 0);
        await connection.Snapshots.Writer.WriteAsync(Snapshot(2, 600), timeout.Token);
        await WaitUntil(frame => frame.Network?.Processes.Any(row => row.ReceivedBytes == 500) == true);
        JsonElement Json(ModelContextProtocol.Protocol.CallToolResult result) => result.StructuredContent!.Value;
        var response = Json(await tools.GetProcessNetworkTraffic(sort: "received", limit: 1));
        var row = response.GetProperty("data").GetProperty("processes")[0];
        Assert.Multiple(() =>
        {
            Assert.That(response.GetProperty("status").GetString(), Is.EqualTo("ok"));
            Assert.That(row.GetProperty("receivedBytes").GetInt64(), Is.EqualTo(500));
            Assert.That(row.GetProperty("identity").GetProperty("startToken").GetString(), Is.EqualTo("100"));
            Assert.That(row.GetProperty("name").GetString(), Is.EqualTo("worker"));
            Assert.That(response.GetProperty("data").GetProperty("quality").GetInt32() & (int)ProcessNetworkQuality.Incomplete,
                Is.Not.Zero);
            Assert.That(response.GetRawText(), Does.Not.Contain("secret.exe").And.Not.Contain("localEndpoint"));
            Assert.That(connects, Is.EqualTo(1));
            Assert.That(response.GetProperty("truncated").GetBoolean(), Is.True);
        });
        var pinned = Json(await tools.GetProcessNetworkTraffic(pid: 42, expectedStartToken: "100", expectedDomain: "WindowsFileTime"));
        Assert.That(pinned.GetProperty("returnedCount").GetInt32(), Is.EqualTo(1), "Other lifetimes of this PID must be excluded.");
        Assert.That(Json(await tools.GetProcessNetworkTraffic(pid: 42, expectedStartToken: "98", expectedDomain: "WindowsFileTime"))
            .GetProperty("status").GetString(), Is.EqualTo("identityMismatch"));
        Assert.That(Json(await tools.GetProcessNetworkTraffic(pid: 99)).GetProperty("status").GetString(), Is.EqualTo("notObserved"));
        Assert.That(Json(await tools.GetProcessNetworkTraffic(limit: 0)).GetProperty("status").GetString(), Is.EqualTo("invalidInput"));

        async Task WaitUntil(Func<PublishedFrame, bool> condition)
        {
            long sequence = 0;
            while (true)
            {
                var frames = owner.ReadAfter(sequence);
                if (frames.Any(condition)) return;
                if (frames.Length > 0) sequence = frames[^1].Sequence;
                await owner.WaitForChangeAsync(sequence, timeout.Token);
            }
        }
    }
}
