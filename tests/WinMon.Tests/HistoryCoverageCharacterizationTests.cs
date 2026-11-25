using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class HistoryCoverageCharacterizationTests
{
    private static readonly DateTimeOffset Start = new(2025, 2, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly List<object> _addedResources = [];
    private MainWindow _window = null!;
    private static string[] Labels => [Strings.ExportResourceHistory + " / " + Strings.ProcessesTab,
        Strings.Services, Strings.ConnectionsTab, Strings.UsageTab];

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) { app.Resources[key] = new Style(); _addedResources.Add(key); }
        if (!app.Resources.Contains("TitleBarButtonText"))
        { app.Resources["TitleBarButtonText"] = new DataTemplate(); _addedResources.Add("TitleBarButtonText"); }
        if (!app.Resources.Contains(typeof(Button)))
        { app.Resources[typeof(Button)] = new Style(typeof(Button)); _addedResources.Add(typeof(Button)); }
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
        foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
        _addedResources.Clear();
    }

    private static MonitorSample Sample(int second) => new(Start.AddSeconds(second), 50, [], 0)
        { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 };

    private static NetworkConnectionsFrame Connections(bool available = true) =>
        new([], new NetworkAddressFamilyStatus(available), new NetworkAddressFamilyStatus(available));

    private void RecordAll(MonitorSample sample, bool available = true) =>
        _window.Session.Record(sample, sample, new ServiceFrame(sample, [], available), Connections(available));

    private static string Line(int source, string span, bool available, bool limited = false) =>
        Strings.Format(Strings.HistoryCoverageLine, Labels[source], span,
            available ? Strings.HistorySelectedAvailable : Strings.HistorySelectedUnavailable) +
        (limited ? " · " + Strings.HistoryShortenedByMemoryLimit : "");

    private static string Bounds(DateTimeOffset first, DateTimeOffset last) =>
        Strings.Format(Strings.HistoryCoverageBounds, first.ToLocalTime().DateTime, last.ToLocalTime().DateTime);

    private void AssertDetails(string[] expected, string resourceCapacity = "0:10:00", string serviceCapacity = "0:10:00")
    {
        var help = Strings.Format(Strings.HistoryCoverageHelp, resourceCapacity, serviceCapacity);
        Assert.That(_window.HistoryCoverageDetails(), Is.EqualTo(string.Join(Environment.NewLine, expected.Append(help))));
        Assert.That(_window.IsVisible, Is.False);
    }

    [Test]
    public void EmptyHistoriesHaveNoBoundsAndDoNotInventSelectedAvailability()
    {
        AssertDetails(Enumerable.Range(0, 4).Select(source => Line(source, Strings.MissingReading, false)).ToArray(),
            serviceCapacity: "0:02:00");
    }

    [Test]
    public void EachSourceKeepsItsOwnRetainedBoundsAndUsageIncludesTheFirstInterval()
    {
        var first = Sample(0);
        var selected = Sample(10);
        var last = Sample(20);
        _window.Session.Record(first, services: new ServiceFrame(first, []));
        RecordAll(selected);
        _window.Session.Record(last, last, connections: Connections());
        _window.SelectedSample = selected;
        AssertDetails([
            Line(0, "0:00:20", true), Bounds(Start, last.Timestamp),
            Line(1, "0:00:10", true), Bounds(Start, selected.Timestamp),
            Line(2, "0:00:10", true), Bounds(selected.Timestamp, last.Timestamp),
            Line(3, "0:00:11", true), Bounds(Start.AddSeconds(9), last.Timestamp)]);
    }

    [TestCase("live")]
    [TestCase("unretained")]
    [TestCase("same-record")]
    public void SelectionIdentityRetainsTheExistingPerSourceAvailabilityRules(string selection)
    {
        var sample = Sample(10);
        RecordAll(sample);
        _window.SelectedSample = selection switch
        {
            "live" => null,
            "unretained" => Sample(11),
            _ => sample with { }
        };
        var equalRecord = selection == "same-record";
        AssertDetails(Enumerable.Range(0, 4).SelectMany(source => new[]
        {
            Line(source, source == 3 ? "0:00:01" : "0:00:00", equalRecord && source != 2),
            Bounds(source == 3 ? sample.Timestamp.AddSeconds(-1) : sample.Timestamp, sample.Timestamp)
        }).ToArray());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ResourceGapsAndMissingCpuDoNotEraseAvailableServiceOrConnectionObservations(bool gap)
    {
        var sample = Sample(10) with { IsGap = gap, TotalCpuPercent = gap ? 50 : null };
        RecordAll(sample);
        _window.SelectedSample = sample;
        AssertDetails(Enumerable.Range(0, 4).SelectMany(source => new[]
        {
            Line(source, source == 3 ? "0:00:01" : "0:00:00", source is 1 or 2 || source == 0 && !gap),
            Bounds(source == 3 ? sample.Timestamp.AddSeconds(-1) : sample.Timestamp, sample.Timestamp)
        }).ToArray());
    }

    [Test]
    public void FailedAlignedObservationsKeepTheirBoundsWithoutBorrowingOtherSourcesAvailability()
    {
        var sample = Sample(10);
        RecordAll(sample, false);
        _window.SelectedSample = sample;
        AssertDetails(Enumerable.Range(0, 4).SelectMany(source => new[]
        {
            Line(source, source == 3 ? "0:00:01" : "0:00:00", source is 0 or 3),
            Bounds(source == 3 ? sample.Timestamp.AddSeconds(-1) : sample.Timestamp, sample.Timestamp)
        }).ToArray());
    }

    [Test]
    public void MissingAlignedObservationsNeverBorrowTheLatestAvailableFrame()
    {
        var selected = Sample(10);
        var latest = Sample(20);
        _window.Session.Record(selected);
        RecordAll(latest);
        _window.SelectedSample = selected;
        AssertDetails([
            Line(0, "0:00:10", true), Bounds(selected.Timestamp, latest.Timestamp),
            Line(1, "0:00:00", false), Bounds(latest.Timestamp, latest.Timestamp),
            Line(2, "0:00:00", false), Bounds(latest.Timestamp, latest.Timestamp),
            Line(3, "0:00:01", false), Bounds(latest.Timestamp.AddSeconds(-1), latest.Timestamp)]);
    }

    [Test]
    public void EvictingTheResourceSelectionMakesAllSourcesUnavailableEvenWhenTheirFramesRemain()
    {
        var selected = Sample(10);
        var latest = Sample(20);
        RecordAll(selected);
        RecordAll(latest);
        _window.Session.History.Resize(1);
        _window.SelectedSample = selected;
        AssertDetails([
            Line(0, "0:00:00", false), Bounds(latest.Timestamp, latest.Timestamp),
            Line(1, "0:00:10", false), Bounds(selected.Timestamp, latest.Timestamp),
            Line(2, "0:00:10", false), Bounds(selected.Timestamp, latest.Timestamp),
            Line(3, "0:00:11", false), Bounds(selected.Timestamp.AddSeconds(-1), latest.Timestamp)],
            resourceCapacity: "0:00:01");
    }

    [Test]
    public void ServiceBudgetShorteningIsReportedOnlyForTheAffectedSource()
    {
        var first = Sample(0);
        var selected = Sample(1);
        _window.Session.Record(first);
        _window.Session.Record(selected);
        var processes = Enumerable.Repeat(new ProcessUsage(1, 1, "fixture", 0), ServiceHistory.MaximumObservations + 1).ToArray();
        _window.Session.Services.Add(new ServiceFrame(first, [new ServiceGroup("fixture", [], null, processes)]));
        _window.Session.Services.Add(new ServiceFrame(selected, []));
        _window.SelectedSample = selected;
        Assert.That(_window.Session.Services.WasMemoryLimited, Is.True);
        AssertDetails([
            Line(0, "0:00:01", true), Bounds(first.Timestamp, selected.Timestamp),
            Line(1, "0:00:00", true, true), Bounds(selected.Timestamp, selected.Timestamp),
            Line(2, Strings.MissingReading, false), Line(3, Strings.MissingReading, false)]);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void TheSelectedTabBadgeOmitsBoundsAndKeepsItsAccessibleNameInSync(int source)
    {
        var sample = Sample(10);
        RecordAll(sample);
        _window.SelectedSample = sample;
        new[] { _window.ProcessesTab, _window.ServicesTab, _window.ConnectionsTab, _window.UsageTab }[source].IsChecked = true;
        _window.RefreshVisiblePresentation();
        var expected = Line(source, source == 3 ? "0:00:01" : "0:00:00", true);
        Assert.That(_window.CaptureCoverage.Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(_window.CaptureCoverage.Text, Is.EqualTo(expected));
        Assert.That(AutomationProperties.GetName(_window.CaptureCoverage), Is.EqualTo(expected));
        Assert.That(AutomationProperties.GetHelpText(_window.CaptureCoverage), Does.Contain(Strings.HistoryCoverageHelp.Split('{')[0]));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LiveOrCompactPresentationHidesTheCaptureCoverageBadge(bool compact)
    {
        var sample = Sample(10);
        RecordAll(sample);
        _window.SelectedSample = compact ? sample : null;
        _window.CompactMode = compact;
        _window.RefreshVisiblePresentation();
        Assert.That(_window.CaptureCoverage.Visibility, Is.EqualTo(Visibility.Collapsed));
    }
}
