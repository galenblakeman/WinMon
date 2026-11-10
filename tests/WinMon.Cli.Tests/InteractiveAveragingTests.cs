using NUnit.Framework;
using WinMon.Cli;
using WinMon.Monitoring;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class InteractiveAveragingTests
{
    private static MonitorSample Sample(int second, double cpu, double io, double? gpu = null) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 90,
        [
            new ProcessUsage(1, 1, "spiky", cpu, (ulong)(1000 + second), io)
                { GpuPercent = gpu, ThreadCount = second, HandleCount = second * 10 },
            new ProcessUsage(2, 2, "steady", 30, 500, 200) { GpuPercent = 40 }
        ], 0) { IntervalSeconds = 1 };

    private static MonitorSample[] Window() =>
    [
        Sample(0, 1000, 10000), // Outside the five-second window.
        Sample(1, 0, 0), Sample(2, 0, 0), Sample(3, 0, 0), Sample(4, 0, 0),
        Sample(5, 90, 500, 100)
    ];

    private static SnapshotReport Report(InteractiveState state, ProcessSort sort) =>
        SnapshotReport.Create(state.DisplayedFrame!, "test", 10, sort, "", state.DisplayedSamples);

    [TestCase(ProcessSort.Cpu)]
    [TestCase(ProcessSort.Io)]
    public void UsesDesktopFiveSecondWindowInsteadOfLatestSpike(ProcessSort sort)
    {
        var history = Window();
        var state = new InteractiveState(sort, "");
        state.Observe(history[^1], null, history);
        var report = Report(state, sort);
        var desktop = ProcessRanking.Rank(history, sort: sort, includeInactive: true);
        Assert.Multiple(() =>
        {
            Assert.That(report.Processes.Select(p => p.Pid), Is.EqualTo(desktop.Processes.Select(p => p.ProcessId)));
            Assert.That(report.Processes[0].Pid, Is.EqualTo(2));
            Assert.That(report.Processes.Single(p => p.Pid == 1).CpuPercent, Is.EqualTo(18));
            Assert.That(report.Processes.Single(p => p.Pid == 1).IoBytesPerSecond, Is.EqualTo(100));
            Assert.That(report.CpuPercent, Is.EqualTo(90), "System summary remains the latest reading");
        });
    }

    [Test]
    public void GpuUsesReadableIntervalsAndMemoryCountsUseLatestValues()
    {
        var history = Window();
        var state = new InteractiveState(ProcessSort.Gpu, "");
        state.Observe(history[^1], null, history);
        var process = Report(state, ProcessSort.Gpu).Processes[0];
        Assert.Multiple(() =>
        {
            Assert.That(process.Pid, Is.EqualTo(1));
            Assert.That(process.GpuPercent, Is.EqualTo(100));
            Assert.That(process.WorkingSetBytes, Is.EqualTo(1005));
            Assert.That(process.Threads, Is.EqualTo(5));
            Assert.That(process.Handles, Is.EqualTo(50));
        });
    }

    [Test]
    public void ReplacesNewestCompactedFrameWithFullFrameLikeDesktop()
    {
        var history = Window();
        var full = history[^1];
        history[^1] = full with { Processes = [] };
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Observe(full, null, history);
        Assert.That(state.DisplayedSamples[^1], Is.SameAs(full));
        Assert.That(Report(state, ProcessSort.Cpu).Processes.Single(p => p.Pid == 1).CpuPercent, Is.EqualTo(18));
    }

    [Test]
    public void PauseFreezesEntireAveragingWindowAndResumeUsesLatestWindow()
    {
        var history = Window();
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Observe(history[^1], null, history);
        var pause = new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false);
        state.Handle(pause, 5);
        var next = Enumerable.Range(10, 5).Select(i => Sample(i, 50, 500)).ToArray();
        state.Observe(next[^1], null, next);
        Assert.Multiple(() =>
        {
            Assert.That(state.DisplayedSamples.Count, Is.EqualTo(history.Length));
            Assert.That(Report(state, ProcessSort.Cpu).Processes[0].Pid, Is.EqualTo(2));
            Assert.That(Report(state, ProcessSort.Io).Processes.Single(p => p.Pid == 1).IoBytesPerSecond, Is.EqualTo(100));
        });
        state.Handle(pause, 5);
        Assert.That(Report(state, ProcessSort.Cpu).Processes[0].Pid, Is.EqualTo(1));
        Assert.That(Report(state, ProcessSort.Cpu).Processes[0].CpuPercent, Is.EqualTo(50));
    }

    [Test]
    public void GapExcludesOlderIntervalsAndSnapshotKeepsOneIntervalSemantics()
    {
        var history = Window();
        history[^2] = history[^2] with { IsGap = true };
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Observe(history[^1], null, history);
        Assert.That(Report(state, ProcessSort.Cpu).Processes[0].CpuPercent, Is.EqualTo(90));
        var snapshot = SnapshotReport.Create(new CapturedFrame(history[^1]), "test", 10, ProcessSort.Cpu, "");
        Assert.That(snapshot.Processes[0].CpuPercent, Is.EqualTo(90));
    }
}
