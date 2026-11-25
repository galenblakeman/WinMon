using MonCore.Monitoring;
using NUnit.Framework;
using static WinMon.NetworkService.Tests.CollectorRuntimeFixture;

namespace WinMon.NetworkService.Tests;

[TestFixture]
public sealed class CollectorReconciliationTests
{
    [Test]
    public async Task MissingLedgerReturnsWithoutReadingClocksOrProcesses()
    {
        await using var f = new CollectorRuntimeFixture(ledger: false);
        f.Reconcile();
        Assert.That(f.Clock.Reads, Is.Empty);
        Assert.That(f.Native.Reads, Is.Empty);
        Assert.That(f.Collector.Failure, Is.EqualTo(ProcessNetworkFailure.None));
    }

    [TestCase(89_999_999L, false)]
    [TestCase(90_000_000L, true)]
    [TestCase(110_000_000L, true)]
    [TestCase(110_000_001L, false)]
    public async Task DriftToleranceIsInclusiveAndCheckedBeforeAnyNativeRead(long utcTicks, bool valid)
    {
        await using var f = new CollectorRuntimeFixture();
        Assert.That(f.Register(), Is.True);
        f.Native.Reads.Clear();
        f.Clock.Utc = new(CollectorTestClock.Epoch.AddTicks(utcTicks));
        f.Reconcile();
        Assert.Multiple(() =>
        {
            Assert.That(f.Clock.Reads, Is.EqualTo(new[] { "qpc", "utc", "qpc" }));
            Assert.That(f.Native.Reads, Is.EqualTo(valid ? new[] { "creation:42" } : Array.Empty<string>()));
            Assert.That(f.Collector.Failure, Is.EqualTo(valid ? ProcessNetworkFailure.None : ProcessNetworkFailure.ClockDiscontinuity));
            Assert.That(f.State.Cached(42), Is.EqualTo(new CachedOwner(Birth(1), Tick(1), valid ? Tick(10) : Tick(3))));
        });
    }

    [Test]
    public async Task BracketedSamplingDelayRefreshesEveryOwnerAtTheFirstTimestampBeforeNativeIo()
    {
        await using var f = new CollectorRuntimeFixture();
        Assert.That(f.Register(), Is.True);
        f.Native.Reads.Clear();
        f.Clock.Timestamps.Enqueue(Tick(10));
        f.Clock.Timestamps.Enqueue(Tick(12));
        f.Clock.Utc = new(CollectorTestClock.Epoch.AddSeconds(11));
        f.Clock.OnUtc = () => Assert.That(f.Clock.Reads, Is.EqualTo(new[] { "qpc", "utc" }));
        f.Native.OnCreation = () => f.Clock.At(20);
        f.Reconcile();
        Assert.That(f.Clock.Reads, Is.EqualTo(new[] { "qpc", "utc", "qpc" }));
        Assert.That(f.Native.Reads, Is.EqualTo(new[] { "creation:42" }));
        Assert.That(f.State.Cached(42), Is.EqualTo(new CachedOwner(Birth(1), Tick(1), Tick(10))));
        Assert.That(f.Collector.Failure, Is.EqualTo(ProcessNetworkFailure.None));
    }

    [Test]
    public async Task ReversedClockBracketRejectsBeforeNativeQueries()
    {
        await using var f = new CollectorRuntimeFixture();
        Assert.That(f.Register(), Is.True);
        f.Native.Reads.Clear();
        f.Clock.Timestamps.Enqueue(Tick(10));
        f.Clock.Timestamps.Enqueue(Tick(9));
        f.Reconcile();
        Assert.That(f.Native.Reads, Is.Empty);
        Assert.That(f.Collector.Failure, Is.EqualTo(ProcessNetworkFailure.ClockDiscontinuity));
        Assert.That(f.State.Cached(42), Is.EqualTo(new CachedOwner(Birth(1), Tick(1), Tick(3))));
    }

    [Test]
    public async Task FailedCreationReadEndsTheLifetimeAtObservationTimeEvenWhenItsOldTokenIsReturned()
    {
        await using var f = new CollectorRuntimeFixture();
        Assert.That(f.Register(), Is.True);
        f.Native.Reads.Clear();
        f.Native.CreationRead = _ => null;
        f.Native.FailedCreationValue = Birth(1);
        f.Reconcile();
        Assert.Multiple(() =>
        {
            Assert.That(f.Native.Reads, Is.EqualTo(new[] { "creation:42" }));
            Assert.That(f.State.OwnerCount, Is.Zero);
            Assert.That(f.State.Ledger.HasExitedLifetime(42, Tick(5)), Is.True);
            Assert.That(f.State.Ledger.HasExitedLifetime(42, Tick(10)), Is.False);
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        });
    }

