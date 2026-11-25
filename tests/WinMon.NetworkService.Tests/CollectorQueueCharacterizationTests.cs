using MonCore.Monitoring;
using NUnit.Framework;

namespace WinMon.NetworkService.Tests;

[TestFixture]
public sealed class CollectorQueueCharacterizationTests
{
    [Test]
    public async Task MissingLedgerLeavesQueuedEventsUntouched()
    {
        await using var f = new EtwCallbackFixture();
        f.Lifecycle(false, 42, 100);
        Assert.That(f.Batch(), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(f.Count, Is.EqualTo(1));
            Assert.That(f.Take(), Is.EqualTo(new QueuedPacket("Stop", 42, 100, 0, false, null, null, 0, null, 0)));
            Assert.That(f.Collector.Failure, Is.EqualTo(ProcessNetworkFailure.None));
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        });
    }

    [TestCase(0, false, 0)]
    [TestCase(4095, false, 0)]
    [TestCase(4096, true, 0)]
    [TestCase(4097, true, 1)]
    public async Task BatchProcessesAtMost4096AndReportsWhetherTheLimitWasReached(int count, bool expected, int left)
    {
        var probe = new ThrowingProbe();
        await using var f = new EtwCallbackFixture(new(["owner"], probe));
        _ = new CollectorOwnershipState(f.Collector, DateTime.UnixEpoch);
        for (var i = 0; i < count; i++) f.Lifecycle(false, i + 1, 100);
        Assert.That(f.Batch(), Is.EqualTo(expected));
        Assert.Multiple(() =>
        {
            Assert.That(f.Count, Is.EqualTo(left));
            Assert.That(probe.Reads, Is.Zero);
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
            Assert.That(f.Collector.Failure, Is.EqualTo(ProcessNetworkFailure.None));
        });
        if (left != 0)
            Assert.That(f.Take(), Is.EqualTo(new QueuedPacket("Stop", 4097, 100, 0, false, null, null, 0, null, 0)));
        Assert.That(f.Batch(), Is.False);
    }

    [Test]
    public async Task AlreadyCancelledConsumerReturnsWithoutReadingQueuedEvents()
    {
        await using var f = new EtwCallbackFixture();
        f.Lifecycle(false, 42, 100);
        f.Cancel();
        var consumer = f.Consume();
        Assert.That(consumer.IsCompletedSuccessfully, Is.True);
        await consumer;
        Assert.That(f.Count, Is.EqualTo(1));
        Assert.That(f.Collector.Failure, Is.EqualTo(ProcessNetworkFailure.None));
    }

    [Test]
    public async Task CompletedEmptyChannelEndsConsumerWithoutFailure()
    {
        await using var f = new EtwCallbackFixture();
        Assert.That(f.Complete(), Is.True);
        await f.Consume().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(f.Count, Is.Zero);
        Assert.That(f.Collector.Failure, Is.EqualTo(ProcessNetworkFailure.None));
        Assert.That(f.RateGap, Is.Zero);
    }

    [Test]
    public async Task WaitingConsumerFinishesWhenItsOwnedCancellationIsRequested()
    {
        await using var f = new EtwCallbackFixture();
        var consumer = f.Consume();
        Assert.That(consumer.IsCompleted, Is.False);
        f.Cancel();
        await consumer.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(f.Collector.Failure, Is.EqualTo(ProcessNetworkFailure.None));
        Assert.That(f.Count, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FaultedChannelMarksConsumerFailureButKeepsAnEarlierFailure(bool previous)
    {
        await using var f = new EtwCallbackFixture();
        if (previous) f.Collector.SignalFailure(ProcessNetworkFailure.TraceConsumer);
        Assert.That(f.Complete(new InvalidOperationException("Synthetic channel failure")), Is.True);
        await f.Consume().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(f.Collector.Failure, Is.EqualTo(previous ? ProcessNetworkFailure.TraceConsumer : ProcessNetworkFailure.EventConsumer));
        Assert.That(f.Count, Is.Zero);
        Assert.That(f.RateGap, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OperationCancellationFromAProbeIsExpectedOnlyAfterOwnedCancellation(bool owned)
    {
        var probe = new ThrowingProbe();
        await using var f = new EtwCallbackFixture(new(["owner"], probe));
        _ = new CollectorOwnershipState(f.Collector, DateTime.UnixEpoch);
        probe.Read = () =>
        {
            if (owned) f.Cancel();
            throw new OperationCanceledException("Synthetic probe cancellation");
        };
        f.Lifecycle(true, 42, 100);
        await f.Consume().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(probe.Reads, Is.EqualTo(1));
            Assert.That(f.Count, Is.Zero);
            Assert.That(f.Collector.Failure, Is.EqualTo(owned ? ProcessNetworkFailure.None : ProcessNetworkFailure.EventConsumer));
            Assert.That(f.RateGap, Is.EqualTo(owned ? 0 : 1));
        });
    }

    [Test]
    public async Task CompletedQueueIsDrainedBeforeConsumerChecksClockCalibration()
    {
        var probe = new ThrowingProbe();
        await using var f = new EtwCallbackFixture(new(["owner"], probe));
        _ = new CollectorOwnershipState(f.Collector, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));
        f.Lifecycle(false, 42, 100);
        Assert.That(f.Complete(), Is.True);
        await f.Consume().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(f.Count, Is.Zero);
            Assert.That(probe.Reads, Is.Zero);
            Assert.That(f.Collector.Failure, Is.EqualTo(ProcessNetworkFailure.ClockDiscontinuity));
            Assert.That(f.RateGap, Is.EqualTo(1));
        });
    }

    private sealed class ThrowingProbe : EtwNetworkCollector.IProcessOwnerProbe
    {
        internal Action Read = () => throw new InvalidOperationException("Unexpected native probe");
        internal int Reads;
        public bool TryReadCreation(int pid, out long creation) => throw new InvalidOperationException("Unexpected creation probe");
        public bool TryRead(int pid, out ProcessOwner owner)
        {
            Reads++; Read(); owner = null!; return false;
        }
    }
}
