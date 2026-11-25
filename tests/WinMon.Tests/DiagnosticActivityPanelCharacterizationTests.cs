using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase(0), TestCase(1), TestCase(2), TestCase(3), Category("Integration")]
    public void ActivityPanelDiagnosticPreservesTabsSplitCollapseFocusPersistenceAndOriginalWindowState(int originalTab)
    {
        using var fixture = new ActivityPanelDiagnostic(_directory);
        fixture.Tabs[originalTab].IsChecked = true;
        var preferences = fixture.Window.Preferences;
        var width = fixture.Window.Width;
        var height = fixture.Window.Height;
        var windows = Application.Current.Windows.Cast<Window>().ToArray();
        FinishWindowCheck(fixture.Run());
        Assert.That(fixture.Window.Preferences, Is.EqualTo(preferences));
        Assert.That(fixture.Window.Width, Is.EqualTo(width));
        Assert.That(fixture.Window.Height, Is.EqualTo(height));
        Assert.That(fixture.Tabs[originalTab].IsChecked, Is.True);
        Assert.That(fixture.Window.CompactMode, Is.False);
        Assert.That(fixture.Window.Session.HasLiveSample, Is.False);
        Assert.That(Application.Current.Windows.Cast<Window>(), Is.EquivalentTo(windows));
        Assert.That(File.ReadAllText(Path.Combine(_directory, "activity-panel-passed.txt")),
            Is.EqualTo("Collapse, restore, resized split, all tabs, focus, Compact view and persisted restart checks passed."));
        var images = Directory.GetFiles(_directory, "*.png");
        Assert.That(images, Has.Length.EqualTo(16));
        Assert.That(Directory.GetFiles(_directory, "*.json"), Has.Length.EqualTo(3));
        foreach (var path in images)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.That(frame.PixelWidth, Is.GreaterThan(0));
            Assert.That(frame.PixelHeight, Is.GreaterThan(0));
            if (originalTab == 0 && TestContext.Parameters.Get("ActivityPanelDiagnosticArtifactDirectory") is { Length: > 0 } artifacts)
            {
                Directory.CreateDirectory(artifacts);
                stream.Position = 0;
                using var copy = File.Create(Path.Combine(artifacts, Path.GetFileName(path)));
                stream.CopyTo(copy);
            }
        }
    }

    [TestCase(false), TestCase(true), Category("Integration")]
    public void ActivityPanelDiagnosticCancellationRestoresOriginalPreferencesTabSizeAndWindowOwnership(bool afterHeaders)
    {
        using var fixture = new ActivityPanelDiagnostic(_directory);
        fixture.Tabs[3].IsChecked = true;
        var original = fixture.Window.Preferences;
        using var cancellation = new CancellationTokenSource();
        var signalled = false;
        EventHandler resized = (_, _) =>
        {
            if (fixture.Window.Height == 1000 && Directory.GetFiles(_directory, "headers-*.png").Length == 9)
            { signalled = true; cancellation.Cancel(); }
        };
        if (afterHeaders) fixture.Window.LayoutUpdated += resized;
        else cancellation.Cancel();
        try
        {
            Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(fixture.Run(cancellation.Token)));
            Assert.That(signalled, Is.EqualTo(afterHeaders));
            fixture.AssertRestored(original, 3);
            Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(afterHeaders ? 9 : 0));
            Assert.That(File.Exists(Path.Combine(_directory, "activity-panel-passed.txt")), Is.False);
        }
        finally { fixture.Window.LayoutUpdated -= resized; }
    }

    [TestCase("tab-settings"), TestCase("first-header"), TestCase("final-marker"), Category("Integration")]
    public void ActivityPanelDiagnosticWriteFailuresPreserveExistingDataAndRestoreWindowState(string stage)
    {
        using var fixture = new ActivityPanelDiagnostic(_directory);
        fixture.Tabs[1].IsChecked = true;
        var original = fixture.Window.Preferences;
        var name = stage switch
        {
            "tab-settings" => "activity-tab-settings.json",
            "first-header" => $"headers-{fixture.Window.ProcessesTab.Name}-900.png",
            _ => "activity-panel-passed.txt"
        };
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "owned activity diagnostic sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var error = Assert.Catch(() => FinishWindowCheck(fixture.Run()));
        Assert.That(error, Is.TypeOf(stage == "tab-settings" ? typeof(UnauthorizedAccessException) : typeof(IOException)));
        Assert.That(File.ReadAllText(path), Is.EqualTo("owned activity diagnostic sentinel"));
        fixture.AssertRestored(original, 1);
    }

    [Test, Category("Integration")]
    public void ActivityPanelDiagnosticBeforePreferencesReadyPreservesSelectorFailureAndRestoration()
    {
        using var fixture = new ActivityPanelDiagnostic(_directory, ready: false);
        var original = fixture.Window.Preferences;
        var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Run()));
        Assert.That(error!.Message, Is.EqualTo("Changing a chart selector did not expand the chart with its saved split."));
        fixture.AssertRestored(original, 0);
        Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(13));
        Assert.That(File.Exists(Path.Combine(_directory, "activity-panel-passed.txt")), Is.False);
    }

    [Test, Category("Integration")]
    public void ActivityPanelDiagnosticRejectsAnUnsavedTabChoiceAndRestoresPreferences()
    {
        using var fixture = new ActivityPanelDiagnostic(_directory);
        var original = fixture.Window.Preferences;
        var applied = false;
        RoutedEventHandler corrupt = (_, _) =>
        {
            applied = true;
            fixture.Window.Preferences = fixture.Window.Preferences with { SelectedActivityTab = 0 };
        };
        fixture.Window.ServicesTab.Checked += corrupt;
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Run()));
            Assert.That(error!.Message, Is.EqualTo("Selecting an activity tab did not update its saved preference."));
            Assert.That(applied, Is.True);
            fixture.AssertRestored(original, 0);
            Assert.That(Directory.GetFiles(_directory, "*.png"), Is.Empty);
        }
        finally { fixture.Window.ServicesTab.Checked -= corrupt; }
    }

    [TestCase("preference"), TestCase("body"), TestCase("divider"), TestCase("height")]
    [TestCase("chart-space"), TestCase("focus"), TestCase("name"), Category("Integration")]
    public void ActivityPanelDiagnosticRejectsIncompleteActivityCollapseAndRestoresWindow(string partition)
    {
        using var fixture = new ActivityPanelDiagnostic(_directory);
        var original = fixture.Window.Preferences;
        var applied = false;
        var maxHeight = fixture.Window.ChartAreaRow.MaxHeight;
        RoutedEventHandler corrupt = (_, _) =>
        {
            if (applied) return;
            applied = true;
            var window = fixture.Window;
            switch (partition)
            {
                case "preference": window.Preferences = window.Preferences with { ActivityPanelCollapsed = false }; break;
                case "body": window.ActivityBody.Visibility = Visibility.Visible; break;
                case "divider": window.ChartProcessDivider.Visibility = Visibility.Visible; break;
                case "height": window.ProcessesAreaRow.MinHeight = 100; break;
                case "chart-space": window.ChartAreaRow.MaxHeight = 0; break;
                case "focus": window.InfoButton.Focus(); break;
                case "name": AutomationProperties.SetName(window.ActivityPanelToggle, "owned stale activity name"); break;
            }
        };
        fixture.Window.ActivityPanelToggle.Click += corrupt;
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Run()));
            Assert.That(error!.Message, Is.EqualTo("Activity collapse did not reclaim space, preserve the header, or restore focus."));
            Assert.That(applied, Is.True);
            fixture.Window.ChartAreaRow.MaxHeight = maxHeight;
            fixture.AssertRestored(original, 0);
            Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(10));
        }
        finally
        {
            fixture.Window.ActivityPanelToggle.Click -= corrupt;
            fixture.Window.ChartAreaRow.MaxHeight = maxHeight;
        }
    }

    [TestCase("preference"), TestCase("body"), TestCase("name"), Category("Integration")]
    public void ActivityPanelDiagnosticRejectsIncompleteChartCollapseAndRestoresWindow(string partition)
    {
        using var fixture = new ActivityPanelDiagnostic(_directory);
        var original = fixture.Window.Preferences;
        var applied = false;
        RoutedEventHandler corrupt = (_, _) =>
        {
            if (applied) return;
            applied = true;
            var window = fixture.Window;
            if (partition == "preference") window.Preferences = window.Preferences with { ChartPanelCollapsed = false };
            if (partition == "body") window.ChartBody.Visibility = Visibility.Visible;
            if (partition == "name") AutomationProperties.SetName(window.ChartPanelToggle, "owned stale chart name");
        };
        fixture.Window.ChartPanelToggle.Click += corrupt;
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Run()));
            Assert.That(error!.Message, Is.EqualTo("Chart collapse did not reclaim space or restore focus."));
            Assert.That(applied, Is.True);
            fixture.AssertRestored(original, 0);
            Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(10));
        }
        finally { fixture.Window.ChartPanelToggle.Click -= corrupt; }
    }

    private sealed class ActivityPanelDiagnostic : IDisposable
    {
        private readonly MetricCaptureDiagnostic _fixture;
        private readonly string _directory;
        internal MainWindow Window => _fixture.Window;
        internal System.Windows.Controls.RadioButton[] Tabs => [Window.ProcessesTab, Window.ServicesTab, Window.UsageTab, Window.ConnectionsTab];
        private readonly Window[] _windows;
        internal ActivityPanelDiagnostic(string directory, bool ready = true)
        {
            _directory = directory;
            _fixture = new MetricCaptureDiagnostic(directory, activate: true);
            // Preferences are supplied by this fixture; avoid OnLoaded's native sampler
            // while allowing chart-selector handlers past their startup readiness guard.
            typeof(MainWindow).GetField("_preferencesLoaded", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Window, ready);
            _windows = Application.Current.Windows.Cast<Window>().ToArray();
        }
        internal Task Run(CancellationToken token = default, string? output = null) => typeof(WindowDiagnostics)
            .GetMethod("CaptureActivityPanelAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, CancellationToken, Task>>(new WindowDiagnostics(Window))(output ?? _directory, token);
        internal void AssertRestored(Preferences.MonitorPreferences original, int tab)
        {
            Assert.That(Window.Preferences, Is.EqualTo(original));
            Assert.That(Window.Width, Is.EqualTo(1000));
            Assert.That(Window.Height, Is.EqualTo(840));
            Assert.That(Tabs[tab].IsChecked, Is.True);
            Assert.That(Window.CompactMode, Is.False);
            Assert.That(Window.Session.HasLiveSample, Is.False);
            Assert.That(Window.SamplingTask, Is.Null);
            Assert.That(Application.Current.Windows.Cast<Window>(), Is.EquivalentTo(_windows));
        }
        public void Dispose() => _fixture.Dispose();
    }
}
