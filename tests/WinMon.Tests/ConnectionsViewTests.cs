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
    private static readonly ProcessUsage Owner = new(424242, 123, "sample", 1) { AppName = "Sample app" };
    private static readonly NetworkConnection Tcp = new(Owner.ProcessId, Owner.CreationTicks, TransportProtocol.Tcp,
        new(IPAddress.Loopback, 50000), new(IPAddress.Parse("203.0.113.24"), 443), TcpState.Established);
    private static readonly NetworkConnection Udp = new(Owner.ProcessId, Owner.CreationTicks, TransportProtocol.Udp,
        new(IPAddress.IPv6Any, 5353));
    private static NetworkConnectionsFrame Frame(params NetworkConnection[] connections) =>
        new(connections, NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success);

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
            Assert.That(view.Notice.Text, Does.Contain("remain live"));
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
            Assert.That(groups.Single(row => row.Members.Count == 2).Local, Is.EqualTo("192.168.1.25 · 2 ports"));
            Assert.That(groups.Single(row => row.Members.Count == 2).State,
                Does.Contain(established).And.Contain(listening));
            Assert.That(groups.Single(row => row.Members.Count == 2).LocalToolTip, Does.Contain("192.168.1.25:5000").And.Contain("192.168.1.25:5001"));
            Assert.That(groups.Single(row => row.Members.Count == 2).StateToolTip,
                Does.Contain(established).And.Contain(listening));
            Assert.That(groups.Single(row => row.Members.Count == 1).Remote, Is.EqualTo(WinMon.Resources.Strings.MissingReading));
        });

        var movedConnection = firstConnection with { LocalEndpoint = new IPEndPoint(IPAddress.Parse("192.168.1.25"), 5002) };
        view.Update(Frame(movedConnection, secondConnection, unknownConnection), [first, second, unknown], false);
        var updated = view.DisplayedRows.Single(row => row.IsGroup && row.Members.Count == 2);
        Assert.Multiple(() =>
        {
            Assert.That(updated.Local, Is.EqualTo("192.168.1.25 · 2 ports"));
            Assert.That(updated.LocalToolTip, Does.Contain("192.168.1.25:5002"));
            Assert.That(updated.LocalToolTip, Does.Not.Contain("192.168.1.25:5000"));
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
            Assert.That(row.Local, Is.EqualTo("127.0.0.1 · 2 ports"));
            Assert.That(row.Remote, Is.EqualTo("203.0.113.24:443"));
            Assert.That(row.Tcp, Is.EqualTo("2"));
            Assert.That(row.Udp, Is.EqualTo("1"));
            Assert.That(row.LocalToolTip, Does.Contain("TCP 127.0.0.1:50000").And.Contain("UDP 127.0.0.1:50000"));
            Assert.That(row.State, Does.Contain("2").And.Contain(WinMon.Resources.Strings.ConnectionsTcpStateEstablished));
        });
        view.Protocol.SelectedIndex = 2;
        row = view.DisplayedRows.Single();
        Assert.That(row.Local, Is.EqualTo("127.0.0.1:50000"));
        Assert.That(row.Remote, Is.EqualTo(WinMon.Resources.Strings.MissingReading));
        Assert.That(row.State, Is.EqualTo(WinMon.Resources.Strings.MissingReading));
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
}
