using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class TrendNavigationCharacterizationTests
{
    private static readonly DateTimeOffset Start = new(2025, 2, 11, 12, 0, 0, TimeSpan.Zero);

    [TestCase("not-loaded")]
    [TestCase("empty-archive")]
    public void EmptyTrendsIgnoreBothNavigationCommands(string partition)
    {
        var window = new FlightRecorderWindow([]);
        try
        {
            if (partition == "empty-archive") window.UpdateTrends(new(1, Start, []));
            window.HistoryMode.SelectedIndex = 1;
            var selected = new List<ResourceTrendBucket>();
            window.TrendGraph.BucketSelected += (_, bucket) => selected.Add(bucket);
            Assert.That(window.TrendPrevious.IsEnabled, Is.False);
            Assert.That(window.TrendNext.IsEnabled, Is.False);
            var text = window.TrendSelection.Text;
            Click(window.TrendPrevious);
            Click(window.TrendNext);
            Assert.That(selected, Is.Empty);
            Assert.That(window.TrendSelection.Text, Is.EqualTo(text));
            Assert.That(window.Selection, Is.Null);
        }
        finally { window.Close(); }
    }

    [TestCase(-1)]
    [TestCase(1)]
    public void NavigationSelectsTheAdjacentMinuteWithoutChangingDetailedHistory(int direction)
    {
        var sample = new MonitorSample(Start.AddSeconds(30), 15, [], 0);
        var window = new FlightRecorderWindow([sample]);
        try
        {
            window.UpdateTrends(Archive());
            window.HistoryMode.SelectedIndex = 1;
            window.SelectTrendMinuteForFixture(Start.AddMinutes(1));
            var selected = new List<ResourceTrendBucket>();
            window.TrendGraph.BucketSelected += (_, bucket) => selected.Add(bucket);
            Click(direction < 0 ? window.TrendPrevious : window.TrendNext);
            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.That(selected[0].StartUtc, Is.EqualTo(Start.AddMinutes(1 + direction)));
            Assert.That(window.TrendSelection.Text, Does.Contain(direction < 0 ? "10.0%" : "30.0%"));
            Assert.That(window.Selection, Is.SameAs(sample));
            Assert.That(window.HistoryMode.SelectedIndex, Is.EqualTo(1));
        }
        finally { window.Close(); }
    }

    [TestCase(-1)]
    [TestCase(1)]
    public void NavigationClampsAtTheDisplayedRangeBoundary(int direction)
    {
        var window = new FlightRecorderWindow([]);
        try
        {
            window.UpdateTrends(Archive());
            window.HistoryMode.SelectedIndex = 1;
            var end = Start.AddMinutes(3);
            var boundary = direction < 0 ? end.AddDays(-1) : end;
            window.SelectTrendMinuteForFixture(boundary);
            var previousText = window.TrendSelection.Text;
            var selected = new List<ResourceTrendBucket>();
            window.TrendGraph.BucketSelected += (_, bucket) => selected.Add(bucket);
            var outward = direction < 0 ? window.TrendPrevious : window.TrendNext;
            Assert.That(outward.IsEnabled, Is.False);
            // RaiseEvent also verifies the defensive clamp if an already-queued command arrives.
            Click(outward);
            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.That(selected[0].StartUtc, Is.EqualTo(boundary));
            Assert.That(window.TrendSelection.Text, Is.EqualTo(previousText));
            Click(direction < 0 ? window.TrendNext : window.TrendPrevious);
            Assert.That(selected[^1].StartUtc, Is.EqualTo(boundary.AddMinutes(-direction)));
        }
        finally { window.Close(); }
    }

    private static ResourceTrendArchiveState Archive() => new(1, Start.AddMinutes(3),
        Enumerable.Range(0, 3).Select(minute => new ResourceTrendBucket(Start.AddMinutes(minute),
            new Dictionary<ResourceTrendMetric, ResourceTrendValue>
            {
                [ResourceTrendMetric.Cpu] = new((minute + 1) * 600, 60, 10, 30, "", ResourceTrendBreak.None)
            })).ToArray());

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
