using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture, Apartment(ApartmentState.STA), SetUICulture("en")]
public sealed class ConnectionsViewTests
{
    [Test]
    public void RankedSeparatorFollowsVisiblePinsAndSelectedRanking()
    {
        var other = Owner with { ProcessId = 99, CreationTicks = 456, ImagePath = @"C:\other.exe", AppName = "Other" };
        var view = new ConnectionsView();
        view.Load(new ConnectionListSettings(10, ConnectionSort.Tcp));
        view.Update(Frame(Tcp, Udp with { ProcessId = 99, ProcessCreationTicks = 456 }), [Owner, other], false);
        Assert.That(view.DisplayedRows.All(row => row.RankedLabel.Length == 0), Is.True);
        view.TogglePin(Owner);
        Assert.That(view.DisplayedRows.First().RankedLabel, Is.Empty);
        Assert.That(view.DisplayedRows.Last().RankedLabel,
            Is.EqualTo(WinMon.Resources.Strings.Format(WinMon.Resources.Strings.RankedByMetric, WinMon.Resources.Strings.ConnectionsTcpCount)));
        view.Load(new ConnectionListSettings(10, ConnectionSort.Udp));
        Assert.That(view.DisplayedRows.Last().RankedLabel,
            Is.EqualTo(WinMon.Resources.Strings.Format(WinMon.Resources.Strings.RankedByMetric, WinMon.Resources.Strings.ConnectionsUdpCount)));
        view.PinnedOnly.IsChecked = true;
        Assert.That(view.DisplayedRows.All(row => row.RankedLabel.Length == 0), Is.True);
        view.PinnedOnly.IsChecked = false;
        view.Search.Text = "Other";
        Assert.That(view.DisplayedRows.Single().RankedLabel, Is.Empty);
        view.Search.Clear();
        view.TogglePin(Owner);
        Assert.That(view.DisplayedRows.All(row => row.RankedLabel.Length == 0), Is.True);
        Assert.That(view.PinSummary.Visibility, Is.EqualTo(System.Windows.Visibility.Collapsed));
    }

