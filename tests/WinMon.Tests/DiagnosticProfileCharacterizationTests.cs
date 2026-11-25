using System.Text.Json;
using System.Windows.Threading;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, Category("Integration")]
    public void ProfileDiagnosticReportsOwnedHistoriesAndPreservesEmptyDurationFailuresWithTheRealWarmup()
    {
        using var empty = new UpdateUi(Path.Combine(_directory, "empty-owner"), true);
        using var populated = new UpdateUi(Path.Combine(_directory, "populated-owner"), true);
        populated.Window.Session.Record(new(DateTimeOffset.UnixEpoch, 7,
            [new ProcessUsage(42, 100, "owned first", 3)], 1));
        populated.Window.Session.Record(new(DateTimeOffset.UnixEpoch.AddSeconds(1), 9,
            [new ProcessUsage(43, 100, "owned second", 4), new ProcessUsage(44, 100, "owned third", 5)], 4));
        var history = JsonSerializer.Serialize(populated.Window.Session.History.Samples);
        var emptyPath = Path.Combine(_directory, "empty-profile");
        var populatedPath = Path.Combine(_directory, "populated-profile");
        var zeroPath = Path.Combine(_directory, "zero-profile");
        var negativePath = Path.Combine(_directory, "negative-profile");
        using var cleanup = new CancellationTokenSource();
        var emptyCapture = new WindowDiagnostics(empty.Window).CaptureProfileAsync(emptyPath, 1, cleanup.Token);
        var populatedCapture = new WindowDiagnostics(populated.Window).CaptureProfileAsync(populatedPath, 1, cleanup.Token);
        var zero = new WindowDiagnostics(empty.Window).CaptureProfileAsync(zeroPath, 0, cleanup.Token);
        var negative = new WindowDiagnostics(empty.Window).CaptureProfileAsync(negativePath, -1, cleanup.Token);
        var all = Task.WhenAll(emptyCapture, populatedCapture,
            AssertEmptyProfileDuration(zero, zeroPath), AssertEmptyProfileDuration(negative, negativePath));
        try
        {
            Assert.That(new[] { emptyCapture, populatedCapture, zero, negative }.All(task => !task.IsCompleted), Is.True,
                "Every original invocation must retain its real warmup before sampling or duration failure.");
            FinishProfileDiagnostic(all);
            AssertProfileReport(emptyPath, 0, 0, 0, 0);
            AssertProfileReport(populatedPath, 2, 3, 2, 4);
            Assert.That(empty.Window.Session.History.Samples, Is.Empty);
            Assert.That(JsonSerializer.Serialize(populated.Window.Session.History.Samples), Is.EqualTo(history));
            Assert.That(empty.Window.Session.HasLiveSample || populated.Window.Session.HasLiveSample, Is.False);
        }
        finally
        {
            cleanup.Cancel();
            if (!all.IsCompleted) FinishWindowCheck(all.ContinueWith(_ => { }, TaskScheduler.Default));
            _ = all.Exception;
        }
    }

    [TestCase("null"), TestCase("empty"), TestCase("whitespace")]
    public void ProfileDiagnosticValidatesOutputBeforeTheAlreadyCancelledRequest(string input)
    {
        using var ui = new UpdateUi(_directory, true);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var output = input switch { "null" => null!, "empty" => "", _ => " \t" };

        var error = Assert.Catch<ArgumentException>(() => FinishWindowCheck(
            new WindowDiagnostics(ui.Window).CaptureProfileAsync(output, 1, cancelled.Token)));

        Assert.That(error, input == "null" ? Is.TypeOf<ArgumentNullException>() : Is.TypeOf<ArgumentException>());
        Assert.That(error!.ParamName, Is.EqualTo("outputDirectory"));
        Assert.That(ui.Window.Session.HasLiveSample, Is.False);
    }

    [Test, Category("Integration")]
    public void ProfileDiagnosticDirectoryFailurePrecedesCancellationAndPreservesExistingData()
    {
        using var ui = new UpdateUi(_directory, true);
        Directory.CreateDirectory(_directory);
        var output = Path.Combine(_directory, "occupied-profile");
        File.WriteAllText(output, "owned profile destination sentinel");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<IOException>(() => FinishWindowCheck(
            new WindowDiagnostics(ui.Window).CaptureProfileAsync(output, 1, cancelled.Token)));

        Assert.That(File.ReadAllText(output), Is.EqualTo("owned profile destination sentinel"));
        Assert.That(ui.Window.Session.HasLiveSample, Is.False);
    }

    [TestCase(false), TestCase(true), Category("Integration")]
    public void ProfileDiagnosticWarmupCancellationPreservesTheTokenAndExistingReport(bool initiallyCancelled)
    {
        using var ui = new UpdateUi(_directory, true);
        var output = Path.Combine(_directory, "cancelled-profile");
        Directory.CreateDirectory(output);
        var path = Path.Combine(output, "profile.json");
        File.WriteAllText(path, "owned existing profile");
        using var cancellation = new CancellationTokenSource();
        if (initiallyCancelled) cancellation.Cancel();
        var capture = new WindowDiagnostics(ui.Window).CaptureProfileAsync(output, 1, cancellation.Token);
        try
        {
            if (!initiallyCancelled)
            {
                Assert.That(capture.IsCompleted, Is.False);
                Assert.That(Directory.Exists(output), Is.True);
                cancellation.Cancel();
            }
            var error = Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(capture));
            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(File.ReadAllText(path), Is.EqualTo("owned existing profile"));
            Assert.That(Directory.GetFileSystemEntries(output).Select(Path.GetFileName), Is.EqualTo(new[] { "profile.json" }));
            Assert.That(ui.Window.Session.HasLiveSample, Is.False);
        }
        finally
        {
            cancellation.Cancel();
            if (!capture.IsCompleted) FinishWindowCheck(capture.ContinueWith(_ => { }, TaskScheduler.Default));
            _ = capture.Exception;
        }
    }

    private static async Task AssertEmptyProfileDuration(Task capture, string output)
    {
        Exception? failure = null;
        try { await capture; }
        catch (Exception error) { failure = error; }
        Assert.That(failure, Is.TypeOf<InvalidOperationException>(),
            "The original zero/negative duration reaches an empty metric aggregation after warmup.");
        Assert.That(Directory.Exists(output), Is.True);
        Assert.That(Directory.GetFileSystemEntries(output), Is.Empty);
    }

    private static void FinishProfileDiagnostic(Task task)
    {
        // Keep the original 20-second warmup and real one-second sample interval intact.
        var bounded = task.WaitAsync(TimeSpan.FromSeconds(50));
        if (!bounded.IsCompleted)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            _ = bounded.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        bounded.GetAwaiter().GetResult();
    }

    private static void AssertProfileReport(string output, int samples, int entries, int latest, int unavailable)
    {
        using var file = File.Open(Path.Combine(output, "profile.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var document = JsonDocument.Parse(file);
        var report = document.RootElement;
        Assert.That(report.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(new[]
        {
            "MeasuredSeconds", "LogicalProcessors", "CpuPercentOfMachine", "CpuMillisecondsPerSecond",
            "WorkingSetMiBAverage", "WorkingSetMiBMax", "PrivateMiBAverage", "PrivateMiBMax", "ManagedHeapMiBAverage",
            "AllocatedKiBPerSecond", "Gen0Collections", "Gen1Collections", "Gen2Collections", "RetainedSamples",
            "RetainedProcessEntries", "ProcessesPerSample", "UnavailableProcesses"
        }));
        foreach (var field in report.EnumerateObject())
        {
            var value = field.Value.GetDouble();
            Assert.That(double.IsFinite(value) && value >= 0, Is.True, field.Name);
        }
        Assert.That(report.GetProperty("MeasuredSeconds").GetDouble(), Is.GreaterThanOrEqualTo(1));
        Assert.That(report.GetProperty("LogicalProcessors").GetInt32(), Is.EqualTo(Environment.ProcessorCount));
        var cpu = report.GetProperty("CpuMillisecondsPerSecond").GetDouble() / (10 * Environment.ProcessorCount);
        Assert.That(report.GetProperty("CpuPercentOfMachine").GetDouble(), Is.EqualTo(cpu).Within(1e-9));
        foreach (var prefix in new[] { "WorkingSet", "Private" })
        {
            var average = report.GetProperty(prefix + "MiBAverage").GetDouble();
            Assert.That(average, Is.GreaterThan(0));
            Assert.That(report.GetProperty(prefix + "MiBMax").GetDouble(), Is.GreaterThanOrEqualTo(average));
        }
        Assert.That(report.GetProperty("RetainedSamples").GetInt32(), Is.EqualTo(samples));
        Assert.That(report.GetProperty("RetainedProcessEntries").GetInt32(), Is.EqualTo(entries));
        Assert.That(report.GetProperty("ProcessesPerSample").GetInt32(), Is.EqualTo(latest));
        Assert.That(report.GetProperty("UnavailableProcesses").GetInt32(), Is.EqualTo(unavailable));
    }
}
