using NUnit.Framework;
using WinMon.Preferences;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase(0), TestCase(2), TestCase(4)]
    [Category("Integration"), SetCulture("en-US")]
    public void ChartsWorkflowDiagnosticRejectsNonFocusableResourcesBeforeInspectingTheChart(int index)
    {
        using var scope = new ChartsWorkflowScope(_directory);
        var window = scope.Window;
        var samples = window.Session.History.Samples.ToArray();
        var metrics = new[] { window.CpuMetric, window.MemoryMetric, window.DiskMetric,
            window.NetworkMetric, window.GpuMetric };
        metrics[index].Focusable = false;

        var exception = Assert.Throws<InvalidOperationException>(scope.Run);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message,
                Is.EqualTo("Resource controls must belong to the chart, with accessible gauge details."));
            Assert.That(metrics[index].Focusable, Is.False);
            Assert.That(window.Session.History.Samples, Is.EqualTo(samples));
            Assert.That(window.SelectedSample, Is.Null);
            Assert.That(window.Preferences.ResourceChartType, Is.EqualTo(ResourceChartType.Overlay));
            Assert.That(window.ChartTypeSelector.IsDropDownOpen, Is.False);
            Assert.That(Directory.GetFiles(_directory, "*.png").Select(Path.GetFileName).Order(),
                Is.EqualTo(new[] { "chart-help-header-dark.png", "chart-help-header-light.png",
                    "chart-help-selector-dark.png", "chart-help-selector-light.png", "chart-menu.png" }));
        });
        scope.AssertOwnerAvailable();
    }
}
