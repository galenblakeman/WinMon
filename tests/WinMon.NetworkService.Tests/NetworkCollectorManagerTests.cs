using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.NetworkService;

namespace WinMon.Tests;

[TestFixture]
public sealed class NetworkCollectorManagerTests
{
    private const string User = "S-1-5-21-1-2-3-1001";

    [Test]
    public async Task Subscriptions_ShareCollectorAndStopOnlyAfterLastLease()
    {
        var collector = new FakeCollector();
        await using var manager = new NetworkCollectorManager([User], () => collector, TimeSpan.Zero);
        var first = await manager.SubscribeAsync(User, CancellationToken.None);
        var second = await manager.SubscribeAsync(User, CancellationToken.None);
        await first.DisposeAsync();
        await first.DisposeAsync();
        Assert.That(collector.Starts, Is.EqualTo(1));
        Assert.That(collector.Disposed, Is.False);
        Assert.That(manager.HasSubscriptions, Is.True);
        await second.DisposeAsync();
        await collector.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(collector.Disposed, Is.True);
        Assert.That(manager.HasSubscriptions, Is.False);
    }

    [Test]
    public async Task ReconnectDuringGrace_ReusesCollector()
    {
        var collector = new FakeCollector();
        await using var manager = new NetworkCollectorManager([User], () => collector, TimeSpan.FromSeconds(10));
        var first = await manager.SubscribeAsync(User, CancellationToken.None);
        await first.DisposeAsync();
        await using var second = await manager.SubscribeAsync(User, CancellationToken.None);
        Assert.That(collector.Starts, Is.EqualTo(1));
        Assert.That(collector.Disposed, Is.False);
    }

    [Test]
    public async Task UnprovisionedSid_CannotStartOrReadCollector()
    {
        var collector = new FakeCollector();
        await using var manager = new NetworkCollectorManager([User], () => collector);
        Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await manager.SubscribeAsync("S-1-5-21-1-2-3-1002", CancellationToken.None));
        Assert.Throws<UnauthorizedAccessException>(() => manager.Snapshot("S-1-5-21-1-2-3-1002"));
        Assert.That(collector.Starts, Is.Zero);
    }

    [Test]
    public async Task FailedStart_DisposesCandidateAndDoesNotCreateSubscription()
    {
        var collector = new FakeCollector { FailStart = true };
        await using var manager = new NetworkCollectorManager([User], () => collector);
        Assert.ThrowsAsync<IOException>(async () => await manager.SubscribeAsync(User, CancellationToken.None));
        Assert.That(collector.Disposed, Is.True);
        Assert.That(manager.HasSubscriptions, Is.False);
    }

    [Test]
    public async Task SuspendResume_StopsOldGenerationAndStartsFreshCollector()
    {
        var created = new List<FakeCollector>();
        await using var manager = new NetworkCollectorManager([User], () =>
        {
            var collector = new FakeCollector();
            created.Add(collector);
            return collector;
        });
        await using var lease = await manager.SubscribeAsync(User, CancellationToken.None);
        var generation = manager.Snapshot(User).Generation;
        await manager.SetSuspendedAsync(true, CancellationToken.None);
        Assert.That(created.Single().Disposed, Is.True);
        Assert.That(manager.Snapshot(User).Quality.HasFlag(ProcessNetworkQuality.Unavailable), Is.True);
        await manager.SetSuspendedAsync(false, CancellationToken.None);
        Assert.That(created, Has.Count.EqualTo(2));
        Assert.That(manager.Snapshot(User).Generation, Is.Not.EqualTo(generation));
    }

    [Test]
    public async Task FailedCollector_ReturnsUnavailableInsteadOfOldRates()
    {
        var collector = new FakeCollector();
        await using var manager = new NetworkCollectorManager([User], () => collector);
        await using var lease = await manager.SubscribeAsync(User, CancellationToken.None);
        collector.Failed = true;
        collector.Failure = ProcessNetworkFailure.ClockDiscontinuity;
        Assert.That(manager.Snapshot(User).Quality.HasFlag(ProcessNetworkQuality.Unavailable), Is.True);
        Assert.That(manager.Snapshot(User).Processes, Is.Empty);
        Assert.That(manager.Snapshot(User).Failure, Is.EqualTo(ProcessNetworkFailure.ClockDiscontinuity));
    }

    [Test]
    public async Task ReconnectAfterFailure_StartsFreshGenerationEvenDuringGrace()
    {
        var sources = new List<FakeCollector>();
        await using var manager = new NetworkCollectorManager([User], () =>
        {
            var next = new FakeCollector();
            sources.Add(next);
            return next;
        });
        await using var first = await manager.SubscribeAsync(User, CancellationToken.None);
        var before = manager.Snapshot(User).Generation;
        sources.Single().Failed = true;
        await using var second = await manager.SubscribeAsync(User, CancellationToken.None);
        Assert.That(sources, Has.Count.EqualTo(2));
        Assert.That(sources[0].Disposed, Is.True);
        Assert.That(manager.Snapshot(User).Generation, Is.Not.EqualTo(before));
    }

    private sealed class FakeCollector : INetworkCollector
    {
        private readonly Guid _generation = Guid.NewGuid();
        internal int Starts;
        internal bool Disposed;
        internal bool FailStart;
        internal readonly TaskCompletionSource Stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Failed { get; set; }
        public ProcessNetworkFailure Failure { get; set; }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            Starts++;
            return FailStart ? Task.FromException(new IOException("Test startup failure")) : Task.CompletedTask;
        }
        public ProcessNetworkSnapshot Snapshot(string ownerSid) => new(_generation, 1, 100, 1, 90, 95,
            ProcessNetworkQuality.Provisional, 0, 0, []);
        public ValueTask DisposeAsync() { Disposed = true; Stopped.TrySetResult(); return ValueTask.CompletedTask; }
    }
}
