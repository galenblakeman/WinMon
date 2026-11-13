using NUnit.Framework;
using System.CommandLine;
using System.Text.Json;
using WinMon.Cli;
using WinMon.Monitoring;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class TimelineTests
{
    [Test]
    public async Task HistoryCommandReadsSavedDaysWithoutSampling()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "daily-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var archive = new UsageArchive(TimeZoneInfo.Utc);
            archive.Add(new MonitorSample(DateTimeOffset.Parse("2026-11-11T12:00:00Z"), 10,
                [new ProcessUsage(42, 1, "worker", 10)], 0) { IntervalSeconds = 2, IntervalCpuTicks = 20_000_000 });
            await UsageArchive.SaveAsync(path, archive.Export());
            using var output = new StringWriter();
            using var error = new StringWriter();
            var command = CliCommand.Create(() => throw new AssertionException("Must not sample"), output, error);
            Assert.That(await command.Parse(["usage-history", "--file", path, "--ending", "2026-11-11", "--days", "7", "--format", "json"]).InvokeAsync(), Is.Zero, error.ToString());
            using var json = JsonDocument.Parse(output.ToString());
            Assert.That(json.RootElement.GetProperty("observedSeconds").GetDouble(), Is.EqualTo(2));
            Assert.That(json.RootElement.GetProperty("apps")[0].GetProperty("running").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(command.Parse(["usage-history", "--days", "14"]).Errors, Is.Not.Empty);
            Assert.That(command.Parse(["usage-history", "--ending", "invalid"]).Errors, Is.Not.Empty);
        }
        finally { File.Delete(path); }
    }
    [Test]
    public void CursorSelectsPastUsageAndLiveSamplingContinuesUntilResume()
    {
        var session = new MonitorSession(() => throw new AssertionException("No collection"), () => new(), a => a(), retainUsageHistory: true);
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Configure(0, false, false, TerminalTab.Usage);
        var anchor = DateTimeOffset.UnixEpoch.AddHours(1);
        for (var i = 0; i < 5; i++)
        {
            var sample = new MonitorSample(anchor.AddSeconds(i), 10, [new ProcessUsage(42, 1, "worker", 10)], 0)
                { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 };
            session.Record(sample, sample);
        }
        state.ObserveSession(session);
        state.NavigateHistory(-1);
        Assert.That(state.DisplayedFrame!.Full.Timestamp, Is.EqualTo(anchor.AddSeconds(3)));
        Assert.That(state.Observations.Usage.Single().CpuTicks, Is.EqualTo(4_000_000));
        Assert.That(state.Observations.UsageComplete, Is.False);
        state.NavigateHistory(-1);
        Assert.That(state.DisplayedFrame.Full.Timestamp, Is.EqualTo(anchor.AddSeconds(2)));
        var next = new MonitorSample(anchor.AddSeconds(5), 10, [], 0) { IntervalSeconds = 1 };
        session.Record(next, next);
        state.ObserveSession(session);
        Assert.That(state.DisplayedFrame.Full.Timestamp, Is.EqualTo(anchor.AddSeconds(2)));
        state.Handle(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false), 10);
        Assert.That(state.Paused, Is.False);
        Assert.That(state.DisplayedFrame.Full.Timestamp, Is.EqualTo(next.Timestamp));
    }
}
