using System.Diagnostics;
using System.Windows;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, Apartment(ApartmentState.STA), SetUICulture("en")]
public sealed class ResourceTrendViewTests
{
    [Test]
    public void TrendRefresh_PreservesTheSelectedMinuteAndReportsClockBreak()
    {
        var end = Minute(DateTimeOffset.UtcNow);
        var first = Bucket(end.AddMinutes(-2), 10);
        var selected = Bucket(end.AddMinutes(-1), 20, ResourceTrendBreak.ClockChanged);
        var window = new FlightRecorderWindow([]);
        try
        {
            Show(window);
            window.UpdateTrends(new(1, end, [first, selected]));
            window.HistoryMode.SelectedIndex = 1;
            window.SelectTrendMinuteForFixture(selected.StartUtc);
            window.UpdateTrends(new(1, end.AddSeconds(10), [first, selected, Bucket(end, 30)]));
            Assert.Multiple(() =>
            {
                Assert.That(window.TrendSelection.Text, Does.Contain(Strings.FlightRecorderTrendClockChanged));
                Assert.That(window.TrendGraph.IsVisible, Is.True);
            });
        }
        finally { window.Close(); }
    }

    [Test]
    public void EmptyTrend_ShowsTheRecordingGuidanceWithoutEnablingNavigation()
    {
        var window = new FlightRecorderWindow([]);
        try
        {
            Show(window);
            window.UpdateTrends(new(1, DateTimeOffset.UtcNow, []));
            window.HistoryMode.SelectedIndex = 1;
            Assert.Multiple(() =>
            {
                Assert.That(window.TrendCoverage.Text, Is.EqualTo(Strings.FlightRecorderTrendEmpty));
                Assert.That(window.TrendPrevious.IsEnabled, Is.False);
                Assert.That(window.OpenTrendDetail.IsEnabled, Is.False);
            });
        }
        finally { window.Close(); }
    }

    [Test]
    public void TrendSelection_UsesNoReadingForAnEmptyMinute()
    {
        var end = Minute(DateTimeOffset.UtcNow);
        var window = new FlightRecorderWindow([]);
        try
        {
            Show(window);
            window.UpdateTrends(new(1, end, [new ResourceTrendBucket(end.AddMinutes(-1), new Dictionary<ResourceTrendMetric, ResourceTrendValue>())]));
            window.HistoryMode.SelectedIndex = 1;
            window.SelectTrendMinuteForFixture(end.AddMinutes(-1));
            Assert.That(window.TrendSelection.Text, Does.Contain(Strings.FlightRecorderTrendNoReading));
        }
        finally { window.Close(); }
    }

    [Test]
    public void TrendDetailLink_RequiresARelevantNonGapSampleInTheSelectedMinute()
    {
        var end = Minute(DateTimeOffset.UtcNow);
        var timestamp = end.AddMinutes(-1).AddSeconds(30);
        var valid = Sample(timestamp, false, 40);
        var gap = Sample(timestamp, true, 40);
        Assert.Multiple(() =>
        {
            Assert.That(DetailEnabled([valid], end), Is.True);
            Assert.That(DetailEnabled([gap], end), Is.False);
            Assert.That(DetailEnabled([], end), Is.False);
        });
    }

    [Test]
    public void TrendDetailLink_OpensTheNearestDetailedObservation()
    {
        var end = Minute(DateTimeOffset.UtcNow);
        var sample = Sample(end.AddMinutes(-1).AddSeconds(30), false, 40);
        var window = new FlightRecorderWindow([sample]);
        try
        {
            Show(window);
            window.UpdateTrends(new(1, end, [Bucket(end.AddMinutes(-1), 40)]));
            window.HistoryMode.SelectedIndex = 1;
            window.SelectTrendMinuteForFixture(end.AddMinutes(-1));
            window.OpenTrendDetail.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.Multiple(() =>
            {
                Assert.That(window.HistoryMode.SelectedIndex, Is.Zero);
                Assert.That(window.Selection, Is.SameAs(sample));
            });
        }
        finally { window.Close(); }
    }

