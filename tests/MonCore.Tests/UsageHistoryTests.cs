using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class UsageHistoryTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-10T09:00:00Z");

    [Test]
    public void Aggregate_UsesUncompactedIntervalsAndKeepsExitedApps()
    {
        var history = new UsageHistory();
        var first = Sample(1, 80, Process(1, 10, "alpha", 20, 100, 50), Process(2, 20, "beta", 10));
        var last = Sample(2, 40, Process(1, 10, "alpha", 30, 200));
        history.Add(first);
        history.Add(last);

        var period = history.Query(last, 2)!;
        var alpha = period.Apps.Single(app => app.Name == "alpha");
        var beta = period.Apps.Single(app => app.Name == "beta");
        Assert.Multiple(() =>
        {
            Assert.That(period.MachineBusyTicks, Is.EqualTo(1_200));
            Assert.That(period.NotAttributedTicks, Is.EqualTo(600));
            Assert.That(alpha.CpuTicks, Is.EqualTo(500));
            Assert.That(alpha.IoBytes, Is.EqualTo(300));
            Assert.That(alpha.GpuSeconds, Is.EqualTo(0.5));
            Assert.That(alpha.Lifetimes, Is.EqualTo(1));
            Assert.That(alpha.Running, Is.EqualTo(1));
            Assert.That(beta.CpuTicks, Is.EqualTo(100));
            Assert.That(beta.Running, Is.Zero, "The app exited before the selected capture.");
            Assert.That(period.Complete, Is.True);
        });
    }

    [Test]
    public void Aggregate_ReportsIncompleteCoverageForGapsAndTrimmedStarts()
    {
        var first = Sample(1, 50, Process(1, 10, "alpha", 10));
        var gap = new MonitorSample(Start.AddSeconds(2), null, [], 0, true, Stopwatch.Frequency * 2);
        var last = Sample(3, 50, Process(1, 10, "alpha", 10));
        var history = new UsageHistory(capacity: 2);
        history.Add(first);
        history.Add(gap);
        history.Add(last);

        var period = history.Query(last, 2)!;
        Assert.Multiple(() =>
        {
            Assert.That(period.Complete, Is.False);
            Assert.That(period.Apps.Single().CpuTicks, Is.EqualTo(100));
            Assert.That(period.Since, Is.EqualTo(gap.Timestamp), "The evicted first interval is not substituted.");
        });
    }

    [Test]
    public void Resize_AndEntryBudgetBoundRetainedObservations()
    {
        var first = Sample(1, 50, Process(1, 10, "alpha", 10));
        var second = Sample(2, 50, Process(2, 20, "beta", 10));
        var history = new UsageHistory(capacity: 60, entryBudget: 1);
        history.Add(first);
        history.Add(second);

        Assert.Multiple(() =>
        {
            Assert.That(history.WasMemoryLimited, Is.True);
            Assert.That(history.Query(second, 1)!.Complete, Is.True,
                "The retained interval itself is complete; the caller can expose WasMemoryLimited separately.");
        });
    }

    [Test]
    public void Query_ReturnsPartialCoverageAndCachesAnUnchangedSelectedEndpoint()
    {
        var history = new UsageHistory();
        var first = Sample(1, 50, Process(1, 10, "alpha", 10));
        var selected = Sample(2, 50, Process(1, 10, "alpha", 20));
        history.Add(first);
        history.Add(selected);

        var partial = history.Query(selected, 10)!;
        var cached = history.Query(selected, 10);
        history.Add(Sample(3, 50, Process(1, 10, "alpha", 30)));

        Assert.Multiple(() =>
        {
            Assert.That(partial.Complete, Is.False);
            Assert.That(partial.Since, Is.EqualTo(first.Timestamp.AddSeconds(-1)));
            Assert.That(cached, Is.SameAs(partial));
            Assert.That(history.Query(selected, 10), Is.SameAs(partial));
        });
    }

    [Test]
    public void Query_UsesExactlyTheRequestedNumberOfCompletedIntervals()
    {
        var history = new UsageHistory(capacity: 120);
        MonitorSample? selected = null;
        for (var second = 1; second <= 120; second++)
        {
            selected = Sample(second, 10, Process(1, 10, "alpha", 10));
            history.Add(selected);
        }

        var period = history.Query(selected!, 120)!;
        Assert.Multiple(() =>
        {
            Assert.That(period.Apps.Single().CpuTicks, Is.EqualTo(12_000));
            Assert.That(period.MachineBusyTicks, Is.EqualTo(12_000));
            Assert.That(period.Complete, Is.True);
        });
    }

    [Test]
    public void Add_OversizedSampleRecordsIncompleteCoverageWithoutBreakingMonitoring()
    {
        var history = new UsageHistory(entryBudget: 1);
        var oversized = Sample(1, 50, Process(1, 10, "alpha", 10), Process(2, 20, "beta", 10));

        Assert.DoesNotThrow(() => history.Add(oversized));
        Assert.Multiple(() =>
        {
            Assert.That(history.WasMemoryLimited, Is.True);
            Assert.That(history.Query(oversized, 1)!.Complete, Is.False);
        });
    }

    [Test]
    public void Query_MarksAnUnrecordedIntervalIncomplete()
    {
        var history = new UsageHistory();
        history.Add(Sample(1, 50, Process(1, 10, "alpha", 10)));
        var selected = Sample(3, 50, Process(1, 10, "alpha", 10));
        history.Add(selected);

        Assert.That(history.Query(selected, 3)!.Complete, Is.False);
    }

    [Test]
    public void Query_RequiresTheExactRetainedCapture()
    {
        var history = new UsageHistory();
        history.Add(Sample(1, 50, Process(1, 10, "alpha", 10)));
        var restored = new MonitorSample(Start.AddSeconds(1), 50, [], 0, false, Stopwatch.Frequency * 99)
            { IntervalSeconds = 1, IntervalCpuTicks = 1_000 };

        Assert.That(history.Query(restored, 1), Is.Null);
    }

    [Test]
    public void Query_PreservesEarlierMetadataAfterTheLiveOwnerChanges()
    {
        var history = new UsageHistory();
        var first = Sample(1, 50, Process(1, 10, "alpha", 10) with { AppName = "Earlier" });
        var last = Sample(2, 50, Process(1, 10, "alpha", 10) with { AppName = "Later" });
        history.Add(first);
        history.Add(last);
        Assert.That(history.Query(first, 1)!.Apps.Single().AppName, Is.EqualTo("Earlier"));
        Assert.That(history.Query(last, 1)!.Apps.Single().AppName, Is.EqualTo("Later"));
    }

    [Test]
    public void Query_BoundsAppRowsWithoutLosingOverflowUsage()
    {
        var history = new UsageHistory();
        var processes = Enumerable.Range(1, UsageLedger.MaxApps + 10)
            .Select(id => Process(id, id, "app-" + id, 1)).ToArray();
        var sample = Sample(1, 50, processes);
        history.Add(sample);
        var apps = history.Query(sample, 1)!.Apps;
        Assert.Multiple(() =>
        {
            Assert.That(apps, Has.Count.EqualTo(UsageLedger.MaxApps + 1));
            Assert.That(apps.Single(app => app.Key == UsageLedger.OtherAppsKey).CpuTicks, Is.EqualTo(100));
            Assert.That(apps.Sum(app => app.CpuTicks), Is.EqualTo(processes.Length * 10));
        });
    }

    [Test]
    public void Query_GroupsExecutablePathsAndKeepsReusedPidsSeparate()
    {
        var history = new UsageHistory();
        var first = Sample(1, 50, Process(7, 10, "alpha", 10) with { ImagePath = @"C:\\Apps\\alpha.exe" });
        var selected = Sample(2, 50,
            Process(7, 20, "beta", 10) with { ImagePath = @"C:\\Apps\\beta.exe" },
            Process(8, 30, "alpha", 10) with { ImagePath = @"c:\\apps\\ALPHA.exe" });
        history.Add(first);
        history.Add(selected);

        var apps = history.Query(selected, 2)!.Apps;
        Assert.Multiple(() =>
        {
            Assert.That(apps, Has.Count.EqualTo(2));
            Assert.That(apps.Single(app => app.Name == "alpha").Lifetimes, Is.EqualTo(2));
            Assert.That(apps.Single(app => app.Name == "beta").Lifetimes, Is.EqualTo(1));
        });
    }

    private static ProcessUsage Process(int id, long created, string name, double cpu, double? io = null,
        double? gpu = null)
    {
        return new ProcessUsage(id, created, name, cpu, IoBytesPerSecond: io) { GpuPercent = gpu };
    }

    private static MonitorSample Sample(int second, double totalCpu, params ProcessUsage[] processes)
    {
        return new MonitorSample(Start.AddSeconds(second), totalCpu, processes, 0, false,
            Stopwatch.Frequency * second) { IntervalSeconds = 1, IntervalCpuTicks = 1_000 };
    }
}
