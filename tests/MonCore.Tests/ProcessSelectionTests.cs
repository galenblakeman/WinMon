using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ProcessSelectionTests
{
    private static readonly ProcessUsage Target = new(100, 1000, "selected.exe", 80) { ImagePath = @"C:\Apps\selected.exe" };
    private static MonitorSample Sample(int second, params ProcessUsage[] processes) => new(DateTimeOffset.UnixEpoch.AddSeconds(second), 20, processes, 0);

    [Test]
    public void SelectionSurvivesRankingLossAndRefreshesLatestReadings()
    {
        var selection = new ProcessSelection();
        selection.Select(Target, false);
        var idle = Target with { CpuPercent = 0, WorkingSetBytes = 987, ThreadCount = 42 };
        var samples = Enumerable.Range(1, 6).Select(i => Sample(i, idle, new(200, 2000, "busy.exe", 90))).ToArray();
        Assert.That(ProcessRanking.Rank(samples).Processes.Any(p => p.ProcessId == Target.ProcessId), Is.False);
        var resolved = selection.Resolve(samples, false, false, ProcessSort.Cpu)!;
        Assert.That(resolved.State, Is.EqualTo(PinnedProcessState.Present));
        Assert.That(resolved.Usage.ProcessId, Is.EqualTo(Target.ProcessId));
        Assert.That(resolved.Usage.ThreadCount, Is.EqualTo(42));
        Assert.That(resolved.Usage.CpuPercent, Is.Zero);
        Assert.That(selection.Contains(idle, false), Is.True);
    }

    [Test]
    public void ReusedPidNeverReplacesSelectedLifetimeAndMissingDataCanRecover()
    {
        var selection = new ProcessSelection(); selection.Select(Target, false);
        var replacement = Target with { CreationTicks = 2000 };
        var exited = selection.Resolve([Sample(1, replacement)], false, false, ProcessSort.Cpu)!;
        Assert.That(exited.State, Is.EqualTo(PinnedProcessState.Exited));
        Assert.That(exited.Usage.CreationTicks, Is.EqualTo(Target.CreationTicks));
        Assert.That(selection.Contains(replacement, false), Is.False);
        Assert.That(selection.Resolve([Sample(2) with { TotalCpuPercent = null }], false, false, ProcessSort.Cpu)!.State, Is.EqualTo(PinnedProcessState.Unavailable));
        Assert.That(selection.Resolve([Sample(3, Target)], false, false, ProcessSort.Cpu)!.State, Is.EqualTo(PinnedProcessState.Present));
        Assert.That(selection.Resolve([Sample(4, replacement)], false, true, ProcessSort.Cpu)!.State, Is.EqualTo(PinnedProcessState.NotRecorded));
    }

    [Test]
    public void GroupFollowsExecutableAndModeChangeClearsSelection()
    {
        var selection = new ProcessSelection(); selection.Select(Target, true);
        var next = Target with { ProcessId = 300, CreationTicks = 3000, CpuPercent = 1 };
        var resolved = selection.Resolve([Sample(1, next)], true, false, ProcessSort.Cpu)!;
        Assert.That(resolved.State, Is.EqualTo(PinnedProcessState.Present));
        Assert.That(resolved.Usage.MemberIds, Is.EqualTo(new[] { 300 }));
        Assert.That(selection.Resolve([Sample(1, next)], false, false, ProcessSort.Cpu), Is.Null);
        Assert.That(selection.HasSelection, Is.False);
    }

    [Test]
    public void RetentionUsesImmutableSelectionAndPreservesIdleProcess()
    {
        var selection = new ProcessSelection(); selection.Select(Target, false);
        var retain = selection.RetentionSnapshot()!;
        selection.Select(new(300, 3000, "new.exe", 0), false);
        var idle = Target with { CpuPercent = 0, WorkingSetBytes = 1, ThreadCount = 1, HandleCount = 1 };
        var others = Enumerable.Range(1, 60).Select(i => new ProcessUsage(1000 + i, i, "other.exe", 0, 999) { ThreadCount = 99, HandleCount = 99 });
        var sample = Sample(1, others.Append(idle).ToArray());
        Assert.That(ProcessRetention.Compact(sample).Processes, Does.Not.Contain(idle));
        Assert.That(ProcessRetention.Compact(sample, retain: retain).Processes, Does.Contain(idle));
        Assert.That(retain(new(300, 3000, "new.exe", 0)), Is.False);
    }
}
