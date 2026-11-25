using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[SetUICulture("en")]
public sealed class FlightRecorderEventTests
{
    [Test]
    public void MarkerNavigation_FollowsTimeFromBetweenObservationEventsAcrossLogs()
    {
        var graph = new CpuGraph
        {
            Markers = [new(9, "", "", false, "later"), new(1, "", "", false, "earlier"),
                new(5, "", "", true, "between")]
        };
        Assert.That(graph.AdjacentMarker(true)?.Tag, Is.EqualTo("earlier"));
        Assert.That(graph.AdjacentMarker(false)?.Tag, Is.EqualTo("later"));
    }

    [Test]
    public void EventWithoutFormattedMessageShowsAMissingReading()
    {
        var events = Events("source", IncidentEventCoverageStatus.Complete);
        events = events with { Events = [events.Events[0] with { Message = "" }] };
        var window = new FlightRecorderWindow(Samples(0, 1), eventReader: (_, _, _) => Task.FromResult(events));
        try
        {
            Show(window);
            window.SelectEventForFixture(window.EventMappings.Single());
            Assert.That(window.SelectedEvent.Text, Does.EndWith("—"));
        }
        finally { window.Close(); }
    }

    [Test]
    public async Task EventQuery_RefreshGenerationDiscardsLateResult()
    {
        var first = new TaskCompletionSource<IncidentEventResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<IncidentEventResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = new Queue<Task<IncidentEventResult>>([first.Task, second.Task]);
        var window = new FlightRecorderWindow(Samples(0, 1), eventReader: (_, _, _) => reads.Dequeue());
        try
        {
            Show(window);
            window.Update(Samples(10, 11));
            second.SetResult(Events("current", IncidentEventCoverageStatus.Complete));
            var elapsed = Stopwatch.StartNew();
            while (window.EventReadPending && elapsed.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Yield();
            Assert.That(window.EventReadPending, Is.False, "The current generation's completed query was not applied.");
            Assert.That(window.EventMappings.Select(item => item.Event.Provider), Is.EqualTo(["current"]));
            first.SetResult(Events("stale", IncidentEventCoverageStatus.Complete));
            await Task.Yield();
            Assert.That(window.EventMappings.Select(item => item.Event.Provider), Is.EqualTo(["current"]));
        }
        finally { window.Close(); }
    }

    [Test]
    public async Task OpenRecording_UsesSavedEventsWithoutQueryingLiveReader()
    {
        var reads = 0;
        var late = new TaskCompletionSource<IncidentEventResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var samples = Samples(0, 1);
        var window = new FlightRecorderWindow(samples, eventReader: (_, _, _) =>
        {
            reads++;
            return late.Task;
        });
        try
        {
            Show(window);
            await Task.Yield();
            var recording = new IncidentRecording(IncidentRecording.CurrentVersion, Stopwatch.Frequency,
                samples.Select(sample => new IncidentObservation(sample, null, [], null, null)).ToArray(), CaptureMetric.Cpu, 0)
            { WindowsEvents = Events("saved", IncidentEventCoverageStatus.Complete) };
            window.OpenRecording(recording, "fixture.wmincident");
            late.SetResult(Events("late", IncidentEventCoverageStatus.Complete));
            await Task.Yield();
            Assert.Multiple(() =>
            {
                Assert.That(reads, Is.EqualTo(1));
                Assert.That(window.EventMappings.Single().Event.Provider, Is.EqualTo("saved"));
                Assert.That(window.EventReadPending, Is.False);
            });
        }
        finally { window.Close(); }
    }

    [Test]
    public async Task EventPane_FiltersHalfOpenMomentWindowAndKeepsBetweenObservationDetail()
    {
        var samples = Samples(0, 1, 30);
        var events = new IncidentEventResult(
            [new IncidentEvent(samples[0].Timestamp.AddSeconds(15), "shown", 2, IncidentEventLevel.Warning, "near"),
                new IncidentEvent(samples[0].Timestamp.AddSeconds(16), "excluded", 3, IncidentEventLevel.Information, "edge"),
                new IncidentEvent(samples[2].Timestamp.AddSeconds(10), "between", 4, IncidentEventLevel.Error, "far")],
            [new IncidentEventLogCoverage("Application", IncidentEventCoverageStatus.Complete),
                new IncidentEventLogCoverage("System", IncidentEventCoverageStatus.Complete)], false);
        var window = new FlightRecorderWindow(samples, eventReader: (_, _, _) => Task.FromResult(events));
        try
        {
            Show(window);
            await Task.Yield();
            window.Select(1);
            var summaries = window.Events.Items.Cast<object>().Select(item => item.GetType().GetProperty("Summary")!.GetValue(item)?.ToString()).ToArray();
            Assert.That(summaries, Has.Some.Contains("shown"));
            Assert.That(summaries, Has.None.Contains("excluded"));
            Assert.Multiple(() =>
            {
                Assert.That(window.Graph.Markers.Single(marker => ((IncidentEventMapping)marker.Tag).Event.Provider == "shown").Pattern,
                    Is.EqualTo(ChartMarkerPattern.Triangle));
                Assert.That(window.Graph.Markers.Single(marker => ((IncidentEventMapping)marker.Tag).Event.Provider == "between").Pattern,
                    Is.EqualTo(ChartMarkerPattern.Diamond));
            });
            var between = window.EventMappings.Single(item => item.Event.Provider == "between");
            window.SelectEventForFixture(between);
            Assert.That(window.SelectedEvent.Text, Does.Contain("Between observations").And.Contain("far"));
            window.Select(2);
            var shownRow = window.Events.Items.Cast<object>().First(item =>
                item.GetType().GetProperty("Summary")!.GetValue(item)?.ToString()?.Contains("shown", StringComparison.Ordinal) == true);
            window.Events.SelectedItem = shownRow;
            Assert.That(window.SelectedEvent.Text, Does.Contain("shown").And.Contain("near"));
        }
        finally { window.Close(); }
    }

    [Test]
    public async Task EventLink_RetainedOwnerDoesNotSupplyReadingsForAnotherMoment()
    {
        var owner = new ProcessUsage(7, 123, "retained", 99, 1_000_000, 5_000) { GpuPercent = 88 };
        var samples = Samples(0, 1);
        var events = new IncidentEventResult(
            [new(samples[1].Timestamp, "Application Error", 1000, IncidentEventLevel.Error, "Crash", 7, 123)],
            [new("Application", IncidentEventCoverageStatus.Complete), new("System", IncidentEventCoverageStatus.Complete)], false);
        var window = new FlightRecorderWindow(samples,
            traffic: [new ConnectionCapture(samples[0], null, [owner], null)],
            eventReader: (_, _, _) => Task.FromResult(events));
        try
        {
            Show(window);
            await Task.Yield();
            window.SelectEventForFixture(window.EventMappings.Single());
            Assert.That(window.Views.SelectedIndex, Is.EqualTo(1));
            Assert.That(window.Processes.SelectedItem, Is.Not.Null);
            var row = window.Processes.SelectedItem!;
            foreach (var property in new[] { "Cpu", "Memory", "Io", "Gpu", "Network" })
                Assert.That(row.GetType().GetProperty(property)!.GetValue(row), Is.EqualTo("—"));
            Assert.That(window.Graph.Markers.Single().Selected, Is.True);
        }
        finally { window.Close(); }
    }

    [Test]
    public async Task CrashSummary_GroupsSignedEventsAndSelectsEveryMemberOnTheTimeline()
    {
        var samples = Samples(0, 1, 2);
        var signature = new CrashSignature(CrashKind.Crash, "Example.exe", "1.0", "example.dll", "2.0", 0xC0000005, 0x10);
        var events = new IncidentEventResult(
            [new(samples[0].Timestamp, "Application Error", 1000, IncidentEventLevel.Error, "first", CrashSignature: signature),
                new(samples[2].Timestamp, "Application Error", 1000, IncidentEventLevel.Error, "second", CrashSignature: signature),
                new(samples[1].Timestamp, "Application Hang", 1002, IncidentEventLevel.Error, "hang",
                    CrashSignature: new(CrashKind.Hang, "Hung.exe", "1.0"))],
            [new("Application", IncidentEventCoverageStatus.Complete), new("System", IncidentEventCoverageStatus.Complete)], false);
        var window = new FlightRecorderWindow(samples, eventReader: (_, _, _) => Task.FromResult(events));
        try
        {
            Show(window);
            await Task.Yield();
            Assert.That(window.CrashSummaryGroups, Has.Count.EqualTo(2));
            Assert.That(window.Crashes.Items.Count, Is.EqualTo(2));
            foreach (var (width, height) in new[] { (1000d, 760d), (620d, 540d) })
            {
                window.Width = width;
                window.Height = height;
                window.UpdateLayout();
                var scroll = Descendants<ScrollViewer>(window.Crashes).Single();
                var row = (ListViewItem)window.Crashes.ItemContainerGenerator.ContainerFromIndex(0);
                var count = Descendants<TextBlock>(row).Single(text => text.Text.Contains("× · ", StringComparison.Ordinal));
                Assert.Multiple(() =>
                {
                    Assert.That(scroll.ComputedHorizontalScrollBarVisibility, Is.EqualTo(Visibility.Collapsed));
                    Assert.That(row.ActualHeight, Is.GreaterThan(0));
                    Assert.That(count.ActualWidth, Is.GreaterThan(0));
                    Assert.That(count.Text, Does.StartWith("2× · "));
                    Assert.That(count.Text.Split('·')[1].Trim(), Is.Not.Empty);
                    Assert.That(count.TransformToAncestor(window.Crashes).Transform(new Point()).Y, Is.GreaterThanOrEqualTo(0));
                    var bottomRight = count.TransformToAncestor(window.Crashes).Transform(new Point(count.ActualWidth, count.ActualHeight));
                    Assert.That(bottomRight.Y, Is.LessThanOrEqualTo(window.Crashes.ActualHeight));
                    Assert.That(bottomRight.X, Is.LessThanOrEqualTo(window.Crashes.ActualWidth));
                });
            }
            window.Crashes.SelectedItem = window.Crashes.Items.Cast<object>().Single(item =>
                item.GetType().GetProperty("Application")!.GetValue(item)?.ToString() == "Example.exe");
            Assert.Multiple(() =>
            {
                Assert.That(window.SelectedEvent.Text, Does.Contain("second"));
                Assert.That(window.Graph.Markers.Where(marker => ((IncidentEventMapping)marker.Tag).Event.CrashSignature == signature)
                    .Select(marker => marker.Selected), Is.All.True);
                Assert.That(window.Graph.Markers.Single(marker => ((IncidentEventMapping)marker.Tag).Event.CrashSignature!.Kind == CrashKind.Hang).Selected,
                    Is.False);
                Assert.That(window.Crashes.Items.Cast<object>().Select(item => item.GetType().GetProperty("Failure")!.GetValue(item)?.ToString()),
                    Has.Some.Contains("Access violation"));
            });
            window.Crashes.SelectedItem = null;
            Assert.That(window.Graph.Markers.Select(marker => marker.Selected), Has.None.True);
        }
        finally { window.Close(); }
    }

    [Test]
    public async Task Recording_DuringQueryPreservesUnknownCoverage()
    {
        var pending = new TaskCompletionSource<IncidentEventResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new FlightRecorderWindow(Samples(0, 1), eventReader: (_, _, _) => pending.Task);
        try
        {
            Show(window);
            var frozen = window.Recording();
            Assert.That(frozen.WindowsEvents.Coverage.Select(item => item.Status),
                Is.All.EqualTo(IncidentEventCoverageStatus.Unknown));
            pending.SetResult(Events("ready", IncidentEventCoverageStatus.Complete));
            var elapsed = Stopwatch.StartNew();
            while (window.EventReadPending && elapsed.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Yield();
            Assert.That(window.EventReadPending, Is.False, "The completed event query was not applied.");
            Assert.That(window.Recording().WindowsEvents.Events.Single().Provider, Is.EqualTo("ready"));
            Assert.That(frozen.WindowsEvents.Events, Is.Empty);
        }
        finally { window.Close(); }
    }

    [Test]
    public async Task EventQuery_FailureShowsPerLogUnavailableCoverage()
    {
        var window = new FlightRecorderWindow(Samples(0, 1), eventReader: (_, _, _) =>
            Task.FromException<IncidentEventResult>(new InvalidOperationException("fixture")));
        try
        {
            Show(window);
            await Task.Yield();
            Assert.That(window.EventCoverageTextValue, Does.Contain("Application").And.Contain("Unavailable"));
        }
        finally { window.Close(); }
    }

    private static void Show(FlightRecorderWindow window)
    {
        window.Resources["Surface"] = System.Windows.Media.Brushes.Black;
        window.Resources["Text"] = System.Windows.Media.Brushes.White;
        window.Show();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static IncidentEventResult Events(string provider, IncidentEventCoverageStatus coverage) => new(
        [new IncidentEvent(DateTimeOffset.UnixEpoch, provider, 1, IncidentEventLevel.Warning, provider)],
        [new IncidentEventLogCoverage("Application", coverage), new IncidentEventLogCoverage("System", coverage)], false);

    private static MonitorSample[] Samples(params int[] seconds) => seconds.Select(second =>
        new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(second), 10, [], 0, false,
            Stopwatch.Frequency * (second + 1L))).ToArray();
}
