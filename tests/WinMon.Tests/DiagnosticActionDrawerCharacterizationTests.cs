using System.Reflection;
using DependencyPropertyDescriptor = System.ComponentModel.DependencyPropertyDescriptor;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Diagnostics;
using WinMon.Resources;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test]
    public void ActionDrawerDiagnosticRejectsMissingProcessesWithoutChangingPreferences()
    {
        using var fixture = new ActionDrawerDiagnostic(_directory, false);
        var original = fixture.Window.Preferences;
        var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Run()));
        Assert.That(error!.Message, Is.EqualTo("No sampled process to select for the action drawer."));
        Assert.That(fixture.Window.Preferences, Is.SameAs(original));
        Assert.That(Directory.GetFiles(_directory, "*.png"), Is.Empty);
    }

    [TestCase("process-empty"), TestCase("thread-empty"), TestCase("handle-empty")]
    [TestCase("process-missing"), TestCase("thread-missing"), TestCase("handle-missing")]
    [TestCase("tooltip-null"), TestCase("tooltip-object"), TestCase("tooltip-label")]
    public void ActionDrawerDiagnosticRejectsUnavailableMachineCounts(string partition)
    {
        using var fixture = new ActionDrawerDiagnostic(_directory);
        var original = fixture.Window.Preferences;
        var display = partition.StartsWith("process", StringComparison.Ordinal) ? fixture.Window.ProcessCountValue
            : partition.StartsWith("thread", StringComparison.Ordinal) ? fixture.Window.ThreadCountValue : fixture.Window.HandleCountValue;
        if (!partition.StartsWith("tooltip", StringComparison.Ordinal))
            display.Text = partition.EndsWith("empty", StringComparison.Ordinal) ? "" : Strings.MissingReading;
        else fixture.Window.SystemCounts.ToolTip = partition switch { "tooltip-null" => null, "tooltip-object" => new object(), _ => "owned incorrect counts label" };
        var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Run()));
        Assert.That(error!.Message, Is.EqualTo("The tab strip did not show process, thread and handle counts."));
        Assert.That(fixture.Window.Preferences, Is.SameAs(original));
        Assert.That(Directory.GetFiles(_directory, "*.png"), Is.Empty);
    }

    [TestCase(true), TestCase(false)]
    public void ActionDrawerDiagnosticCancellationRestoresPreferencesAndCounts(bool afterCollapse)
    {
        using var fixture = new ActionDrawerDiagnostic(_directory);
        var original = fixture.Window.Preferences;
        using var cancellation = new CancellationTokenSource();
        if (afterCollapse) fixture.OnChange(collapsed => { if (collapsed) cancellation.Cancel(); });
        else cancellation.Cancel();
        var error = Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(fixture.Run(cancellation.Token)));
        if (afterCollapse) Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
        fixture.AssertRestored(original);
        Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(afterCollapse ? 1 : 0));
    }

    [TestCase("preferences"), TestCase("services"), TestCase("usage")]
    [TestCase("peek"), TestCase("title"), TestCase("height")]
    [Category("Integration")]
    public void ActionDrawerDiagnosticRejectsIncompleteCollapseAndRestoresSharedFlags(string partition)
    {
        using var fixture = new ActionDrawerDiagnostic(_directory);
        var original = fixture.Window.Preferences;
        var drawer = fixture.Window.Processes.ProcessActionDrawer;
        if (partition == "peek") fixture.ForceProperty(drawer.Peek, UIElement.VisibilityProperty, Visibility.Hidden, () => drawer.Collapsed);
        fixture.OnChange(collapsed =>
        {
            if (!collapsed) return;
            if (partition == "preferences") fixture.Window.Preferences = fixture.Window.Preferences with { ProcessActionsHidden = false };
            if (partition == "services") fixture.Window.ServiceView.ActionsHidden = false;
            if (partition == "usage") fixture.Window.UsageView.ActionsHidden = false;
            if (partition == "title") drawer.Summarize("owned wrong title", "", true);
            if (partition == "height") drawer.MinHeight = 300;
        });
        var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Run()));
        Assert.That(error!.Message, Does.StartWith("The chevron did not collapse the action bar to its strip:"));
        fixture.AssertRestored(original);
        Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(1));
    }

    [TestCase("preferences"), TestCase("services"), TestCase("peek"), TestCase("height")]
    [Category("Integration")]
    public void ActionDrawerDiagnosticRejectsIncompleteRestoreAndRestoresOriginalPreferences(string partition)
    {
        using var fixture = new ActionDrawerDiagnostic(_directory);
        var original = fixture.Window.Preferences;
        var drawer = fixture.Window.Processes.ProcessActionDrawer;
        if (partition == "peek") fixture.ForceProperty(drawer.Peek, UIElement.VisibilityProperty, Visibility.Visible, () => !drawer.Collapsed);
        fixture.OnChange(collapsed =>
        {
            if (collapsed) return;
            if (partition == "preferences") fixture.Window.Preferences = fixture.Window.Preferences with { ProcessActionsHidden = true };
            if (partition == "services") fixture.Window.ServiceView.ActionsHidden = true;
            if (partition == "height") drawer.MinHeight = 300;
        });
        var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Run()));
        Assert.That(error!.Message, Does.StartWith("Show actions did not restore the action bar:"));
        fixture.AssertRestored(original);
        Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(3));
    }

    [TestCase("open"), TestCase("collapsed-dark"), TestCase("collapsed-light")]
    [Category("Integration")]
    public void ActionDrawerDiagnosticWriteFailuresPreserveExistingFileAndRestoreState(string stage)
    {
        using var fixture = new ActionDrawerDiagnostic(_directory);
        var original = fixture.Window.Preferences;
        var path = Path.Combine(_directory, $"action-drawer-{stage}.png");
        File.WriteAllText(path, "owned drawer sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Catch<IOException>(() => FinishWindowCheck(fixture.Run()));
        Assert.That(File.ReadAllText(path), Is.EqualTo("owned drawer sentinel"));
        fixture.AssertRestored(original);
    }

    [Test, Category("Integration")]
    public void ActionDrawerDiagnosticPreservesCollapseShowCountsAndOriginalPreferences()
    {
        using var fixture = new ActionDrawerDiagnostic(_directory);
        var original = fixture.Window.Preferences;
        FinishWindowCheck(fixture.Run());
        fixture.AssertRestored(original);
        foreach (var name in new[] { "open", "collapsed-dark", "collapsed-light" })
        {
            var path = Path.Combine(_directory, $"action-drawer-{name}.png");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.That(frame.PixelWidth, Is.GreaterThan(0));
            Assert.That(frame.PixelHeight, Is.GreaterThan(0));
            if (TestContext.Parameters.Get("ActionDrawerArtifactDirectory") is { Length: > 0 } artifacts)
            {
                Directory.CreateDirectory(artifacts);
                stream.Position = 0;
                using var copy = File.Create(Path.Combine(artifacts, Path.GetFileName(path)));
                stream.CopyTo(copy);
            }
        }
        Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(3));
    }

    private sealed class ActionDrawerDiagnostic : IDisposable
    {
        private readonly MetricCaptureDiagnostic _fixture;
        private readonly string _directory;
        private readonly List<Action> _cleanup = [];
        internal MainWindow Window => _fixture.Window;
        internal ActionDrawerDiagnostic(string directory, bool seed = true)
        {
            _directory = directory;
            _fixture = new MetricCaptureDiagnostic(directory);
            try
            {
                // The minimal fixture resources omit the App.xaml command setter.
                var style = new Style(typeof(Button));
                style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 28d));
                style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 28d));
                style.Setters.Add(new Setter(Button.CommandProperty, ActionDrawer.CollapseCommand));
                Application.Current.Resources["ActionBarCollapseButton"] = style;
                if (seed)
                {
                    var process = new ProcessUsage(int.MaxValue - 500, 100, "owned drawer process", 3, 1_048_576)
                    { HasAppWindow = true, ThreadCount = 7, HandleCount = 11 };
                    FinishWindowCheck(ProcessIconProvider.GetAsync(process));
                    Window.Session.Record(new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(1), 7, [process], 0)
                    { Resources = new ResourceSample { ProcessCount = 7, ThreadCount = 70, HandleCount = 700 } });
                    Window.Session.Record(new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(2), 9, [process], 0)
                    { Resources = new ResourceSample { ProcessCount = 7, ThreadCount = 70, HandleCount = 700 } });
                }
                Window.RefreshVisiblePresentation();
                Window.UpdateLayout();
            }
            catch { _fixture.Dispose(); throw; }
        }
        internal Task Run(CancellationToken token = default, string? output = null) => typeof(WindowDiagnostics)
            .GetMethod("CaptureActionDrawerAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, CancellationToken, Task>>(new WindowDiagnostics(Window))(output ?? _directory, token);
        internal void OnChange(Action<bool> action)
        {
            var drawer = Window.Processes.ProcessActionDrawer;
            drawer.CollapsedChanged += action;
            _cleanup.Add(() => drawer.CollapsedChanged -= action);
        }
        internal void ForceProperty(DependencyObject element, DependencyProperty property, object badValue, Func<bool> when)
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(property, element.GetType());
            EventHandler apply = (_, _) =>
            {
                if (when() && !Equals(element.GetValue(property), badValue)) element.SetValue(property, badValue);
            };
            descriptor.AddValueChanged(element, apply);
            _cleanup.Add(() => descriptor.RemoveValueChanged(element, apply));
        }
        internal void AssertRestored(Preferences.MonitorPreferences original)
        {
            Assert.That(Window.Preferences, Is.SameAs(original));
            Assert.That(Window.Processes.ActionsHidden, Is.EqualTo(original.ProcessActionsHidden));
            Assert.That(Window.ServiceView.ActionsHidden, Is.EqualTo(original.ProcessActionsHidden));
            Assert.That(Window.UsageView.ActionsHidden, Is.EqualTo(original.ProcessActionsHidden));
            Assert.That(Window.SelectedSample, Is.Null);
            Assert.That(Window.ShownSamplingError, Is.Null);
            Assert.That(Window.ProcessCountValue.Text, Is.EqualTo("7"));
            Assert.That(Window.Session.HasLiveSample, Is.False);
        }
        public void Dispose()
        {
            foreach (var cleanup in _cleanup) cleanup();
            _fixture.Dispose();
        }
    }
}
