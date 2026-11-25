using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Preferences;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, Category("Integration")]
    public void ExampleDrawDiagnosticCancellationPreservesChartSizingSelectionAndVisibility()
    {
        using var fixture = new ExampleDrawDiagnostic(_directory);
        fixture.CancelledDraw("owned-overlay", 1000, 840);
    }

    [TestCase(ResourceChartType.Overlay, 0), TestCase(ResourceChartType.SmallMultiples, 0)]
    [TestCase(ResourceChartType.Focus, 0), TestCase(ResourceChartType.Focus, 3)]
    [TestCase(ResourceChartType.Heatmap, 0), TestCase(ResourceChartType.LogicalProcessors, 0)]
    [Category("Integration")]
    public void ExampleDrawDiagnosticPreservesEachChartModeBeforeNativeCaptureCancellation(ResourceChartType chart, int focus)
    {
        using var fixture = new ExampleDrawDiagnostic(_directory);
        fixture.Window.Preferences = fixture.Window.Preferences with { ResourceChartType = chart, FocusMetric = focus };
        fixture.CancelledDraw("owned-chart", 1000, 840);
    }

    [TestCase(false, 836, 840), TestCase(true, 1000, 156), Category("Integration")]
    public void ExampleDrawDiagnosticShowsHiddenWindowAndPreservesGroupedOrCompactSizing(bool compact, double width, double height)
    {
        using var fixture = new ExampleDrawDiagnostic(_directory);
        fixture.Window.Preferences = fixture.Window.Preferences with { GroupProcesses = !compact };
        fixture.Window.SetCompactView(compact);
        fixture.Window.Hide();
        Assert.That(fixture.Window.IsVisible, Is.False);
        fixture.CancelledDraw("owned-hidden", width, height);
        Assert.That(fixture.Window.CompactMode, Is.EqualTo(compact));
    }

    [Test, Category("Integration")]
    public void ExampleDrawDiagnosticPreservesPinnedRowsAndConditionalIllustrativeIconLookups()
    {
        using var fixture = new ExampleDrawDiagnostic(_directory, illustrativeRows: true);
        fixture.Pin();
        fixture.CancelledDraw("owned-icons", 1000, 840);
        Assert.That(fixture.Window.Processes.PinnedRows.Any(row => row.Identity?.Item1 == int.MaxValue - 600), Is.True);
    }

    [TestCase(true, -1d), TestCase(true, double.PositiveInfinity)]
    [TestCase(false, -1d), TestCase(false, double.PositiveInfinity)]
    public void ExampleDrawDiagnosticPreservesInvalidDimensionFailureAndEarlierWidthAssignment(bool invalidWidth, double value)
    {
        using var fixture = new ExampleDrawDiagnostic(_directory);
        fixture.Window.Hide();
        var originalWidth = fixture.Window.Width;
        var originalHeight = fixture.Window.Height;
        Assert.Catch<ArgumentException>(() => FinishWindowCheck(fixture.Draw("owned-invalid", invalidWidth ? value : 900, invalidWidth ? 700 : value)));
        Assert.That(fixture.Window.Width, Is.EqualTo(invalidWidth ? originalWidth : 900));
        Assert.That(fixture.Window.Height, Is.EqualTo(originalHeight));
        Assert.That(fixture.Window.IsVisible, Is.False);
        Assert.That(Directory.GetFiles(_directory, "*.png"), Is.Empty);
    }

    private sealed class ExampleDrawDiagnostic : IDisposable
    {
        private readonly MetricCaptureDiagnostic _fixture;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly ProcessUsage _process;
        private readonly string _directory;
        internal MainWindow Window => _fixture.Window;
        internal ExampleDrawDiagnostic(string directory, bool illustrativeRows = false)
        {
            _directory = directory;
            _fixture = new MetricCaptureDiagnostic(directory);
            try
            {
                _process = new ProcessUsage(int.MaxValue - 600, 100, "owned example draw", 3, 1_048_576)
                { HasAppWindow = true, AppName = "Owned example", ThreadCount = 7, HandleCount = 11 };
                FinishWindowCheck(ProcessIconProvider.GetAsync(_process));
                var processes = illustrativeRows ? new[] { _process }.Concat(new[] { 12000, 12001, 12003, 12004 }.Select(id =>
                    new ProcessUsage(id, id, "owned illustrative " + id, 1, 1_048_576) { HasAppWindow = true })).ToArray() : [_process];
                foreach (var process in processes) FinishWindowCheck(ProcessIconProvider.GetAsync(process));
                Window.Session.Record(new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(1), 7, processes, 0));
                Window.Session.Record(new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(2), 9, processes, 0));
                Window.RefreshVisiblePresentation();
                Window.UpdateLayout();
            }
            catch { _fixture.Dispose(); _cancellation.Dispose(); throw; }
        }
        internal void CancelledDraw(string name, double width, double height)
        {
            _cancellation.Cancel();
            var preferences = Window.Preferences;
            var samples = Window.Session.History.Samples.ToArray();
            Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(Draw(name, width, height)));
            Assert.That(Window.Width, Is.EqualTo(width));
            Assert.That(Window.Height, Is.EqualTo(height));
            Assert.That(Window.IsVisible, Is.True);
            Assert.That(Window.Preferences, Is.SameAs(preferences));
            Assert.That(Window.OverallGraph.ChartType, Is.EqualTo(preferences.ResourceChartType));
            if (preferences.ResourceChartType == ResourceChartType.Focus)
                Assert.That(Window.OverallGraph.FocusMetric, Is.EqualTo(preferences.FocusMetric));
            Assert.That(Window.Session.History.Samples, Is.EqualTo(samples));
            Assert.That(Window.Processes.Selection.HasSelection, Is.True);
            Assert.That(Window.Processes.Selection.Contains(_process, preferences.GroupProcesses), Is.True);
            Assert.That(Window.Processes.Selection.Grouped, Is.EqualTo(preferences.GroupProcesses));
            if (preferences.GroupProcesses)
                Assert.That(Window.Processes.Rows.Any(row => row.Identity is { Item1: 0, Item2: 0 } identity &&
                    identity.Item3 == "OWNED EXAMPLE DRAW"), Is.True);
            else Assert.That(Window.Processes.Rows.Any(row => row.Identity == (_process.ProcessId, _process.CreationTicks, null)), Is.True);
            Assert.That(Window.Session.HasLiveSample, Is.False);
            Assert.That(Directory.GetFiles(_directory, "*.png"), Is.Empty);
        }
        internal void Pin() => Window.Processes.ToggleProcessPin(_process, false);
        internal Task Draw(string name, double width, double height) =>
            AnonymousDiagnostic<Func<string, double, double, Task>>("<CaptureExamplesAsync>g__Draw|0",
                Window, _process, _directory, _cancellation.Token)(name, width, height);
        public void Dispose() { _fixture.Dispose(); _cancellation.Dispose(); }
    }
}
