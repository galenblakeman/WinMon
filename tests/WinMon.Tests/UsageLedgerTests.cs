using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class UsageLedgerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-24T09:00:00Z");

    [Test]
    public void Add_TotalsCpuIoAndGpuPerExecutable()
    {
        var ledger = new UsageLedger(Start);
        ledger.Add(Valid(1, [
            Process(1, 10, "chrome", 20, io: 1_000, gpu: 50, path: @"C:\Apps\chrome.exe"),
            Process(2, 20, "chrome", 10, io: 500, path: @"c:\apps\CHROME.EXE"),
            Process(4, 40, "System", 5)
        ]));
        ledger.Add(Valid(2, [Process(1, 10, "chrome", 30, io: 2_000, gpu: 25, path: @"C:\Apps\chrome.exe")],
            seconds: 2));

        var chrome = App(ledger, @"C:\Apps\chrome.exe");
        var system = App(ledger, "System");
        Assert.Multiple(() =>
        {
            Assert.That(ledger.Apps, Has.Count.EqualTo(2), "Path case does not split an executable.");
            Assert.That(chrome.CpuTicks, Is.EqualTo(300 + 300), "20% + 10% of 1,000 ticks, then 30% of 1,000.");
            Assert.That(chrome.IoBytes, Is.EqualTo(1_000 + 500 + 4_000), "Rates times each interval's seconds.");
            Assert.That(chrome.GpuSeconds, Is.EqualTo(0.5 + 0.5));
            Assert.That(chrome.Lifetimes, Is.EqualTo(2));
            Assert.That(chrome.Running, Is.EqualTo(1));
            Assert.That(system.Key, Is.EqualTo(ProcessRanking.GroupKey(Process(4, 40, "System", 0))));
            Assert.That(system.GpuSeconds, Is.Null, "No GPU reading is not zero GPU time.");
        });
    }

    [Test]
    public void Add_GapsAndBadDeltasNeitherCountNorEndLifetimes()
    {
        var running = new[] { Process(1, 10, "app", 10), Process(2, 20, "tool", 10) };
        var interrupted = new UsageLedger(Start);
        interrupted.Add(Valid(1, running));
        interrupted.Add(new MonitorSample(Start.AddSeconds(2), null, [], 0, true, Stopwatch.Frequency * 2));
        interrupted.Add(Valid(3, running));
        interrupted.Add(new MonitorSample(Start.AddSeconds(4), null, [], 0, false, Stopwatch.Frequency * 4));
        interrupted.Add(Valid(5, running));
        var uninterrupted = new UsageLedger(Start);
        foreach (var second in new[] { 1, 3, 5 }) uninterrupted.Add(Valid(second, running));

        Assert.Multiple(() =>
        {
            Assert.That(interrupted.Apps, Is.EqualTo(uninterrupted.Apps));
            Assert.That(interrupted.Apps.Select(app => app.Lifetimes), Is.All.EqualTo(1));
            Assert.That(interrupted.Apps.Select(app => app.Running), Is.All.EqualTo(1));
            Assert.That(interrupted.MachineBusyTicks, Is.EqualTo(uninterrupted.MachineBusyTicks));
        });
    }

    [Test]
    public void Add_RunningKeepsItsLastValidValueThroughAGap()
    {
        var ledger = new UsageLedger(Start);
        ledger.Add(Valid(1, [Process(1, 10, "app", 10)]));
        ledger.Add(new MonitorSample(Start.AddSeconds(2), null, [], 0, true, Stopwatch.Frequency * 2));

        Assert.That(App(ledger, "app").Running, Is.EqualTo(1));
    }

    [Test]
    public void Add_ALifetimeMissingFromOneSampleIsNotCountedTwice()
    {
        var ledger = new UsageLedger(Start);
        ledger.Add(Valid(1, [Process(1, 10, "app", 10)]));
        ledger.Add(Valid(2, []));
        ledger.Add(Valid(3, [Process(1, 10, "app", 10)]));

        Assert.That(App(ledger, "app").Lifetimes, Is.EqualTo(1));
    }

    [Test]
    public void Add_RetiresALifetimeOnlyAfterConsecutiveValidAbsences()
    {
        var ledger = new UsageLedger(Start);
        ledger.Add(Valid(0, [Process(1, 10, "app", 10)]));
        for (var second = 1; second < UsageLedger.RetireAfterAbsences; second++)
        {
            ledger.Add(Valid(second, []));
            ledger.Add(new MonitorSample(Start.AddSeconds(second + 0.5), null, [], 0, true, 0));
        }

        var beforeLast = ledger.RememberedLifetimes;
        ledger.Add(Valid(UsageLedger.RetireAfterAbsences, []));

        Assert.Multiple(() =>
        {
            Assert.That(beforeLast, Is.EqualTo(1), "Gaps between the absences did not advance the count.");
            Assert.That(ledger.RememberedLifetimes, Is.Zero);
            Assert.That(App(ledger, "app").Lifetimes, Is.EqualTo(1), "Retiring forgets the identity, not the totals.");
        });
    }

    [Test]
    public void Add_AReusedPidIsANewLifetimeOfItsOwnExecutable()
    {
        var ledger = new UsageLedger(Start);
        ledger.Add(Valid(1, [Process(7, 100, "old", 10, path: @"C:\old.exe")]));
        ledger.Add(Valid(2, [Process(7, 200, "new", 10, path: @"C:\new.exe")]));

        Assert.Multiple(() =>
        {
            Assert.That(App(ledger, @"C:\old.exe").Lifetimes, Is.EqualTo(1));
            Assert.That(App(ledger, @"C:\old.exe").Running, Is.Zero);
            Assert.That(App(ledger, @"C:\new.exe").Lifetimes, Is.EqualTo(1));
            Assert.That(App(ledger, @"C:\new.exe").Running, Is.EqualTo(1));
        });
    }

    [Test]
    public void Shares_UseMachineBusyTimeForEveryRow()
    {
        var ledger = new UsageLedger(Start);
        ledger.Add(Valid(1, [Process(1, 10, "a", 30), Process(2, 20, "b", 20)], totalCpu: 80));

        var attributed = ledger.Apps.Sum(app => app.CpuTicks);
        Assert.Multiple(() =>
        {
            Assert.That(ledger.MachineBusyTicks, Is.EqualTo(800));
            Assert.That(ledger.NotAttributedCpuTicks, Is.EqualTo(300));
            Assert.That(attributed + ledger.NotAttributedCpuTicks, Is.EqualTo(ledger.MachineBusyTicks),
                "Apps and Not attributed add up to 100% of machine busy time.");
        });
    }

    [Test]
    public void NotAttributed_IsNeverNegative()
    {
        var ledger = new UsageLedger(Start);
        ledger.Add(Valid(1, [Process(1, 10, "a", 60)], totalCpu: 50));

        Assert.That(ledger.NotAttributedCpuTicks, Is.Zero);
    }

    [Test]
    public void LastActive_IsTheLatestSampleWithActivityAndExitedAppsKeepTotals()
    {
        var ledger = new UsageLedger(Start);
        var active = Valid(1, [Process(1, 10, "app", 10)]);
        ledger.Add(active);
        ledger.Add(Valid(2, [Process(1, 10, "app", 0)]));
        ledger.Add(Valid(3, []));

        var app = App(ledger, "app");
        Assert.Multiple(() =>
        {
            Assert.That(app.LastActive, Is.EqualTo(SampleKey.Of(active)));
            Assert.That(app.LastActive!.Value.Matches(ProcessRetention.Compact(active)), Is.True);
            Assert.That(app.Running, Is.Zero);
            Assert.That(app.CpuTicks, Is.EqualTo(100));
        });
    }

    [Test]
    public void IsApp_FollowsAnyObservedAppWindow()
    {
        var ledger = new UsageLedger(Start);
        ledger.Add(Valid(1, [Process(1, 10, "app", 1) with { HasAppWindow = true }, Process(2, 20, "svc", 1)]));
        ledger.Add(Valid(2, [Process(1, 10, "app", 1) with { HasAppWindow = false }, Process(2, 20, "svc", 1)]));

        Assert.Multiple(() =>
        {
            Assert.That(App(ledger, "app").IsApp, Is.True);
            Assert.That(App(ledger, "svc").IsApp, Is.False);
        });
    }

    [Test]
    public void Add_FoldsExecutablesBeyondTheCapIntoOtherApps()
    {
        var ledger = new UsageLedger(Start);
        ledger.Add(Valid(1, [
            .. Enumerable.Range(0, UsageLedger.MaxApps + 2).Select(i => Process(i, i + 1, $"tool-{i}", 0.01))
        ]));

        var other = App(ledger, UsageLedger.OtherAppsKey);
        Assert.Multiple(() =>
        {
            Assert.That(ledger.Apps, Has.Count.EqualTo(UsageLedger.MaxApps + 1));
            Assert.That(other.Lifetimes, Is.EqualTo(2));
            Assert.That(other.Name, Is.Empty, "The shared row names no single executable.");
        });
    }

    [Test]
    public void Reset_ClearsTotalsAndCountsRunningProcessesAgain()
    {
        var ledger = new UsageLedger(Start);
        ledger.Add(Valid(1, [Process(1, 10, "app", 10)]));
        ledger.Reset(Start.AddHours(1));
        var cleared = ledger.Apps.Count;
        ledger.Add(Valid(2, [Process(1, 10, "app", 10)]));

        Assert.Multiple(() =>
        {
            Assert.That(cleared, Is.Zero);
            Assert.That(ledger.Since, Is.EqualTo(Start.AddHours(1)));
            Assert.That(App(ledger, "app").Lifetimes, Is.EqualTo(1));
            Assert.That(App(ledger, "app").CpuTicks, Is.EqualTo(100));
            Assert.That(ledger.MachineBusyTicks, Is.EqualTo(500));
        });
    }

    [Test]
    public void Apps_IsAStableCopyUntilTheNextSample()
    {
        var ledger = new UsageLedger(Start);
        ledger.Add(Valid(1, [Process(1, 10, "app", 10)]));
        var first = ledger.Apps;

        Assert.That(ledger.Apps, Is.SameAs(first));
        ledger.Add(Valid(2, [Process(1, 10, "app", 10)]));
        Assert.That(ledger.Apps, Is.Not.SameAs(first));
    }

    private static AppUsage App(UsageLedger ledger, string key)
    {
        return ledger.Apps.Single(app => string.Equals(app.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static ProcessUsage Process(int id, long creation, string name, double cpu, double? io = null,
        double? gpu = null, string path = "")
    {
        return new ProcessUsage(id, creation, name, cpu, IoBytesPerSecond: io) { GpuPercent = gpu, ImagePath = path };
    }

    private static MonitorSample Valid(double second, IReadOnlyList<ProcessUsage> processes, double totalCpu = 50,
        double seconds = 1)
    {
        return new MonitorSample(Start.AddSeconds(second), totalCpu, processes, 0, false,
            (long)(Stopwatch.Frequency * (second + 1)))
        {
            IntervalSeconds = seconds,
            IntervalCpuTicks = 1_000
        };
    }
}
