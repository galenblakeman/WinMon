using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class IncidentLifecycleCharacterizationTests
{
    private static ProcessUsage Owner(int id, long token = 100, string? name = null) => new(id, token, name ?? "p" + id, 20, 500, 900)
        { ImagePath = "path", AppName = "app", Publisher = "publisher", PrivateBytes = 700, GpuPercent = 30 };
    private static MonitorSample Sample(int second, params ProcessUsage[] processes) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 20, processes, 0, false, (second + 1) * Stopwatch.Frequency);
    private static UsageHistory History(params MonitorSample[] samples)
    {
        var usage = new UsageHistory();
        foreach (var sample in samples) usage.Add(sample);
        return usage;
    }

    [TestCase("before-gap")]
    [TestCase("after-gap")]
    [TestCase("before-unavailable")]
    [TestCase("after-unavailable")]
    [TestCase("before-missing")]
    [TestCase("after-missing")]
    [TestCase("before-invalid")]
    [TestCase("after-invalid")]
    [TestCase("before-zero-token")]
    [TestCase("after-zero-token")]
    [TestCase("before-negative-token")]
    [TestCase("after-negative-token")]
    public void CaptureChanges_IncompleteSamplesOrInventoriesKeepTheTransitionUnknown(string condition)
    {
        var before = Sample(10, Owner(1));
        var after = Sample(11, Owner(2));
        var usage = new UsageHistory();
        var storedBefore = condition switch
        {
            "before-invalid" => before with { TotalCpuPercent = null },
            "before-zero-token" => Sample(10, Owner(1, 0)),
            "before-negative-token" => Sample(10, Owner(1, -1)), _ => before
        };
        var storedAfter = condition switch
        {
            "after-invalid" => after with { TotalCpuPercent = null },
            "after-zero-token" => Sample(11, Owner(2, 0)),
            "after-negative-token" => Sample(11, Owner(2, -1)), _ => after
        };
        if (condition != "before-missing") usage.Add(storedBefore);
        if (condition != "after-missing") usage.Add(storedAfter);
        if (condition == "before-gap") before = before with { IsGap = true };
        if (condition == "after-gap") after = after with { IsGap = true };
        if (condition == "before-unavailable") before = before with { UnavailableProcesses = 1 };
        if (condition == "after-unavailable") after = after with { UnavailableProcesses = 1 };
        Assert.That(IncidentTimeline.CaptureChanges([before, after], usage), Is.Empty);
    }

    [TestCase(-1, true)]
    [TestCase(0, true)]
    [TestCase(1, false)]
    public void CaptureChanges_FiveSecondUpperBoundaryIsInclusiveToTheQpcTick(int offset, bool connected)
    {
        var before = Sample(10, Owner(1));
        var after = Sample(15, Owner(2)) with { MonotonicTimestamp = before.MonotonicTimestamp + 5 * Stopwatch.Frequency + offset };
        var result = IncidentTimeline.CaptureChanges([before, after], History(before, after));
        Assert.That(result.ContainsKey(SampleKey.Of(after)), Is.EqualTo(connected));
        if (connected) Assert.That(result[SampleKey.Of(after)].Started.Single().ProcessId, Is.EqualTo(2));
    }

    [TestCase(-1, false)]
    [TestCase(0, false)]
    [TestCase(1, true)]
    public void CaptureChanges_RequiresStrictlyForwardTime(int offset, bool connected)
    {
        var before = Sample(10, Owner(1));
        var after = Sample(11, Owner(2)) with { MonotonicTimestamp = before.MonotonicTimestamp + offset };
        Assert.That(IncidentTimeline.CaptureChanges([before, after], History(before, after)).ContainsKey(SampleKey.Of(after)),
            Is.EqualTo(connected));
    }

    [TestCase(true, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, true)]
    [TestCase(false, false, true)]
    public void CaptureChanges_UsesQpcOnlyWhenBothSamplesHavePositiveTokens(bool beforeQpc, bool afterQpc, bool connected)
    {
        var before = Sample(10, Owner(1)) with { MonotonicTimestamp = beforeQpc ? 100 * Stopwatch.Frequency : 0 };
        var after = Sample(15, Owner(2)) with { MonotonicTimestamp = afterQpc ? 200 * Stopwatch.Frequency : 0 };
        // Keep both inventories: adding a wall-clock key after a QPC key would exercise UsageHistory pruning instead.
        var usage = beforeQpc && !afterQpc ? History(after, before) : History(before, after);
        Assert.That(usage.IsAvailableAt(before) && usage.IsAvailableAt(after), Is.True);
        var result = IncidentTimeline.CaptureChanges([before, after], usage);
        Assert.That(result.ContainsKey(SampleKey.Of(after)), Is.EqualTo(connected));
    }

    [TestCase("before-qpc")]
    [TestCase("before-wall")]
    [TestCase("after-qpc")]
    [TestCase("after-wall")]
    public void CaptureChanges_InventoryKeysRequireBothSampleTimestamps(string changed)
    {
        var before = Sample(10, Owner(1));
        var after = Sample(11, Owner(2));
        var usage = History(before, after);
        if (changed == "before-qpc") before = before with { MonotonicTimestamp = before.MonotonicTimestamp + 1 };
        if (changed == "before-wall") before = before with { Timestamp = before.Timestamp.AddTicks(1) };
        if (changed == "after-qpc") after = after with { MonotonicTimestamp = after.MonotonicTimestamp + 1 };
        if (changed == "after-wall") after = after with { Timestamp = after.Timestamp.AddTicks(1) };
        Assert.That(IncidentTimeline.CaptureChanges([before, after], usage), Is.Empty);
    }

    [Test]
    public void CaptureChanges_UsesFullInventoriesRatherThanCompactedOrCurrentSampleReadings()
    {
        var old = Owner(1);
        var added = Owner(2) with { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds };
        var before = Sample(10, old);
        var after = Sample(11, added);
        var usage = History(before, after);
        before = before with { Processes = [Owner(99, 0)], CompactedProcesses = 7, TotalCpuPercent = null };
        after = after with { Processes = [], CompactedProcesses = 4, TotalCpuPercent = null };
        var result = IncidentTimeline.CaptureChanges([before, after], usage)[SampleKey.Of(after)];
        Assert.That(result.Started.Single().Identity, Is.EqualTo(added.Identity));
        Assert.That(result.Stopped.Single().Identity, Is.EqualTo(old.Identity));
        var projected = result.Started.Single();
        Assert.Multiple(() =>
        {
            Assert.That(projected.Name, Is.EqualTo(added.Name));
            Assert.That(projected.ObservedCpuPercent, Is.Null);
            Assert.That(projected.CpuPercent, Is.Zero);
            Assert.That(projected.WorkingSetBytes, Is.Null);
            Assert.That(projected.IoBytesPerSecond, Is.Null);
            Assert.That(projected.GpuPercent, Is.Null);
            Assert.That(projected.ImagePath, Is.Empty);
        });
    }

    [Test]
    public void CaptureChanges_CompleteEmptyInventoriesProduceAKnownEmptyTransition()
    {
        var before = Sample(10); var after = Sample(11);
        var result = IncidentTimeline.CaptureChanges([before, after], History(before, after));
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[SampleKey.Of(after)].Started, Is.Empty);
        Assert.That(result[SampleKey.Of(after)].Stopped, Is.Empty);
    }

    [Test]
    public void CaptureChanges_PreservesDomainLifetimeIdentityDuplicateObservationsAndInventoryOrder()
    {
        var old = Owner(1, 100, "old"); var survivor = Owner(2, 200, "survivor");
        var stopped = Owner(3, 300, "stopped");
        var reused = Owner(1, 101, "reused");
        var unix = survivor with { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds, Name = "unix" };
        var started = Owner(4, 400, "started");
        var before = Sample(10, old, survivor, stopped, stopped with { Name = "stopped-alias" });
        var after = Sample(11, survivor, reused, unix, started, started with { Name = "started-alias" });
        var result = IncidentTimeline.CaptureChanges([before, after], History(before, after))[SampleKey.Of(after)];
        Assert.That(result.Started.Select(p => p.Name), Is.EqualTo(new[] { "reused", "unix", "started", "started-alias" }));
        Assert.That(result.Stopped.Select(p => p.Name), Is.EqualTo(new[] { "old", "stopped", "stopped-alias" }));
        Assert.That(result.Started[1].Identity, Is.EqualTo(unix.Identity));
    }

    [Test]
    public void CaptureChanges_RepeatedOutputKeysKeepTheLastEligibleTransitionWithoutSortingSamples()
    {
        var a = Sample(10, Owner(1));
        var c = Sample(10, Owner(3)) with { MonotonicTimestamp = a.MonotonicTimestamp + Stopwatch.Frequency / 2,
            Timestamp = a.Timestamp.AddMilliseconds(500) };
        var b = Sample(11, Owner(2));
        var result = IncidentTimeline.CaptureChanges([a, b, c, b], History(a, c, b));
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[SampleKey.Of(b)].Stopped.Single().ProcessId, Is.EqualTo(3));
        Assert.That(result[SampleKey.Of(b)].Started.Single().ProcessId, Is.EqualTo(2));
    }

    [Test]
    public void CaptureChanges_ResultsStayFrozenAcrossLaterPairsAndUsageMutations()
    {
        var a = Sample(10, Owner(1)); var b = Sample(11, Owner(2)); var c = Sample(12, Owner(3));
        var usage = History(a, b, c);
        var result = IncidentTimeline.CaptureChanges([a, b, c], usage);
        var json = new JsonSerializerOptions { IncludeFields = true };
        var snapshot = JsonSerializer.Serialize(result.Values.ToArray(), json);
        usage.Export()[0].Observations[0] = default;
        usage.Resize(1);
        usage.Add(Sample(100, Owner(99)));
        Assert.That(JsonSerializer.Serialize(result.Values.ToArray(), json), Is.EqualTo(snapshot));
        Assert.That(result[SampleKey.Of(b)].Started.Single().ProcessId, Is.EqualTo(2));
        Assert.That(result[SampleKey.Of(c)].Stopped.Single().ProcessId, Is.EqualTo(2));
    }

    [TestCase("gap")]
    [TestCase("missing-before")]
    [TestCase("invalid-before")]
    public void CaptureChanges_EarlyEligibilityFailureSkipsCorruptLaterInventoryOwners(string gate)
    {
        var before = Sample(10, Owner(1)); var after = Sample(11, Owner(2));
        var usage = new UsageHistory();
        if (gate != "missing-before") usage.Add(gate == "invalid-before" ? before with { TotalCpuPercent = null } : before);
        usage.Add(after);
        usage.Export()[^1].Observations[0] = default;
        if (gate == "gap") before = before with { IsGap = true };
        Assert.That(IncidentTimeline.CaptureChanges([before, after], usage), Is.Empty);
    }

    [Test]
    public void CaptureChanges_CorruptEligibleInventoryOwnersKeepTheirNullReferenceException()
    {
        var before = Sample(10, Owner(1)); var after = Sample(11, Owner(2));
        var usage = History(before, after);
        usage.Export()[^1].Observations[0] = default;
        Assert.That(() => IncidentTimeline.CaptureChanges([before, after], usage), Throws.TypeOf<NullReferenceException>());
        Assert.That(IncidentTimeline.CaptureChanges([before with { IsGap = true }, null!], usage), Is.Empty);
    }

    [Test]
    public void CaptureChanges_DuplicateUsageKeysThrowBeforeEvenAnEmptySampleListIsRead()
    {
        var sample = Sample(10, Owner(1));
        Assert.That(() => IncidentTimeline.CaptureChanges([], History(sample, sample)), Throws.TypeOf<ArgumentException>());
    }

    [TestCase("usage")]
    [TestCase("samples")]
    [TestCase("before")]
    [TestCase("after")]
    public void CaptureChanges_NullRequiredInputsKeepTheirExistingExceptions(string input)
    {
        var before = Sample(10); var after = Sample(11);
        IReadOnlyList<MonitorSample> samples = input == "samples" ? null! : input == "before" ? [null!, after]
            : input == "after" ? [before, null!] : [before, after];
        Assert.That(() => IncidentTimeline.CaptureChanges(samples, input == "usage" ? null! : History(before, after)),
            Throws.TypeOf<NullReferenceException>());
    }
}
