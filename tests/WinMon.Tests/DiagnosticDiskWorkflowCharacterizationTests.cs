using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using WinMon.Diagnostics;
using WinMon.Monitoring;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, Category("Integration"), SetCulture("en-US")]
    public void DiskDiagnosticPreservesTargetReportsIndependentSeriesAndOwnedSettingsDialogs()
    {
        using var fixture = new DiskWorkflowFixture(_directory);
        var preferences = fixture.Window.Preferences;
        var windows = Application.Current.Windows.Cast<Window>().ToArray();
        fixture.Run();
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "disks.json")));
        var unavailable = report.RootElement.GetProperty("UnavailableDrive");
        Assert.Multiple(() =>
        {
            Assert.That(report.RootElement.GetProperty("IndependentToggle").GetBoolean(), Is.True);
            Assert.That(unavailable.GetProperty("SecondaryDiskName").GetString(), Is.Not.Null.And.Not.Empty);
            Assert.That(unavailable.GetProperty("SecondaryDiskActivePercent").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(unavailable.GetProperty("DiskActivePercent").ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(unavailable.GetProperty("MemoryPercent").GetDouble(), Is.EqualTo(50));
            Assert.That(fixture.Window.Preferences, Is.EqualTo(preferences));
            Assert.That(fixture.Window.Width, Is.EqualTo(fixture.Window.MinWidth));
            Assert.That(fixture.Window.MinWidth, Is.EqualTo(836));
            Assert.That(fixture.Window.SecondaryDiskMetric.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(fixture.Window.SelectedSample, Is.Null);
            Assert.That(fixture.Window.SamplingTask, Is.Null);
            Assert.That(fixture.Window.Session.HasLiveSample, Is.False);
            Assert.That(fixture.Recording.Session.HasLiveSample, Is.True);
            Assert.That(fixture.Recording.BridgeError, Is.Null);
            Assert.That(Application.Current.Windows.Cast<Window>(), Is.EquivalentTo(windows));
        });
        var dual = report.RootElement.GetProperty("DualNative");
        if (dual.ValueKind != JsonValueKind.Null)
        {
            Assert.That(dual.GetProperty("SecondaryDiskActivePercent").ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(File.Exists(Path.Combine(_directory, "disk-native.png")), Is.True);
        }
        foreach (var path in Directory.GetFiles(_directory, "*.png")) AssertDiskWorkflowPng(path);
        Assert.That(File.Exists(Path.Combine(_directory, "disk-unavailable.png")), Is.True);
        fixture.Recording.Dispose();
        Assert.That(fixture.Recording.SourceDisposed, Is.True);
    }

    [Test, Category("Integration")]
    public void DiskDiagnosticPreCancellationPreservesItsTargetMutationAndCallerToken()
    {
        using var fixture = new DiskWorkflowFixture(_directory);
        fixture.Cancel();
        var error = Assert.Catch<OperationCanceledException>(fixture.Run);
        Assert.That(error, Is.TypeOf<TaskCanceledException>());
        Assert.That(error!.CancellationToken, Is.EqualTo(fixture.Token));
        AssertDiskWorkflowRetainedTarget(fixture.Window);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [TestCase("name"), TestCase("secondary-active"), TestCase("primary-active"), TestCase("memory")]
    [Category("Integration")]
    public void DiskDiagnosticRejectsEveryUnavailableTargetMismatchWithoutRestoringItsOriginalPreferences(string mismatch)
    {
        using var fixture = new DiskWorkflowFixture(_directory);
        fixture.Recording.Session.LiveSampleRecorded += sample =>
        {
            var resources = sample.Resources;
            resources = mismatch switch
            {
                "name" => resources with { SecondaryDiskName = "owned mismatch" },
                "secondary-active" => resources with { SecondaryDiskActivePercent = 3.5 },
                "primary-active" => resources with { DiskActivePercent = null },
                _ => resources with { TotalMemoryBytes = null }
            };
            var bad = sample with { Resources = resources };
            fixture.Window.Session.Record(bad, bad);
            fixture.Window.RefreshPresentation();
        };
        var error = Assert.Throws<InvalidOperationException>(fixture.Run);
        Assert.That(error!.Message, Does.StartWith("An unavailable second volume disrupted native resource sampling: "));
        AssertDiskWorkflowRetainedTarget(fixture.Window);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [TestCase("disk-unavailable.png"), TestCase("disks.json"), Category("Integration")]
    public void DiskDiagnosticWriteFailurePreservesSentinelAndOriginalPartialState(string name)
    {
        using var fixture = new DiskWorkflowFixture(_directory);
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "owned disk workflow sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Throws<IOException>(fixture.Run);
        Assert.That(File.ReadAllText(path), Is.EqualTo("owned disk workflow sentinel"));
        AssertDiskWorkflowRetainedTarget(fixture.Window);
        if (name == "disks.json") AssertDiskWorkflowPng(Path.Combine(_directory, "disk-unavailable.png"));
        else Assert.That(File.Exists(Path.Combine(_directory, "disks.json")), Is.False);
    }

    [Test, Category("Integration")]
    public void DiskDiagnosticPreservesItsMissingSampleNullFailureAfterChangingTheTarget()
    {
        using var ui = new UpdateUi(_directory, true);
        Directory.CreateDirectory(_directory);
        Assert.Throws<NullReferenceException>(() => FinishWindowCheck(
            DiskWorkflowCapture(ui.Window)(_directory, default)));
        AssertDiskWorkflowRetainedTarget(ui.Window);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [TestCase("null"), TestCase("missing-folder"), Category("Integration")]
    public void DiskDiagnosticInvalidOutputRetainsTargetAndDoesNotCreateTheMissingFolder(string kind)
    {
        using var fixture = new DiskWorkflowFixture(_directory);
        var output = kind == "null" ? null : Path.Combine(_directory, "owned-missing-folder");
        if (kind == "null") Assert.Throws<ArgumentNullException>(() => fixture.RunAt(output));
        else Assert.Throws<DirectoryNotFoundException>(() => fixture.RunAt(output));
        AssertDiskWorkflowRetainedTarget(fixture.Window);
        if (output is not null) Assert.That(Directory.Exists(output), Is.False);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    private static void AssertDiskWorkflowRetainedTarget(MainWindow window)
    {
        Assert.That(window.Preferences.SecondaryDrive, Is.Not.Null.And.Not.Empty);
        Assert.That(window.Preferences.ShowSecondaryDisk, Is.True);
        Assert.That(window.SecondaryDiskMetric.Toggle.IsChecked, Is.True);
        Assert.That(window.SamplingTask, Is.Null);
    }

    private static void AssertDiskWorkflowPng(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert.That(image.PixelWidth, Is.GreaterThan(0));
        Assert.That(image.PixelHeight, Is.GreaterThan(0));
    }

    private static Func<string, CancellationToken, Task> DiskWorkflowCapture(MainWindow window) =>
        typeof(WindowDiagnostics).GetMethod("CaptureDiskDiagnosticsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, CancellationToken, Task>>(new WindowDiagnostics(window));

    private sealed class DiskWorkflowFixture : IDisposable
    {
        private readonly AutoCaptureDiagnosticScope _scope;
        private readonly ActionDrawerDiagnostic _fixture;
        internal MainWindow Window => _fixture.Window;
        internal MainDiagnosticRecording Recording { get; }
        internal CancellationToken Token => _scope.Token;
        internal DiskWorkflowFixture(string directory)
        {
            _scope = new AutoCaptureDiagnosticScope(directory,
                window => window.Title == "Owned metric capture diagnostic fixture");
            try
            {
                _fixture = new ActionDrawerDiagnostic(directory);
                InstallDesignActionStyles();
                typeof(MainWindow).GetField("_preferencesLoaded", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(Window, true);
                Window.UpdateLayout();
                Recording = new MainDiagnosticRecording(Window);
            }
            catch { _fixture?.Dispose(); _scope.Dispose(); throw; }
        }
        internal void Cancel() => _scope.Cancel();
        internal void Run() => _scope.Run(DiskWorkflowCapture(Window));
        internal void RunAt(string? output) => _scope.Run((_, token) => DiskWorkflowCapture(Window)(output!, token));
        public void Dispose()
        {
            Recording.Dispose();
            _fixture.Dispose();
            _scope.Dispose();
        }
    }
}
