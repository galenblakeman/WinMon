using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ServiceRankingTests
{
    [Test]
    public void Rankings_MatchProcessFiveSecondAverages_AndUseLatestPrivateMemory()
    {
        var frames = Enumerable.Range(1, 7).Select(i => Frame(i, P(1, 100, i, i * 100, (ulong)i),
            P(2, 200, i * 2, i * 200, (ulong)(i * 2)))).ToArray();
        var ranked = ServiceRanking.Build(frames, true);
        var processes = ProcessRanking.Rank(frames.Select(f => f.Sample with { Processes = f.Groups[0].Processes }).ToArray(), includeInactive: true);
        Assert.Multiple(() =>
        {
            Assert.That(ranked.IntervalCount, Is.EqualTo(processes.IntervalCount).And.EqualTo(5));
            Assert.That(ranked.Groups["host:1:100"].Cpu, Is.EqualTo(processes.Processes.Sum(p => p.CpuPercent)));
            Assert.That(ranked.Groups["host:1:100"].Io, Is.EqualTo(processes.Processes.Sum(p => p.IoBytesPerSecond)));
            Assert.That(ranked.Groups["host:1:100"].PrivateBytes, Is.EqualTo(21));
            Assert.That(ranked.Processes[(1, 100)].Cpu, Is.EqualTo(5));
            Assert.That(ranked.Processes[(1, 100)].PrivateBytes, Is.EqualTo(7));
            Assert.That(ServiceRanking.Build(frames, false).Groups["host:1:100"].Cpu, Is.EqualTo(5));
        });
    }

    [TestCase("gap")]
    [TestCase("unavailable")]
    [TestCase("cpu")]
    [TestCase("interval")]
    public void Window_DoesNotCrossRecordingOrDiscoveryGaps(string reason)
    {
        var older = Frame(1, P(1, 100, 80, 800, 1));
        var middle = Frame(2, P(1, 100, 80, 800, 1));
        middle = reason switch
        {
            "gap" => middle with { Sample = middle.Sample with { IsGap = true } },
            "unavailable" => middle with { Available = false },
            "cpu" => middle with { Sample = middle.Sample with { TotalCpuPercent = null } },
            _ => middle
        };
        var latest = Frame(reason == "interval" ? 5 : 3, P(1, 100, 2, 20, 3));
        var ranking = ServiceRanking.Build([older, middle, latest], true);
        Assert.That(ranking.IntervalCount, Is.EqualTo(1));
        Assert.That(ranking.Groups["host:1:100"], Is.EqualTo(new ServiceMetrics(2, 3, 20)));
    }

    [Test]
    public void RestartedPid_DoesNotInheritPreviousLifetimeUsage_AndSharedHostHasOneTotal()
    {
        var frames = new[] { Frame(1, P(1, 100, 90, 900, 1)), Frame(2, P(1, 200, 10, 100, 2)) };
        var latest = frames[1].Groups[0];
        frames[1] = frames[1] with { Groups = [latest with { Services = [new("one", "One", 4, 1, 1), new("two", "Two", 4, 1, 1)] }] };
        var ranking = ServiceRanking.Build(frames, true);
        Assert.That(ranking.Groups, Has.Count.EqualTo(1));
        Assert.That(ranking.Groups["host:1:200"], Is.EqualTo(new ServiceMetrics(5, 2, 50)));
        Assert.That(ranking.Processes.Keys, Is.EquivalentTo(new[] { (1, 200L) }));
    }

    [Test]
    public void HistoricalPrefix_ExcludesFutureSpikes_AndUnknownIoRemainsUnavailable()
    {
        var frames = new[] { Frame(1, P(1, 100, 2, null, 1)), Frame(2, P(1, 100, 4, null, 2)), Frame(3, P(1, 100, 90, 900, 3)) };
        var ranking = ServiceRanking.Build(frames.Take(2).ToArray(), true);
        Assert.That(ranking.Groups["host:1:100"], Is.EqualTo(new ServiceMetrics(3, 2, null)));
        Assert.That(ServiceRanking.Build([], true).IntervalCount, Is.Zero);
    }

    [Test]
    public void ExitedWorker_RemainsInCpuIoWindow_ButNotLatestMemory()
    {
        var ranking = ServiceRanking.Build([
            Frame(1, P(1, 100, 2, 20, 10), P(2, 200, 8, 80, 100)),
            Frame(2, P(1, 100, 2, 20, 20))], true);
        Assert.That(ranking.Groups["host:1:100"], Is.EqualTo(new ServiceMetrics(6, 20, 60)));
        Assert.That(ranking.Processes.ContainsKey((2, 200)), Is.False);
    }

    internal static ProcessUsage P(int pid, long created, double cpu, double? io, ulong memory) =>
        new(pid, created, "p" + pid, cpu, IoBytesPerSecond: io) { PrivateBytes = memory };
    internal static ServiceFrame Frame(int second, params ProcessUsage[] processes) => new(
        new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(second), 50, [], 0, MonotonicTimestamp: second * Stopwatch.Frequency),
        [new ServiceGroup($"host:{processes[0].ProcessId}:{processes[0].CreationTicks}",
            [new("agent", "Agent", 4, processes[0].ProcessId, 1)], processes[0], processes)]);
}
