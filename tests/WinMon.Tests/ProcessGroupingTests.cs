using System.Diagnostics;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ProcessGroupingTests
{
    private const string ChromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

    private static CpuSample Sample(double second, params ProcessUsage[] processes) =>
        new(DateTimeOffset.UtcNow, 50, processes, 0, false, (long)(Stopwatch.Frequency * second));

    private static ProcessUsage Usage(int id, string name, string path, double cpu, ulong? privateBytes = null,
        double? io = null, double? gpu = null, string engine = "") =>
        new(id, id * 10, name, cpu, privateBytes * 2, io) { ImagePath = path, PrivateBytes = privateBytes, GpuPercent = gpu, GpuEngine = engine };

    [Test]
    public void Rank_GroupsByExecutablePath_SummingCpuIoAndPrivateMemory_WithBusiestGpu()
    {
        CpuSample[] history =
        [
            Sample(1, Usage(1, "chrome", ChromePath, 4, 100, io: 10, gpu: 5, engine: "3D"), Usage(2, "chrome", ChromePath, 2, 50, io: 20, gpu: 30, engine: "VideoDecode"),
                Usage(3, "devenv", "", 3, 400)),
            Sample(2, Usage(1, "chrome", ChromePath, 4, 120, io: 10, gpu: 5, engine: "3D"), Usage(2, "chrome", ChromePath, 2, 60, io: 20, gpu: 30, engine: "VideoDecode"),
                Usage(3, "devenv", "", 3, 400)),
        ];

        var ranking = ProcessRanking.Rank(history, grouped: true);
        var chrome = ranking.Processes[0];

        Assert.Multiple(() =>
        {
            Assert.That(ranking.Processes.Select(p => (p.Name, p.MemberCount)), Is.EqualTo(new[] { ("chrome", 2), ("devenv", 1) }));
            Assert.That(chrome.ProcessId, Is.EqualTo(1), "The busiest member represents the group.");
            Assert.That(chrome.CpuPercent, Is.EqualTo(6).Within(1e-9));
            Assert.That(chrome.IoBytesPerSecond, Is.EqualTo(30).Within(1e-9));
            Assert.That(chrome.GpuPercent, Is.EqualTo(30), "GPU is the busiest member, not a sum of engines.");
            Assert.That(chrome.GpuEngine, Is.EqualTo("VideoDecode"));
            Assert.That(chrome.PrivateBytes, Is.EqualTo(180), "Private bytes of the newest sample are summed.");
            Assert.That(chrome.MemberIds, Is.EqualTo(new[] { 1, 2 }));
        });
    }

    [Test]
    public void Rank_KeepsSameNamedExecutablesInDifferentFoldersApart_AndFallsBackToNames()
    {
        CpuSample[] history =
        [
            Sample(1, Usage(1, "setup", @"C:\A\setup.exe", 1), Usage(2, "setup", @"C:\B\setup.exe", 1),
                Usage(3, "svchost", "", 1), Usage(4, "svchost", "", 1))
        ];

        var ranking = ProcessRanking.Rank(history, grouped: true);

        Assert.That(ranking.Processes.Select(p => (p.Name, p.MemberCount)).Order(),
            Is.EqualTo(new[] { ("setup", 1), ("setup", 1), ("svchost", 2) }));
    }

    [Test]
    public void Rank_GroupedMemory_UsesCurrentMembersOnly_AndRanksByPrivateBytes()
    {
        // Member 2 exited before the newest sample: it still counts as a member, but not toward current memory.
        CpuSample[] history =
        [
            Sample(1, Usage(1, "chrome", ChromePath, 1, 100), Usage(2, "chrome", ChromePath, 1, 900), Usage(3, "devenv", "", 0, 500)),
            Sample(2, Usage(1, "chrome", ChromePath, 1, 100), Usage(3, "devenv", "", 0, 500)),
        ];

        var ranking = ProcessRanking.Rank(history, sort: ProcessSort.Memory, grouped: true);

        Assert.That(ranking.Processes.Select(p => (p.Name, p.PrivateBytes, p.MemberCount)),
            Is.EqualTo(new (string, ulong?, int)[] { ("devenv", 500, 1), ("chrome", 100, 2) }));
    }

    [Test]
    public void Sparklines_SumGroupMembers_ZeroWhenCompactedIdle_AndGapBeforeTheGroupStarted()
    {
        var now = DateTimeOffset.UtcNow;
        var first = new ProcessUsage(1, now.AddMinutes(-5).ToFileTime(), "chrome", 1) { ImagePath = ChromePath };
        var second = new ProcessUsage(2, now.AddMinutes(-5).ToFileTime(), "chrome", 2) { ImagePath = ChromePath };
        var later = new ProcessUsage(3, now.ToFileTime(), "late", 1);
        CpuSample[] history =
        [
            new(now.AddSeconds(-2), 50, [], 0, false, Stopwatch.Frequency) { CompactedProcesses = 3 },
            new(now.AddSeconds(1), 50, [first, second, later], 0, false, Stopwatch.Frequency * 2),
        ];

        var data = ProcessGraphData.Build(history, 120, [first, later], grouped: true);

        Assert.Multiple(() =>
        {
            Assert.That(data[0].Values, Is.EqualTo(new double?[] { 0, 3 }));
            Assert.That(data[1].Values, Is.EqualTo(new double?[] { null, 1 }));
            Assert.That(data[0].ScaleMaximum, Is.EqualTo(3.3).Within(1e-9));
        });
    }
}