    [Test]
    public async Task RemovingOneOwnerDoesNotSkipRefreshingTheOtherOwners()
    {
        await using var f = new CollectorRuntimeFixture();
        Assert.That(f.Register(42), Is.True);
        Assert.That(f.Register(43), Is.True);
        f.Native.Reads.Clear();
        f.Native.CreationRead = pid => pid == 42 ? null : Birth(1);
        f.Reconcile();
        Assert.That(f.Native.Reads, Is.EquivalentTo(new[] { "creation:42", "creation:43" }));
        Assert.That(f.State.OwnerCount, Is.EqualTo(1));
        Assert.That(f.State.Cached(42), Is.Null);
        Assert.That(f.State.Cached(43), Is.EqualTo(new CachedOwner(Birth(1), Tick(1), Tick(10))));
    }

    [TestCase("other")]
    [TestCase("missing")]
    [TestCase("unprovisioned")]
    public async Task ReusedPidClosesOldLifetimeBeforeResolvingItsNewOwner(string verdict)
    {
        await using var f = new CollectorRuntimeFixture();
        Assert.That(f.Register(), Is.True);
        Assert.That(f.State.Ledger.Add(42, Tick(5), Tick(10), false, 5), Is.True);
        f.Native.Reads.Clear();
        f.Native.CreationRead = _ => Birth(6);
        f.Native.OwnerRead = pid => verdict == "missing" ? null : new(pid, Birth(6), verdict == "other" ? Other : "unprovisioned");
        f.Reconcile();
        Assert.Multiple(() =>
        {
            Assert.That(f.Native.Reads, Is.EqualTo(new[] { "creation:42", "owner:42" }));
            Assert.That(f.State.Ledger.HasExitedLifetime(42, Tick(5)), Is.True);
            Assert.That(f.State.Ledger.HasExitedLifetime(42, Tick(6)), Is.False);
            Assert.That(f.State.Cached(42), Is.EqualTo(verdict == "other" ? new CachedOwner(Birth(6), Tick(6), Tick(10)) : null));
            Assert.That(f.State.Rejected(42), Is.EqualTo(verdict == "other" ? null : new RejectedOwner(Tick(11), verdict == "missing", verdict == "missing" ? 0 : Birth(6))));
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        });
        if (verdict == "other")
        {
            f.Queue.Network(42, Tick(8), 7, true);
            Assert.That(f.Queue.Batch(), Is.False);
            f.Clock.At(13);
            Assert.That(f.Collector.Snapshot(Owner).Processes.Single().ReceivedBytes, Is.EqualTo(5));
            var current = f.Collector.Snapshot(Other).Processes.Single();
            Assert.That((current.CreationTicks, current.ReceivedBytes, current.SentBytes), Is.EqualTo((Birth(6), 0L, 7L)));
        }
    }

    [Test]
    public async Task PendingDropQualityIsDrainedAtTheReconciliationObservationTime()
    {
        await using var f = new CollectorRuntimeFixture();
        f.State.Set("_coverageReasons", (int)ProcessNetworkIncompleteReason.QueueOverflow);
        f.State.Set("_rateGapPending", 1);
        f.State.Set("_droppedEvents", 3L);
        f.Reconcile();
        var snapshot = f.State.Ledger.Snapshot(Owner, Tick(13));
        Assert.That(f.State.RateGap, Is.Zero);
        Assert.That(snapshot.IncompleteReasons, Is.EqualTo(ProcessNetworkIncompleteReason.QueueOverflow));
        Assert.That(snapshot.Quality & ProcessNetworkQuality.Incomplete, Is.EqualTo(ProcessNetworkQuality.Incomplete));
    }

    [Test]
    public async Task InvalidNativeCreationKeepsConversionExceptionAndExistingCache()
    {
        await using var f = new CollectorRuntimeFixture();
        Assert.That(f.Register(), Is.True);
        f.Native.CreationRead = _ => -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => f.Reconcile());
        Assert.That(f.State.Cached(42), Is.EqualTo(new CachedOwner(Birth(1), Tick(1), Tick(3))));
        Assert.That(f.Collector.Failure, Is.EqualTo(ProcessNetworkFailure.None));
    }
}
