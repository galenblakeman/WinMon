using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private static bool IsUsageDiagnosticWindow(MainWindow window) =>
        (bool)(typeof(MainWindow).GetField("_diagnosticMode", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Missing diagnostic fixture flag")).GetValue(window)!;

    [Test, Category("Integration"), SetCulture("en-US")]
    public void UsageDiagnosticPreservesLedgerNavigationReportsAndArchiveScopes()
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory, IsUsageDiagnosticWindow);
        try
        {
            scope.Run(WindowDiagnostics.CaptureUsageDiagnosticsAsync);
            AssertArchiveRecoveryOutputs(_directory);
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "usage.json")));
            Assert.Multiple(() =>
            {
                Assert.That(report.RootElement.GetProperty("Rows").GetArrayLength(), Is.EqualTo(6));
                Assert.That(report.RootElement.GetProperty("Rows")[0].GetProperty("Name").GetString(), Does.Contain("Google Chrome"));
                Assert.That(report.RootElement.GetProperty("Report").GetString(), Does.Contain("Example Setup"));
                Assert.That(report.RootElement.GetProperty("PeriodReport").GetString(), Does.Contain("Google Chrome"));
                Assert.That(report.RootElement.GetProperty("CopiedCapture").GetString(), Does.Contain("Example Setup"));
                Assert.That(scope.Window!.Session.History.Samples, Has.Count.EqualTo(120));
                Assert.That(scope.Window.SamplingTask, Is.Null);
                Assert.That(scope.Window.IsVisible, Is.False);
                Assert.That(Application.Current.Windows.Cast<Window>(), Does.Not.Contain(scope.Window));
                Assert.That(scope.Window.SelectedSample, Is.Null);
                Assert.That(scope.Window.UsageView.ArchiveScope.SelectedIndex, Is.Zero);
                Assert.That(scope.Window.UsageView.ResetUsage.IsEnabled, Is.True);
                Assert.That(scope.Window.SnapshotCopiedTimer.IsEnabled, Is.False);
                Assert.That(scope.DispatcherError, Is.Null);
                Assert.That(scope.TimedOut, Is.False);
            });
            var samples = scope.Window!.Session.History.Samples;
            Assert.That(samples.Take(60).All(sample => sample.Processes.Any(process => process.Name == "setup")), Is.True);
            Assert.That(samples.Skip(60).All(sample => sample.Processes.All(process => process.Name != "setup")), Is.True);
            var apps = scope.Window.Session.Usage.Apps;
            Assert.That(apps, Has.Count.EqualTo(6));
            Assert.That(apps.Single(app => app.Name == "setup").Running, Is.Zero);
            Assert.That(apps.Single(app => app.AppName == "Google Chrome").Running, Is.EqualTo(2));
            var images = Directory.GetFiles(_directory, "*.png");
            Assert.That(images, Has.Length.EqualTo(9));
            foreach (var path in images)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert.That(frame.PixelWidth, Is.GreaterThan(0));
                Assert.That(frame.PixelHeight, Is.GreaterThan(0));
            }
        }
        finally { PreserveUsageArtifacts(); }
    }

    [Test, Category("Integration"), SetCulture("en-US")]
    public void UsageDiagnosticLockedFirstCapturePreservesFilesAndClosesItsFixture()
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory, IsUsageDiagnosticWindow);
        var native = Path.Combine(_directory, "usage-from-processes.png");
        var content = Path.Combine(_directory, "usage-from-processes-content.png");
        File.WriteAllText(native, "owned native sentinel");
        File.WriteAllText(content, "owned content sentinel");
        using var nativeLock = new FileStream(native, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var contentLock = new FileStream(content, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            AssertLockedPngWriteFailure(() => scope.Run(WindowDiagnostics.CaptureUsageDiagnosticsAsync));
            AssertArchiveRecoveryOutputs(_directory);
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(native), Is.EqualTo("owned native sentinel"));
                Assert.That(File.ReadAllText(content), Is.EqualTo("owned content sentinel"));
                Assert.That(scope.Window!.Session.History.Samples, Has.Count.EqualTo(120));
                Assert.That(scope.Window.SamplingTask, Is.Null);
                Assert.That(Application.Current.Windows.Cast<Window>(), Does.Not.Contain(scope.Window));
                Assert.That(File.Exists(Path.Combine(_directory, "usage.json")), Is.False);
                Assert.That(scope.DispatcherError, Is.Null);
                Assert.That(scope.TimedOut, Is.False);
            });
        }
        finally { PreserveUsageArtifacts("locked-first-capture"); }
    }

    private void PreserveUsageArtifacts(string? scenario = null)
    {
        if (TestContext.Parameters.Get("UsageArtifactDirectory") is not { Length: > 0 } artifacts) return;
        if (scenario is not null) artifacts = Path.Combine(artifacts, scenario);
        foreach (var path in Directory.GetFiles(_directory, "*"))
        {
            Directory.CreateDirectory(artifacts);
            File.Copy(path, Path.Combine(artifacts, Path.GetFileName(path)), true);
        }
    }
}
