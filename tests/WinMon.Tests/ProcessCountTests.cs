using System.Buffers.Binary;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ProcessCountTests
{
    [TestCase(ProcessSort.Threads, false)]
    [TestCase(ProcessSort.Handles, false)]
    [TestCase(ProcessSort.Threads, true)]
    [TestCase(ProcessSort.Handles, true)]
    public void CountSort_UsesLatestCountsIncludesIdleAndZeroExcludesExitedAndMissing(ProcessSort sort, bool grouped)
    {
        var a = new ProcessUsage(1, 10, "a", 50) { ThreadCount = 2, HandleCount = 2 };
        var b = new ProcessUsage(2, 20, "group", 0) { ThreadCount = 5, HandleCount = 5 };
        var c = b with { ProcessId = 3, CreationTicks = 30, ThreadCount = 4, HandleCount = 4 };
        var exited = a with { ProcessId = 4, Name = "exited", ThreadCount = 1000, HandleCount = 1000 };
        var missing = new ProcessUsage(5, 50, "missing", 10);
        var tie = a with { ProcessId = 6, Name = "tie" };
        var zero = a with { ProcessId = 7, Name = "zero", ThreadCount = 0, HandleCount = 0 };
        MonitorSample[] history = [Sample(1, a, b with { ThreadCount = 100, HandleCount = 100 }, c, exited), Sample(2, a, b, c, missing, tie, zero)];
        var ranking = ProcessRanking.Rank(history, sort: sort, grouped: grouped);
        Assert.Multiple(() =>
        {
            Assert.That(ranking.Processes.Select(p => p.ProcessId), Is.EqualTo(grouped ? new[] { 2, 1, 6, 7 } : new[] { 2, 3, 1, 6, 7 }));
            Assert.That(sort == ProcessSort.Threads ? ranking.Processes[0].ThreadCount : ranking.Processes[0].HandleCount, Is.EqualTo(grouped ? 9 : 5));
            var historical = ProcessRanking.Rank([history[0]], sort: sort, grouped: grouped);
            Assert.That(historical.Processes.First().ProcessId, Is.EqualTo(4));
        });
    }

    [TestCase(ProcessSort.Threads)]
    [TestCase(ProcessSort.Handles)]
    public void GroupGraph_MissingCountDoesNotBecomePartialTotal(ProcessSort sort)
    {
        var a = new ProcessUsage(1, 10, "group", 0) { ThreadCount = 5, HandleCount = 5 };
        var missing = a with { ProcessId = 2, ThreadCount = null, HandleCount = null };
        var data = ProcessGraphData.Build([Sample(1, a, missing)], 120, [a], grouped: true, sort: sort);
        Assert.That(data[0].Values, Is.EqualTo(new double?[] { null }));
    }

    [Test]
    public void Retention_PreservesTopCountsForCpuIdleHistoricalProcesses()
    {
        var processes = Enumerable.Range(1, 40).Select(id => new ProcessUsage(id, id, $"p{id}", 0, (ulong)(100 - id))
        {
            ThreadCount = id,
            HandleCount = id is 38 or 39 ? 1000 : 1
        }).ToArray();
        var compacted = ProcessRetention.Compact(Sample(1, processes), largestByMemory: 2);
        Assert.That(compacted.Processes.Select(p => p.ProcessId), Is.EqualTo(new[] { 1, 2, 38, 39, 40 }));
        foreach (var sort in new[] { ProcessSort.Threads, ProcessSort.Handles })
            Assert.That(ProcessRanking.Rank([compacted], sort: sort).Processes.Take(2).Select(p => p.ProcessId),
                Is.EqualTo(ProcessRanking.Rank([Sample(1, processes)], sort: sort).Processes.Take(2).Select(p => p.ProcessId)));
    }

    [Test]
    public void Parse_ReadsUnsignedThreadAndHandleFields()
    {
        var buffer = new byte[WindowsCpuSnapshotProvider.EntrySize];
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(0x50), 123);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x04), 47);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x60), 3_000_000_000);

        var process = WindowsCpuSnapshotProvider.Parse(buffer, 0, [], [], out _).Single();
        Assert.Multiple(() =>
        {
            Assert.That(process.ThreadCount, Is.EqualTo(47));
            Assert.That(process.HandleCount, Is.EqualTo(3_000_000_000L));
        });
    }

    [Test]
    public void NativeCapture_ReadsCountsForThisProcess()
    {
        var process = new WindowsCpuSnapshotProvider().Capture().Processes.Single(p => p.ProcessId == Environment.ProcessId);
        Assert.Multiple(() =>
        {
            Assert.That(process.ThreadCount, Is.GreaterThan(0));
            Assert.That(process.HandleCount, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Sampler_RecordsCountsWithoutChangingEarlierSamples()
    {
        var process = new ProcessCpuTimes(10, 100, "example", 0) { ThreadCount = 3, HandleCount = 40 };
        using var sampler = new WindowsCpuSampler(new Snapshots(
            Snapshot(1, process), Snapshot(2, process),
            Snapshot(3, process with { ThreadCount = 7, HandleCount = 80 })));
        sampler.Sample();
        var earlier = sampler.Sample();
        var later = sampler.Sample();
        Assert.Multiple(() =>
        {
            Assert.That(earlier.Processes.Single().ThreadCount, Is.EqualTo(3));
            Assert.That(earlier.Processes.Single().HandleCount, Is.EqualTo(40));
            Assert.That(later.Processes.Single().ThreadCount, Is.EqualTo(7));
            Assert.That(later.Processes.Single().HandleCount, Is.EqualTo(80));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Ranking_UsesSelectedCountsAndGroupsOnlyCurrentMembers(bool grouped)
    {
        var first = new ProcessUsage(1, 10, "example", 2) { ThreadCount = 3, HandleCount = 40 };
        var second = first with { ProcessId = 2, CreationTicks = 20, ThreadCount = 5, HandleCount = 60 };
        MonitorSample[] history =
        [
            Sample(1, first, second),
            Sample(2, first with { ThreadCount = 7, HandleCount = 80 })
        ];
        var earlier = ProcessRanking.Rank([history[0]], grouped: grouped).Processes.First();
        var latest = ProcessRanking.Rank(history, grouped: grouped).Processes.First();
        Assert.Multiple(() =>
        {
            Assert.That(earlier.ThreadCount, Is.EqualTo(grouped ? 8 : 3));
            Assert.That(earlier.HandleCount, Is.EqualTo(grouped ? 100 : 40));
            Assert.That(latest.ThreadCount, Is.EqualTo(7));
            Assert.That(latest.HandleCount, Is.EqualTo(80));
        });
    }

    [Test]
    public void Group_MissingMemberCountDoesNotBecomeAPartialTotal()
    {
        var first = new ProcessUsage(1, 10, "example", 2) { ThreadCount = 3, HandleCount = 40 };
        var second = first with { ProcessId = 2, HandleCount = null };
        var group = ProcessRanking.Rank([Sample(1, first, second)], grouped: true).Processes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(group.ThreadCount, Is.EqualTo(6));
            Assert.That(group.HandleCount, Is.Null);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Row_CountsAlignAfterIo_AndClearForRemainder()
    {
        var row = new ProcessRow();
        row.Update(new ProcessUsage(1, 10, "example", 2, 16UL * 1_048_576, 4_000_000)
            { ThreadCount = 47, HandleCount = 1234 }, [], 120, true, null);
        row.Measure(new Size(788, 36));
        row.Arrange(new Rect(0, 0, 788, 36));
        var threads = row.Children.OfType<TextBlock>().Single(t => Grid.GetColumn(t) == 6);
        var handles = row.Children.OfType<TextBlock>().Single(t => Grid.GetColumn(t) == 7);
        var memory = row.Children.OfType<TextBlock>().Single(t => Grid.GetColumn(t) == 4);
        var io = row.Children.OfType<TextBlock>().Single(t => Grid.GetColumn(t) == 5);
        Assert.Multiple(() =>
        {
            Assert.That(memory.Text, Is.EqualTo("16.0 MiB"));
            Assert.That(io.Text, Is.EqualTo("4.0 MB/s"));
            Assert.That(threads.Text, Is.EqualTo("47"));
            Assert.That(handles.Text, Is.EqualTo(1234.ToString("N0")));
            Assert.That(row.ColumnDefinitions[0].ActualWidth, Is.GreaterThanOrEqualTo(200));
            Assert.That(row.ColumnDefinitions[1].ActualWidth, Is.EqualTo(160));
        });
        row.ShowRemainder(3, 1, 0);
        Assert.That(new[] { threads.Text, handles.Text }, Is.All.Empty);
        Assert.That(io.Text, Is.EqualTo("0 B/s"));
        row.Update(new ProcessUsage(1, 10, "example", 2), [], 120, true, null);
        Assert.That(threads.Text, Is.EqualTo(WinMon.Resources.Strings.MissingReading));
        Assert.That(handles.Text, Is.EqualTo(WinMon.Resources.Strings.MissingReading));
    }

    private static MonitorSample Sample(int second, params ProcessUsage[] processes) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 10, processes, 0, false, second * Stopwatch.Frequency);

    private static CpuSnapshot Snapshot(int second, ProcessCpuTimes process) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), second * Stopwatch.Frequency,
            new SystemCpuTimes(second * 100, second * 500, second * 500), [process], 0);

    private sealed class Snapshots(params CpuSnapshot[] snapshots) : ICpuSnapshotProvider
    {
        private readonly Queue<CpuSnapshot> _snapshots = new(snapshots);
        public CpuSnapshot Capture() => _snapshots.Dequeue();
    }
}
