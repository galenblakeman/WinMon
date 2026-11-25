using System.Diagnostics;
using MonCore.Monitoring;
using NUnit.Framework;
using static WinMon.NetworkService.Tests.CollectorRuntimeFixture;

namespace WinMon.NetworkService.Tests;

[TestFixture]
public sealed class CollectorTimerTests
{
    [Test]
    public async Task IdleReconciliationWaitsOneSecondAndThenRegistersTheNextTimer()
    {
        await using var f = new CollectorRuntimeFixture();
        Assert.That(f.Register(), Is.True);
        f.Native.Reads.Clear();
        var consumer = f.Queue.Consume();
        f.State.Set("_worker", consumer);
        await f.Clock.NextTimer();
        f.Clock.AdvanceQpc(Stopwatch.Frequency - 1);
        Assert.Multiple(() =>
        {
            Assert.That(f.Clock.Created, Is.EqualTo(1));
            Assert.That(f.Native.Reads, Is.Empty);
            Assert.That(f.State.Cached(42), Is.EqualTo(new CachedOwner(Birth(1), Tick(1), Tick(3))));
            Assert.That(consumer.IsCompleted, Is.False);
        });
        f.Clock.AdvanceQpc(1);
        await f.Clock.NextTimer();
        Assert.That(f.Native.Reads, Is.EqualTo(new[] { "creation:42" }));
        Assert.That(f.State.Cached(42), Is.EqualTo(new CachedOwner(Birth(1), Tick(1), Tick(11))));
        Assert.That(f.Clock.Created, Is.EqualTo(2));
        Assert.That(f.Clock.Pending, Is.EqualTo(1));
        f.Queue.Cancel();
        await consumer.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(f.Clock.Pending, Is.Zero);
        Assert.That(f.Collector.Failure, Is.EqualTo(ProcessNetworkFailure.None));
    }

    [Test]
    public async Task DataAndCompletedChannelIterationsReuseOnePendingTimerUntilOwnedDisposal()
    {
        await using var f = new CollectorRuntimeFixture();
        f.Queue.Lifecycle(false, 42, Tick(8));
        Assert.That(f.Queue.Complete(), Is.True);
        var consumer = f.Queue.Consume();
        f.State.Set("_worker", consumer);
        await consumer.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(f.Queue.Count, Is.Zero);
            Assert.That(f.Clock.Created, Is.EqualTo(1));
            Assert.That(f.Clock.Pending, Is.EqualTo(1));
            Assert.That(f.Collector.Failure, Is.EqualTo(ProcessNetworkFailure.None));
        });
        await f.Collector.DisposeAsync();
        Assert.That(f.Clock.Pending, Is.Zero);
    }
}
