using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class TrendDetailCharacterizationTests
{
    private static readonly DateTimeOffset Start = new(2025, 2, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly ResourceTrendMetric[] Metrics = [ResourceTrendMetric.Cpu, ResourceTrendMetric.MemoryBytes,
        ResourceTrendMetric.PrimaryDiskActive, ResourceTrendMetric.SecondaryDiskActive, ResourceTrendMetric.Receive,
        ResourceTrendMetric.Gpu];

    private static MonitorSample Sample(double second, ResourceTrendMetric metric, double? value)
    {
        var resources = new ResourceSample
        {
            UsedMemoryBytes = 99, TotalMemoryBytes = 1000, DiskActivePercent = 99,
            SecondaryDiskActivePercent = 99, ReceiveBytesPerSecond = 99, GpuPercent = 99
        };
        resources = metric switch
        {
            ResourceTrendMetric.MemoryBytes => resources with { UsedMemoryBytes = value is { } used ? (ulong)used : null },
            ResourceTrendMetric.PrimaryDiskActive => resources with { DiskActivePercent = value },
            ResourceTrendMetric.SecondaryDiskActive => resources with { SecondaryDiskActivePercent = value },
            ResourceTrendMetric.Receive => resources with { ReceiveBytesPerSecond = value },
            ResourceTrendMetric.Gpu => resources with { GpuPercent = value },
            _ => resources
        };
        return new(Start.AddSeconds(second), metric == ResourceTrendMetric.Cpu ? value : 99, [], 0,
            MonotonicTimestamp: (long)((100 + second) * Stopwatch.Frequency))
            { Resources = resources, IntervalSeconds = 1 };
    }

    private static void SelectMinute(FlightRecorderWindow window, ResourceTrendMetric metric)
    {
        var values = Metrics.ToDictionary(item => item, _ => new ResourceTrendValue(600, 60, 10, 10, "", ResourceTrendBreak.None));
        window.UpdateTrends(new(1, Start.AddMinutes(1), [new ResourceTrendBucket(Start, values)]));
        window.HistoryMode.SelectedIndex = 1;
        window.TrendMetric.SelectedIndex = Array.IndexOf(Metrics, metric);
        window.SelectTrendMinuteForFixture(Start);
    }

    private static void Open(FlightRecorderWindow window) =>
        window.OpenTrendDetail.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Test]
    public void RecordedPeakOpensExactSampleInsteadOfMinuteMidpoint()
    {
        var peak = Sample(5, ResourceTrendMetric.Cpu, 98);
        var midpoint = Sample(30, ResourceTrendMetric.Cpu, 4);
        var archive = new ResourceTrendArchive();
        archive.Add(peak);
        archive.Add(midpoint);
        var window = new FlightRecorderWindow([peak, midpoint]);
        try
        {
            window.UpdateTrends(archive.Snapshot(Start.AddMinutes(1)));
            window.HistoryMode.SelectedIndex = 1;
            window.SelectTrendMinuteForFixture(Start);
            Assert.That(window.TrendSelection.Text, Does.Contain("Peak 98.0%"));
            Open(window);
            Assert.That(window.Selection, Is.SameAs(peak));
        }
        finally { window.Close(); }
    }

    [Test]
    public void SavedIncidentOnlyAttributesAnExactPeakAndRetainsItsContributors()
    {
        var peak = Sample(5, ResourceTrendMetric.Cpu, 98) with { Processes = [new(72, 123, "worker", 90)] };
        var trigger = Sample(8, ResourceTrendMetric.Cpu, 80);
        var folder = Path.Combine(Path.GetTempPath(), "WinMon.TrendIncident." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var id = Guid.NewGuid();
            var report = CaptureReports.Write(folder, trigger.Timestamp, CaptureMetric.Memory, CaptureReports.FirstLine(id) + "\n");
            var path = CaptureReports.IncidentPath(report, id.ToString("N"));
            new IncidentRecording(IncidentRecording.CurrentVersion, Stopwatch.Frequency,
                [new(peak, null, [], null, null), new(trigger, null, [], null, null)], CaptureMetric.Memory, 1).Save(path);
            var point = new ResourceTrendValue(98, 1, 98, 98, "", ResourceTrendBreak.None) { PeakUtcTicks = peak.Timestamp.UtcTicks };

            var found = FlightRecorderWindow.FindTrendIncident(folder, ResourceTrendMetric.Cpu, point);

            Assert.That(found, Is.Not.Null);
            var recording = found!.Value.Recording;
            Assert.Multiple(() =>
            {
                Assert.That(recording.Metric, Is.EqualTo(CaptureMetric.Cpu));
                Assert.That(recording.Observations[recording.SelectedIndex].Sample.Processes.Single().Name, Is.EqualTo("worker"));
                Assert.That(FlightRecorderWindow.FindTrendIncident(folder, ResourceTrendMetric.Cpu, point with { Maximum = 99 }), Is.Null);
                Assert.That(FlightRecorderWindow.FindTrendIncident(folder, ResourceTrendMetric.Cpu,
                    point with { PeakUtcTicks = peak.Timestamp.AddSeconds(1).UtcTicks }), Is.Null);
            });
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Test]
    public void EachMetricRequiresItsOwnReadingAndAcceptsZero(
        [Values(ResourceTrendMetric.Cpu, ResourceTrendMetric.MemoryBytes, ResourceTrendMetric.PrimaryDiskActive,
            ResourceTrendMetric.SecondaryDiskActive, ResourceTrendMetric.Receive, ResourceTrendMetric.Gpu)] ResourceTrendMetric metric,
        [Values(null, 0d, 42d)] double? value)
    {
        var farther = Sample(10, metric, 7);
        var center = Sample(30, metric, value);
        var window = new FlightRecorderWindow([farther, center]);
        try
        {
            SelectMinute(window, metric);
            Assert.That(window.OpenTrendDetail.IsEnabled, Is.True);
            Open(window);
            Assert.That(window.HistoryMode.SelectedIndex, Is.Zero);
            Assert.That(window.Selection, Is.SameAs(value is null ? farther : center));
        }
        finally { window.Close(); }
    }

    [Test]
    public void NetworkDetailUsesUploadWhenDownloadIsMissing([Values(null, 0d, 42d)] double? upload)
    {
        var farther = Sample(10, ResourceTrendMetric.Receive, 7);
        var center = Sample(30, ResourceTrendMetric.Receive, null);
        center = center with { Resources = center.Resources with { SendBytesPerSecond = upload } };
        var window = new FlightRecorderWindow([farther, center]);
        try
        {
            SelectMinute(window, ResourceTrendMetric.Receive);
            Assert.That(window.OpenTrendDetail.IsEnabled, Is.True);
            Open(window);
            Assert.That(window.Selection, Is.SameAs(upload is null ? farther : center));
            Assert.That(window.HistoryMode.SelectedIndex, Is.Zero);
        }
        finally { window.Close(); }
    }

    [Test]
    public void UnavailableMetricDisablesDetailUntilReadingArrives(
        [Values(ResourceTrendMetric.Cpu, ResourceTrendMetric.MemoryBytes, ResourceTrendMetric.PrimaryDiskActive,
            ResourceTrendMetric.SecondaryDiskActive, ResourceTrendMetric.Receive, ResourceTrendMetric.Gpu)] ResourceTrendMetric metric)
    {
        var missing = Sample(30, metric, null);
        var available = Sample(30, metric, 0);
        var window = new FlightRecorderWindow([missing]);
        try
        {
            SelectMinute(window, metric);
            Assert.That(window.OpenTrendDetail.IsEnabled, Is.False);
            Open(window);
            Assert.That(window.HistoryMode.SelectedIndex, Is.EqualTo(1));
            Assert.That(window.Selection, Is.SameAs(missing));
            window.Update([available]);
            SelectMinute(window, metric);
            Assert.That(window.OpenTrendDetail.IsEnabled, Is.True);
            Open(window);
            Assert.That(window.Selection, Is.SameAs(available));
            Assert.That(window.HistoryMode.SelectedIndex, Is.Zero);
        }
        finally { window.Close(); }
    }

    [TestCase(-1L, false)]
    [TestCase(0L, true)]
    [TestCase(TimeSpan.TicksPerMinute - 1, true)]
    [TestCase(TimeSpan.TicksPerMinute, false)]
    public void MinuteBoundsAreHalfOpenAfterConvertingToUtc(long ticks, bool enabled)
    {
        var sample = Sample(30, ResourceTrendMetric.Cpu, 42) with
            { Timestamp = Start.AddTicks(ticks).ToOffset(TimeSpan.FromMinutes(330)) };
        var window = new FlightRecorderWindow([sample]);
        try
        {
            SelectMinute(window, ResourceTrendMetric.Cpu);
            Assert.That(window.OpenTrendDetail.IsEnabled, Is.EqualTo(enabled));
            Open(window);
            Assert.That(window.HistoryMode.SelectedIndex, Is.EqualTo(enabled ? 0 : 1));
            Assert.That(window.Selection, Is.SameAs(sample));
        }
        finally { window.Close(); }
    }

    [Test]
    public void NearestReadingSkipsGapsAndKeepsTheFirstEqualDistance([Values(false, true)] bool centerIsGap)
    {
        var early = Sample(10, ResourceTrendMetric.Cpu, 1);
        var before = Sample(29, ResourceTrendMetric.Cpu, 2);
        var center = Sample(30, ResourceTrendMetric.Cpu, 3) with { IsGap = centerIsGap };
        var after = Sample(31, ResourceTrendMetric.Cpu, 4);
        var late = Sample(59, ResourceTrendMetric.Cpu, 5);
        var window = new FlightRecorderWindow([early, before, center, after, late]);
        try
        {
            SelectMinute(window, ResourceTrendMetric.Cpu);
            Assert.That(window.OpenTrendDetail.IsEnabled, Is.True);
            Open(window);
            Assert.That(window.Selection, Is.SameAs(centerIsGap ? before : center));
            Assert.That(window.HistoryMode.SelectedIndex, Is.Zero);
        }
        finally { window.Close(); }
    }

    [Test]
    public void OpenedRecordingCannotLinkUntilLiveHistoryReplacesIt()
    {
        var sample = Sample(30, ResourceTrendMetric.Cpu, 42);
        var window = new FlightRecorderWindow([sample]);
        try
        {
            SelectMinute(window, ResourceTrendMetric.Cpu);
            Assert.That(window.OpenTrendDetail.IsEnabled, Is.True);
            window.OpenRecording(new(IncidentRecording.CurrentVersion, Stopwatch.Frequency,
                [new IncidentObservation(sample, null, [], null, null)], CaptureMetric.Cpu, 0), "saved.wmincident");
            SelectMinute(window, ResourceTrendMetric.Cpu);
            Assert.That(window.OpenTrendDetail.IsEnabled, Is.False);
            Open(window);
            Assert.That(window.HistoryMode.SelectedIndex, Is.EqualTo(1));
            Assert.That(window.Selection, Is.SameAs(sample));
            window.Update([sample]);
            SelectMinute(window, ResourceTrendMetric.Cpu);
            Assert.That(window.OpenTrendDetail.IsEnabled, Is.True);
            Open(window);
            Assert.That(window.HistoryMode.SelectedIndex, Is.Zero);
            Assert.That(window.Selection, Is.SameAs(sample));
        }
        finally { window.Close(); }
    }
}
