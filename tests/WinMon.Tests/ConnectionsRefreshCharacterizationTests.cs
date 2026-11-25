using System.Net;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ConnectionsRefreshCharacterizationTests
{
    private static readonly DateTimeOffset Start = new(2025, 2, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProcessUsage Owner = new(987654, 100, "fixture", 0);
    private readonly List<object> _addedResources = [];
    private MainWindow? _window;

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
    }

    [TearDown]
    public void TearDown()
    {
        if (_window is not null)
        {
            foreach (var view in new FrameworkElement[] { _window.UsageView, _window.Processes, _window.ServiceView, _window.ConnectionView })
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            _window.CloseForFixture();
            _window = null;
        }
        foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
        _addedResources.Clear();
    }

    private MainWindow Window(bool diagnostic = true)
    {
        _window = new MainWindow(diagnostic);
        _window.Loaded -= _window.OnLoaded;
        _window.Preferences = _window.Preferences with { ConnectionTrafficSeconds = 0 };
        _window.ConnectionsTab.IsChecked = true;
        return _window;
    }

    private static MonitorSample Sample(int second, ProcessUsage? owner = null) =>
        new(Start.AddSeconds(second), 50, [owner ?? Owner], 0) { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 };

    private static NetworkConnection Endpoint(TransportProtocol protocol, ProcessUsage? owner = null) =>
        new((owner ?? Owner).ProcessId, (owner ?? Owner).CreationTicks, protocol, new IPEndPoint(IPAddress.Loopback, 1234));

    private static NetworkConnectionsFrame Frame(int tcp = 2, int udp = 1, string availability = "full", ProcessUsage? owner = null) =>
        new(Enumerable.Repeat(Endpoint(TransportProtocol.Tcp, owner), tcp)
                .Concat(Enumerable.Repeat(Endpoint(TransportProtocol.Udp, owner), udp)).ToArray(),
            new NetworkAddressFamilyStatus(availability != "unavailable"),
            new NetworkAddressFamilyStatus(availability == "full"));

    private static ProcessNetworkFrame Network(long received, long sent, ProcessNetworkQuality quality = ProcessNetworkQuality.None) =>
        new(100, 100, quality, [new ProcessNetworkReading(Owner.ProcessId, Owner.CreationTicks, received, sent, 100, 50)]);

    private static void Counts(MainWindow window, string tcp, string udp)
    {
        Assert.That(window.ConnectionTcpValue.Text, Is.EqualTo(tcp));
        Assert.That(window.ConnectionUdpValue.Text, Is.EqualTo(udp));
        Assert.That(AutomationProperties.GetName(window.ConnectionCounts), Is.EqualTo(window.ConnectionCounts.ToolTip));
    }

    [TestCase("full", "2", "1")]
    [TestCase("partial", "2", "1")]
    [TestCase("unavailable", null, null)]
    [TestCase("empty", "0", "0")]
    public void EndpointCountsDistinguishEmptyPartialAndUnavailableCollections(string availability, string? tcp, string? udp)
    {
        var window = Window();
        var sample = Sample(0);
        window.Session.Record(sample, sample);
        window.SetConnectionsForFixture(availability == "empty" ? Frame(0, 0) : Frame(availability: availability));
        Counts(window, tcp ?? Strings.MissingReading, udp ?? Strings.MissingReading);
        var expected = $"TCP: {tcp ?? Strings.MissingReading}\nUDP: {udp ?? Strings.MissingReading}";
        if (availability == "partial") expected = Strings.ConnectionsPartial + "\n" + expected;
        Assert.That(window.ConnectionCounts.ToolTip, Is.EqualTo(expected));
        Assert.That(window.ConnectionVolumeValue.Text, Is.Empty);
    }

    [TestCase(false, SamplingError.CpuUnavailable)]
    [TestCase(false, SamplingError.ReadingsUnavailable)]
    [TestCase(false, SamplingError.ProcessesUnavailable)]
    [TestCase(true, SamplingError.CpuUnavailable)]
    [TestCase(true, SamplingError.ReadingsUnavailable)]
    [TestCase(true, SamplingError.ProcessesUnavailable)]
    public void LiveDiagnosticFramesAndNativeCapturesKeepTheirDifferentErrorPolicies(bool diagnostic, SamplingError error)
    {
        var window = Window(diagnostic);
        var sample = Sample(0);
        window.Session.Record(sample, sample, connections: Frame());
        if (diagnostic) window.SetConnectionsForFixture(Frame(0, 4));
        window.ShownSamplingError = error;
        window.RefreshVisiblePresentation();
        var missing = !diagnostic && error == SamplingError.ReadingsUnavailable;
        Counts(window, missing ? Strings.MissingReading : diagnostic ? "0" : "2",
            missing ? Strings.MissingReading : diagnostic ? "4" : "1");
        window.ShownSamplingError = null;
        window.RefreshVisiblePresentation();
        Counts(window, diagnostic ? "0" : "2", diagnostic ? "4" : "1");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HistoricalCapturesOverrideBothLiveSourcesAndIgnoreTheCurrentSamplingError(bool diagnostic)
    {
        var window = Window(diagnostic);
        var selected = Sample(0);
        window.Session.Record(selected, selected, connections: Frame(1, 2));
        var latest = Sample(1);
        window.Session.Record(latest, latest, connections: Frame(7, 8));
        if (diagnostic) window.SetConnectionsForFixture(Frame(9, 10));
        window.SelectedSample = selected;
        window.ShownSamplingError = SamplingError.ReadingsUnavailable;
        window.RefreshVisiblePresentation();
        Counts(window, "1", "2");
        Assert.That(window.ConnectionCounts.ToolTip, Is.EqualTo("TCP: 1\nUDP: 2"));
        Assert.That(window.ConnectionView.TopCount.IsEnabled, Is.False);
        Assert.That(window.ConnectionView.DisplayedRows.Single().Connections, Has.Count.EqualTo(3));
    }

    [Test]
    public void MissingHistoricalConnectionsNeverBorrowTheLiveFrameOrLiveTraffic()
    {
        var window = Window();
        var selected = Sample(0);
        window.Session.Record(selected);
        var latest = Sample(1);
        window.Session.Record(latest, latest, connections: Frame(), network: Network(1500, 600));
        window.SetConnectionsForFixture(Frame());
        window.SetProcessNetworkForFixture(Network(1500, 600));
        window.SelectedSample = selected;
        window.RefreshVisiblePresentation();
        Counts(window, Strings.MissingReading, Strings.MissingReading);
        Assert.That(window.ConnectionCounts.ToolTip, Is.EqualTo(Strings.ConnectionsHistoryUnavailable +
            $"\nTCP: {Strings.MissingReading}\nUDP: {Strings.MissingReading}"));
        Assert.That(window.ConnectionVolumeValue.Text, Is.Empty);
        Assert.That(window.ConnectionView.ReceiveHeader.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [TestCase("unavailable")]
    [TestCase("partial")]
    public void RetainedHistoricalCollectionsKeepFailureAndPartialWarningsDistinctFromMissingHistory(string availability)
    {
        var window = Window();
        var selected = Sample(0);
        window.Session.Record(selected, selected, connections: Frame(availability: availability));
        var latest = Sample(1);
        window.Session.Record(latest, latest, connections: Frame(7, 8));
        window.SetConnectionsForFixture(Frame(9, 10));
        window.SelectedSample = selected;
        window.RefreshVisiblePresentation();
        var partial = availability == "partial";
        var tcp = partial ? "2" : Strings.MissingReading;
        var udp = partial ? "1" : Strings.MissingReading;
        Counts(window, tcp, udp);
        Assert.That(window.ConnectionCounts.ToolTip, Is.EqualTo(
            (partial ? Strings.ConnectionsPartial + "\n" : "") + $"TCP: {tcp}\nUDP: {udp}"));
        Assert.That(window.ConnectionVolumeValue.Text, Is.Empty);
    }

    [TestCase(ProcessNetworkQuality.None, "1.5 KB", "600 B")]
    [TestCase(ProcessNetworkQuality.Incomplete, "≈ 1.5 KB", "≈ 600 B")]
    [TestCase(ProcessNetworkQuality.WarmingUp, null, null)]
    [TestCase(ProcessNetworkQuality.Unavailable, null, null)]
    public void CumulativeTrafficPreservesQualityAvailabilityAndApproximationPrefixes(ProcessNetworkQuality quality,
        string? received, string? sent)
    {
        var window = Window();
        var sample = Sample(0);
        window.Session.Record(sample, sample, connections: Frame(), network: Network(1500, 600, quality));
        window.SetConnectionsForFixture(Frame());
        window.SetProcessNetworkForFixture(Network(1500, 600, quality));
        Assert.That(window.ConnectionVolumeValue.Text, Is.EqualTo($"  ·  {Strings.History}: ↓ {received ?? Strings.MissingReading} | ↑ {sent ?? Strings.MissingReading}"));
        var details = (string)window.ConnectionCounts.ToolTip;
        Assert.That(details, Does.Contain(Strings.ConnectionsNetworkOwnerTotals + "\n" + Strings.ConnectionsRateTiming));
        Assert.That(details, Does.Contain(Strings.ConnectionsSinceStart));
        Assert.That(details, Does.Contain(window.ConnectionVolumeValue.Text.Trim()));
        Assert.That(details, Does.Contain(received is null ? Strings.NetworkProcessMonitoringUnavailable : Strings.ConnectionsTrafficHelp));
        Counts(window, "2", "1");
        Assert.That(window.ConnectionView.ReceiveHeader.Visibility, Is.EqualTo(Visibility.Visible));
        var row = window.ConnectionView.DisplayedRows.Single();
        Assert.That(row.Received, Is.EqualTo(received ?? Strings.MissingReading));
        Assert.That(row.Sent, Is.EqualTo(sent ?? Strings.MissingReading));
    }

    [Test]
    public void DiagnosticTrafficAfterAnUnobservedRetainedCaptureStaysApproximate()
    {
        var window = Window();
        var sample = Sample(0);
        window.Session.Record(sample, sample);
        window.SetConnectionsForFixture(Frame());
        window.SetProcessNetworkForFixture(Network(1500, 600));
        Assert.That(window.ConnectionVolumeValue.Text,
            Is.EqualTo($"  ·  {Strings.History}: ↓ ≈ 1.5 KB | ↑ ≈ 600 B"));
        Assert.That(window.ConnectionCounts.ToolTip, Does.Contain(Strings.NetworkProcessMonitoringIncomplete));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DiagnosticTrafficWithoutAFullSampleCannotInventAnAvailableTrafficEnd(bool compacted)
    {
        var window = Window();
        if (compacted) window.Session.Record(Sample(0));
        window.SetConnectionsForFixture(Frame());
        window.SetProcessNetworkForFixture(Network(1500, 600));
        Counts(window, "2", "1");
        Assert.That(window.ConnectionVolumeValue.Text,
            Is.EqualTo($"  ·  {Strings.History}: ↓ {Strings.MissingReading} | ↑ {Strings.MissingReading}"));
        Assert.That(window.ConnectionCounts.ToolTip, Does.Contain(Strings.NetworkProcessMonitoringUnavailable));
    }

    [Test]
    public void SelectedTrafficUsesItsOwnWindowAndExcludesLaterCountersAndQualityFlags()
    {
        var window = Window(false);
        window.Preferences = window.Preferences with { ConnectionTrafficSeconds = 1, ProcessNetworkMonitoringEnabled = false };
        foreach (var second in new[] { 0, 1, 2 })
        {
            var sample = Sample(second);
            window.Session.Record(sample, sample, connections: Frame(),
                network: Network(second == 0 ? 100 : second == 1 ? 300 : 9000,
                    second == 0 ? 200 : second == 1 ? 400 : 9000,
                    second == 2 ? ProcessNetworkQuality.Incomplete : ProcessNetworkQuality.None));
            if (second == 1) window.SelectedSample = sample;
        }
        window.RefreshVisiblePresentation();
        Assert.That(window.ConnectionVolumeValue.Text, Is.EqualTo($"  ·  {Strings.History}: ↓ 200 B | ↑ 200 B"));
        Assert.That(window.ConnectionView.DisplayedRows.Single().Received, Is.EqualTo("200 B"));
        Assert.That(window.ConnectionCounts.ToolTip, Does.Contain(Strings.Format(Strings.GaugePeakWindowMinutes, 0)));
        Assert.That(window.ConnectionCounts.ToolTip, Does.Contain(Strings.Format(Strings.ConnectionsTrafficCoverage, 1)));
        Assert.That(window.ConnectionCounts.ToolTip, Does.Not.Contain(Strings.NetworkProcessMonitoringIncomplete));
        Assert.That(window.ConnectionView.ReceiveHeader.Visibility, Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void DisablingDiagnosticTrafficClearsItsVolumeAndColumnsWithoutClearingEndpointCounts()
    {
        var window = Window();
        var sample = Sample(0);
        window.Session.Record(sample, sample);
        window.SetConnectionsForFixture(Frame());
        window.SetProcessNetworkForFixture(Network(1500, 600));
        Assert.That(window.ConnectionVolumeValue.Text, Is.Not.Empty);
        window.SetProcessNetworkForFixture(null);
        Counts(window, "2", "1");
        Assert.That(window.ConnectionVolumeValue.Text, Is.Empty);
        Assert.That(window.ConnectionCounts.ToolTip, Is.EqualTo("TCP: 2\nUDP: 1"));
        Assert.That(window.ConnectionView.ReceiveHeader.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NativeLiveTrafficRequiresBothThePreferenceAndVariantSupport(bool enabled)
    {
        var window = Window(false);
        var sample = Sample(0);
        window.Session.Record(sample, sample, connections: Frame(), network: Network(1500, 600));
        window.Preferences = window.Preferences with { ProcessNetworkMonitoringEnabled = enabled };
        window.RefreshVisiblePresentation();
        Assert.That(window.ConnectionVolumeValue.Text, Is.EqualTo(enabled && NetworkSetupBroker.Supported
            ? $"  ·  {Strings.History}: ↓ 1.5 KB | ↑ 600 B" : ""));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InactiveOrCompactViewsSkipAllConnectionPresentationUpdates(bool compact)
    {
        var window = Window();
        window.SetConnectionsForFixture(Frame());
        window.ConnectionVolumeValue.Text = "unchanged";
        if (compact) window.CompactMode = true;
        else window.ProcessesTab.IsChecked = true;
        window.SetConnectionsForFixture(Frame(0, 0));
        Counts(window, "2", "1");
        Assert.That(window.ConnectionVolumeValue.Text, Is.EqualTo("unchanged"));
    }

    [Test]
    public void CollapsedActivityUpdatesTheHeaderButRetainsRowsUntilRestored()
    {
        var window = Window();
        var first = Sample(0);
        window.Session.Record(first, first);
        window.SetConnectionsForFixture(Frame());
        var row = window.ConnectionView.DisplayedRows.Single();
        Assert.That(row.Process?.Identity, Is.EqualTo(Owner.Identity));
        window.Preferences = window.Preferences with { ActivityPanelCollapsed = true };
        var nextOwner = Owner with { ProcessId = Owner.ProcessId + 1, CreationTicks = 101 };
        var latest = Sample(1, nextOwner);
        window.Session.Record(latest, latest);
        window.SetConnectionsForFixture(Frame(0, 4, owner: nextOwner));
        Counts(window, "0", "4");
        Assert.That(window.ConnectionView.DisplayedRows.Single(), Is.SameAs(row));
        Assert.That(row.Process?.Identity, Is.EqualTo(Owner.Identity));
        window.Preferences = window.Preferences with { ActivityPanelCollapsed = false };
        window.RefreshVisiblePresentation();
        Assert.That(window.ConnectionView.DisplayedRows.Single().Process?.Identity, Is.EqualTo(nextOwner.Identity));
    }
}
