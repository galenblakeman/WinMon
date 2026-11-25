using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ChartRangeSelectionCharacterizationTests
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
        if (_window is not null)
        {
            foreach (var view in new FrameworkElement[] { _window.UsageView, _window.Processes, _window.ServiceView, _window.ConnectionView })
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            _window.CloseForFixture();
        }
        var resources = Application.Current.Resources;
        foreach (var key in resources.Keys.Cast<object>().Where(key => !_resources.ContainsKey(key)).ToArray()) resources.Remove(key);
        foreach (var (key, value) in _resources) resources[key] = value;
        _resources.Clear();
    }

    [TestCase(120, 600, true, false)]
    [TestCase(600, 120, true, false)]
    [TestCase(120, 3600, false, false)]
    [TestCase(120, 120, true, true)]
    [TestCase(120, 120, false, false)]
    [TestCase(600, 600, true, true)]
    [TestCase(600, 600, false, false)]
    [TestCase(0, 120, true, true)]
    [TestCase(999, 120, true, true)]
    public void CheckedRangeEventStoresTheChoiceAndExpandsOnlyForAnEffectiveRangeChange(
        int savedRange, int chosenRange, bool initiallyCollapsed, bool finallyCollapsed)
    {
        Prepare(savedRange, initiallyCollapsed);
        var before = _window.Preferences;
        var selected = _window.SelectedSample;

        Range(chosenRange).RaiseEvent(new RoutedEventArgs(ToggleButton.CheckedEvent));

        Assert.Multiple(() =>
        {
            Assert.That(_window.Preferences, Is.EqualTo(before with
            {
                ChartRangeSeconds = chosenRange, ChartPanelCollapsed = finallyCollapsed
            }));
            Assert.That(_window.Preferences.EffectiveChartRange, Is.EqualTo(chosenRange));
            Assert.That(_window.ChartBody.Visibility, Is.EqualTo(finallyCollapsed ? Visibility.Collapsed : Visibility.Visible));
            Assert.That(_window.SelectedSample, Is.SameAs(selected));
            Assert.That(_window.OwnedWindows.Count, Is.Zero);
            Assert.That(_window.IsVisible, Is.False, "This fixture does not claim visible chart repaint acceptance.");
        });
    }

    [TestCase(null, TestName = "RangeChecked_NullTagKeepsPreferencesAndCollapsedPanel")]
    [TestCase("600", TestName = "RangeChecked_StringTagKeepsPreferencesAndCollapsedPanel")]
    [TestCase(600L, TestName = "RangeChecked_LongTagKeepsPreferencesAndCollapsedPanel")]
    public void NonIntegerTagsLeavePreferencesAndPanelUntouched(object? tag)
    {
        Prepare(120, true);
        var chip = Range(600);
        chip.Tag = tag;
        var before = _window.Preferences;
        var selected = _window.SelectedSample;

        chip.RaiseEvent(new RoutedEventArgs(ToggleButton.CheckedEvent));

        Assert.Multiple(() =>
        {
            Assert.That(_window.Preferences, Is.SameAs(before));
            Assert.That(_window.ChartBody.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(_window.SelectedSample, Is.SameAs(selected));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ClosingOwnerIgnoresAValidRangeEvent(bool collapsed)
    {
        Prepare(120, collapsed);
        var before = _window.Preferences;
        var selected = _window.SelectedSample;
        _window.IsClosing = true;

        Range(600).RaiseEvent(new RoutedEventArgs(ToggleButton.CheckedEvent));

        Assert.Multiple(() =>
        {
            Assert.That(_window.Preferences, Is.SameAs(before));
            Assert.That(_window.ChartBody.Visibility, Is.EqualTo(collapsed ? Visibility.Collapsed : Visibility.Visible));
            Assert.That(_window.SelectedSample, Is.SameAs(selected));
        });
    }

    [TestCase(0)]
    [TestCase(121)]
    public void UnexpectedIntegerTagsRetainTheExistingRawValueAndEffectiveFallback(int tag)
    {
        Prepare(600, true);
        var chip = Range(120);
        chip.Tag = tag;
        var before = _window.Preferences;

        chip.RaiseEvent(new RoutedEventArgs(ToggleButton.CheckedEvent));

        Assert.Multiple(() =>
        {
            Assert.That(_window.Preferences, Is.EqualTo(before with { ChartRangeSeconds = tag, ChartPanelCollapsed = false }));
            Assert.That(_window.Preferences.EffectiveChartRange, Is.EqualTo(120));
            Assert.That(_window.ChartBody.Visibility, Is.EqualTo(Visibility.Visible));
        });
    }

    [TestCase(120)]
    [TestCase(600)]
    public void VisibleRangeEventRefreshesTheLatestReadingForSameAndChangedEffectiveRanges(int chosenRange)
    {
        Prepare(120, true);
        _window.ShowInTaskbar = false;
        _window.ShowActivated = false;
        _window.Opacity = 0;
        _window.ApplyTheme();
        _window.Show();
        var sample = new MonitorSample(DateTimeOffset.UnixEpoch, 31, [], 0)
        {
            MonotonicTimestamp = 1000L * System.Diagnostics.Stopwatch.Frequency,
            IntervalSeconds = 1
        };
        _window.Session.Record(sample);
        _window.SelectedSample = sample;
        _window.CpuMetric.ValueLabel.Text = "stale reading";

        Range(chosenRange).RaiseEvent(new RoutedEventArgs(ToggleButton.CheckedEvent));

        Assert.Multiple(() =>
        {
            Assert.That(_window.IsVisible, Is.True);
            Assert.That(_window.SamplingTask, Is.Null);
            Assert.That(_window.CpuMetric.ValueLabel.Text, Is.EqualTo(Strings.Format(Strings.PercentOneDecimal, 31d)));
            Assert.That(_window.Preferences.ChartRangeSeconds, Is.EqualTo(chosenRange));
            Assert.That(_window.Preferences.ChartPanelCollapsed, Is.EqualTo(chosenRange == 120));
            Assert.That(_window.SelectedSample, Is.SameAs(sample));
            Assert.That(_window.Session.History.Samples, Has.Count.EqualTo(1));
        });
    }

    private void Prepare(int savedRange, bool collapsed)
    {
        _window.Preferences = _window.Preferences with
        {
            RetentionSeconds = 3600, ChartRangeSeconds = savedRange, ChartPanelCollapsed = collapsed
        };
        _window.ApplyActivityPanel();
        _window.BuildRangeChips();
        _window.SelectedSample = new MonitorSample(DateTimeOffset.UnixEpoch, 20, [], 0);
    }

    private RadioButton Range(int seconds) => _window.RangeSelector.Children.OfType<RadioButton>()
        .Single(chip => Equals(chip.Tag, seconds));
}
