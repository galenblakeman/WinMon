using System.Net;
using System.Windows;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration")]
[SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ConnectionRowLabelsCharacterizationTests
{
    private static ProcessUsage Owner(string name = "fixture", string? app = null, string? path = null) =>
        new(int.MaxValue - 40, 140, name, 0) { AppName = app!, ImagePath = path ?? "" };

    private static ConnectionsView.ConnectionEntry Entry(TransportProtocol protocol, int pid = 17) =>
        new(new NetworkConnection(pid, 100, protocol, new IPEndPoint(IPAddress.Loopback, 1234)), null, "fixture");

    private static ConnectionsView.ConnectionGroup Group(ProcessUsage? owner,
        IReadOnlyList<ConnectionsView.ConnectionEntry>? entries = null, IReadOnlyList<ProcessUsage>? members = null) =>
        new("fixture", entries ?? [], members ?? (owner is null ? [] : [owner]), owner);

    private static ConnectionDisplayRow Row(ProcessUsage? owner)
    {
        // Complete the existing read-only icon worker before observing synchronous row notifications.
        if (owner is not null)
            ProcessIconProvider.GetAsync(owner).WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        return new("fixture");
    }

    [TestCase("executable", "Application", "Application")]
    [TestCase("executable", "", "executable")]
    [TestCase("executable", null, "executable")]
    [TestCase("", "", null)]
    public void DisplayNamePrefersApplicationThenExecutableThenUnknown(string name, string? app, string? expected)
    {
        var owner = Owner(name, app);
        var row = Row(owner);
        row.UpdateGroup(Group(owner), false, ConnectionsView.NetworkSummary.Disabled);
        Assert.That(row.Name, Is.EqualTo(expected ?? Strings.ConnectionsUnknownProcess));
        Assert.That(row.Detail, Is.EqualTo($"PID {owner.ProcessId}"));
    }

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(2, true)]
    public void MultipleMembersTakePrecedenceOverTheFirstEndpointPid(int memberCount, bool grouped)
    {
        var owner = Owner();
        var members = Enumerable.Range(0, memberCount).Select(i => owner with { ProcessId = 30 + i }).ToArray();
        var row = Row(owner);
        row.UpdateGroup(Group(owner, [Entry(TransportProtocol.Tcp, 91), Entry(TransportProtocol.Udp, 92)], members),
            false, ConnectionsView.NetworkSummary.Disabled);
        Assert.That(row.Detail, Is.EqualTo(grouped
            ? Strings.Format(Strings.ConnectionsProcessCount, memberCount) : "PID 91"));
        Assert.That(row.Members, Is.SameAs(members));
        Assert.That(row.Connections.Select(connection => connection.ProcessId), Is.EqualTo(new[] { 91, 92 }));
    }

    [TestCase("none", "2", "1")]
    [TestCase("no-endpoints", "2", "1")]
    [TestCase("unavailable", null, null)]
    [TestCase("empty", null, null)]
    [TestCase("custom", null, null)]
    public void OnlyNullAndNoObservedEndpointsStatusesExposeProtocolCounts(string partition, string? tcp, string? udp)
    {
        var owner = Owner();
        var row = Row(owner);
        var status = partition switch
        {
            "none" => null,
            "no-endpoints" => Strings.ConnectionsNoObservedEndpoints,
            "unavailable" => Strings.Unavailable,
            "empty" => "",
            _ => "fixture status"
        };
        row.UpdateGroup(Group(owner, [Entry(TransportProtocol.Tcp), Entry(TransportProtocol.Tcp),
            Entry(TransportProtocol.Udp)]), false, ConnectionsView.NetworkSummary.Disabled, status);
        Assert.Multiple(() =>
        {
            Assert.That(row.Detail, Is.EqualTo(status ?? "PID 17"));
            Assert.That(row.Tcp, Is.EqualTo(tcp ?? Strings.MissingReading));
            Assert.That(row.Udp, Is.EqualTo(udp ?? Strings.MissingReading));
            Assert.That(row.AccessibleName, Is.EqualTo($"fixture, {status ?? "PID 17"}, {tcp ?? Strings.MissingReading} TCP, {udp ?? Strings.MissingReading} UDP"));
        });
    }

    [TestCase(false, "C:\\fixture\\app.exe")]
    [TestCase(true, "C:\\fixture\\app.exe")]
    [TestCase(true, "")]
    public void HistoricalDetailsAppendTheExecutableOrMissingPathLabel(bool historical, string path)
    {
        var owner = Owner(path: path);
        var row = Row(owner);
        row.UpdateGroup(Group(owner), false, ConnectionsView.NetworkSummary.Disabled, "recorded", historical);
        Assert.That(row.Detail, Is.EqualTo("recorded" + (historical
            ? " · " + (path.Length > 0 ? path : Strings.ExecutablePathUnavailable) : "")));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OwnerlessEndpointRowsKeepTheirPidAndUnknownName(bool historical)
    {
        var row = Row(null);
        row.UpdateGroup(Group(null, [Entry(TransportProtocol.Udp, 73)]), false,
            ConnectionsView.NetworkSummary.Disabled, historical: historical);
        Assert.Multiple(() =>
        {
            Assert.That(row.Name, Is.EqualTo(Strings.ConnectionsUnknownProcess));
            Assert.That(row.Detail, Is.EqualTo("PID 73" + (historical ? " · " + Strings.ExecutablePathUnavailable : "")));
            Assert.That(row.CanPin, Is.False);
            Assert.That(row.Tcp, Is.EqualTo("0"));
            Assert.That(row.Udp, Is.EqualTo("1"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EmptyGroupsWithExplicitStatusDoNotRequireAnOwner(bool historical)
    {
        var row = Row(null);
        row.UpdateGroup(Group(null), true, ConnectionsView.NetworkSummary.Disabled,
            Strings.ConnectionsNoObservedEndpoints, historical);
        Assert.That(row.Detail, Is.EqualTo(Strings.ConnectionsNoObservedEndpoints +
            (historical ? " · " + Strings.ExecutablePathUnavailable : "")));
        Assert.That((row.Tcp, row.Udp), Is.EqualTo(("0", "0")));
        Assert.That(row.Pinned, Is.True);
        Assert.That(row.CanPin, Is.False);
    }

    [TestCase("disabled")]
    [TestCase("unavailable")]
    [TestCase("rates")]
    public void NetworkStateControlsColumnsRatesAndAccessibleOwnerTotals(string partition)
    {
        var owner = Owner();
        var row = Row(owner);
        row.UpdateTraffic(null, [owner]);
        row.SetRankedLabel("rank fixture");
        var network = partition switch
        {
            "disabled" => ConnectionsView.NetworkSummary.Disabled,
            "unavailable" => ConnectionsView.NetworkSummary.Unavailable,
            _ => new ConnectionsView.NetworkSummary(1024, 2048, 4096, 8192, ProcessNetworkQuality.None, true, true)
        };
        row.UpdateGroup(Group(owner), false, network);
        var receive = partition == "rates" ? "4.1 KB/s" : Strings.MissingReading;
        var send = partition == "rates" ? "8.2 KB/s" : Strings.MissingReading;
        Assert.Multiple(() =>
        {
            Assert.That(row.NetworkSummary, Is.EqualTo(network));
            Assert.That(row.NetworkVisibility, Is.EqualTo(network.Enabled ? Visibility.Visible : Visibility.Collapsed));
            Assert.That(row.NetworkColumnWidth.Value, Is.EqualTo(network.Enabled ? 172 : 0));
            Assert.That(row.ReceiveColumnWidth.Value, Is.EqualTo(network.Enabled ? 80 : 0));
            Assert.That(row.SendColumnWidth.Value, Is.EqualTo(network.Enabled ? 80 : 0));
            Assert.That(row.Receive, Is.EqualTo(network.Enabled ? receive : ""));
            Assert.That(row.Send, Is.EqualTo(network.Enabled ? send : ""));
            Assert.That(row.Network, Is.EqualTo(network.Enabled ? $"↓ {receive}  ↑ {send}" : ""));
            Assert.That(row.AccessibleName, Is.EqualTo($"fixture, PID {owner.ProcessId}, 0 TCP, 0 UDP" +
                (network.Enabled ? $", {Strings.ConnectionsNetworkOwnerTotals}, ↓ {receive}  ↑ {send}, {row.TrafficAccessibleName}" : "")));
            Assert.That(row.RankedLabel, Is.EqualTo("rank fixture"));
            Assert.That(row.Received, Is.EqualTo(Strings.MissingReading));
            Assert.That(row.Sent, Is.EqualTo(Strings.MissingReading));
        });
    }

    [Test]
    public void RepeatedGroupsAreSilentAndPinChangesPublishFullyUpdatedState()
    {
        var owner = Owner();
        var row = Row(owner);
        var group = Group(owner);
        row.UpdateGroup(group, false, ConnectionsView.NetworkSummary.Disabled);
        var notices = new List<string?>();
        row.PropertyChanged += (_, args) =>
        {
            notices.Add(args.PropertyName);
            Assert.That(row.Pinned, Is.True);
            Assert.That(row.PinLabel, Is.EqualTo(Strings.UnpinProcess));
            Assert.That(row.PinIcon, Is.SameAs(ButtonIcon.PinFilled));
            Assert.That(row.Process, Is.SameAs(owner));
            Assert.That(row.Detail, Is.EqualTo($"PID {owner.ProcessId}"));
        };
        row.UpdateGroup(group, false, ConnectionsView.NetworkSummary.Disabled);
        Assert.That(notices, Is.Empty);
        row.UpdateGroup(group, true, ConnectionsView.NetworkSummary.Disabled);
        row.UpdateGroup(group, true, ConnectionsView.NetworkSummary.Disabled);
        Assert.That(notices, Is.EqualTo(new[] { "" }));
    }

    [Test]
    public void MetadataChangesWithIdenticalLabelsStillReplaceBoundStateWithoutANotification()
    {
        var owner = Owner();
        var row = Row(owner);
        row.UpdateGroup(Group(owner), false, ConnectionsView.NetworkSummary.Disabled);
        var changed = owner with { ImagePath = @"C:\fixture\new.exe" };
        var group = Group(changed);
        var notices = new List<string?>();
        row.PropertyChanged += (_, args) => notices.Add(args.PropertyName);
        row.UpdateGroup(group, false, ConnectionsView.NetworkSummary.Disabled);
        Assert.That(row.Process, Is.SameAs(changed));
        Assert.That(row.Members, Is.SameAs(group.Members));
        Assert.That(notices, Is.Empty);
    }

    [Test]
    public void DisablingNetworkClearsItsLabelsColumnsAndAccessibilityWithoutClearingTrafficOrRank()
    {
        var owner = Owner();
        var row = Row(owner);
        var group = Group(owner);
        row.UpdateTraffic(null, [owner]);
        row.SetRankedLabel("rank fixture");
        var traffic = (row.Received, row.Sent, row.TrafficToolTip);
        row.UpdateGroup(group, true, new(10, 20, 30, 40, ProcessNetworkQuality.None, true, true));
        Assert.That(row.NetworkToolTip, Is.Not.Empty);
        row.UpdateGroup(group, false, ConnectionsView.NetworkSummary.Disabled);
        Assert.Multiple(() =>
        {
            Assert.That((row.Network, row.Receive, row.Send, row.NetworkToolTip), Is.EqualTo(("", "", "", "")));
            Assert.That(row.NetworkVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That((row.NetworkColumnWidth.Value, row.ReceiveColumnWidth.Value, row.SendColumnWidth.Value),
                Is.EqualTo((0d, 0d, 0d)));
            Assert.That(row.AccessibleName, Is.EqualTo($"fixture, PID {owner.ProcessId}, 0 TCP, 0 UDP"));
            Assert.That((row.Received, row.Sent, row.TrafficToolTip), Is.EqualTo(traffic));
            Assert.That(row.RankedLabel, Is.EqualTo("rank fixture"));
            Assert.That(row.PinIcon, Is.SameAs(ButtonIcon.Pin));
            Assert.That(row.PinLabel, Is.EqualTo(Strings.PinProcess));
        });
    }

    [Test]
    public void ThrowingChangeObserversSeeCommittedStateAndTheNextIdenticalUpdateIsSilent()
    {
        var owner = Owner();
        var row = Row(owner);
        var group = Group(owner);
        row.UpdateGroup(group, false, ConnectionsView.NetworkSummary.Disabled);
        var failure = new InvalidOperationException("observer fixture");
        var calls = 0;
        row.PropertyChanged += (_, args) =>
        {
            Assert.That(args.PropertyName, Is.Empty);
            Assert.That(row.Pinned, Is.True);
            Assert.That(row.Detail, Is.EqualTo("changed"));
            Assert.That(row.PinLabel, Is.EqualTo(Strings.UnpinProcess));
            calls++;
            throw failure;
        };
        Assert.That(Assert.Throws<InvalidOperationException>(() => row.UpdateGroup(group, true,
            ConnectionsView.NetworkSummary.Disabled, "changed")), Is.SameAs(failure));
        row.UpdateGroup(group, true, ConnectionsView.NetworkSummary.Disabled, "changed");
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void MissingOwnerAndEndpointsThrowsAfterBindingStateBeforeChangingLabelsOrNotifying()
    {
        var owner = Owner();
        var row = Row(owner);
        row.UpdateGroup(Group(owner), false, ConnectionsView.NetworkSummary.Disabled, "before");
        var notices = new List<string?>();
        row.PropertyChanged += (_, args) => notices.Add(args.PropertyName);
        Assert.Throws<NullReferenceException>(() => row.UpdateGroup(Group(null), true,
            ConnectionsView.NetworkSummary.Unavailable));
        Assert.Multiple(() =>
        {
            Assert.That(row.Process, Is.Null);
            Assert.That(row.Pinned, Is.True);
            Assert.That(row.Connections, Is.Empty);
            Assert.That(row.Members, Is.Empty);
            Assert.That(row.NetworkSummary, Is.EqualTo(ConnectionsView.NetworkSummary.Unavailable));
            Assert.That(row.Name, Is.EqualTo(Strings.ConnectionsUnknownProcess));
            Assert.That(row.Detail, Is.EqualTo("before"));
            Assert.That(row.PinLabel, Is.EqualTo(Strings.PinProcess));
            Assert.That(row.NetworkVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(notices, Is.Empty);
        });
    }
}
