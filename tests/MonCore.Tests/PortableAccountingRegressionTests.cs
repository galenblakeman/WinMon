using System.Diagnostics;
using System.Text.Json;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class PortableAccountingRegressionTests
{
    private static ProcessUsage Process(int pid, string path, double cpu = 10) =>
        new(pid, pid, "worker", cpu, 100, 1024)
        {
            LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds, ImagePath = path,
            PrivateBytes = 50, ThreadCount = 2, HandleCount = 3, GpuPercent = 20
        };

    private static MonitorSample Sample(int second, params ProcessUsage[] processes) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 50, processes, 0, false, second * Stopwatch.Frequency)
        { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000, IndependentProcessReadings = true };

    [Test]
    public void Rank_ExecKeepsEachExecutablesActivityAndOnlyCurrentMemory()
    {
        var before = Process(1, "/old", 80);
        var after = before with { ImagePath = "/new", CpuPercent = 20, IoBytesPerSecond = 2048, GpuPercent = 40 };
        var ranking = ProcessRanking.Rank([Sample(1, before), Sample(2, after)], grouped: true,
            includeInactive: true, identityPolicy: ProcessIdentityPolicy.ExactPaths);
        var old = ranking.Processes.Single(p => p.ImagePath == "/old");
        var current = ranking.Processes.Single(p => p.ImagePath == "/new");
        Assert.Multiple(() =>
        {
            Assert.That(old.CpuPercent, Is.EqualTo(40));
            Assert.That(current.CpuPercent, Is.EqualTo(10));
            Assert.That(ranking.MeasuredCpuPercent, Is.EqualTo(50));
            Assert.That(old.IoBytesPerSecond, Is.EqualTo(512));
            Assert.That(current.IoBytesPerSecond, Is.EqualTo(1024));
            Assert.That(old.GpuPercent, Is.EqualTo(20));
            Assert.That(current.GpuPercent, Is.EqualTo(40));
            Assert.That(old.PrivateBytes, Is.Null);
            Assert.That(old.ThreadCount, Is.Null);
            Assert.That(current.PrivateBytes, Is.EqualTo(50));
            Assert.That(current.ThreadCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void Rank_CustomExecutableComparerKeepsAliasesInOneCurrentGroup()
    {
        var policy = new ProcessIdentityPolicy("aliases", StringComparer.OrdinalIgnoreCase, p => p.ImagePath);
        var ranking = ProcessRanking.Rank([Sample(1, Process(1, "/App", 80)), Sample(2, Process(1, "/app", 20))],
            grouped: true, identityPolicy: policy);
        Assert.Multiple(() =>
        {
            Assert.That(ranking.Processes, Has.Count.EqualTo(1));
            Assert.That(ranking.Processes.Single().CpuPercent, Is.EqualTo(50));
            Assert.That(ranking.Processes.Single().PrivateBytes, Is.EqualTo(50));
            Assert.That(ranking.Processes.Single().ImagePath, Is.EqualTo("/app"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Usage_IndependentReadingsSurviveMissingSystemCpu(bool processCpuAvailable)
    {
        var process = Process(1, "/worker") with { CpuAvailable = processCpuAvailable };
        var sample = Sample(1, process) with { TotalCpuPercent = null };
        var ledger = new UsageLedger(DateTimeOffset.UnixEpoch, ProcessIdentityPolicy.ExactPaths);
        var history = new UsageHistory(identityPolicy: ProcessIdentityPolicy.ExactPaths);
        ledger.Add(sample);
        history.Add(sample);
        var app = ledger.Apps.Single();
        var period = history.Query(sample, 1)!;
        Assert.Multiple(() =>
        {
            Assert.That(app.CpuTicks, Is.EqualTo(processCpuAvailable ? 1_000_000 : 0));
            Assert.That(app.IoBytes, Is.EqualTo(1024));
            Assert.That(app.GpuSeconds, Is.EqualTo(0.2));
            Assert.That(app.Running, Is.EqualTo(1));
            Assert.That(ledger.MachineBusyTicks, Is.Zero);
            Assert.That(history.IsAvailableAt(sample), Is.True);
            Assert.That(period.Apps.Single().IoBytes, Is.EqualTo(1024));
            Assert.That(period.Apps.Single().CpuTicks, Is.EqualTo(app.CpuTicks));
            Assert.That(period.Complete, Is.False);
        });
        ledger.Add(Sample(2, process) with { TotalCpuPercent = null, IndependentProcessReadings = false });
        ledger.Add(Sample(3, process) with { IsGap = true });
        Assert.That(ledger.Apps.Single().IoBytes, Is.EqualTo(1024), "Windows baselines and gaps still contribute nothing.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Usage_OverflowCountsRealExecutableChangesAcrossRestore(bool restore)
    {
        var ledger = new UsageLedger(DateTimeOffset.UnixEpoch, ProcessIdentityPolicy.ExactPaths);
        var initial = Enumerable.Range(1, UsageLedger.MaxApps + 1).Select(i => Process(i, "/app/" + i)).ToArray();
        ledger.Add(Sample(1, initial));
        Assert.That(ledger.Apps.Single(a => a.Key == UsageLedger.OtherAppsKey).Lifetimes, Is.EqualTo(1));
        if (restore)
        {
            var state = JsonSerializer.Deserialize<UsageLedgerState>(JsonSerializer.Serialize(ledger.Export()))!;
            ledger = new UsageLedger(DateTimeOffset.UnixEpoch, ProcessIdentityPolicy.ExactPaths);
            ledger.Restore(state);
        }
        var overflow = initial[^1];
        ledger.Add(Sample(2, overflow));
        var unchanged = ledger.Apps.Single(a => a.Key == UsageLedger.OtherAppsKey);
        ledger.Add(Sample(3, overflow with { ImagePath = "/replacement" }));
        var changed = ledger.Apps.Single(a => a.Key == UsageLedger.OtherAppsKey);
        Assert.Multiple(() =>
        {
            Assert.That(unchanged.Lifetimes, Is.EqualTo(1));
            Assert.That(unchanged.Running, Is.EqualTo(1));
            Assert.That(unchanged.IoBytes, Is.EqualTo(2048));
            Assert.That(changed.Lifetimes, Is.EqualTo(2));
            Assert.That(changed.Running, Is.EqualTo(1));
            Assert.That(changed.IoBytes, Is.EqualTo(3072));
            Assert.That(ledger.Apps.Count, Is.EqualTo(UsageLedger.MaxApps + 1));
        });
    }
}
