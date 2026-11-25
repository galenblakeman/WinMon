using System.Diagnostics;
using System.Text.Json;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace WinMon.NetworkService.Tests;

public sealed partial class NetworkPipeTests
{
    [TestCase("same", 1, 2, 3)]
    [TestCase("change", 1, 1, 2)]
    [TestCase("return", 1, 1, 1)]
    [TestCase("empty", 1, 2, 1)]
    [Category("Integration")]
    public async Task WireSequenceFollowsTheCurrentGenerationAndNeverMutatesTheCollectorSnapshot(string pattern,
        long first, long second, long third)
    {
        var one = Guid.NewGuid(); var two = Guid.NewGuid();
        var generations = pattern switch
        {
            "same" => new[] { one, one, one }, "change" => [one, two, two],
            "return" => [one, two, one], _ => [Guid.Empty, Guid.Empty, one]
        };
        var originals = generations.Select(generation => EmptySnapshot() with
        {
            Generation = generation, Sequence = long.MaxValue, SubscriptionId = Guid.NewGuid(),
            Quality = generation == Guid.Empty ? ProcessNetworkQuality.Unavailable : ProcessNetworkQuality.Provisional,
            DroppedEvents = 7, EventsLost = 3, LateEvents = 2
        }).ToArray();
        var reads = 0; var sid = CurrentSid();
        await using var server = new ServerScope([sid], read: actual =>
        {
            Assert.That(actual, Is.EqualTo(sid));
            return originals[reads++];
        });
        await using var client = new NetworkServiceClient(await server.ConnectAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await client.SubscribeAsync(timeout.Token);
        var frames = new List<ProcessNetworkSnapshot>();
        for (var i = 0; i < 3; i++) frames.Add(await client.SnapshotAsync(timeout.Token));
        Assert.That(frames.Select(frame => frame.Sequence), Is.EqualTo(new[] { first, second, third }));
        Assert.That(frames.Select(frame => frame.Generation), Is.EqualTo(generations));
        Assert.That(frames[0].SubscriptionId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(frames.All(frame => frame.SubscriptionId == frames[0].SubscriptionId), Is.True);
        for (var i = 0; i < originals.Length; i++)
        {
            Assert.That(JsonSerializer.Serialize(frames[i] with
                { Sequence = originals[i].Sequence, SubscriptionId = originals[i].SubscriptionId }),
                Is.EqualTo(JsonSerializer.Serialize(originals[i])));
            Assert.That(originals[i].Sequence, Is.EqualTo(long.MaxValue));
            Assert.That(originals[i].SubscriptionId, Is.Not.EqualTo(frames[i].SubscriptionId));
        }
    }

    [Test]
    [Category("Integration")]
    public async Task BoundedWireSnapshotRetainsSubscriptionStampAndProcessRowsWhenFlowsAreTruncated()
    {
        const string endpoint = "[ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff]:65535";
        var flows = Enumerable.Range(0, 8192).Select(index => new ProcessNetworkFlowReading(42, 100,
            TransportProtocol.Udp, $"[ffff:ffff:ffff:ffff:ffff:ffff:ffff:fffe]:{index}", endpoint,
            long.MaxValue, long.MaxValue, 1_000_000, 1_000_000)).ToArray();
        var original = EmptySnapshot() with
        {
            Sequence = long.MaxValue,
            Processes = Enumerable.Range(1, 8192).Select(pid => new ProcessNetworkReading(pid, pid, 1, 1, 1, 1)).ToArray(),
            Flows = flows, FlowTrafficSupported = true, FlowQuality = ProcessNetworkQuality.Provisional
        };
        await using var server = new ServerScope([CurrentSid()], read: _ => original);
        await using var pipe = await server.ConnectAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.That((await ExchangeAsync(pipe, "subscribe", timeout.Token)).Status, Is.EqualTo("ok"));
        var frame = (await ExchangeAsync(pipe, "snapshot", timeout.Token)).Snapshot!;
        Assert.That(frame.Sequence, Is.EqualTo(1));
        Assert.That(frame.SubscriptionId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(frame.Generation, Is.EqualTo(original.Generation));
        Assert.That(frame.Processes, Is.EqualTo(original.Processes));
        Assert.That(frame.Flows.Count, Is.LessThan(flows.Length));
        Assert.That(frame.Flows, Is.EqualTo(flows.Take(frame.Flows.Count)));
        Assert.That(frame.FlowQuality, Is.EqualTo(ProcessNetworkQuality.Provisional | ProcessNetworkQuality.Incomplete | ProcessNetworkQuality.CapacityExceeded));
        Assert.That(original.Sequence, Is.EqualTo(long.MaxValue));
        Assert.That(original.Flows, Has.Count.EqualTo(8192));
    }

    [TestCase(false)]
    [TestCase(true)]
    [NonParallelizable]
    [Category("Integration")]
    public async Task CallbackEndOfStreamIsLoggedAtItsOperationStageAndDoesNotStopTheListener(bool subscribing)
    {
        using var output = new StringWriter();
        using var listener = new TextWriterTraceListener(output);
        Trace.Listeners.Add(listener);
        try
        {
            var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var server = new ServerScope([CurrentSid()],
                (_, _) => subscribing ? Task.FromException<IAsyncDisposable>(new EndOfStreamException("controlled callback EOF"))
                    : Task.FromResult<IAsyncDisposable>(new CallbackDisposable(() => released.TrySetResult())),
                _ => throw new EndOfStreamException("controlled callback EOF"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using (var pipe = await server.ConnectAsync())
            {
                if (!subscribing) Assert.That((await ExchangeAsync(pipe, "subscribe", timeout.Token)).Status, Is.EqualTo("ok"));
                Assert.ThrowsAsync<EndOfStreamException>(async () =>
                    await ExchangeAsync(pipe, subscribing ? "subscribe" : "snapshot", timeout.Token));
            }
            if (!subscribing) await released.Task.WaitAsync(timeout.Token);
            await using var next = await server.ConnectAsync();
            Assert.That((await ExchangeAsync(next, "status", timeout.Token)).Status, Is.EqualTo("ok"));
            listener.Flush();
            Assert.That(output.ToString(), Does.Contain(subscribing
                ? "operation=Subscribe stage=Subscribe subscribed=False"
                : "operation=Snapshot stage=Snapshot subscribed=True").And.Contain("controlled callback EOF"));
        }
        finally { Trace.Listeners.Remove(listener); }
    }

    [Test]
    [NonParallelizable]
    [Category("Integration")]
    public async Task FailedUnsubscribeRetainsTheLeaseForFinallyCleanup()
    {
        var lease = new FailingFirstRelease();
        await using var server = new ServerScope([CurrentSid()], (_, _) => Task.FromResult<IAsyncDisposable>(lease));
        await using var pipe = await server.ConnectAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.That((await ExchangeAsync(pipe, "subscribe", timeout.Token)).Status, Is.EqualTo("ok"));
        Assert.ThrowsAsync<EndOfStreamException>(async () => await ExchangeAsync(pipe, "unsubscribe", timeout.Token));
        await lease.Released.Task.WaitAsync(timeout.Token);
        Assert.That(lease.Calls, Is.EqualTo(2));
        await using var next = await server.ConnectAsync();
        Assert.That((await ExchangeAsync(next, "status", timeout.Token)).Status, Is.EqualTo("ok"));
    }

    private sealed class FailingFirstRelease : IAsyncDisposable
    {
        internal int Calls;
        internal readonly TaskCompletionSource Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Increment(ref Calls) == 1) throw new IOException("controlled release failure");
            Released.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
