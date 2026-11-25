using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using WinMon.Diagnostics;
using WinMon.Resources;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, Category("Integration"), SetCulture("en-US")]
    public void FeatureDiagnosticPreservesOwnedReportsPreferencesAndDialogLifetime()
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory,
            window => window.Title == "Owned metric capture diagnostic fixture");
        using var fixture = new ActionDrawerDiagnostic(_directory);
        InstallDesignActionStyles();
        var window = fixture.Window;
        // The fixture supplies preferences without running the native OnLoaded sampler.
        typeof(MainWindow).GetField("_preferencesLoaded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, true);
        window.Width = 1400;
        window.UpdateLayout();
        var preferences = window.Preferences;
        var originalWindows = Application.Current.Windows.Cast<Window>().ToArray();
        scope.Run((output, token) => new WindowDiagnostics(window).CaptureFeatureDiagnosticsAsync(output, token));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "features.json")));
        foreach (var name in new[] { "HardwareInfo", "PaletteRenders", "ProcessContextMenu", "CsvExportDialog",
                     "HistoricalActionsDisabled", "GroupTerminationOffered", "ThemedProcessDialogs", "ActionDrawer",
                     "SystemCounts", "LanguageSelection", "LiveLanguageSwitching", "RecordingStatePreserved" })
            Assert.That(report.RootElement.GetProperty(name).GetBoolean(), Is.True, name);
        Assert.Multiple(() =>
        {
            Assert.That(window.Preferences, Is.EqualTo(preferences));
            Assert.That(window.Width, Is.EqualTo(1400));
            Assert.That(window.SelectedSample, Is.Null);
            Assert.That(window.SamplingTask, Is.Null);
            Assert.That(window.IsVisible, Is.True);
            Assert.That(Application.Current.Windows.Cast<Window>(), Is.EquivalentTo(originalWindows));
            Assert.That(scope.DispatcherError, Is.Null);
            Assert.That(scope.TimedOut, Is.False);
            Assert.That(File.ReadAllText(Path.Combine(_directory, "copied-info.txt")), Does.Contain(Strings.InstalledMemory));
        });
        using var hardware = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "hardware.json")));
        Assert.That(hardware.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(hardware.RootElement.GetArrayLength(), Is.GreaterThan(0));
        var images = Directory.GetFiles(_directory, "*.png");
        Assert.That(images.Length, Is.GreaterThan(20));
        foreach (var path in images)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.That(image.PixelWidth, Is.GreaterThan(0));
            Assert.That(image.PixelHeight, Is.GreaterThan(0));
        }
    }

    [TestCase(0, "null"), TestCase(0, "number"), TestCase(0, "wrong")]
    [TestCase(1, "wrong"), TestCase(2, "wrong"), TestCase(3, "wrong"), Category("Integration")]
    public void FeatureDiagnosticRejectsInvalidTabHelpAtItsOriginalPosition(int index, string kind)
    {
        using var fixture = new MetricCaptureDiagnostic(_directory);
        var window = fixture.Window;
        var tabs = new[] { window.ProcessesTab, window.ServicesTab, window.UsageTab, window.ConnectionsTab };
        var names = new[] { "processes", "services", "usage", "connections" };
        tabs[index].ToolTip = kind == "null" ? null : kind == "number" ? 42 : "owned wrong help";
        var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(
            new WindowDiagnostics(window).CaptureFeatureDiagnosticsAsync(_directory, default)));
        Assert.That(error!.Message, Is.EqualTo($"The {names[index]} tab did not explain its view."));
        Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(index));
        Assert.That(fixture.Opened, Has.Count.EqualTo(index));
        Assert.That(fixture.Opened.All(tooltip => !tooltip.IsOpen), Is.True);
        Assert.That(window.SamplingTask, Is.Null);
        Assert.That(window.Width, Is.EqualTo(1000));
        Assert.That(File.Exists(Path.Combine(_directory, "hardware.json")), Is.False);
    }

    [TestCase(0), TestCase(3), Category("Integration")]
    public void FeatureDiagnosticTabHelpWriteFailureClosesTheCurrentPopupAndPreservesData(int index)
    {
        using var fixture = new MetricCaptureDiagnostic(_directory);
        var names = new[] { "processes", "services", "usage", "connections" };
        var path = Path.Combine(_directory, $"tab-help-{names[index]}.png");
        File.WriteAllText(path, "owned feature capture sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Throws<IOException>(() => FinishWindowCheck(
            new WindowDiagnostics(fixture.Window).CaptureFeatureDiagnosticsAsync(_directory, default)));
        Assert.That(File.ReadAllText(path), Is.EqualTo("owned feature capture sentinel"));
        Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(index + 1));
        Assert.That(fixture.Opened, Has.Count.EqualTo(index + 1));
        Assert.That(fixture.Opened.All(tooltip => !tooltip.IsOpen), Is.True);
        Assert.That(fixture.Window.SamplingTask, Is.Null);
        Assert.That(File.Exists(Path.Combine(_directory, "hardware.json")), Is.False);
    }

    [Test, Category("Integration")]
    public void FeatureDiagnosticPreCancellationClosesItsPopupWithTheOriginalTokenlessFailure()
    {
        using var fixture = new MetricCaptureDiagnostic(_directory);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var error = Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(
            new WindowDiagnostics(fixture.Window).CaptureFeatureDiagnosticsAsync(_directory, stop.Token)));
        Assert.That(error, Is.TypeOf<TaskCanceledException>());
        Assert.That(error!.CancellationToken, Is.EqualTo(CancellationToken.None));
        Assert.That(fixture.Opened.All(tooltip => !tooltip.IsOpen), Is.True);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
        Assert.That(fixture.Window.SamplingTask, Is.Null);
    }

    [TestCase("null"), TestCase("empty"), TestCase("file"), Category("Integration")]
    public void FeatureDiagnosticInvalidOutputFailsBeforeOpeningPopups(string kind)
    {
        using var fixture = new MetricCaptureDiagnostic(_directory);
        var output = kind == "null" ? null : kind == "empty" ? "" : Path.Combine(_directory, "owned-file");
        if (kind == "file") File.WriteAllText(output!, "owned output sentinel");
        var operation = () => FinishWindowCheck(new WindowDiagnostics(fixture.Window)
            .CaptureFeatureDiagnosticsAsync(output!, default));
        if (kind == "null") Assert.Throws<ArgumentNullException>(operation);
        else if (kind == "empty") Assert.Throws<ArgumentException>(operation);
        else
        {
            Assert.Throws<IOException>(operation);
            Assert.That(File.ReadAllText(output!), Is.EqualTo("owned output sentinel"));
        }
        Assert.That(fixture.Opened, Is.Empty);
        Assert.That(fixture.Window.SamplingTask, Is.Null);
        Assert.That(File.Exists(Path.Combine(_directory, "features.json")), Is.False);
    }
}
