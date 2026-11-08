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
    public void TopRanksAppsWithoutTruncatingExpandedEndpointsAndAllRemovesLimit()
    {
        var owners = Enumerable.Range(1, 6).Select(pid => Owner with
        {
            ProcessId = pid, Name = $"app{pid}", AppName = $"app{pid}", ImagePath = $@"C:\app{pid}.exe"
        }).ToArray();
        var endpoints = owners.SelectMany(owner => Enumerable.Range(0, owner.ProcessId).Select(index => Tcp with
        {
            ProcessId = owner.ProcessId, LocalEndpoint = new IPEndPoint(IPAddress.Loopback, 5000 + index)
        })).ToArray();
        var view = new ConnectionsView();
        view.Load(new ConnectionListSettings(5, ConnectionSort.Tcp));
        view.Update(Frame(endpoints), owners, false);
        Assert.That(view.DisplayedRows.Select(row => row.Process!.ProcessId), Is.EqualTo(new[] { 6, 5, 4, 3, 2 }));
        view.SelectConnectionForFixture(6, Owner.CreationTicks);
        Assert.That(view.DisplayedRows.Count(row => row.Connection is not null), Is.EqualTo(6));
        view.Load(new ConnectionListSettings(0, ConnectionSort.Tcp));
        Assert.That(view.DisplayedRows.Count(row => row.IsGroup), Is.EqualTo(6));
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
    public void RefreshAndFiltersPreserveSelectedRowAndExpansion()
    {
        var view = new ConnectionsView();
        var frame = Frame(Tcp, Udp);
        view.Update(frame, [Owner], false);
        view.SelectConnectionForFixture(Owner.ProcessId, Owner.CreationTicks);
        var selected = view.Rows.SelectedItem;
        view.Update(frame, [Owner with { CpuPercent = 9 }], false);
        Assert.Multiple(() =>
        {
            Assert.That(view.Rows.SelectedItem, Is.SameAs(selected));
            Assert.That(view.DisplayedRows, Has.Count.EqualTo(3));
            Assert.That(view.SelectedDetailsForFixture, Does.Contain("203.0.113.24:443"));
        });
        view.Search.Text = "no-match";
        Assert.That(view.DisplayedRows, Is.Empty);
        view.Search.Clear();
        Assert.That(view.DisplayedRows, Has.Count.EqualTo(3));
        view.Protocol.SelectedIndex = 2;
        Assert.That(view.DisplayedRows.Select(r => r.Connection?.Protocol), Is.EqualTo(new TransportProtocol?[] { null, TransportProtocol.Udp }));
    }

    [Test]
    public void IdenticalUdpBindingsRemainDistinctRowsAcrossRefresh()
    {
        var view = new ConnectionsView();
        var frame = Frame(Udp, Udp);
        view.Update(frame, [Owner], false);
        view.SelectConnectionForFixture(Owner.ProcessId, Owner.CreationTicks, 1);
        var selected = view.Rows.SelectedItem;
        view.Update(frame, [Owner], false);
        Assert.Multiple(() =>
        {
            Assert.That(view.DisplayedRows, Has.Count.EqualTo(3));
            Assert.That(view.DisplayedRows.Distinct().Count(), Is.EqualTo(3));
            Assert.That(view.Rows.SelectedItem, Is.SameAs(selected));
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
    public void IndividualModeShowsFlatEndpointRowsWithProcessIdentity()
    {
        var view = new ConnectionsView();
        view.Update(Frame(Tcp, Udp), [Owner], false);
        view.IndividualChip.IsChecked = true;

        Assert.Multiple(() =>
        {
            Assert.That(view.Grouped, Is.False);
            Assert.That(view.DisplayedRows, Has.Count.EqualTo(2));
            Assert.That(view.DisplayedRows.All(row => !row.IsGroup && row.IconVisibility == System.Windows.Visibility.Visible), Is.True);
            Assert.That(view.DisplayedRows.All(row => row.Detail.Contains("PID 424242", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public void GroupedModeCombinesOnlyMatchingExecutablePathsAndSummarizesEndpoints()
    {
        var sibling = Owner with { ProcessId = 99, CreationTicks = 234, AppName = "Sample app" };
        var image = @"C:\\Apps\\sample.exe";
        var first = Owner with { ImagePath = image };
        var second = sibling with { ImagePath = image.ToUpperInvariant() };
        var unknown = Owner with { ProcessId = 100, CreationTicks = 345, AppName = "Sample app" };
        var firstConnection = Tcp with { ProcessId = first.ProcessId, ProcessCreationTicks = first.CreationTicks, LocalEndpoint = new IPEndPoint(IPAddress.Parse("192.168.1.25"), 5000) };
        var secondConnection = Tcp with { ProcessId = second.ProcessId, ProcessCreationTicks = second.CreationTicks, LocalEndpoint = new IPEndPoint(IPAddress.Parse("192.168.1.25"), 5001), State = TcpState.Listen };
        var unknownConnection = Udp with { ProcessId = unknown.ProcessId, ProcessCreationTicks = unknown.CreationTicks };
        var view = new ConnectionsView();
        view.Update(Frame(firstConnection, secondConnection, unknownConnection), [first, second, unknown], false);

        var groups = view.DisplayedRows.Where(row => row.IsGroup).ToArray();
        var established = WinMon.Resources.Strings.Format(WinMon.Resources.Strings.ConnectionsStateCount,
            1, WinMon.Resources.Strings.ConnectionsTcpStateEstablished);
        var listening = WinMon.Resources.Strings.Format(WinMon.Resources.Strings.ConnectionsStateCount,
            1, WinMon.Resources.Strings.ConnectionsTcpStateListen);
        Assert.Multiple(() =>
        {
            Assert.That(groups, Has.Length.EqualTo(2));
            Assert.That(groups.Single(row => row.Members.Count == 2).Local, Is.Empty);
            Assert.That(groups.Single(row => row.Members.Count == 2).State, Is.Empty);
            Assert.That(groups.Single(row => row.Members.Count == 1).Remote, Is.Empty);
        });

        var movedConnection = firstConnection with { LocalEndpoint = new IPEndPoint(IPAddress.Parse("192.168.1.25"), 5002) };
        view.Update(Frame(movedConnection, secondConnection, unknownConnection), [first, second, unknown], false);
        var updated = view.DisplayedRows.Single(row => row.IsGroup && row.Members.Count == 2);
        Assert.Multiple(() =>
        {
            Assert.That(updated.Local, Is.Empty);
        });
    }

    [Test]
    public void IndividualSearchMatchesOnlyTheOwningPidOrEndpoint()
    {
        var first = Owner with { ImagePath = @"C:\Apps\sample.exe" };
        var second = first with { ProcessId = 99, CreationTicks = 234 };
        var sibling = Tcp with
        {
            ProcessId = second.ProcessId, ProcessCreationTicks = second.CreationTicks,
            RemoteEndpoint = new(IPAddress.Parse("203.0.113.25"), 443)
        };
        var view = new ConnectionsView { Grouped = false };
        view.Update(Frame(Tcp, Udp, sibling), [first, second], false);
        view.Search.Text = "424242";
        Assert.That(view.DisplayedRows.Select(row => row.Connection!.ProcessId), Is.EqualTo(new[] { 424242, 424242 }));
        view.Search.Text = "203.0.113.25";
        Assert.That(view.DisplayedRows.Single().Connection!.ProcessId, Is.EqualTo(99));
    }

    [Test]
    public void GroupedSummariesCountDistinctAddressesAndPortsAcrossProtocols()
    {
        var view = new ConnectionsView();
        view.Update(Frame(Tcp, Tcp with { LocalEndpoint = new IPEndPoint(IPAddress.Loopback, 50001) },
            Udp with { LocalEndpoint = Tcp.LocalEndpoint }), [Owner], false);
        var row = view.DisplayedRows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Local, Is.Empty);
            Assert.That(row.Remote, Is.Empty);
            Assert.That(row.Tcp, Is.EqualTo("2"));
            Assert.That(row.Udp, Is.EqualTo("1"));
            Assert.That(row.LocalToolTip, Is.Empty);
            Assert.That(row.State, Is.Empty);
        });
        view.Protocol.SelectedIndex = 2;
        row = view.DisplayedRows.Single();
        Assert.That(row.Local, Is.Empty);
        Assert.That(row.Remote, Is.Empty);
        Assert.That(row.State, Is.Empty);
    }

    [Test]
    public void IndividualSelectionSurvivesLateExecutableMetadata()
    {
        var view = new ConnectionsView { Grouped = false };
        view.Update(Frame(Tcp), [], false);
        view.Rows.SelectedIndex = 0;
        var selected = view.Rows.SelectedItem;
        view.Update(Frame(Tcp), [Owner with { ImagePath = @"C:\Apps\sample.exe" }], false);
        Assert.Multiple(() =>
        {
            Assert.That(view.Rows.SelectedItem, Is.SameAs(selected));
            Assert.That(view.ShowProcess.IsEnabled, Is.True);
            Assert.That(view.SelectedProcessForFixture?.Identity, Is.EqualTo(Owner.Identity));
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
        Assert.That(view.DisplayedRows.Count(row => row.IsGroup), Is.EqualTo(2));

        IReadOnlyList<ProcessUsage>? requested = null;
        view.GroupRequested += members => requested = members;
        view.Rows.SelectedItem = view.DisplayedRows.First(row => row.IsGroup);
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
    public void ProtocolFilterDoesNotChangeExecutableNetworkSummaryOrRepeatItOnEndpoints()
    {
        var view = new ConnectionsView();
        var network = Network(new ProcessNetworkReading(Owner.ProcessId, Owner.CreationTicks, 100, 200, 10, 20));
        view.Update(Frame(Tcp, Udp), [Owner], false, network, true);
        var grouped = view.DisplayedRows.Single();
        view.Protocol.SelectedIndex = 2;
        Assert.Multiple(() =>
        {
        Assert.That(view.DisplayedRows.Single().Network, Is.EqualTo(grouped.Network));
            Assert.That(view.DisplayedRows.Single().NetworkColumnWidth.Value, Is.EqualTo(172));
        });
        view.IndividualChip.IsChecked = true;
        Assert.That(view.DisplayedRows.All(row => row.NetworkColumnWidth.Value == 172 && row.Network.Length == 0), Is.True);
    }

    [Test]
    public void IndividualNetworkDetailsAreOwnerTotalsAndUnknownLifetimeIsUnavailable()
    {
        var view = new ConnectionsView { Grouped = false };
        view.Update(Frame(Tcp), [Owner], false, Network(new ProcessNetworkReading(Owner.ProcessId, Owner.CreationTicks, 100, 200, 10, 20)), true);
        view.Rows.SelectedIndex = 0;
        Assert.Multiple(() =>
        {
            Assert.That(view.SelectedDetailsForFixture, Does.Contain("Received / sent since start: 100 B / 200 B"));
            Assert.That(view.SelectionNetwork.Text, Does.Contain(WinMon.Resources.Strings.ConnectionsNetworkOwnerTotals)
                .And.Contain("10 B/s").And.Contain("20 B/s").And.Contain("100 B / 200 B"));
        });

        view.Update(Frame(Tcp with { ProcessCreationTicks = 456 }), [Owner], false,
            Network(new ProcessNetworkReading(Owner.ProcessId, Owner.CreationTicks, 100, 200, 10, 20)), true);
        view.Rows.SelectedIndex = 0;
        Assert.That(view.SelectedDetailsForFixture, Does.Contain("Received / sent since start: —"));
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
    public void AppFirstRankingFiltersAndKeepsEndpointDetailsOffGroupRows()
    {
        var background = Owner with { ProcessId = 99, CreationTicks = 234, Name = "background" };
        var app = Owner with { HasHadAppWindow = true, ImagePath = @"C:\Apps\sample.exe" };
        var view = new ConnectionsView();
        view.Load(new ConnectionListSettings(10, ConnectionSort.Send, true));
        view.Update(Frame(Tcp, Tcp with { ProcessId = background.ProcessId, ProcessCreationTicks = background.CreationTicks }),
            [app, background], false, Network(
                new(app.ProcessId, app.CreationTicks, 10, 20, 1, 2),
                new(background.ProcessId, background.CreationTicks, 30, 40, 3, 4)), true);

        var row = view.DisplayedRows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Name, Is.EqualTo("Sample app"));
            Assert.That(row.Receive, Is.EqualTo("1 B/s"));
            Assert.That(row.Send, Is.EqualTo("2 B/s"));
            Assert.That(row.Remote, Is.Empty);
            Assert.That(row.State, Is.Empty);
        });
    }

    [Test]
    public void PropertiesUseCompleteEndpointDetails()
    {
        var owner = Owner with { ImagePath = @"C:\Apps\sample.exe" };
        var view = new ConnectionsView { Grouped = false };
        view.Update(Frame(Tcp), [owner], false);
        view.Rows.SelectedIndex = 0;
        (string? Name, string? Details) properties = default;
        view.PropertiesRequested += (name, details) => properties = (name, details);
        view.Properties.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Assert.That(properties.Details, Does.Contain("127.0.0.1:50000").And.Contain("203.0.113.24:443").And.Contain(owner.ImagePath));
        Assert.That(properties.Details, Does.Contain(Environment.NewLine + WinMon.Resources.Strings.ProcessPathLabel));
    }
}
