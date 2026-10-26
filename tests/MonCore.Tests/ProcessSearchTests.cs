using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

public class ProcessSearchTests
{
    [TestCase(ProcessSort.Cpu, false)]
    [TestCase(ProcessSort.Cpu, true)]
    [TestCase(ProcessSort.Gpu, false)]
    [TestCase(ProcessSort.Gpu, true)]
    [TestCase(ProcessSort.Memory, false)]
    [TestCase(ProcessSort.Memory, true)]
    [TestCase(ProcessSort.Io, false)]
    [TestCase(ProcessSort.Io, true)]
    public void AppsOnly_ComposesWithSearchBeforeTopLimit_AndPreservesGroupedTotals(ProcessSort sort, bool grouped)
    {
        var app = new ProcessUsage(10, 1, "Editor", 5, 100, 100)
            { ImagePath = @"C:\editor.exe", HasAppWindow = true, GpuPercent = 5, PrivateBytes = 100 };
        var helper = app with { ProcessId = 20, CreationTicks = 2, CpuPercent = 10, HasAppWindow = false };
        var background = app with { ProcessId = 30, CreationTicks = 3, Name = "Service", ImagePath = @"C:\service.exe", CpuPercent = 20, HasAppWindow = false };
        var unknown = background with { ProcessId = 40, CreationTicks = 4, Name = "Unknown", ImagePath = @"C:\unknown.exe", HasAppWindow = null };
        var sample = new MonitorSample(DateTimeOffset.UtcNow, 60, [app, helper, background, unknown], 0);
        var rows = ProcessRanking.Rank([sample], sort: sort, grouped: grouped).Processes;
        var filtered = ProcessSearch.Filter(rows, "", appsOnly: true).Take(1).Single();
        Assert.Multiple(() =>
        {
            Assert.That(filtered.Name, Is.EqualTo("Editor"));
            Assert.That(filtered.CpuPercent, Is.EqualTo(grouped ? 15 : 5));
            Assert.That(filtered.MemberCount, Is.EqualTo(grouped ? 2 : 1));
            Assert.That(ProcessSearch.Filter(rows, "editor", appsOnly: true), Has.Exactly(1).Items);
            Assert.That(ProcessSearch.Filter(rows, "30", appsOnly: true), Is.Empty);
            Assert.That(ProcessSearch.Filter(rows, "unknown", appsOnly: true), Is.Empty);
            Assert.That(ProcessSearch.Filter(rows, "20", appsOnly: true).Count(), Is.EqualTo(grouped ? 1 : 0));
            Assert.That(ProcessSearch.Filter(rows, "", appsOnly: false), Is.EqualTo(rows));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AppsOnly_UsesStateFromSelectedHistoricalSample(bool grouped)
    {
        var app = new ProcessUsage(10, 1, "Editor", 5) { HasAppWindow = true };
        var earlier = new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(1), 10, [app], 0);
        var later = earlier with { Timestamp = earlier.Timestamp.AddSeconds(1), Processes = [app with { HasAppWindow = false }] };
        Assert.That(ProcessSearch.Filter(ProcessRanking.Rank([earlier], grouped: grouped).Processes, "", appsOnly: true), Has.Exactly(1).Items);
        Assert.That(ProcessSearch.Filter(ProcessRanking.Rank([earlier, later], grouped: grouped).Processes, "", appsOnly: true), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AppNameSearch_FindsIdleExecutableByDescriptionOrProductName(bool grouped)
    {
        var notepad = new ProcessUsage(120, 1, "pn", 0)
        {
            AppName = "Programmer's Notepad 2", ProductName = "PN Editor", ImagePath = @"C:\Apps\pn.exe"
        };
        MonitorSample[] history = [new(DateTimeOffset.UtcNow, 10, [notepad, notepad with { ProcessId = 240, CreationTicks = 2 }], 0)];
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
        MonitorSample[] history = [new(DateTimeOffset.UtcNow, 10, processes, 0)];
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
            Assert.That(ProcessSearch.Filter(Rows, "12x"), Is.Empty, "Only digits can match a PID.");
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
