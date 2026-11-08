using NUnit.Framework;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
public sealed class ChartPreferencesTests
{
    private string _directory = null!;
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "WinMon.ChartPreferences", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, true);
    }

    [Test]
    public async Task LegacyPreferences_SeedEveryChartWithoutChangingSelections()
    {
        await File.WriteAllTextAsync(SettingsPath, """
                                                   {"VisibleMetrics":5,"ShowGpu":false,"ShowSecondaryDisk":false,
                                                    "ChartRangeSeconds":600,"ResourceChartType":"Heatmap","FocusMetric":2}
                                                   """);
        var preferences = await new PreferenceStore(SettingsPath).LoadAsync();
        Assert.Multiple(() =>
        {
            Assert.That(preferences.ResourceChartType, Is.EqualTo(ResourceChartType.Heatmap));
            Assert.That(preferences.ChartConfigurations.Select(chart => chart.Type),
                Is.EqualTo(Enum.GetValues<ResourceChartType>()));
            Assert.That(preferences.ChartConfigurations.All(chart => chart.VisibleMetrics == 5 &&
                                                                     chart.RangeSeconds == 600 &&
                                                                     chart.ControlsCollapsed), Is.True);
        });
    }

    [Test]
    public async Task SwitchingAndRestart_RestoreIndependentResourcesRangesControlsAndFocus()
    {
        var preferences = PreferenceStore.Validate(new MonitorPreferences
            { SecondaryDrive = "D:", RetentionSeconds = 3600 });
        preferences = preferences with
        {
            VisibleMetrics = 3, ShowGpu = false, ShowSecondaryDisk = false,
            ChartRangeSeconds = 600, ChartResourcesCollapsed = false
        };
        preferences = preferences.SelectChart(ResourceChartType.SmallMultiples) with
        {
            VisibleMetrics = 8, ShowGpu = false, ShowSecondaryDisk = true,
            ChartRangeSeconds = 1800, ChartResourcesCollapsed = true
        };
        preferences = preferences.SelectChart(ResourceChartType.Focus, 3) with
        {
            VisibleMetrics = 8, ShowGpu = false, ShowSecondaryDisk = false,
            ChartRangeSeconds = 3600, ChartResourcesCollapsed = false
        };
        await new PreferenceStore(SettingsPath).SaveAsync(preferences);
        var restored = await new PreferenceStore(SettingsPath).LoadAsync();
        var overlay = restored.SelectChart(ResourceChartType.Overlay);
        var lanes = overlay.SelectChart(ResourceChartType.SmallMultiples);
        var focus = lanes.SelectChart(ResourceChartType.Focus);
        Assert.Multiple(() =>
        {
            Assert.That(restored.ResourceChartType, Is.EqualTo(ResourceChartType.Focus));
            Assert.That(restored.FocusMetric, Is.EqualTo(3));
            Assert.That(overlay.SeriesMask, Is.EqualTo(3));
            Assert.That(overlay.EffectiveChartRange, Is.EqualTo(600));
            Assert.That(overlay.ChartResourcesCollapsed, Is.False);
            Assert.That(lanes.SeriesMask, Is.EqualTo(40));
            Assert.That(lanes.EffectiveChartRange, Is.EqualTo(1800));
            Assert.That(lanes.ChartResourcesCollapsed, Is.True);
            Assert.That(focus.SeriesMask, Is.EqualTo(8));
            Assert.That(focus.EffectiveChartRange, Is.EqualTo(3600));
            Assert.That(focus.FocusMetric, Is.EqualTo(3));
            Assert.That(focus.ChartResourcesCollapsed, Is.False);
        });
    }

    [Test]
    public void InvalidMissingAndDuplicateConfigurations_AreNormalized()
    {
        var preferences = PreferenceStore.Validate(new MonitorPreferences
        {
            ChartConfigurations =
            [
                null!, new ResourceChartPreferences
                {
                    Type = ResourceChartType.SmallMultiples, VisibleMetrics = 255, RangeSeconds = 17, FocusMetric = 99
                },
                new ResourceChartPreferences { Type = ResourceChartType.SmallMultiples, VisibleMetrics = 0 },
                new ResourceChartPreferences { Type = (ResourceChartType)99 }
            ]
        });
        var lanes = preferences.SelectChart(ResourceChartType.SmallMultiples);
        Assert.Multiple(() =>
        {
            Assert.That(preferences.ChartConfigurations, Has.Length.EqualTo(5));
            Assert.That(lanes.ShowSecondaryDisk, Is.True);
            Assert.That(lanes.SeriesMask, Is.EqualTo(31));
            Assert.That(lanes.EffectiveChartRange, Is.EqualTo(120));
            Assert.That(lanes.FocusMetric, Is.Zero, "An unconfigured second disk cannot be focused.");
            Assert.That(PreferenceStore.Validate(preferences with { ChartConfigurations = null! }).ChartConfigurations,
                Has.Length.EqualTo(5));
        });
    }

    [Test]
    public void UnconfiguredDisk_KeepsEachChartsSelection_AndLongRangesSurviveShorterRetention()
    {
        var preferences = PreferenceStore.Validate(new MonitorPreferences
            { SecondaryDrive = "D:", RetentionSeconds = 3600 });
        preferences = preferences with { ShowSecondaryDisk = false, ChartRangeSeconds = 3600 };
        preferences = preferences.SelectChart(ResourceChartType.Heatmap) with { SecondaryDrive = null };
        preferences = PreferenceStore.Validate(preferences with { RetentionSeconds = 600 });
        var overlay = preferences.SelectChart(ResourceChartType.Overlay);
        var heatmap = overlay.SelectChart(ResourceChartType.Heatmap) with { SecondaryDrive = "D:" };
        Assert.Multiple(() =>
        {
            Assert.That(overlay.ShowSecondaryDisk, Is.False);
            Assert.That(overlay.EffectiveChartRange, Is.EqualTo(120));
            Assert.That((overlay with { RetentionSeconds = 3600 }).EffectiveChartRange, Is.EqualTo(3600));
            Assert.That(heatmap.ShowSecondaryDisk, Is.True);
            Assert.That(heatmap.SeriesMask & 32, Is.EqualTo(32));
        });
    }

    [Test]
    public void LogicalProcessors_EnablesCpuWithoutChangingOverlay_AndHiddenFocusIsRestored()
    {
        var preferences = PreferenceStore.Validate(new MonitorPreferences { VisibleMetrics = 0, ShowGpu = false });
        var processors = preferences.SelectChart(ResourceChartType.LogicalProcessors);
        var overlay = processors.SelectChart(ResourceChartType.Overlay);
        var focus = overlay.SelectChart(ResourceChartType.Focus, 3);
        Assert.Multiple(() =>
        {
            Assert.That(processors.SeriesMask & 1, Is.EqualTo(1));
            Assert.That(overlay.SeriesMask, Is.Zero);
            Assert.That(focus.SeriesMask, Is.EqualTo(8));
            Assert.That(focus.FocusMetric, Is.EqualTo(3));
        });
    }
}