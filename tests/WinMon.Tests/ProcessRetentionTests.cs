using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ProcessRetentionTests
{
    private static ProcessUsage Usage(int id, double cpu = 0, ulong? memory = null, double? io = null, double? gpu = null) =>
        new(id, id * 10, $"p{id}", cpu, memory, io) { GpuPercent = gpu };

    [Test]
    public void Compact_KeepsActiveAndLargestMemory_AndCountsTheRest()
    {
        var sample = new CpuSample(DateTimeOffset.UtcNow, 10,
        [
            Usage(1, cpu: 2, memory: 10), Usage(2, io: 5, memory: 10), Usage(3, gpu: 0, memory: 10),
            Usage(4, memory: 900), Usage(5, memory: 800), Usage(6, memory: 10), Usage(7, memory: 20), Usage(8)
        ], 0);

        var compacted = ProcessRetention.Compact(sample, largestByMemory: 2);

        Assert.Multiple(() =>
        {
            Assert.That(compacted.Processes.Select(p => p.ProcessId), Is.EqualTo([1, 2, 3, 4, 5]));
            Assert.That(compacted.CompactedProcesses, Is.EqualTo(3));
            Assert.That(ProcessRetention.Compact(compacted, largestByMemory: 2), Is.SameAs(compacted));
        });
    }

    [Test]
    public void Compact_KeepsEveryProcessTiedAtTheMemoryCutOff()
    {
        var sample = new CpuSample(DateTimeOffset.UtcNow, 10,
            [Usage(1, memory: 500), Usage(2, memory: 300), Usage(3, memory: 300), Usage(4, memory: 100), Usage(5)], 0);

        var compacted = ProcessRetention.Compact(sample, largestByMemory: 2);

        Assert.Multiple(() =>
        {
            Assert.That(compacted.Processes.Select(p => p.ProcessId), Is.EqualTo([1, 2, 3]));
            Assert.That(compacted.CompactedProcesses, Is.EqualTo(2));
        });
    }

    [Test]
    public void Compact_LeavesSmallSamplesUnchanged()
    {
        var sample = new CpuSample(DateTimeOffset.UtcNow, 10, [Usage(1), Usage(2)], 0);
        Assert.That(ProcessRetention.Compact(sample), Is.SameAs(sample));
    }

    [TestCase(ProcessSort.Memory)]
    [TestCase(ProcessSort.Threads)]
    [TestCase(ProcessSort.Handles)]
    public void Compact_PreservesHistoricalTop50ForIdleBackgroundProcesses(ProcessSort sort)
    {
        var processes = Enumerable.Range(1, 60).Select(id =>
            Usage(id, memory: (ulong)(100 - id)) with
            {
                ThreadCount = 100 - id,
                HandleCount = 100 - id
            }).ToArray();
        var full = new CpuSample(DateTimeOffset.UtcNow, 10, processes, 0);
        var compacted = ProcessRetention.Compact(full);
        var expected = ProcessRanking.Rank([full], sort: sort).Processes.Take(50).ToArray();
        var actual = ProcessRanking.Rank([compacted], sort: sort).Processes.Take(50).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(compacted.CompactedProcesses, Is.EqualTo(10));
        });
    }

    [Test]
    public void Ranking_TreatsCompactedProcessesAsIdle()
    {
        // p2 had CPU in the first interval and was compacted out of the second as idle.
        var full = new CpuSample(DateTimeOffset.UtcNow, 10, [Usage(1, cpu: 4), Usage(2, cpu: 2, io: 6)], 0, false, Stopwatch.Frequency);
        var idle = new CpuSample(DateTimeOffset.UtcNow, 10, [Usage(1, cpu: 4)], 0, false, Stopwatch.Frequency * 2) { CompactedProcesses = 1 };

        var ranking = ProcessRanking.Rank([full, idle]);

        Assert.That(ranking.Processes.Select(p => (p.ProcessId, p.CpuPercent, p.IoBytesPerSecond)),
            Is.EqualTo(new (int, double, double?)[] { (1, 4, null), (2, 1, 3) }));
    }

    [Test]
    public void Sparklines_DrawZeroForCompactedIdleProcesses_AndGapsBeforeCreation()
    {
        var created = DateTimeOffset.UtcNow;
        var existing = new ProcessUsage(1, created.AddMinutes(-5).ToFileTime(), "old", 1);
        var newer = new ProcessUsage(2, created.ToFileTime(), "new", 1);
        CpuSample[] history =
        [
            new(created.AddSeconds(-1), 10, [], 0, false, Stopwatch.Frequency) { CompactedProcesses = 2 },
            new(created.AddSeconds(1), 10, [existing, newer], 0, false, Stopwatch.Frequency * 2),
        ];

        var data = ProcessGraphData.Build(history, 120, [existing, newer]);

        Assert.Multiple(() =>
        {
            Assert.That(data[0].Values, Is.EqualTo(new double?[] { 0, 1 }));
            Assert.That(data[1].Values, Is.EqualTo(new double?[] { null, 1 }));
        });
    }

    [Test]
    public void Parse_ReadsSystemProcessInformation_SkipsIdle_AndReusesNames()
    {
        var buffer = new byte[3 * WindowsCpuSnapshotProvider.EntrySize + 64];
        const nint baseAddress = 0x10000;
        WriteEntry(buffer, 0, next: WindowsCpuSnapshotProvider.EntrySize, pid: 0, created: 0, "");
        WriteEntry(buffer, WindowsCpuSnapshotProvider.EntrySize, next: WindowsCpuSnapshotProvider.EntrySize, pid: 4, created: 11, "System",
            nameAt: 3 * WindowsCpuSnapshotProvider.EntrySize, user: 5, kernel: 7, workingSet: 4096, read: 100, write: 50, privateBytes: 2048);
        WriteEntry(buffer, 2 * WindowsCpuSnapshotProvider.EntrySize, next: 0, pid: 1234, created: 22, "Chrome.EXE",
            nameAt: 3 * WindowsCpuSnapshotProvider.EntrySize + 16, user: 1, kernel: 2, workingSet: 8192, read: 3, write: 4, privateBytes: 1024);
        var previous = new Dictionary<(int, long), string> { [(4, 11)] = "cached" };
        var names = new Dictionary<(int, long), string>();

        var processes = WindowsCpuSnapshotProvider.Parse(buffer, baseAddress, previous, names, out var unavailable);

        Assert.Multiple(() =>
        {
            Assert.That(unavailable, Is.Zero);
            Assert.That(processes, Is.EqualTo([
                new ProcessCpuTimes(4, 11, "cached", 12, 4096, 100, 50, 2048) { ThreadCount = 0, HandleCount = 0 },
                new ProcessCpuTimes(1234, 22, "Chrome", 3, 8192, 3, 4, 1024) { ThreadCount = 0, HandleCount = 0 }
            ]));
            Assert.That(names.Keys, Is.EquivalentTo([(4, 11L), (1234, 22L)]));
        });

        void WriteEntry(byte[] target, int offset, int next, long pid, long created, string name, int nameAt = 0,
            long user = 0, long kernel = 0, ulong workingSet = 0, ulong read = 0, ulong write = 0, ulong privateBytes = 0)
        {
            var entry = target.AsSpan(offset, WindowsCpuSnapshotProvider.EntrySize);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, (uint)next);
            BinaryPrimitives.WriteInt64LittleEndian(entry[0x20..], created);
            BinaryPrimitives.WriteInt64LittleEndian(entry[0x28..], user);
            BinaryPrimitives.WriteInt64LittleEndian(entry[0x30..], kernel);
            BinaryPrimitives.WriteInt64LittleEndian(entry[0x50..], pid);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[0x90..], workingSet);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[0xE8..], read);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[0xF0..], write);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[0xC8..], privateBytes);
            if (name.Length == 0) return;
            var bytes = Encoding.Unicode.GetBytes(name);
            bytes.CopyTo(target.AsSpan(nameAt));
            BinaryPrimitives.WriteUInt16LittleEndian(entry[0x38..], (ushort)bytes.Length);
            BinaryPrimitives.WriteInt64LittleEndian(entry[0x40..], baseAddress + nameAt);
        }
    }
}
