using System.Windows;
using System.Windows.Controls;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ChartTypeSelectionCharacterizationTests
{
    private readonly Dictionary<object, object?> _resources = [];
    private MainWindow _window = null!;

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (System.Collections.DictionaryEntry entry in app.Resources) _resources.Add(entry.Key, entry.Value);
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) app.Resources[key] = new Style();
        if (!app.Resources.Contains("TitleBarButtonText")) app.Resources["TitleBarButtonText"] = new DataTemplate();
        if (!app.Resources.Contains(typeof(Button))) app.Resources[typeof(Button)] = new Style(typeof(Button));
        _window = new MainWindow(true);
        _window.Loaded -= _window.OnLoaded;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var view in new FrameworkElement[] { _window.UsageView, _window.Processes, _window.ServiceView, _window.ConnectionView })
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        _window.CloseForFixture();
        var resources = Application.Current.Resources;
        foreach (var key in resources.Keys.Cast<object>().Where(key => !_resources.ContainsKey(key)).ToArray()) resources.Remove(key);
        foreach (var (key, value) in _resources) resources[key] = value;
        _resources.Clear();
    }

    [TestCase(ResourceChartType.SmallMultiples, 12)]
    [TestCase(ResourceChartType.Heatmap, 12)]
    [TestCase(ResourceChartType.LogicalProcessors, 13)]
    public void LayoutSelectionRestoresItsSavedControlsAndRemembersThePreviousLayout(ResourceChartType type, int mask)
    {
        Prepare();
        var before = _window.Preferences;

        Choose(type);

        AssertChart(type, mask, 600, 2, false);
        Assert.That(_window.Preferences.PrimaryDrive, Is.EqualTo(before.PrimaryDrive));
        Assert.That(_window.Preferences.RetentionSeconds, Is.EqualTo(before.RetentionSeconds));
        Assert.That(_window.Preferences.PinnedProcesses, Is.SameAs(before.PinnedProcesses));
        Choose(ResourceChartType.Overlay);
        AssertChart(ResourceChartType.Overlay, 3, 120, 0, true);
        var remembered = _window.Preferences.ChartConfigurations.Single(chart => chart.Type == type);
        Assert.Multiple(() =>
        {
            Assert.That(remembered.VisibleMetrics, Is.EqualTo(mask));
            Assert.That(remembered.RangeSeconds, Is.EqualTo(600));
            Assert.That(remembered.ControlsCollapsed, Is.False);
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void FocusSelectionEnablesTheChosenSeriesAndSynchronizesItsSelector(int metric)
    {
        Prepare();

        Choose(ResourceChartType.Focus, metric);

        AssertChart(ResourceChartType.Focus, 4 | (1 << metric), 1800, metric, false);
        Assert.That(_window.Preferences.SecondaryDrive, Is.EqualTo("D:"));
        Assert.That(_window.SelectedSample, Is.Not.Null);
        Assert.That(_window.SamplingTask, Is.Null);
    }

    [Test]
    public void SelectingTheCurrentLayoutStillExpandsThePanel()
    {
        Prepare();
        _window.ChartTypeSelector.RaiseEvent(new SelectionChangedEventArgs(
            System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));

        AssertChart(ResourceChartType.Overlay, 3, 120, 0, true);
    }

    [Test]
    public void AChoiceBeforePreferencesLoadIsIgnored()
    {
        var before = _window.Preferences;
        Choose(ResourceChartType.Heatmap);
        Assert.That(_window.Preferences, Is.SameAs(before));
        Assert.That(_window.SamplingTask, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AClosingOwnerIgnoresChartChoices(bool collapsed)
    {
        Prepare();
        _window.Preferences = _window.Preferences with { ChartPanelCollapsed = collapsed };
        _window.ApplyActivityPanel();
        var before = _window.Preferences;
        _window.IsClosing = true;

        Choose(ResourceChartType.Heatmap);

        Assert.That(_window.Preferences, Is.SameAs(before));
        Assert.That(_window.ChartBody.Visibility, Is.EqualTo(collapsed ? Visibility.Collapsed : Visibility.Visible));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void MissingAndMalformedSelectionsDoNotChangePreferencesOrExpandThePanel(int kind)
    {
        Prepare();
        var before = _window.Preferences;
        object? selection = kind switch
        {
            0 => null,
            1 => new Separator(),
            2 => new ComboBoxItem(),
            _ => new ComboBoxItem { Tag = ResourceChartType.Heatmap }
        };
        if (selection is not null) _window.ChartTypeSelector.Items.Add(selection);

        _window.ChartTypeSelector.SelectedItem = selection;

        Assert.That(_window.Preferences, Is.SameAs(before));
        Assert.That(_window.ChartBody.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void RebuildingSelectorsDoesNotPublishIntermediateChartChoices()
    {
        Prepare();
        var before = _window.Preferences;
        var selected = _window.SelectedSample;

        _window.BuildChartSelectors();

        Assert.Multiple(() =>
        {
            Assert.That(_window.Preferences, Is.SameAs(before));
            Assert.That(_window.SelectedSample, Is.SameAs(selected));
            Assert.That(_window.ChartBody.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(((ComboBoxItem)_window.ChartTypeSelector.SelectedItem).Tag,
                Is.EqualTo(new ResourceChartChoice(ResourceChartType.Overlay)));
        });
    }

    [TestCase(ResourceChartType.Heatmap, null)]
    [TestCase(ResourceChartType.Focus, 3)]
    public void AVisibleChartChoiceRefreshesTheReadingAndGraphWithoutStartingSampling(ResourceChartType type, int? metric)
    {
        Prepare();
        _window.ShowInTaskbar = false;
        _window.ShowActivated = false;
        _window.Opacity = 0;
        _window.ApplyTheme();
        _window.Show();
        var sample = new MonitorSample(DateTimeOffset.UnixEpoch, 31, [], 0)
        {
            MonotonicTimestamp = 1000L * System.Diagnostics.Stopwatch.Frequency, IntervalSeconds = 1
        };
        _window.Session.Record(sample);
        _window.SelectedSample = sample;
        _window.CpuMetric.ValueLabel.Text = "stale reading";

        Choose(type, metric);

        Assert.Multiple(() =>
        {
            Assert.That(_window.CpuMetric.ValueLabel.Text, Is.EqualTo(Strings.Format(Strings.PercentOneDecimal, 31d)));
            Assert.That(_window.OverallGraph.ChartType, Is.EqualTo(type));
            Assert.That(_window.OverallGraph.FocusMetric, Is.EqualTo(metric ?? 2));
            Assert.That(_window.SelectedSample, Is.SameAs(sample));
            Assert.That(_window.Session.History.Samples, Has.Count.EqualTo(1));
            Assert.That(_window.SamplingTask, Is.Null);
        });
    }

    private void Prepare()
    {
        Assert.That(Updates.UpdateRuntime.RestartHistory, Is.Null);
        _window.StopForFixtureAsync().GetAwaiter().GetResult();
        _window.OnLoaded(_window, new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert.That(_window.SamplingTask, Is.Null);
        Assert.That(_window.IsVisible, Is.False);
        _window.Preferences = _window.Preferences with
        {
            SecondaryDrive = "D:", VisibleMetrics = 3, ShowGpu = false, ShowSecondaryDisk = false,
            ResourceChartType = ResourceChartType.Overlay, FocusMetric = 0,
            RetentionSeconds = 3600, ChartRangeSeconds = 120, ChartPanelCollapsed = true,
            ChartResourcesCollapsed = true,
            ChartConfigurations = Enum.GetValues<ResourceChartType>().Select(type => new ResourceChartPreferences
            {
                Type = type, VisibleMetrics = type == ResourceChartType.Focus ? 4 : 12,
                FocusMetric = 2, RangeSeconds = type == ResourceChartType.Focus ? 1800 : 600,
                ControlsCollapsed = false
            }).ToArray()
        };
        _window.ApplyActivityPanel();
        _window.BuildRangeChips();
        _window.BuildChartSelectors();
        _window.SelectedSample = new MonitorSample(DateTimeOffset.UnixEpoch, 20, [], 0);
    }

    private void Choose(ResourceChartType type, int? metric = null) => _window.ChartTypeSelector.SelectedItem =
        _window.ChartTypeSelector.Items.OfType<ComboBoxItem>().Single(item =>
            Equals(item.Tag, new ResourceChartChoice(type, metric)));

    private void AssertChart(ResourceChartType type, int mask, int range, int focus, bool controlsCollapsed)
    {
        var toggles = new MetricReadout[] { _window.CpuMetric, _window.MemoryMetric, _window.DiskMetric,
            _window.NetworkMetric, _window.GpuMetric, _window.SecondaryDiskMetric };
        Assert.Multiple(() =>
        {
            Assert.That(_window.Preferences.ResourceChartType, Is.EqualTo(type));
            Assert.That(_window.Preferences.SeriesMask, Is.EqualTo(mask));
            Assert.That(_window.Preferences.ChartRangeSeconds, Is.EqualTo(range));
            Assert.That(_window.Preferences.FocusMetric, Is.EqualTo(focus));
            Assert.That(_window.Preferences.ChartResourcesCollapsed, Is.EqualTo(controlsCollapsed));
            Assert.That(_window.Preferences.ChartPanelCollapsed, Is.False);
            Assert.That(_window.ChartBody.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(_window.OverallGraph.VisibleMetrics, Is.EqualTo(mask));
            Assert.That(toggles.Select(toggle => toggle.Toggle.IsChecked == true),
                Is.EqualTo(Enumerable.Range(0, 6).Select(i => (mask & (1 << i)) != 0)));
            Assert.That(_window.RangeSelector.Children.OfType<RadioButton>().Single(chip => chip.IsChecked == true).Tag,
                Is.EqualTo(range));
            Assert.That(((ComboBoxItem)_window.ChartTypeSelector.SelectedItem).Tag,
                Is.EqualTo(new ResourceChartChoice(type, type == ResourceChartType.Focus ? focus : null)));
            Assert.That(_window.OwnedWindows.Count, Is.Zero);
            Assert.That(_window.SamplingTask, Is.Null);
        });
    }
}
