using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Updates;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase(false), TestCase(true)]
    public void RestartHandoffGuardsLeaveAnOwnedFileAndSessionUntouched(bool diagnostic)
    {
        Assert.That(UpdateRuntime.RestartHistory, Is.Null, "The isolated host must have no real restart input.");
        using var ui = new UpdateUi(_directory, diagnostic);
        var path = OwnedHandoffFile();
        File.WriteAllText(path, "owned untouched marker");
        var before = RestartState(ui.Window);

        FinishWindowCheck(RestartRestore(ui.Window)());

        Assert.That(RestartState(ui.Window), Is.EqualTo(before));
        Assert.That(File.ReadAllText(path), Is.EqualTo("owned untouched marker"));
    }

#if !WINMON_STORE
    [Test]
    public void RestartHandoffDiagnosticModeDoesNotConsumeAPresentSource()
    {
        using var ui = new UpdateUi(_directory, true);
        var path = OwnedHandoffFile();
        File.WriteAllText(path, "owned diagnostic marker");
        var before = RestartState(ui.Window);

        FinishRestartWithInput(ui.Window, Path.GetDirectoryName(path), Guid.NewGuid().ToString("N"));

        Assert.That(RestartState(ui.Window), Is.EqualTo(before));
        Assert.That(File.ReadAllText(path), Is.EqualTo("owned diagnostic marker"));
    }

    [TestCase("token"), TestCase("directory")]
    public void RestartHandoffRequiresBothProcessArgumentFields(string missing)
    {
        using var ui = new UpdateUi(_directory);
        var path = OwnedHandoffFile();
        File.WriteAllText(path, "owned partial input marker");
        var before = RestartState(ui.Window);

        FinishRestartWithInput(ui.Window, missing == "directory" ? null : Path.GetDirectoryName(path),
            missing == "token" ? null : Guid.NewGuid().ToString("N"));

        Assert.That(RestartState(ui.Window), Is.EqualTo(before));
        Assert.That(File.ReadAllText(path), Is.EqualTo("owned partial input marker"));
    }

    [TestCase("missing"), TestCase("corrupt"), TestCase("token-mismatch"), TestCase("locked")]
    public void RestartHandoffInvalidOrUnavailableFilePreservesExistingSessionState(string kind)
    {
        using var ui = new UpdateUi(_directory);
        var path = OwnedHandoffFile();
        var token = Guid.NewGuid().ToString("N");
        if (kind == "token-mismatch")
            Assert.That(RestartHistory.Save(path, "different-owned-token", "4.2.0", [], null, RestartClock.System), Is.True);
        else if (kind != "missing") File.WriteAllBytes(path, [1, 2, 3]);
        ui.Window.Session.Record(new(DateTimeOffset.UnixEpoch, 7, [], 0, MonotonicTimestamp: 1));
        ui.Window.Session.SetHistoryClockOffset(73);
        var before = RestartState(ui.Window);

        using (var held = kind == "locked" ? new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null)
            FinishRestartWithInput(ui.Window, Path.GetDirectoryName(path), token);

        Assert.That(RestartState(ui.Window), Is.EqualTo(before));
        Assert.That(File.Exists(path), Is.EqualTo(kind == "locked"), "Read consumes rejected files unless locked.");
        if (kind == "locked") Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [TestCase("empty"), TestCase("samples"), TestCase("details"), TestCase("offset-details")]
    public void RestartHandoffRestoresSamplesInvestigationUsageAndClockOffset(string kind)
    {
        using var ui = new UpdateUi(_directory);
        var path = OwnedHandoffFile();
        var token = Guid.NewGuid().ToString("N");
        var saved = Stopwatch.GetTimestamp();
        var savedTicks = Environment.TickCount64;
        var clock = new RestartClock(() => saved, () => savedTicks, Stopwatch.Frequency);
        var details = kind is "details" or "offset-details";
        var offset = kind == "offset-details";
        var source = new MonitorSession(() => throw new AssertionException("No sampler may start."),
            () => new SamplingOptions(), _ => throw new AssertionException("No sampling post may run."), retainUsageHistory: true);
        source.Usage.Reset(DateTimeOffset.UnixEpoch.AddDays(2));
        var samples = kind == "empty" ? Array.Empty<MonitorSample>() : Enumerable.Range(0, 3).Select(index =>
            new MonitorSample(DateTimeOffset.UnixEpoch.AddDays(2).AddSeconds(index), 12 + index,
                [new ProcessUsage(42, 100, "owned history", 5 + index) { ImagePath = @"C:\owned-history.exe" }], 0,
                MonotonicTimestamp: saved + (offset ? index + 1 : index - 3) * Stopwatch.Frequency)
            { IntervalSeconds = 1, IntervalCpuTicks = 1000 }).ToArray();
        foreach (var sample in samples) source.Record(sample, sample);
        var usage = details ? source.Usage.Export() : null;
        var investigation = details ? source.CaptureInvestigation() : null;
        Assert.That(RestartHistory.Save(path, token, "4.2.0", samples, usage, clock, investigation: investigation), Is.True);
        var recorded = 0;
        var live = 0;
        ui.Window.Session.Recorded += () => recorded++;
        ui.Window.Session.LiveSampleRecorded += _ => live++;

        FinishRestartWithInput(ui.Window, Path.GetDirectoryName(path), token);

        Assert.Multiple(() =>
        {
            Assert.That(JsonSerializer.Serialize(ui.Window.Session.History.Samples), Is.EqualTo(JsonSerializer.Serialize(samples)));
            Assert.That(ui.Window.Session.LatestFull, Is.Null, "Restored compacted frames must not count as live samples.");
            Assert.That(ui.Window.Session.HasLiveSample, Is.False);
            Assert.That(ui.Window.SamplingTask, Is.Null);
            Assert.That(recorded, Is.Zero);
            Assert.That(live, Is.Zero);
            Assert.That(HistoryOffset(ui.Window), Is.EqualTo(offset ? 3 * Stopwatch.Frequency : 0));
            Assert.That(ui.Window.Session.Connections.Captures.Count, Is.EqualTo(details ? samples.Length : 0));
            Assert.That(File.Exists(path), Is.False);
        });
        if (details)
        {
            Assert.That(JsonSerializer.Serialize(ui.Window.Session.Usage.Export()), Is.EqualTo(JsonSerializer.Serialize(usage)));
            Assert.That(JsonSerializer.Serialize(ui.Window.Session.Connections.Captures),
                Is.EqualTo(JsonSerializer.Serialize(source.Connections.Captures)));
            Assert.That(ui.Window.Session.UsageHistory.IsAvailableAt(samples[^1]), Is.True);
            Assert.That(ui.Window.Session.UsageHistory.LatestTimestamp, Is.EqualTo(samples[^1].Timestamp));
        }
        else
        {
            Assert.That(ui.Window.Session.Usage.Export().Apps, Is.Empty, "Recording restored samples alone cannot rebuild usage totals.");
            Assert.That(ui.Window.Session.UsageHistory.LatestTimestamp, Is.Null);
        }
        var after = RestartState(ui.Window);
        FinishRestartWithInput(ui.Window, Path.GetDirectoryName(path), token);
        Assert.That(RestartState(ui.Window), Is.EqualTo(after), "Consumed handoff cannot replay or double count.");
        source.Close();
    }

    private static void FinishRestartWithInput(MainWindow window, string? directory, string? token)
    {
        Task task;
        using (new RestartArgumentInput(directory, token)) task = RestartRestore(window)();
        // Restore real process input before dispatching/awaiting: the method has captured its local source tuple.
        Assert.That(UpdateRuntime.RestartHistory, Is.Null);
        FinishWindowCheck(task);
    }

    // Existing post-validation process input, not a mutable dependency/provider override. The filtered test
    // host runs this nonparallel STA fixture alone; no production startup or update polling is launched.
    private sealed class RestartArgumentInput : IDisposable
    {
        private static readonly FieldInfo Token = typeof(UpdateRuntime).GetField("_resultToken", BindingFlags.Static | BindingFlags.NonPublic)!;
        private static readonly FieldInfo Directory = typeof(UpdateRuntime).GetField("_resultDirectory", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? _token = Token.GetValue(null), _directory = Directory.GetValue(null);
        internal RestartArgumentInput(string? directory, string? token)
        {
            Assert.That(_token, Is.Null, "Dedicated host must not replace an existing process restart input.");
            Assert.That(_directory, Is.Null);
            Directory.SetValue(null, directory);
            Token.SetValue(null, token);
        }
        public void Dispose()
        {
            Token.SetValue(null, _token);
            Directory.SetValue(null, _directory);
        }
    }
#endif

    private string OwnedHandoffFile()
    {
        var directory = Path.Combine(_directory, "owned-handoff");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, RestartHistory.FileName);
    }

    private static Func<Task> RestartRestore(MainWindow window) => typeof(MainWindow)
        .GetMethod("RestoreRestartHistoryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
        .CreateDelegate<Func<Task>>(window);

    private static long HistoryOffset(MainWindow window) => (long)typeof(MonitorSession)
        .GetField("_historyClockOffset", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window.Session)!;

    private static string RestartState(MainWindow window) => JsonSerializer.Serialize(new
    {
        Samples = window.Session.History.Samples, Usage = window.Session.Usage.Export(),
        Connections = window.Session.Connections.Captures, Offset = HistoryOffset(window),
        window.Session.HasLiveSample, window.Session.LatestFull,
        window.Session.UsageHistory.OldestTimestamp, window.Session.UsageHistory.LatestTimestamp
    });
}
