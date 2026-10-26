using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class PortableIdentityTests
{
    private static readonly ProcessIdentityPolicy Exact = ProcessIdentityPolicy.ExactPaths;
    private static ProcessUsage Mac(int pid, long token, string path, double cpu = 2) =>
        new(pid, token, "app", cpu, 100) { LifetimeDomain = ProcessLifetimeIdentity.UnixMicroseconds, ImagePath = path };
    private static MonitorSample Sample(params ProcessUsage[] processes) =>
        new(DateTimeOffset.UnixEpoch, 50, processes, 0)
        { IntervalSeconds = 1, IntervalCpuTicks = 80_000_000, IndependentProcessReadings = true };

    [Test]
    public void WindowsDefaultsStillGroupPathsIgnoringCase()
    {
        var a = new ProcessUsage(1, 1, "app", 2) { ImagePath = @"C:\App.exe" };
        var b = a with { ProcessId = 2, ImagePath = @"c:\app.exe" };
        Assert.That(ProcessRanking.Rank([Sample(a, b)], grouped: true).Processes, Has.Count.EqualTo(1));
    }

    [Test]
    public void ExactPathsKeepCaseDistinctAcrossRankingPinsSelectionAndUsage()
    {
        var a = Mac(1, 1, "/Apps/App");
        var b = Mac(2, 2, "/Apps/app");
        var sample = Sample(a, b);
        var rank = ProcessRanking.Rank([sample], grouped: true, identityPolicy: Exact);
        var pins = new ProcessPins(Exact);
        pins.Toggle(a, true);
        var selection = new ProcessSelection(Exact);
        selection.Select(a, true);
        var ledger = new UsageLedger(DateTimeOffset.Now, Exact);
        ledger.Add(sample);
        Assert.Multiple(() =>
        {
            Assert.That(rank.Processes, Has.Count.EqualTo(2));
            Assert.That(pins.Contains(b, true), Is.False);
            Assert.That(pins.RetentionSnapshot()!(b), Is.False);
            Assert.That(selection.Contains(b, true), Is.False);
            Assert.That(ledger.Apps, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void SwitchingPoliciesOnSameThreadDoesNotReuseWrongGroupingComparer()
    {
        var sample = Sample(Mac(1, 1, "/App"), Mac(2, 2, "/app"));
        Assert.That(ProcessRanking.Rank([sample], grouped: true).Processes, Has.Count.EqualTo(1));
        Assert.That(ProcessRanking.Rank([sample], grouped: true, identityPolicy: Exact).Processes, Has.Count.EqualTo(2));
        Assert.That(ProcessRanking.Rank([sample], grouped: true).Processes, Has.Count.EqualTo(1));
    }

    [Test]
    public void UnreadablePathsNeverMergeDifferentLifetimesWithSameName()
    {
        Assert.That(ProcessRanking.Rank([Sample(Mac(1, 1, ""), Mac(2, 2, ""))],
            grouped: true, identityPolicy: Exact).Processes, Has.Count.EqualTo(2));
    }

    [Test]
    public void LifetimeDomainAndStartTokenProtectRankingPinsAndSelectionFromPidReuse()
    {
        var old = Mac(1, 100, "/app");
        var reused = old with { CreationTicks = 101 };
        var windows = old with { LifetimeDomain = ProcessLifetimeIdentity.WindowsFileTime };
        var pins = new ProcessPins(Exact);
        pins.Toggle(old, false);
        var selection = new ProcessSelection(Exact);
        selection.Select(old, false);
        var ledger = new UsageLedger(DateTimeOffset.Now, Exact);
        ledger.Add(Sample(old, reused, windows));
        Assert.Multiple(() =>
        {
            Assert.That(ProcessRanking.Rank([Sample(old, reused, windows)], includeInactive: true).Processes,
                Has.Count.EqualTo(3));
            Assert.That(pins.Contains(reused, false), Is.False);
            Assert.That(pins.Contains(windows, false), Is.False);
            Assert.That(selection.Contains(reused, false), Is.False);
            Assert.That(ledger.RememberedLifetimes, Is.EqualTo(3));
        });
    }

    [Test]
    public void ExecReassignsFutureUsageWithoutChangingTheProcessLifetime()
    {
        var first = Mac(1, 100, "/first");
        var ledger = new UsageLedger(DateTimeOffset.Now, Exact);
        ledger.Add(Sample(first));
        ledger.Add(Sample(first with { ImagePath = "/second" }));
        Assert.That(ledger.RememberedLifetimes, Is.EqualTo(1));
        Assert.That(ledger.Apps, Has.Count.EqualTo(2));
        Assert.That(ledger.Apps.Single(app => app.ImagePath == "/second").Running, Is.EqualTo(1));
        Assert.That(ledger.Apps.Single(app => app.ImagePath == "/first").Running, Is.Zero);
    }

    [Test]
    public void PinAndUsageSnapshotsKeepPortableDomainsAndRejectDifferentUsagePolicy()
    {
        var process = Mac(1, 100, "/app") with { StartedAtUtc = DateTimeOffset.UnixEpoch };
        var pins = new ProcessPins(Exact);
        pins.Toggle(process, false);
        var restored = new ProcessPins(Exact);
        restored.Restore(pins.Snapshot());
        Assert.That(restored.Contains(process, false), Is.True);
        Assert.That(restored.Snapshot()[0].StartedAtUtc, Is.EqualTo(process.StartedAtUtc));
        var ledger = new UsageLedger(DateTimeOffset.Now, Exact);
        ledger.Add(Sample(process));
        var next = new UsageLedger(DateTimeOffset.Now, Exact);
        next.Restore(ledger.Export());
        next.Add(Sample(process));
        Assert.That(next.Apps.Single().Lifetimes, Is.EqualTo(1));
        Assert.Throws<ArgumentException>(() => new UsageLedger(DateTimeOffset.Now).Restore(ledger.Export()));
    }

    [Test]
    public void MissingProcessCpuDoesNotBecomeZeroOrDisappearDuringCompaction()
    {
        var missing = Mac(1, 1, "/app") with { CpuAvailable = false, WorkingSetBytes = null };
        var sample = Sample(missing);
        var rank = ProcessRanking.Rank([sample], sort: ProcessSort.Memory, includeInactive: true);
        Assert.That(rank.Processes[0].ObservedCpuPercent, Is.Null);
        var valid = Sample(missing with { CpuAvailable = true, CpuPercent = 10 }) with
        { Timestamp = sample.Timestamp.AddSeconds(1) };
        Assert.That(ProcessRanking.Rank([sample, valid], includeInactive: true).Processes[0].ObservedCpuPercent,
            Is.EqualTo(10));
        var compact = ProcessRetention.Compact(Sample(Enumerable.Range(1, 60)
            .Select(i => missing with { ProcessId = i }).ToArray()));
        Assert.That(compact.Processes, Has.Count.EqualTo(60));
    }

    [Test]
    public void MissingSystemCpuDoesNotInvalidateIndependentProcessReadings()
    {
        var sample = Sample(Mac(1, 1, "/app")) with { TotalCpuPercent = null };
        var ranking = ProcessRanking.Rank([sample], includeInactive: true);
        Assert.That(ranking.Processes, Has.Count.EqualTo(1));
        Assert.That(ranking.TotalCpuPercent, Is.Null);
        Assert.That(ProcessRanking.Rank([sample with { IndependentProcessReadings = false }]).Processes, Is.Empty);
    }

    [Test]
    public void CaptureRulesBreakOnUnknownCpuAndNeverCarryRunsAcrossDomains()
    {
        var rules = new CaptureRules([new(CaptureMetric.Process, true, 1, 2)], TimeSpan.Zero, 8);
        var process = Mac(1, 100, "/app");
        Assert.That(rules.Observe(Sample(process)), Is.Empty);
        Assert.That(rules.Observe(Sample(process with { CpuAvailable = false }) with
            { Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(1) }), Is.Empty);
        Assert.That(rules.Observe(Sample(process) with
            { Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(2) }), Is.Empty);
        Assert.That(rules.Observe(Sample(process with { LifetimeDomain = ProcessLifetimeIdentity.WindowsFileTime }) with
            { Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(3) }), Is.Empty);
    }

    [Test]
    public void PortableCsvCarriesIdentityDomainAndLeavesUnknownCpuEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        try
        {
            CsvExport.Write(path, CsvExportKind.ProcessSamples,
                [Sample(Mac(1, 100, "/app") with { CpuAvailable = false })], [], DateTimeOffset.Now, 0, 0);
            var lines = File.ReadAllLines(path);
            Assert.That(lines[0], Does.EndWith("lifetime_domain,started_at_utc"));
            Assert.That(lines[1].Split(',')[8], Is.Empty);
            Assert.That(lines[1], Does.Contain(ProcessLifetimeIdentity.UnixMicroseconds.ToString()));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void LegacyHistoryRefusesPortableSamplesWithoutOverwritingExistingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        File.WriteAllText(path, "existing");
        try
        {
            Assert.That(RestartHistory.Save(path, "test", "test", [Sample(Mac(1, 1, "/app"))],
                null, RestartClock.System), Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo("existing"));
        }
        finally { File.Delete(path); }
    }
}