    [Test]
    public void ClearButtonTracksSearchTextAndRestoresFilteredRows()
    {
        var view = new ConnectionsView();
        view.Update(Frame(Tcp), [Owner], false);
        Assert.That(view.ClearConnectionSearchButton.Visibility, Is.EqualTo(System.Windows.Visibility.Collapsed));
        view.Search.Text = "no-such-process";
        Assert.That(view.DisplayedRows, Is.Empty);
        Assert.That(view.ClearConnectionSearchButton.Visibility, Is.EqualTo(System.Windows.Visibility.Visible));
        view.ClearConnectionSearchButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Assert.Multiple(() =>
        {
            Assert.That(view.Search.Text, Is.Empty);
            Assert.That(view.ClearConnectionSearchButton.Visibility, Is.EqualTo(System.Windows.Visibility.Collapsed));
            Assert.That(view.DisplayedRows, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ActionBarKeepsTotalsOnOneLineWithoutRepeatingGridRates()
    {
        var view = new ConnectionsView();
        view.Update(Frame(Tcp), [Owner], false,
            Network(new ProcessNetworkReading(Owner.ProcessId, Owner.CreationTicks, 100, 200, 10, 20)), true);
        view.Rows.SelectedIndex = 0;
        Assert.Multiple(() =>
        {
            Assert.That(view.SelectionDetail.Text, Does.Contain("100 B / 200 B"));
            Assert.That(view.SelectionDetail.Text, Does.Not.Contain("B/s").And.Not.Contain("\n"));
            Assert.That(view.SelectionDetail.ToolTip, Does.Contain("10 B/s").And.Contain("20 B/s"));
            Assert.That(view.SelectedDetailsForFixture, Does.Contain("100 B / 200 B").And.Contain("10 B/s"));
        });
        view.Update(Frame(Tcp), [Owner], false);
        Assert.That(view.SelectionDetail.Text, Is.EqualTo($"PID {Owner.ProcessId}"));
    }

    [Test]
    public void ExecutableRowsKeepAllEndpointsInPropertiesAndPreserveSelection()
    {
        var owner = Owner with { ImagePath = @"C:\sample.exe" };
        var sibling = owner with { ProcessId = 99, CreationTicks = 456 };
        var view = new ConnectionsView();
        var frame = Frame(Tcp, Udp, Tcp with { ProcessId = 99, ProcessCreationTicks = 456 });
        view.Update(frame, [owner, sibling], false);
        view.SelectConnectionForFixture(owner.ProcessId, owner.CreationTicks);
        var row = view.DisplayedRows.Single();
        Assert.That(row.Connections, Has.Count.EqualTo(3));
        Assert.That(view.SelectedDetailsForFixture, Does.Contain("203.0.113.24:443").And.Contain("[::]:5353"));
        view.Update(frame, [owner, sibling], false);
        Assert.That(view.Rows.SelectedItem, Is.SameAs(row));
        view.Search.Text = "203.0.113.24";
        Assert.That(view.DisplayedRows.Single().Connections, Has.Count.EqualTo(3));
        Assert.That(view.FindName("IndividualChip"), Is.Null);
        Assert.That(view.FindName("GroupedChip"), Is.Null);
    }

    [Test]
    public void PinsStayAboveTopAndPersistWhileDisconnectedWithoutStaleReadings()
    {
        var first = Owner with { ImagePath = @"C:\sample.exe", HasAppWindow = true };
        var other = first with { ProcessId = 99, CreationTicks = 456, ImagePath = @"C:\other.exe", AppName = "Other" };
        var view = new ConnectionsView();
        var frame = Frame(Tcp, Tcp with { ProcessId = 99, ProcessCreationTicks = 456 },
            Udp with { ProcessId = 99, ProcessCreationTicks = 456 });
        view.Load(new ConnectionListSettings(5, ConnectionSort.Tcp));
        view.Update(frame, [first, other], false);
        var changed = 0;
        view.PinsChanged += () => changed++;
        view.TogglePin(first);
        Assert.That(view.DisplayedRows.First().Process, Is.EqualTo(first));
        Assert.That(view.DisplayedRows.First().Pinned, Is.True);
        Assert.That(view.PinnedCountLabel.Text, Is.EqualTo(WinMon.Resources.Strings.Format(WinMon.Resources.Strings.PinnedProcessCount, 1)));
        Assert.That(view.PinSummary.Visibility, Is.EqualTo(System.Windows.Visibility.Visible));
        view.PinnedOnly.IsChecked = true;
        Assert.That(view.DisplayedRows, Has.Count.EqualTo(1));
        var restored = new ConnectionsView();
        restored.RestorePins(view.SavedPins);
        var relaunched = first with { ProcessId = 100, CreationTicks = 987 };
        restored.Update(Frame(Tcp with { ProcessId = relaunched.ProcessId, ProcessCreationTicks = relaunched.CreationTicks }), [relaunched], false);
        Assert.That(restored.DisplayedRows.Single().Pinned, Is.True);
        Assert.That(restored.DisplayedRows.Single().Process, Is.EqualTo(relaunched));
        restored.Update(Frame(), [], false);
        var missing = restored.DisplayedRows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(missing.Pinned, Is.True);
            Assert.That(missing.Connections, Is.Empty);
            Assert.That(missing.Detail, Is.EqualTo(WinMon.Resources.Strings.ConnectionsNoObservedEndpoints));
            Assert.That(missing.Graph.Visibility, Is.EqualTo(System.Windows.Visibility.Hidden));
        });
        restored.Update(null, [], true);
        Assert.That(restored.DisplayedRows.Single().Detail, Is.EqualTo(WinMon.Resources.Strings.Unavailable));
        restored.Update(Frame(), [], true);
        Assert.That(restored.DisplayedRows.Single().Detail, Is.EqualTo(WinMon.Resources.Strings.ProcessNotRecorded));
        restored.TogglePin(first);
        Assert.That(restored.DisplayedRows, Is.Empty);
        view.TogglePin(first);
        Assert.That(changed, Is.EqualTo(2));
    }

    [Test]
    public void PinsDoNotConsumeTopLimitAndUnknownPathsUseExactLifetimes()
    {
        var owners = Enumerable.Range(1, 7).Select(pid => Owner with { ProcessId = pid, Name = $"app{pid}" }).ToArray();
        var endpoints = owners.Select(owner => Tcp with { ProcessId = owner.ProcessId }).ToArray();
        var view = new ConnectionsView();
        view.Load(new ConnectionListSettings(5, ConnectionSort.Tcp));
        view.Update(Frame(endpoints), owners, false);
        view.TogglePin(owners[6]);
        Assert.That(view.DisplayedRows, Has.Count.EqualTo(6));
        view.Update(Frame(endpoints.Select(endpoint => endpoint with { ProcessCreationTicks = Owner.CreationTicks + 1 }).ToArray()),
            owners.Select(owner => owner with { CreationTicks = owner.CreationTicks + 1 }).ToArray(), false);
        Assert.That(view.DisplayedRows.First().Connections, Is.Empty);
        Assert.That(view.DisplayedRows.First().Pinned, Is.True);
    }

    [Test]
    public void LateExecutableMetadataPromotesAndDeduplicatesPinsInOriginalOrder()
    {
        var sibling = Owner with { ProcessId = 99, CreationTicks = 456 };
        var endpoints = Frame(Tcp, Tcp with { ProcessId = 99, ProcessCreationTicks = 456 });
        var view = new ConnectionsView();
        view.Update(endpoints, [Owner, sibling], false);
        view.TogglePin(Owner);
        view.TogglePin(sibling);
        var known = Owner with { ImagePath = @"C:\sample.exe" };
        view.Update(endpoints, [known, sibling with { ImagePath = known.ImagePath }], false);
        Assert.Multiple(() =>
        {
            Assert.That(view.DisplayedRows, Has.Count.EqualTo(1));
            Assert.That(view.DisplayedRows[0].Pinned, Is.True);
            Assert.That(view.DisplayedRows[0].Connections, Has.Count.EqualTo(2));
            Assert.That(view.SavedPins, Has.Length.EqualTo(1));
            Assert.That(view.SavedPins[0].Grouped, Is.True);
        });
        view.TogglePin(known);
        Assert.That(view.DisplayedRows[0].Pinned, Is.False);
    }

    [Test]
    public void TrafficHistorySumsExecutableOwnersAndLeavesGapsForMissingRates()
    {
        var owner = Owner with { ImagePath = @"C:\sample.exe" };
        var sibling = owner with { ProcessId = 99, CreationTicks = 456 };
        var other = owner with { ProcessId = 100, ImagePath = @"C:\other.exe" };
        var traffic = Network(new(owner.ProcessId, owner.CreationTicks, 1, 2, 100, 50),
            new(sibling.ProcessId, sibling.CreationTicks, 1, 2, 200, 75), new(other.ProcessId, other.CreationTicks, 1, 2, 9999, 9999));
        var sample = new MonitorSample(DateTimeOffset.Now, 0, [owner, sibling, other], 0);
        var capture = new ConnectionCapture(sample, Frame(Tcp), [owner, sibling, other], traffic);
        ConnectionCapture[] history = [capture, capture with { Network = null },
            capture with { Network = traffic with { Quality = ProcessNetworkQuality.WarmingUp } },
            capture with { Network = Network(new ProcessNetworkReading(owner.ProcessId, owner.CreationTicks + 1, 0, 0, 999, 999)) }];
        var view = new ConnectionsView();
        view.Update(Frame(Tcp), [owner], false, traffic, true, history);
        var data = ConnectionGraphData.Build(history, view.DisplayedRows)[ConnectionGraphData.Key(owner)];
        Assert.That(data.Values, Is.EqualTo(new double?[] { 425, null, null, null }));
        Assert.That(data.ScaleMaximum, Is.EqualTo(467.5).Within(0.001));
    }

    private static readonly ProcessUsage Owner = new(424242, 123, "sample", 1) { AppName = "Sample app" };
    private static readonly NetworkConnection Tcp = new(Owner.ProcessId, Owner.CreationTicks, TransportProtocol.Tcp,
        new(IPAddress.Loopback, 50000), new(IPAddress.Parse("203.0.113.24"), 443), TcpState.Established);
    private static readonly NetworkConnection Udp = new(Owner.ProcessId, Owner.CreationTicks, TransportProtocol.Udp,
        new(IPAddress.IPv6Any, 5353));
    private static NetworkConnectionsFrame Frame(params NetworkConnection[] connections) =>
        new(connections, NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success);
    private static ProcessNetworkFrame Network(params ProcessNetworkReading[] readings) =>
        new(10, 1, ProcessNetworkQuality.None, readings);

    [Test]
    public void DisabledMonitoringUsesTcpRankingAndPreservesSavedThroughputChoice()
    {
        var view = new ConnectionsView();
        view.Load(new ConnectionListSettings(10, ConnectionSort.Send));
        var changes = 0;
        view.SettingsChanged += _ => changes++;
        view.Update(Frame(Tcp), [Owner], false);
        var receive = (System.Windows.Controls.ComboBoxItem)view.Rank.Items[0];
        Assert.Multiple(() =>
        {
            Assert.That(view.Rank.SelectedIndex, Is.EqualTo((int)ConnectionSort.Tcp));
            Assert.That(view.Settings.Sort, Is.EqualTo(ConnectionSort.Send));
            Assert.That(receive.IsEnabled, Is.False);
            Assert.That(((System.Windows.Controls.ComboBoxItem)view.Rank.Items[1]).IsEnabled, Is.False);
            Assert.That(receive.ToolTip, Is.Not.Null);
            Assert.That(view.NetworkHeader.Visibility, Is.EqualTo(System.Windows.Visibility.Collapsed));
            Assert.That(view.DisplayedRows, Has.Count.EqualTo(1));
        });
        view.Update(Frame(Tcp), [Owner], false, Network(new ProcessNetworkReading(Owner.ProcessId, Owner.CreationTicks, 1, 2, 3, 4)), true);
        Assert.Multiple(() =>
        {
            Assert.That(view.Rank.SelectedIndex, Is.EqualTo((int)ConnectionSort.Send));
            Assert.That(((System.Windows.Controls.ComboBoxItem)view.Rank.Items[0]).IsEnabled, Is.True);
            Assert.That(view.TrafficPrompt.Visibility, Is.EqualTo(System.Windows.Visibility.Collapsed));
            Assert.That(changes, Is.Zero);
        });
        view.Update(Frame(Tcp), [Owner], false);
        Assert.That(view.Rank.SelectedIndex, Is.EqualTo((int)ConnectionSort.Tcp));
        view.TopCount.SelectedItem = 20;
        Assert.That(view.Settings.Sort, Is.EqualTo(ConnectionSort.Send));
        view.Rank.SelectedIndex = 0;
        Assert.That(view.Rank.SelectedIndex, Is.EqualTo((int)ConnectionSort.Tcp));
        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void EnableTrafficLinkOnlyOffersLiveSupportedSettingsAndRaisesRequest()
    {
        var view = new ConnectionsView();
        view.Update(Frame(Tcp), [Owner], false);
        Assert.That(view.TrafficPrompt.Visibility, Is.EqualTo(NetworkSetupBroker.Supported
            ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed));
        var requested = false;
        view.TrafficSettingsRequested += () => requested = true;
        view.EnableTrafficMonitoring.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));
        Assert.That(requested, Is.True);
        view.Update(Frame(Tcp), [Owner], true);
        Assert.That(view.TrafficPrompt.Visibility, Is.EqualTo(System.Windows.Visibility.Collapsed));
    }

    [Test]
    public void ThroughputRankingExplainsDisabledMonitoring()
    {
        var view = new ConnectionsView();
        view.Update(Frame(Tcp), [Owner], false);
        Assert.That(view.Notice.Text, Is.EqualTo(WinMon.Resources.Strings.NetworkProcessMonitoringDisabled));
        Assert.That(view.Notice.Visibility, Is.EqualTo(System.Windows.Visibility.Collapsed));
        view.Load(new ConnectionListSettings(10, ConnectionSort.Tcp));
        Assert.That(view.Notice.Text, Is.Empty);
    }

    [Test]
    public void MissingProcessFromTargetSampleDisablesNavigationButKeepsProperties()
    {
        var view = new ConnectionsView { CanShowInProcesses = _ => false };
        view.Update(Frame(Tcp), [Owner], true);
        view.Rows.SelectedIndex = 0;
        Assert.Multiple(() =>
        {
            Assert.That(view.ShowProcess.IsEnabled, Is.False);
            Assert.That(view.Properties.IsEnabled, Is.True);
        });
    }

    [Test]
    public void ReusedPidAndUnknownLifetimeCannotNavigateToCurrentOwner()
    {
        var view = new ConnectionsView();
        view.Update(Frame(Tcp with { ProcessCreationTicks = 456 }), [Owner], false);
        view.SelectConnectionForFixture(Owner.ProcessId, 456);
        Assert.Multiple(() =>
        {
            Assert.That(view.SelectedProcessForFixture, Is.Null);
            Assert.That(view.ShowProcess.IsEnabled, Is.False);
            Assert.That(view.CopyDetails.IsEnabled, Is.True);
        });
        view.Update(Frame(Tcp with { ProcessCreationTicks = null }), [Owner], false);
        view.Rows.SelectedIndex = 0;
        Assert.That(view.ShowProcess.IsEnabled, Is.False);
    }

    [Test]
    public void UnavailableFrameClearsRowsAndActionsAndSuccessfulEmptyIsDistinct()
    {
        var view = new ConnectionsView();
        view.Update(Frame(Tcp), [Owner], false);
        view.SelectConnectionForFixture(Owner.ProcessId, Owner.CreationTicks);
        view.Update(new([], new(false, 5), new(false, 5)), [Owner], false);
        Assert.Multiple(() =>
        {
            Assert.That(view.DisplayedRows, Is.Empty);
            Assert.That(view.CopyDetails.IsEnabled, Is.False);
            Assert.That(view.ShowProcess.IsEnabled, Is.False);
            Assert.That(view.Notice.Text, Does.Contain("unavailable"));
        });
        view.Update(Frame(), [Owner], false);
        Assert.That(view.Notice.Text, Is.EqualTo("No matching connections."));
    }

    [Test]
    public void GroupCopyIncludesEveryFilteredEndpointAndUdpHasNoPeerOrState()
    {
        var view = new ConnectionsView();
        view.Update(Frame(Tcp, Udp), [Owner], true);
        view.Rows.SelectedIndex = 0;
        string? report = null;
        view.CopyRequested += text => report = text;
        view.CopyDetails.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Assert.Multiple(() =>
        {
            Assert.That(report, Does.Contain("Sample app").And.Contain("203.0.113.24:443").And.Contain("[::]:5353"));
            Assert.That(view.Notice.Text, Does.Contain("selected sample"));
            Assert.That(view.DisplayedRows[0].Icon, Is.Not.Null);
            Assert.That(ConnectionsView.StateText(null), Is.EqualTo(WinMon.Resources.Strings.MissingReading));
        });
    }

    [Test]
    public void UnmatchedOwnerShowsItsPidInsteadOfZeroProcesses()
    {
        var view = new ConnectionsView();
        view.Update(Frame(Tcp), [], false);
        Assert.That(view.DisplayedRows.Single().Detail, Is.EqualTo("PID 424242"));
    }

    [Test]
    public void UnknownExecutablePathsNeverMergeAndGroupNavigationUsesLiveMembers()
    {
        var other = Owner with { ProcessId = 99, CreationTicks = 234, AppName = Owner.AppName };
        var view = new ConnectionsView();
        view.Update(Frame(Tcp, Tcp with { ProcessId = other.ProcessId, ProcessCreationTicks = other.CreationTicks }), [Owner, other], false);
        Assert.That(view.DisplayedRows.Count, Is.EqualTo(2));

        IReadOnlyList<ProcessUsage>? requested = null;
        view.GroupRequested += members => requested = members;
        view.Rows.SelectedItem = view.DisplayedRows.First();
        view.ShowProcess.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Assert.That(requested, Is.Not.Null.And.Count.EqualTo(1));
    }

    [Test]
    public void GroupedNetworkSummaryUsesAllKnownExecutableLifetimesNotEndpoints()
    {
        var sibling = Owner with { ProcessId = 99, CreationTicks = 234, ImagePath = @"C:\Apps\sample.exe" };
        var owner = Owner with { ImagePath = @"c:\apps\SAMPLE.exe" };
        var view = new ConnectionsView();
        var network = Network(
            new ProcessNetworkReading(owner.ProcessId, owner.CreationTicks, 100, 200, 10, 20),
            new ProcessNetworkReading(sibling.ProcessId, sibling.CreationTicks, 300, 400, 30, 40)) with { Owners = [sibling] };
        view.Update(Frame(Tcp), [owner], false, network, true);

        var group = view.DisplayedRows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(group.Network, Does.Contain("40 B/s").And.Contain("60 B/s"));
            Assert.That(group.NetworkToolTip, Does.Contain("400 B / 600 B"));
            Assert.That(group.NetworkColumnWidth.Value, Is.EqualTo(172));
        });
    }

    [Test]
    public void DisabledNetworkPreservesExistingColumnWidths()
    {
        var view = new ConnectionsView();
        view.Update(Frame(Tcp), [Owner], false);
        Assert.Multiple(() =>
        {
            Assert.That(view.NetworkHeaderColumn.Width.Value, Is.Zero);
            Assert.That(view.NetworkHeader.Visibility, Is.EqualTo(System.Windows.Visibility.Collapsed));
            Assert.That(view.DisplayedRows.Single().NetworkColumnWidth.Value, Is.Zero);
        });
    }

    [Test]
    public void RetainedExitedMemberTotalsDoNotMakeCurrentExecutableRatesUnavailable()
    {
        var owner = Owner with { ImagePath = @"C:\Apps\sample.exe" };
        var exited = owner with { ProcessId = 99, CreationTicks = 234 };
        var network = Network(
            new ProcessNetworkReading(owner.ProcessId, owner.CreationTicks, 100, 200, 10, 20),
            new ProcessNetworkReading(exited.ProcessId, exited.CreationTicks, 300, 400, null, null)) with
        {
            Owners = [exited],
            CurrentLifetimes = new HashSet<ProcessLifetimeIdentity> { owner.Identity }
        };
        var view = new ConnectionsView();
        view.Update(Frame(Tcp), [owner], false, network, true);

        var group = view.DisplayedRows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(group.Network, Does.Contain("10 B/s").And.Contain("20 B/s"));
            Assert.That(group.NetworkToolTip, Does.Contain("400 B / 600 B"));
        });
    }

    [Test]
    public void UnavailableNetworkFrameNeverShowsItsNumericRates()
    {
        var view = new ConnectionsView();
        var network = Network(new ProcessNetworkReading(Owner.ProcessId, Owner.CreationTicks, 100, 200, 10, 20)) with
        { Quality = ProcessNetworkQuality.Unavailable };
        view.Update(Frame(Tcp), [Owner], false, network, true);

        var group = view.DisplayedRows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(group.Network, Does.Contain(WinMon.Resources.Strings.MissingReading));
            Assert.That(group.NetworkToolTip, Does.Contain("100 B / 200 B"));
        });
    }

    [Test]
    public void PropertiesUseCompleteEndpointDetails()
    {
        var owner = Owner with { ImagePath = @"C:\Apps\sample.exe" };
        var view = new ConnectionsView();
        view.Update(Frame(Tcp), [owner], false);
        view.Rows.SelectedIndex = 0;
        (string? Name, string? Details) properties = default;
        view.PropertiesRequested += (name, details) => properties = (name, details);
        view.Properties.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Assert.That(properties.Details, Does.Contain("127.0.0.1:50000").And.Contain("203.0.113.24:443").And.Contain(owner.ImagePath));
        Assert.That(properties.Details, Does.Contain(Environment.NewLine + WinMon.Resources.Strings.ProcessPathLabel));
    }
}
