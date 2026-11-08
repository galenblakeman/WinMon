using System.IO.Pipes;
using System.Security.Principal;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.NetworkService;

namespace WinMon.Tests;

[TestFixture]
public sealed partial class NetworkPipeTests
{
    [TestCase(ProcessNetworkFailure.None)]
    [TestCase(ProcessNetworkFailure.ClockDiscontinuity)]
    public async Task SubscribedClient_ReceivesOnlyItsSnapshotAndDisposesLease(ProcessNetworkFailure failure)
    {
        using var stop = new CancellationTokenSource();
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!.Value;
        var subscriptions = 0;
        var releases = 0;
        var snapshot = new ProcessNetworkSnapshot(Guid.NewGuid(), 1, 12, 10, 1, 9,
            (failure == ProcessNetworkFailure.None ? ProcessNetworkQuality.Provisional : ProcessNetworkQuality.Unavailable) | ProcessNetworkQuality.Incomplete,
            0, 0, []) { Failure = failure, IncompleteReasons = ProcessNetworkIncompleteReason.NativeLoss };
        var pipeName = $"WinMon-NetworkPipeTest-{Guid.NewGuid():N}";
        var server = new NetworkPipeServer(new HashSet<string>(StringComparer.Ordinal) { sid },
            (_, _) =>
            {
                subscriptions++;
                return Task.FromResult<IAsyncDisposable>(new CallbackDisposable(() => releases++));
            }, actual => { Assert.That(actual, Is.EqualTo(sid)); return snapshot; }, pipeName);
        var running = server.RunAsync(stop.Token);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var client = await NetworkServiceClient.ConnectProtectedPipeAsync(pipeName, timeout.Token);
            using var requestTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await NetworkServiceProtocol.WriteAsync(client, new NetworkServiceRequest(NetworkServiceProtocol.Version, "subscribe"), requestTimeout.Token);
            Assert.That((await NetworkServiceProtocol.ReadAsync<NetworkServiceResponse>(client, requestTimeout.Token)).Status, Is.EqualTo("ok"));
            await NetworkServiceProtocol.WriteAsync(client, new NetworkServiceRequest(NetworkServiceProtocol.Version, "snapshot"), requestTimeout.Token);
            var received = (await NetworkServiceProtocol.ReadAsync<NetworkServiceResponse>(client, requestTimeout.Token)).Snapshot!;
            Assert.That(received.Generation, Is.EqualTo(snapshot.Generation));
            Assert.That(received.Sequence, Is.EqualTo(snapshot.Sequence));
            Assert.That(received.Processes, Is.Empty);
            Assert.That(received.Failure, Is.EqualTo(failure));
            Assert.That(received.IncompleteReasons, Is.EqualTo(ProcessNetworkIncompleteReason.NativeLoss));
            await NetworkServiceProtocol.WriteAsync(client, new NetworkServiceRequest(NetworkServiceProtocol.Version, "unsubscribe"), requestTimeout.Token);
            Assert.That((await NetworkServiceProtocol.ReadAsync<NetworkServiceResponse>(client, requestTimeout.Token)).Status, Is.EqualTo("ok"));
            Assert.Multiple(() => { Assert.That(subscriptions, Is.EqualTo(1)); Assert.That(releases, Is.EqualTo(1)); });
        }
        finally
        {
            stop.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Test]
    public void Protocol_RejectsOversizedFrameBeforeAllocation()
    {
        using var stream = new MemoryStream();
        stream.Write(BitConverter.GetBytes(NetworkServiceProtocol.MaximumMessageBytes + 1));
        stream.Position = 0;
        Assert.That(async () => await NetworkServiceProtocol.ReadAsync<NetworkServiceRequest>(stream, CancellationToken.None),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public async Task EachSubscriptionHasItsOwnSequenceRegardlessOfOtherClients()
    {
        using var stop = new CancellationTokenSource();
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!.Value;
        var pipeName = $"WinMon-NetworkPipeSequence-{Guid.NewGuid():N}";
        var globalSequence = 100L;
        var generation = Guid.NewGuid();
        var server = new NetworkPipeServer([sid], (_, _) => Task.FromResult<IAsyncDisposable>(new CallbackDisposable(() => { })),
            _ => new ProcessNetworkSnapshot(generation, Interlocked.Increment(ref globalSequence), 20, 10, 0, 10, ProcessNetworkQuality.Provisional, 0, 0, []), pipeName);
        var running = server.RunAsync(stop.Token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await using var first = await NetworkServiceClient.ConnectProtectedPipeAsync(pipeName, timeout.Token);
            await using var second = await NetworkServiceClient.ConnectProtectedPipeAsync(pipeName, timeout.Token);
            async Task<ProcessNetworkSnapshot> SubscribeAndRead(NamedPipeClientStream pipe)
            {
                await NetworkServiceProtocol.WriteAsync(pipe, new NetworkServiceRequest(1, "subscribe"), timeout.Token);
                _ = await NetworkServiceProtocol.ReadAsync<NetworkServiceResponse>(pipe, timeout.Token);
                return await Read(pipe);
            }
            async Task<ProcessNetworkSnapshot> Read(NamedPipeClientStream pipe)
            {
                await NetworkServiceProtocol.WriteAsync(pipe, new NetworkServiceRequest(1, "snapshot"), timeout.Token);
                return (await NetworkServiceProtocol.ReadAsync<NetworkServiceResponse>(pipe, timeout.Token)).Snapshot!;
            }
            var one = await SubscribeAndRead(first);
            var two = await SubscribeAndRead(second);
            await Task.Delay(1100, timeout.Token);
            var next = await Read(first);
            Assert.Multiple(() =>
            {
                Assert.That(one.Sequence, Is.EqualTo(1));
                Assert.That(two.Sequence, Is.EqualTo(1));
                Assert.That(next.Sequence, Is.EqualTo(2));
                Assert.That(one.SubscriptionId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(two.SubscriptionId, Is.Not.EqualTo(one.SubscriptionId));
                Assert.That(next.SubscriptionId, Is.EqualTo(one.SubscriptionId));
            });
        }
        finally { stop.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    private sealed class CallbackDisposable(Action action) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { action(); return ValueTask.CompletedTask; }
    }
}
