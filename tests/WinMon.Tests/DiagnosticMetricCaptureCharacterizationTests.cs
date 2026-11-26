using DependencyPropertyDescriptor = System.ComponentModel.DependencyPropertyDescriptor;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase(false, "cpu"), TestCase(false, "memory"), TestCase(false, "disk"), TestCase(false, "gpu")]
    [TestCase(true, "cpu"), TestCase(true, "memory"), TestCase(true, "disk"), TestCase(true, "gpu")]
    [Category("Integration")]
    public void MetricCaptureDiagnosticPreservesMetricFilenamesAndClosesTemporaryDetails(bool compact, string name)
    {
        using var fixture = new MetricCaptureDiagnostic(_directory);
        fixture.Window.SetCompactView(compact);
        FinishWindowCheck(fixture.Capture(compact, fixture.Metric(name)));
        fixture.AssertPng(name, compact);
        fixture.AssertClosed(fixture.Metric(name));
        Assert.That(fixture.Window.CompactMode, Is.EqualTo(compact));
    }

    [TestCase(false), TestCase(true)]
    public void MetricCaptureDiagnosticCancellationClosesTemporaryDetails(bool compact)
    {
        using var fixture = new MetricCaptureDiagnostic(_directory);
        fixture.Window.SetCompactView(compact);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(fixture.Capture(compact, fixture.Window.MemoryMetric, cancellation.Token)));
        Assert.That(Directory.GetFiles(_directory, "*.png"), Is.Empty);
        fixture.AssertClosed(fixture.Window.MemoryMetric);
    }

    [TestCase(false, "missing-folder"), TestCase(false, "locked-file")]
    [TestCase(true, "missing-folder"), TestCase(true, "locked-file"), Category("Integration")]
    public void MetricCaptureDiagnosticWriteFailuresPreserveDataAndCloseTemporaryDetails(bool compact, string failure)
    {
        var output = failure == "missing-folder" ? Path.Combine(_directory, "missing") : _directory;
        using var fixture = new MetricCaptureDiagnostic(_directory, output);
        fixture.Window.SetCompactView(compact);
        var path = Path.Combine(output, $"tooltip-memory-owned-{(compact ? "compact" : "full")}.png");
        if (failure == "locked-file") File.WriteAllText(path, "owned metric image sentinel");
        using var locked = failure == "locked-file" ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
        var error = Assert.Catch<IOException>(() => FinishWindowCheck(fixture.Capture(compact, fixture.Window.MemoryMetric)));
        Assert.That(error, failure == "missing-folder" ? Is.TypeOf<DirectoryNotFoundException>() : Is.TypeOf<IOException>());
        if (failure == "locked-file") Assert.That(File.ReadAllText(path), Is.EqualTo("owned metric image sentinel"));
        else Assert.That(Directory.Exists(output), Is.False);
        fixture.AssertClosed(fixture.Window.MemoryMetric);
    }

    [TestCase(false), TestCase(true)]
    public void MetricCaptureDiagnosticPreservesNullMetricFailureBeforeOpeningAPopup(bool compact)
    {
        using var fixture = new MetricCaptureDiagnostic(_directory);
        var error = Assert.Catch(() => FinishWindowCheck(fixture.Capture(compact, null!)));
        Assert.That(error, compact ? Is.TypeOf<InvalidOperationException>() : Is.TypeOf<NullReferenceException>());
        Assert.That(fixture.Opened, Is.Empty);
        Assert.That(Directory.GetFiles(_directory, "*.png"), Is.Empty);
    }

    [Test, Category("Integration")]
    public void MetricCaptureDiagnosticWorkflowCapturesBothModesAndRestoresFullView()
    {
        using var fixture = new MetricCaptureDiagnostic(_directory, activate: true);
        FinishWindowCheck(fixture.Workflow());
        foreach (var name in new[] { "cpu", "cpu-thermal", "memory", "disk", "gpu" })
        {
            fixture.AssertPng(name, false);
            fixture.AssertPng(name, true);
        }
        foreach (var name in new[] { "cpu", "memory", "disk", "gpu" })
            fixture.AssertClosed(fixture.Metric(name));
        Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(10));
        Assert.That(fixture.Window.CompactMode, Is.False);
    }

    [TestCase("model-type"), TestCase("keyboard"), TestCase("toggle-model")]
    public void MetricCaptureDiagnosticWorkflowPreservesFullTooltipValidation(string partition)
    {
        using var fixture = new MetricCaptureDiagnostic(_directory);
        var metric = fixture.Window.CpuMetric;
        if (partition == "model-type") metric.ToolTip = new object();
        if (partition == "keyboard") ToolTipService.SetShowsToolTipOnKeyboardFocus(metric.Toggle, false);
        if (partition == "toggle-model") metric.Toggle.ToolTip = new object();
        var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Workflow()));
        Assert.That(error!.Message, Is.EqualTo("Full resource tooltip does not share its detail model."));
        Assert.That(fixture.Opened, Is.Empty);
        Assert.That(fixture.Window.CompactMode, Is.False);
    }

    [TestCase(-1), TestCase(3), TestCase(4), TestCase(5), TestCase(6), TestCase(7), TestCase(8)]
    [Category("Integration")]
    public void MetricCaptureDiagnosticWorkflowPreservesMemoryRowValidation(int row)
    {
        using var fixture = new MetricCaptureDiagnostic(_directory);
        var details = (MetricDetails)fixture.Window.MemoryMetric.ToolTip;
        if (row == -1) details.Rows.Add(new DetailRow { Label = "extra" });
        else details.Rows[row].Label = "owned wrong label";
        var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Workflow()));
        Assert.That(error!.Message, Is.EqualTo("Memory details did not expose all nullable pressure readings."));
        fixture.AssertPng("cpu", false);
        fixture.AssertPng("cpu-thermal", false);
        Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(2));
        fixture.AssertClosed(fixture.Window.CpuMetric);
        Assert.That(fixture.Window.CompactMode, Is.False);
    }

    [TestCase("focusable"), TestCase("keyboard"), TestCase("model"), TestCase("name")]
    [Category("Integration")]
    public void MetricCaptureDiagnosticWorkflowPreservesCompactAccessibilityValidationAndRestoresFullView(string partition)
    {
        using var fixture = new MetricCaptureDiagnostic(_directory);
        var box = fixture.Window.CompactTiles.Single(tile => tile.Source == fixture.Window.CpuMetric).Box;
        if (partition == "focusable") box.Focusable = false;
        if (partition == "keyboard") ToolTipService.SetShowsToolTipOnKeyboardFocus(box, false);
        if (partition == "model") box.ToolTip = new object();
        var descriptor = partition == "name"
            ? DependencyPropertyDescriptor.FromProperty(AutomationProperties.NameProperty, box.GetType()) : null;
        // RefreshCompactView normally restores the accessible name. Keep this deliberately
        // invalid validator input in place through that refresh, without changing production.
        EventHandler invalidateName = (_, _) =>
        {
            if (AutomationProperties.GetName(box) != "owned unrelated tile")
                AutomationProperties.SetName(box, "owned unrelated tile");
        };
        descriptor?.AddValueChanged(box, invalidateName);
        try
        {
            if (partition == "name")
            {
                Assert.That(descriptor, Is.Not.Null);
                AutomationProperties.SetName(box, "owned unrelated tile");
            }
            var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Workflow()));
            Assert.That(error!.Message, Is.EqualTo("Compact resource tile lacks keyboard-accessible details."));
            Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(5));
            fixture.AssertClosed(fixture.Window.GpuMetric);
            Assert.That(fixture.Window.CompactMode, Is.False);
        }
        finally { descriptor?.RemoveValueChanged(box, invalidateName); }
    }

    [Test, Category("Integration")]
    public void MetricCaptureDiagnosticWorkflowPreservesTabOrderFailureAndRestoresFullView()
    {
        using var fixture = new MetricCaptureDiagnostic(_directory, activate: true);
        var second = fixture.Window.CompactTiles[1].Box;
        KeyboardNavigation.SetIsTabStop(second, false);
        var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Workflow()));
        Assert.That(error!.Message, Is.EqualTo("Compact resource tab order is not left to right."));
        Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(10));
        fixture.AssertClosed(fixture.Window.GpuMetric);
        Assert.That(fixture.Window.CompactMode, Is.False);
    }

    private sealed class MetricCaptureDiagnostic : IDisposable
    {
        private readonly UpdateUi _ui;
        private readonly string _output;
        internal MainWindow Window => _ui.Window;
        internal List<ToolTip> Opened { get; } = [];
        internal MetricCaptureDiagnostic(string directory, string? output = null, bool activate = false)
        {
            _ui = new UpdateUi(directory, true);
            try
            {
                Directory.CreateDirectory(directory);
                _output = output ?? directory;
                Window.ApplyTheme(false, false);
                var style = new Style(typeof(ToolTip));
                style.Setters.Add(new EventSetter(ToolTip.OpenedEvent,
                    new RoutedEventHandler((sender, _) => Opened.Add((ToolTip)sender))));
                Application.Current.Resources[typeof(ToolTip)] = style;
                Window.Session.Record(new MonitorSample(DateTimeOffset.UnixEpoch, 7, [], 0)
                { Resources = new ResourceSample { TotalMemoryBytes = 32UL * 1_073_741_824, UsedMemoryBytes = 16UL * 1_073_741_824 } });
                Window.ShowActivated = activate;
                Window.ShowInTaskbar = false;
                Window.Title = "Owned metric capture diagnostic fixture";
                Window.Width = 1000;
                Window.Height = 840;
                Window.Show();
                if (activate) Window.Activate();
                FinishWindowCheck(Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
            }
            catch { _ui.Dispose(); throw; }
        }

        internal MetricReadout Metric(string name) => name switch
        {
            "cpu" => Window.CpuMetric, "memory" => Window.MemoryMetric,
            "gpu" => Window.GpuMetric, _ => Window.DiskMetric
        };

        internal Task Workflow() => typeof(WindowDiagnostics)
            .GetMethod("CaptureMetricDetailsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, string, CancellationToken, Task>>(new WindowDiagnostics(Window))(_output, "owned", default);

        internal Task Capture(bool compact, MetricReadout metric, CancellationToken token = default)
        {
            var method = typeof(WindowDiagnostics).GetNestedTypes(BindingFlags.NonPublic)
                .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Single(value => value.Name.StartsWith("<CaptureMetricDetailsAsync>g__CaptureMetric|", StringComparison.Ordinal));
            var closure = Activator.CreateInstance(method.DeclaringType!, nonPublic: true)!;
            foreach (var field in method.DeclaringType!.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object value = field.Name switch
                {
                    "output" => _output, "theme" => "owned", "cancellationToken" => token,
                    "<>4__this" => new WindowDiagnostics(Window),
                    _ => throw new AssertionException("Metric capture closure changed: " + field.Name)
                };
                field.SetValue(closure, value);
            }
            return method.CreateDelegate<Func<bool, MetricReadout, Task>>(closure)(compact, metric);
        }

        internal void AssertPng(string name, bool compact)
        {
            using var stream = File.Open(Path.Combine(_output, $"tooltip-{name}-owned-{(compact ? "compact" : "full")}.png"),
                FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
            Assert.That(frame.PixelWidth, Is.GreaterThan(0));
            Assert.That(frame.PixelHeight, Is.GreaterThan(0));
            Assert.That(frame.DpiX, Is.EqualTo(96).Within(.1));
        }

        internal void AssertClosed(MetricReadout metric)
        {
            Assert.That(Opened.All(tooltip => !tooltip.IsOpen), Is.True);
            var details = (MetricDetails)metric.ToolTip;
            details.Rows[0].Value = "owned closed-details marker";
            Window.RefreshVisiblePresentation();
            Assert.That(details.Rows[0].Value, Is.EqualTo("owned closed-details marker"));
            Assert.That(Window.Session.HasLiveSample, Is.False);
        }

        public void Dispose()
        {
            foreach (var tooltip in Opened) tooltip.IsOpen = false;
            _ui.Dispose();
        }
    }
}
