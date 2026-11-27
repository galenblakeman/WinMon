using System.Diagnostics;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
[Category("Integration")]
[SetCulture("en-US")]
[SetUICulture("en-US")]
public sealed class ConnectionGraphCharacterizationTests
{
    private static ProcessUsage Owner(int id = 42, string path = @"C:\fixture\app.exe") =>
        new(id, 100 + id, "fixture", 0) { ImagePath = path };

    private static ProcessNetworkReading Reading(ProcessUsage owner, double? receive = 3, double? send = 7) =>
        new(owner.ProcessId, owner.CreationTicks, 9000, 8000, receive, send);

    private static ConnectionDisplayRow Row(ProcessUsage? owner)
    {
        var row = new ConnectionDisplayRow(owner is null ? "unknown" : ConnectionGraphData.Key(owner));
        if (owner is null) return row;
        // UpdateGroup requests an icon; finish its existing read-only worker before binding the row.
        ProcessIconProvider.GetAsync(owner).WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        row.UpdateGroup(new ConnectionsView.ConnectionGroup(row.Key, [], [owner], owner), false,
            ConnectionsView.NetworkSummary.Disabled);
        return row;
    }

    private static ConnectionCapture Capture(int second, IReadOnlyList<ProcessUsage> owners,
        IReadOnlyList<ProcessNetworkReading> readings, ProcessNetworkQuality quality = ProcessNetworkQuality.None,
        IReadOnlyList<ProcessUsage>? wireOwners = null, IReadOnlySet<ProcessLifetimeIdentity>? current = null) =>
        new(new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(second), 0, [], 0,
                MonotonicTimestamp: Stopwatch.Frequency * (second + 1)), null, owners,
            new ProcessNetworkFrame(second + 1, 1, quality, readings)
            {
                Owners = wireOwners ?? [], CurrentLifetimes = current
            });

    private static ProcessGraphData Build(ProcessUsage requested, params ConnectionCapture[] captures) =>
        ConnectionGraphData.Build(captures, [Row(requested)])[ConnectionGraphData.Key(requested)];

    [Test]
    public void ReusedWindowsMatchFreshBuildsAsTheWindowSlidesAndRowsChange()
    {
        var first = Owner(42, @"C:\fixture\one.exe");
        var second = Owner(43, @"C:\fixture\two.exe");
        var unnamed = Owner(44, "");
        var captures = Enumerable.Range(0, 40).Select(i => Capture(i, [first, second, unnamed],
            [Reading(first, i, 1), Reading(second, 2 * i, i % 3 == 0 ? null : 1), Reading(unnamed, 5, i)],
            i % 11 == 5 ? ProcessNetworkQuality.WarmingUp : ProcessNetworkQuality.None)).ToArray();
        ConnectionDisplayRow[] both = [Row(first), Row(second)], all = [Row(first), Row(second), Row(unnamed)];
        var reuse = new ConnectionGraphData.Reuse();

        for (var end = 10; end <= captures.Length; end++)
        {
            var window = captures[Math.Max(0, end - 20)..end];
            var rows = end is >= 25 and < 30 ? all : both;
            var reused = ConnectionGraphData.Build(window, rows, reuse);
            var fresh = ConnectionGraphData.Build(window, rows);
            Assert.That(reused.Keys, Is.EquivalentTo(fresh.Keys), $"window ending {end}");
            foreach (var (key, data) in fresh)
                Assert.Multiple(() =>
                {
                    Assert.That(reused[key].Values, Is.EqualTo(data.Values), $"{key} ending {end}");
                    Assert.That(reused[key].ScaleMaximum, Is.EqualTo(data.ScaleMaximum), $"{key} ending {end}");
                    Assert.That(reused[key].Samples, Is.EqualTo(data.Samples), $"{key} ending {end}");
                });
        }
    }

    [Test]
    public void EmptyRowsReturnBeforeAccessingHistory()
    {
        Assert.That(ConnectionGraphData.Build(null!, []), Is.Empty);
    }

    [Test]
    public void RowsWithoutProcessMetadataDoNotCreateSeries()
    {
        var owner = Owner();
        Assert.That(ConnectionGraphData.Build([Capture(0, [owner], [Reading(owner)])], [Row(null)]), Is.Empty);
    }

    [Test]
    public void EmptyHistoryKeepsRequestedKeysWithEmptyValuesAndAUnitScale()
    {
        var owner = Owner();
        var data = Build(owner);
        Assert.Multiple(() =>
        {
            Assert.That(data.Samples, Is.Empty);
            Assert.That(data.Values, Is.Empty);
            Assert.That(data.ScaleMaximum, Is.EqualTo(1));
            Assert.That(data.Sort, Is.EqualTo(ProcessSort.Cpu));
        });
    }

    [TestCase(ProcessNetworkQuality.None, true)]
    [TestCase(ProcessNetworkQuality.Provisional, true)]
    [TestCase(ProcessNetworkQuality.Incomplete, true)]
    [TestCase(ProcessNetworkQuality.CapacityExceeded, true)]
    [TestCase(ProcessNetworkQuality.Discontinuity, true)]
    [TestCase(ProcessNetworkQuality.WarmingUp, false)]
    [TestCase(ProcessNetworkQuality.Unavailable, false)]
    [TestCase(ProcessNetworkQuality.WarmingUp | ProcessNetworkQuality.Incomplete, false)]
    public void QualityFilteringRejectsOnlyUnavailableOrWarmingUp(ProcessNetworkQuality quality, bool available)
    {
        var owner = Owner();
        var capture = Capture(0, [owner], [Reading(owner)], quality);
        var data = Build(owner, capture);
        Assert.That(capture.Available, Is.False);
        Assert.That(data.Values, Is.EqualTo(new double?[] { available ? 10 : null }));
        Assert.That(data.Samples.Single(), Is.SameAs(capture.Sample));
    }

    [Test]
    public void GapsAndMissingNetworkKeepAlignedNullSlotsBetweenValidAndZeroRates()
    {
        var owner = Owner();
        var first = Capture(0, [owner], [Reading(owner)]);
        var gap = Capture(1, [owner], [Reading(owner)]) with { Sample = first.Sample with { IsGap = true } };
        var missing = Capture(2, [owner], [Reading(owner)]) with { Network = null };
        var zero = Capture(3, [owner], [Reading(owner, 0, 0)]);
        var data = Build(owner, first, gap, missing, zero);
        Assert.That(data.Values, Is.EqualTo(new double?[] { 10, null, null, 0 }));
        Assert.That(data.ScaleMaximum, Is.EqualTo(11));
        Assert.That(data.Samples, Is.EqualTo(new[] { first.Sample, gap.Sample, missing.Sample, zero.Sample }));
    }

    [TestCase(0d, 0d, 0d)]
    [TestCase(1.5d, 2.25d, 3.75d)]
    [TestCase(null, 1d, null)]
    [TestCase(1d, null, null)]
    [TestCase(-0.01d, 0d, null)]
    [TestCase(0d, -0.01d, null)]
    [TestCase(double.NaN, 1d, null)]
    [TestCase(1d, double.NaN, null)]
    [TestCase(double.PositiveInfinity, 0d, null)]
    [TestCase(0d, double.PositiveInfinity, null)]
    [TestCase(double.NegativeInfinity, 0d, null)]
    [TestCase(0d, double.NegativeInfinity, null)]
    [TestCase(double.MaxValue, double.MaxValue, null)]
    public void RatesRequireBothFiniteNonnegativeDirectionsAndAFiniteSum(double? receive, double? send, double? expected)
    {
        var owner = Owner();
        var data = Build(owner, Capture(0, [owner], [Reading(owner, receive, send)]));
        Assert.That(data.Values, Is.EqualTo(new[] { expected }));
        Assert.That(data.ScaleMaximum, Is.EqualTo(expected is { } value ? Math.Max(1, value * 1.1) : 1));
    }

    [TestCase("wire-only", true)]
    [TestCase("blank-capture", true)]
    [TestCase("promote", true)]
    [TestCase("remove", false)]
    [TestCase("restore", true)]
    [TestCase("last-remove", false)]
    [TestCase("last-restore", true)]
    public void CaptureMetadataOverridesKnownPathsButCannotEraseAUsableWirePathWithBlankMetadata(
        string mode, bool available)
    {
        var owner = Owner();
        var blank = owner with { ImagePath = "" };
        var other = owner with { ImagePath = @"C:\fixture\other.exe" };
        var wire = mode == "promote" ? blank : mode == "restore" ? other : owner;
        ProcessUsage[] captured = mode switch
        {
            "wire-only" => [], "blank-capture" => [blank], "promote" => [owner],
            "remove" => [other], "restore" => [owner],
            "last-remove" => [owner, other], "last-restore" => [other, owner],
            _ => throw new AssertionException("Unknown fixture mode")
        };
        var data = Build(owner, Capture(0, captured, [Reading(owner)], wireOwners: [wire]));
        Assert.That(data.Values, Is.EqualTo(new double?[] { available ? 10 : null }));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TheFirstReadingForALifetimeWinsEvenWhenALaterDuplicateIsValid(bool firstValid)
    {
        var owner = Owner();
        var valid = Reading(owner);
        var invalid = Reading(owner, null, 7);
        var data = Build(owner, Capture(0, [owner], firstValid ? [valid, invalid] : [invalid, valid]));
        Assert.That(data.Values, Is.EqualTo(new double?[] { firstValid ? 10 : null }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReadingsForAnotherPidOrCreationTokenCannotPopulateTheRequestedLifetime(bool reusedPid)
    {
        var owner = Owner(path: "");
        var reading = Reading(owner) with
        {
            ProcessId = reusedPid ? owner.ProcessId : owner.ProcessId + 1,
            CreationTicks = reusedPid ? owner.CreationTicks + 1 : owner.CreationTicks
        };
        Assert.That(Build(owner, Capture(0, [owner], [reading])).Values, Is.EqualTo(new double?[] { null }));
    }

    [Test]
    public void WindowsWireReadingsNeverMatchAnImportedLifetimeWithTheSameNumericToken()
    {
        var windows = Owner(path: "");
        var imported = windows with { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds };
        var result = ConnectionGraphData.Build([Capture(0, [windows, imported], [Reading(windows)])],
            [Row(windows), Row(imported)]);
        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[ConnectionGraphData.Key(windows)].Values, Is.EqualTo(new double?[] { 10 }));
            Assert.That(result[ConnectionGraphData.Key(imported)].Values, Is.EqualTo(new double?[] { null }));
        });
    }

    [TestCase("absent", true)]
    [TestCase("empty", false)]
    [TestCase("matching", true)]
    [TestCase("different", false)]
    public void CurrentLifetimeMembershipDistinguishesAbsentFromEmptyAndRequiresExactIdentity(string mode, bool available)
    {
        var owner = Owner();
        IReadOnlySet<ProcessLifetimeIdentity>? current = mode switch
        {
            "absent" => null, "empty" => new HashSet<ProcessLifetimeIdentity>(),
            "matching" => new HashSet<ProcessLifetimeIdentity> { owner.Identity },
            "different" => new HashSet<ProcessLifetimeIdentity> { (owner with { CreationTicks = 999 }).Identity },
            _ => throw new AssertionException("Unknown fixture mode")
        };
        var data = Build(owner, Capture(0, [owner], [Reading(owner)], current: current));
        Assert.That(data.Values, Is.EqualTo(new double?[] { available ? 10 : null }));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void MissingOrInvalidGroupMembersKeepTheWholePointUnavailableInEitherOwnerOrder(bool missing, bool reverse)
    {
        var owner = Owner();
        var sibling = Owner(43, @"c:\FIXTURE\APP.EXE");
        var data = Build(owner, Capture(0, reverse ? [sibling, owner] : [owner, sibling],
            missing ? [Reading(owner)] : [Reading(owner), Reading(sibling, -1, 0)]));
        Assert.That(data.Values, Is.EqualTo(new double?[] { null }));
        Assert.That(data.ScaleMaximum, Is.EqualTo(1));
    }

    [Test]
    public void RequestedKeysDeduplicateByExecutableAndShareThePeakAcrossAllCaptures()
    {
        var owner = Owner();
        var sameKey = owner with { ImagePath = @"c:\FIXTURE\APP.EXE" };
        var other = Owner(43, @"C:\fixture\other.exe");
        var unrelated = Owner(44, @"C:\fixture\unrequested.exe");
        var first = Capture(1, [owner, other, unrelated], [Reading(owner), Reading(other, 20, 10), Reading(unrelated, 9000, 9000)]);
        var second = Capture(0, [owner, other], [Reading(owner, 50, 50), Reading(other, 0, 5)]);
        var result = ConnectionGraphData.Build([first, second], [Row(owner), Row(other), Row(sameKey), Row(null)]);
        var a = result[ConnectionGraphData.Key(owner)];
        var b = result[ConnectionGraphData.Key(other)];
        Assert.Multiple(() =>
        {
            Assert.That(result.Keys, Is.EqualTo(new[] { ConnectionGraphData.Key(owner), ConnectionGraphData.Key(other) }));
            Assert.That(a.Values, Is.EqualTo(new double?[] { 10, 100 }));
            Assert.That(b.Values, Is.EqualTo(new double?[] { 30, 5 }));
            Assert.That(a.Values, Is.Not.SameAs(b.Values));
            Assert.That(a.Samples, Is.SameAs(b.Samples));
            Assert.That(a.Samples[0], Is.SameAs(first.Sample));
            Assert.That(a.Samples[1], Is.SameAs(second.Sample));
            Assert.That(a.ScaleMaximum, Is.EqualTo(110).Within(1e-12));
            Assert.That(b.ScaleMaximum, Is.EqualTo(a.ScaleMaximum));
        });
    }

    [Test]
    public void ReusedScratchDictionariesCannotCarryAnOwnerReadingOrTotalIntoAnotherCapture()
    {
        var owner = Owner();
        var data = Build(owner,
            Capture(0, [owner], [Reading(owner)]), Capture(1, [], [Reading(owner)]),
            Capture(2, [owner], [Reading(owner, 0, 0)]),
            Capture(3, [owner], [Reading(owner)], ProcessNetworkQuality.WarmingUp),
            Capture(4, [owner], []), Capture(5, [owner], [Reading(owner, 2, 3)]));
        Assert.That(data.Values, Is.EqualTo(new double?[] { 10, null, 0, null, null, 5 }));
        Assert.That(data.ScaleMaximum, Is.EqualTo(11));
    }

    [Test]
    public void BuildingDoesNotMutateInputsAndEveryCallReturnsIndependentValues()
    {
        var owner = Owner();
        var row = Row(owner);
        var reading = Reading(owner);
        var capture = Capture(0, [owner], [reading]);
        ConnectionCapture[] history = [capture];
        var notifications = 0;
        row.PropertyChanged += (_, _) => notifications++;
        var first = ConnectionGraphData.Build(history, [row])[ConnectionGraphData.Key(owner)];
        first.Values[0] = 999;
        var second = ConnectionGraphData.Build(history, [row])[ConnectionGraphData.Key(owner)];
        Assert.Multiple(() =>
        {
            Assert.That(second.Values, Is.EqualTo(new double?[] { 10 }));
            Assert.That(first.Values, Is.Not.SameAs(second.Values));
            Assert.That(history.Single(), Is.SameAs(capture));
            Assert.That(capture.Owners.Single(), Is.SameAs(owner));
            Assert.That(capture.Network!.Processes.Single(), Is.SameAs(reading));
            Assert.That(row.Process, Is.SameAs(owner));
            Assert.That(row.Graph.SelectionInPlot, Is.False);
            Assert.That(notifications, Is.Zero);
        });
    }
}
