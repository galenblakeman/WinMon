using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.NetworkService;

namespace WinMon.Tests;

[TestFixture]
public sealed class NetworkTrafficLedgerTests
{
    private const long Frequency = 10;
    private const string Owner = "S-1-5-21-owner";

    [Test]
    public void Snapshot_UsesDelayedEventTimeWindowRatherThanArrivalTime()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Add(42, 35, 100, false, 500), Is.True);

        var snapshot = ledger.Snapshot(Owner, 100);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.WindowStartQpc, Is.EqualTo(20));
            Assert.That(snapshot.WindowEndQpc, Is.EqualTo(70));
            Assert.That((snapshot.Quality & ProcessNetworkQuality.Provisional) != 0, Is.True);
            Assert.That((snapshot.Quality & ProcessNetworkQuality.WarmingUp) == 0, Is.True);
            Assert.That(snapshot.LateEvents, Is.EqualTo(1));
            Assert.That(snapshot.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(100));
        });
    }

    [Test]
    public void Add_AttributesDelayedEventToExitedLifetimeWithoutReusingPid()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        ledger.End(42, 50);
        Assert.That(ledger.Register(42, 200, Owner, 50), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Add(42, 49, 100, true, 10), Is.True);
            Assert.That(ledger.Add(42, 50, 100, true, 10), Is.False);
            Assert.That(ledger.Add(42, 60, 100, true, 20), Is.True);
        });

        var rows = ledger.Snapshot(Owner, 100).Processes.OrderBy(row => row.CreationTicks).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(2));
            Assert.That(rows[0], Is.EqualTo(new ProcessNetworkReading(42, 100, 0, 10, null, null)));
            Assert.That(rows[1], Is.EqualTo(new ProcessNetworkReading(42, 200, 0, 20, null, null)));
        });
    }

    [Test]
    public void Snapshot_FiltersOtherOwners()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Register(43, 200, "S-1-5-21-other", 0), Is.True);
        Assert.That(ledger.Add(42, 30, 100, false, 10), Is.True);
        Assert.That(ledger.Add(43, 30, 100, false, 20), Is.True);

        var snapshot = ledger.Snapshot(Owner, 100);

        Assert.That(snapshot.Processes.Select(row => row.ProcessId), Is.EquivalentTo(new[] { 42 }));
    }

    [Test]
    public void Snapshot_KeepsRatesUnavailableUntilLossWindowHasAgedOut()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        ledger.ReportLoss(3, 100);

        var affected = ledger.Snapshot(Owner, 150);
        var recovered = ledger.Snapshot(Owner, 190);

        Assert.Multiple(() =>
        {
            Assert.That((affected.Quality & ProcessNetworkQuality.Incomplete) != 0, Is.True);
            Assert.That(affected.Processes, Is.Empty);
            Assert.That(affected.WindowStartQpc, Is.LessThanOrEqualTo(100));
            Assert.That(recovered.WindowStartQpc, Is.GreaterThan(100));
            Assert.That((recovered.Quality & ProcessNetworkQuality.WarmingUp) == 0, Is.True);
        });
    }

    [Test]
    public void ReportLoss_LowerCounterStartsNewGenerationAndMarksDiscontinuity()
    {
        var ledger = NewLedger();
        ledger.ReportLoss(3, 100);
        var before = ledger.Snapshot(Owner, 150);
        ledger.ReportLoss(1, 160);
        var after = ledger.Snapshot(Owner, 170);

        Assert.Multiple(() =>
        {
            Assert.That(after.Generation, Is.Not.EqualTo(before.Generation));
            Assert.That((after.Quality & ProcessNetworkQuality.Discontinuity) != 0, Is.True);
            Assert.That(after.EventsLost, Is.EqualTo(1));
            Assert.That(after.IncompleteReasons, Is.EqualTo(ProcessNetworkIncompleteReason.NativeLoss));
        });
    }

    [Test]
    public void Snapshot_BackwardsClockStartsNewGenerationAndSuppressesRates()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Add(42, 30, 100, false, 100), Is.True);
        var before = ledger.Snapshot(Owner, 100);
        var after = ledger.Snapshot(Owner, 90);

        Assert.Multiple(() =>
        {
            Assert.That(after.Generation, Is.Not.EqualTo(before.Generation));
            Assert.That((after.Quality & ProcessNetworkQuality.Discontinuity) != 0, Is.True);
            Assert.That((after.Quality & ProcessNetworkQuality.WarmingUp) != 0, Is.True);
            Assert.That(after.Processes, Is.Empty);
        });
    }

    [Test]
    public void HasLifetime_AcceptsRetainedExitedLifetimeButExcludesReuseBoundary()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        ledger.End(42, 50);
        Assert.That(ledger.Register(42, 200, Owner, 50), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(ledger.HasLifetime(42, 49), Is.True);
            Assert.That(ledger.HasExitedLifetime(42, 49), Is.True);
            Assert.That(ledger.HasLifetime(42, 50), Is.False);
            Assert.That(ledger.HasExitedLifetime(42, 50), Is.False);
            Assert.That(ledger.HasLifetime(42, 51), Is.True);
            Assert.That(ledger.HasExitedLifetime(42, 51), Is.False);
        });
    }

    [Test]
    public void Register_CapacityFailureDoesNotReplaceExistingLifetime()
    {
        var ledger = new NetworkTrafficLedger(Frequency, 0, 1);
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        ledger.End(42, 10);
        Assert.That(ledger.Register(43, 200, Owner, 20), Is.False);
        Assert.That(ledger.Add(42, 5, 30, false, 20), Is.True);

        var snapshot = ledger.Snapshot(Owner, 100);
        Assert.Multiple(() =>
        {
            Assert.That((snapshot.Quality & ProcessNetworkQuality.CapacityExceeded) != 0, Is.True);
            Assert.That((snapshot.Quality & ProcessNetworkQuality.Incomplete) != 0, Is.True);
            Assert.That(snapshot.Processes.Single().ProcessId, Is.EqualTo(42));
        });
    }

    [Test]
    public void Add_TooOldEventIsDroppedAndMarksReadingIncomplete()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Add(42, 10, 311, false, 20), Is.False);

        var snapshot = ledger.Snapshot(Owner, 320);
        Assert.That((snapshot.Quality & ProcessNetworkQuality.Incomplete) != 0, Is.True);
    }

    [Test]
    public void HasLifetime_ReturnsFalseAfterRetainedLifetimeIsPruned()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        ledger.End(42, 10);

        _ = ledger.Snapshot(Owner, 311);

        Assert.That(ledger.HasLifetime(42, 5), Is.False);
        Assert.That(ledger.HasExitedLifetime(42, 5), Is.False);
    }

    [Test]
    public void HasLifetime_ReturnsFalseAfterClockDiscontinuityResetsGeneration()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Add(42, 30, 100, false, 10), Is.True);
        _ = ledger.Snapshot(Owner, 100);

        _ = ledger.Snapshot(Owner, 90);

        Assert.That(ledger.HasLifetime(42, 30), Is.False);
    }

    private static NetworkTrafficLedger NewLedger()
    {
        return new NetworkTrafficLedger(Frequency, 0);
    }

    [Test]
    public void DuplicateRegistration_RecoversRatesAfterFreshWindowButKeepsCoverageReason()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Register(42, 100, Owner, 10), Is.False);
        Assert.That(ledger.Add(42, 40, 45, false, 50), Is.True);
        var snapshot = ledger.Snapshot(Owner, 110);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Quality.HasFlag(ProcessNetworkQuality.WarmingUp), Is.False);
            Assert.That(snapshot.Quality.HasFlag(ProcessNetworkQuality.Incomplete), Is.True);
            Assert.That(snapshot.IncompleteReasons, Is.EqualTo(ProcessNetworkIncompleteReason.AmbiguousLifetime));
            Assert.That(snapshot.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(10));
        });
    }
}