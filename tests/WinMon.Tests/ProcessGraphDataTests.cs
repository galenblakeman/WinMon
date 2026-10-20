using System.Diagnostics;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ProcessGraphDataTests
{
    [TestCase(ProcessSort.Cpu, false, 2, 3)]
    [TestCase(ProcessSort.Gpu, false, 20, 40)]
    [TestCase(ProcessSort.Memory, false, 2000, 4000)]
    [TestCase(ProcessSort.Io, false, 200, 400)]
    [TestCase(ProcessSort.Cpu, true, 5, 3)]
    [TestCase(ProcessSort.Gpu, true, 40, 40)]
    [TestCase(ProcessSort.Memory, true, 600, 500)]
    [TestCase(ProcessSort.Io, true, 600, 400)]
    public void Build_SelectedMetricUsesMatchingValuesAndSharedScale(ProcessSort sort, bool grouped, double first, double second)
    {
        var a = new ProcessUsage(1, 100, "a", 2, 2000, 200) { PrivateBytes = 100, GpuPercent = 20 };
        var b = new ProcessUsage(2, 200, "a", 3, 4000, 400) { PrivateBytes = 500, GpuPercent = 40 };
        var c = b with { ProcessId = 3, Name = "c" };
        CpuSample[] samples = [new(DateTimeOffset.UtcNow, 50, [a, b, c], 0, false, Stopwatch.Frequency)];

        var data = ProcessGraphData.Build(samples, 120, [a, c], grouped, sort);

        Assert.Multiple(() =>
        {
            Assert.That(data[0].Values, Is.EqualTo(new double?[] { first }));
            Assert.That(data[1].Values, Is.EqualTo(new double?[] { second }));
            Assert.That(data[0].Sort, Is.EqualTo(sort));
            Assert.That(data[0].ScaleMaximum, Is.GreaterThan(Math.Max(first, second)));
            Assert.That(data[1].ScaleMaximum, Is.EqualTo(data[0].ScaleMaximum));
        });
    }

    [TestCase(ProcessSort.Gpu, false)]
    [TestCase(ProcessSort.Memory, false)]
    [TestCase(ProcessSort.Io, false)]
    [TestCase(ProcessSort.Gpu, true)]
    [TestCase(ProcessSort.Memory, true)]
    [TestCase(ProcessSort.Io, true)]
    public void Build_PreservesUnavailableReadingsAndRecordingGaps(ProcessSort sort, bool grouped)
    {
        var now = DateTimeOffset.UtcNow;
        var process = new ProcessUsage(1, now.AddMinutes(-1).ToFileTime(), "a", 1);
        CpuSample[] samples =
        [
            new(now, 50, [process], 0, false, Stopwatch.Frequency),
            new(now.AddSeconds(1), null, [process], 0, true, Stopwatch.Frequency * 2),
            new(now.AddSeconds(2), 50, [], 0, false, Stopwatch.Frequency * 3) { CompactedProcesses = 1 }
        ];

        var data = ProcessGraphData.Build(samples, 120, [process], grouped, sort)[0];

        Assert.That(data.Values, Is.EqualTo(new double?[] { null, null, sort == ProcessSort.Io ? 0 : null }));
    }

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