    [Test]
    public void TrendDetailLink_IsDisabledForAnOpenedIncident()
    {
        var end = Minute(DateTimeOffset.UtcNow);
        var sample = Sample(end.AddMinutes(-1).AddSeconds(30), false, 40);
        var window = new FlightRecorderWindow([sample]);
        try
        {
            Show(window);
            window.OpenRecording(new IncidentRecording(IncidentRecording.CurrentVersion, Stopwatch.Frequency,
                [new IncidentObservation(sample, null, [], null, null)], CaptureMetric.Cpu, 0), "saved.wmincident");
            window.UpdateTrends(new(1, end, [Bucket(end.AddMinutes(-1), 40)]));
            window.HistoryMode.SelectedIndex = 1;
            window.SelectTrendMinuteForFixture(end.AddMinutes(-1));
            Assert.That(window.OpenTrendDetail.IsEnabled, Is.False);
        }
        finally { window.Close(); }
    }

    private static ResourceTrendBucket Bucket(DateTimeOffset start, double value, ResourceTrendBreak trendBreak = ResourceTrendBreak.None) =>
        new(start, new Dictionary<ResourceTrendMetric, ResourceTrendValue>
        {
            [ResourceTrendMetric.Cpu] = new(value * 60, 60, value, value, "", trendBreak)
        });

    [Test]
    public void TrendRefresh_KeepsSevenDayRangeResourceAndSelectedGap()
    {
        var end = Minute(DateTimeOffset.UtcNow);
        var gap = end.AddDays(-2);
        var window = new FlightRecorderWindow([]);
        try
        {
            Show(window);
            window.UpdateTrends(new(1, end, [Bucket(end.AddDays(-3), 10), Bucket(end.AddMinutes(-1), 20)]));
            window.HistoryMode.SelectedIndex = 1;
            window.TrendRange.SelectedIndex = 1;
            window.TrendMetric.SelectedIndex = 1;
            window.SelectTrendMinuteForFixture(gap);
            window.UpdateTrends(new(1, end.AddSeconds(10), [Bucket(end.AddDays(-3), 10), Bucket(end, 30)]));
            Assert.Multiple(() =>
            {
                Assert.That(window.TrendRange.SelectedIndex, Is.EqualTo(1));
                Assert.That(window.TrendMetric.SelectedIndex, Is.EqualTo(1));
                Assert.That(window.TrendSelection.Text, Does.Contain(gap.ToLocalTime().ToString("G")));
                Assert.That(window.TrendSelection.Text, Does.Contain(Strings.FlightRecorderTrendNoReading));
            });
        }
        finally { window.Close(); }
    }

    private static bool DetailEnabled(IReadOnlyList<MonitorSample> samples, DateTimeOffset end)
    {
        var window = new FlightRecorderWindow(samples);
        try
        {
            Show(window);
            window.UpdateTrends(new(1, end, [Bucket(end.AddMinutes(-1), 40)]));
            window.HistoryMode.SelectedIndex = 1;
            window.SelectTrendMinuteForFixture(end.AddMinutes(-1));
            return window.OpenTrendDetail.IsEnabled;
        }
        finally { window.Close(); }
    }

    private static MonitorSample Sample(DateTimeOffset timestamp, bool gap, double cpu) => new(timestamp, cpu, [], 0, gap)
    {
        IntervalSeconds = 1,
        Resources = new ResourceSample { UsedMemoryBytes = 1 }
    };

    private static void Show(Window window)
    {
        window.Resources["Surface"] = System.Windows.Media.Brushes.Black;
        window.Resources["Text"] = System.Windows.Media.Brushes.White;
        window.Show();
        window.UpdateLayout();
    }

    private static DateTimeOffset Minute(DateTimeOffset value) => new(value.UtcDateTime.AddTicks(
        -(value.UtcDateTime.Ticks % TimeSpan.TicksPerMinute)));
}
