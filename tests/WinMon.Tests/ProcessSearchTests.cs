using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

public class ProcessSearchTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void AppNameSearch_FindsIdleExecutableByDescriptionOrProductName(bool grouped)
    {
        var notepad = new ProcessUsage(120, 1, "pn", 0)
        {
            AppName = "Programmer's Notepad 2", ProductName = "PN Editor", ImagePath = @"C:\Apps\pn.exe"
        };
        CpuSample[] history = [new(DateTimeOffset.UtcNow, 10, [notepad, notepad with { ProcessId = 240, CreationTicks = 2 }], 0)];
        var rows = ProcessRanking.Rank(history, grouped: grouped, includeInactive: true).Processes;

        Assert.Multiple(() =>
        {
            Assert.That(ProcessSearch.Filter(rows, "  pRo  ").ToArray(), Has.Length.EqualTo(grouped ? 1 : 2));
            Assert.That(ProcessSearch.Filter(rows, "EDITOR"), Is.EqualTo(rows));
            Assert.That(ProcessSearch.Filter(rows, "pn"), Is.EqualTo(rows));
            Assert.That(ProcessSearch.Filter(rows, "240").Single().MemberCount, Is.EqualTo(grouped ? 2 : 1));
            Assert.That(ProcessSearch.Filter(rows, "absent"), Is.Empty);
        });
    }

    [TestCase(ProcessSort.Cpu, false)]
    [TestCase(ProcessSort.Cpu, true)]
    [TestCase(ProcessSort.Gpu, false)]
    [TestCase(ProcessSort.Gpu, true)]
    [TestCase(ProcessSort.Memory, false)]
    [TestCase(ProcessSort.Memory, true)]
    [TestCase(ProcessSort.Io, false)]
    [TestCase(ProcessSort.Io, true)]
    public void Search_FindsIdleAndMissingMetricProcessesBeforeTopLimit(ProcessSort sort, bool grouped)
    {
        ProcessUsage[] processes =
        [
            new(10, 1, "Busy", 10, 100, 100) { GpuPercent = 10, PrivateBytes = 100 },
            new(20, 2, "Idle", 0, 0, 0) { GpuPercent = 0, PrivateBytes = 0 },
            new(30, 3, "Unavailable", 0)
        ];
        CpuSample[] history = [new(DateTimeOffset.UtcNow, 10, processes, 0)];
        var normal = ProcessRanking.Rank(history, sort: sort, grouped: grouped);
        var searchable = ProcessRanking.Rank(history, sort: sort, grouped: grouped, includeInactive: true);

        Assert.Multiple(() =>
        {
            Assert.That(normal.Processes.Select(p => p.ProcessId), Is.EqualTo(new[] { 10 }));
            Assert.That(searchable.Processes.Select(p => p.ProcessId), Is.EqualTo(new[] { 10, 20, 30 }));
            Assert.That(ProcessSearch.Filter(searchable.Processes, "idle").Take(1).Single().ProcessId, Is.EqualTo(20));
            Assert.That(ProcessSearch.Filter(searchable.Processes, "30").Take(1).Single().ProcessId, Is.EqualTo(30));
            Assert.That(searchable.MeasuredCpuPercent, Is.EqualTo(normal.MeasuredCpuPercent));
            Assert.That(searchable.Processes.Single(p => p.ProcessId == 30).GpuPercent, Is.Null);
        });
    }

    private static readonly ProcessUsage[] Rows =
    [
        new(120, 1, "Editor", 8),
        new(240, 2, "msedge", 4) { MemberCount = 2, MemberIds = [240, 360] },
        new(480, 3, "WinMon", 1)
    ];

    [Test]
    public void NameSearch_IgnoresCaseAndSurroundingWhitespace()
    {
        Assert.That(ProcessSearch.Filter(Rows, "  EDGE  "), Is.EqualTo([Rows[1]]));
    }

    [Test]
    public void PidSearch_FindsIndividualAndNonRepresentativeGroupMember()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProcessSearch.Filter(Rows, "120"), Is.EqualTo([Rows[0]]));
            Assert.That(ProcessSearch.Filter(Rows, "360"), Is.EqualTo([Rows[1]]));
            Assert.That(ProcessSearch.Filter(Rows, "360").Single().MemberCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void FilteringBeforeTopLimit_FindsLowerRankedRowsAndPreservesOrder()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProcessSearch.Filter(Rows, "WinMon").Take(1), Is.EqualTo([Rows[2]]));
            Assert.That(ProcessSearch.Filter(Rows, "0"), Is.EqualTo(Rows));
            Assert.That(ProcessSearch.Filter(Rows, "  "), Is.EqualTo(Rows));
            Assert.That(ProcessSearch.Filter(Rows, "absent"), Is.Empty);
        });
    }
}
