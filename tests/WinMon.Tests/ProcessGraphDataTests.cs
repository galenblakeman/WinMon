using System.Diagnostics;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ProcessGraphDataTests
{
    [Test]
    public void Build_MatchesOriginalLookupAndSharesScaleAcrossRankChangesAndMissingLifetimes()
    {
        var identities = Enumerable.Range(1, 5).Select(i => new ProcessUsage(i, i, $"p{i}", 1)).ToArray();
        var history = Enumerable.Range(1, 200).Select(second => new CpuSample(
            DateTimeOffset.UtcNow, 50,
            Enumerable.Range(1, 30).Where(i => (i + second) % 9 != 0)
                .Select(i => new ProcessUsage(i, second % 11 == 0 ? i + 1 : i, $"p{i}", (i * second) % 15 / 10d))
                .OrderByDescending(p => p.CpuPercent).ToArray(), 0, second % 17 == 0,
            Stopwatch.Frequency * second)).ToArray();

        var data = ProcessGraphData.Build(history, 120, identities);
        var visible = history.Where(s => CpuGraph.Moment(s) >= 80).ToArray();
        var sharedPeak = identities.SelectMany(identity => visible.Where(s => !s.IsGap).SelectMany(s => s.Processes)
            .Where(p => p.ProcessId == identity.ProcessId && p.CreationTicks == identity.CreationTicks)
            .Select(p => p.CpuPercent)).DefaultIfEmpty().Max();

        for (var row = 0; row < identities.Length; row++)
        {
            var identity = identities[row];
            var expected = visible.Select(s => s.IsGap ? (double?)null : s.Processes
                .FirstOrDefault(p => p.ProcessId == identity.ProcessId && p.CreationTicks == identity.CreationTicks)?.CpuPercent).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(data[row].Samples, Is.EqualTo(visible));
                Assert.That(data[row].Values, Is.EqualTo(expected));
                Assert.That(data[row].ScaleMaximum, Is.EqualTo(Math.Clamp(Math.Ceiling(sharedPeak * 11) / 10, 0.1, 100)));
            });
        }
        Assert.That(data[0].Samples, Is.SameAs(data[1].Samples));
    }

    [Test]
    public void Build_HistoricalPrefixExcludesFutureSpikeAndPreservesZeroVersusMissing()
    {
        var process = new ProcessUsage(42, 100, "example", 0);
        CpuSample Sample(int second, params ProcessUsage[] processes) =>
            new(DateTimeOffset.UtcNow, 50, processes, 0, false, Stopwatch.Frequency * second);
        CpuSample[] history = [Sample(1, process), Sample(2), Sample(3, process with { CpuPercent = 90 })];

        var data = ProcessGraphData.Build(history.Take(2).ToArray(), 120, [process])[0];

        Assert.Multiple(() =>
        {
            Assert.That(data.Values, Is.EqualTo(new double?[] { 0, null }));
            Assert.That(data.ScaleMaximum, Is.EqualTo(0.1));
            Assert.That(ProcessGraphData.Build([], 120, [process])[0].Values, Is.Empty);
            Assert.That(ProcessGraphData.Build(history, 120, []), Is.Empty);
        });
    }
}
