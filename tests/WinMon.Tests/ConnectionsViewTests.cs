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
}
