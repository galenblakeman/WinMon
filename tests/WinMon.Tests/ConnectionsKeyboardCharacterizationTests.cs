using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
[SetCulture("en-US")]
[SetUICulture("en-US")]
public sealed class ConnectionsKeyboardCharacterizationTests
{
    private readonly List<ConnectionsView> _owned = [];
    private static readonly ProcessUsage Owner = new(42, 1, "viewer", 0)
        { AppName = "Viewer", ImagePath = @"C:\Apps\viewer.exe" };
    private static readonly NetworkConnection Tcp = new(42, 1, TransportProtocol.Tcp,
        new(IPAddress.Parse("192.168.1.25"), 50000), new(IPAddress.Parse("203.0.113.24"), 443), TcpState.Established);
    private static readonly NetworkConnection Udp = new(42, 1, TransportProtocol.Udp, new(IPAddress.IPv6Any, 5353));

    private static NetworkConnectionsFrame Frame(params NetworkConnection[] connections) =>
        new(connections, NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success);

    private ConnectionsView View(bool knownOwner = true, bool empty = false)
    {
        var view = new ConnectionsView();
        _owned.Add(view);
        view.Update(empty ? Frame() : Frame(Tcp, Udp), knownOwner ? [Owner] : [], false);
        if (!empty) view.Rows.SelectedIndex = 0;
        return view;
    }

    [TearDown]
    public void UnloadOwnedViews()
    {
        foreach (var view in _owned) view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        _owned.Clear();
    }

    private static KeyEventArgs KeyEvent(Key key = Key.Enter) =>
        new(Keyboard.PrimaryDevice, new WindowlessInputSource(), 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };

    private static string ExpectedDetails(bool knownOwner = true) => string.Join(Environment.NewLine,
        knownOwner ? "Viewer" : Strings.ConnectionsUnknownProcess,
        "PID: 42", $"{Strings.ProcessPathLabel}: {(knownOwner ? Owner.ImagePath : Strings.ExecutablePathUnavailable)}",
        $"PID\t{Strings.ConnectionsProtocol}\t{Strings.ConnectionsLocalEndpoint}\t{Strings.ConnectionsRemoteEndpoint}\t{Strings.ConnectionsState}",
        "42\tTCP\t192.168.1.25:50000\t203.0.113.24:443\tEstablished",
        $"42\tUDP\t[::]:5353\t{Strings.MissingReading}\t{Strings.MissingReading}", "");

    [TestCase(true)]
    [TestCase(false)]
    public void EnterRaisesCompleteDetailsBeforeMarkingHandled(bool knownOwner)
    {
        var view = View(knownOwner);
        var selected = view.Rows.SelectedItem;
        var input = KeyEvent();
        var requests = new List<(string Name, string Details)>();
        view.PropertiesRequested += (name, details) =>
        {
            Assert.That(input.Handled, Is.False);
            requests.Add((name, details));
        };
        view.Rows.RaiseEvent(input);
        Assert.Multiple(() =>
        {
            Assert.That(requests, Is.EqualTo(new[]
                { (knownOwner ? "Viewer" : Strings.ConnectionsUnknownProcess, ExpectedDetails(knownOwner)) }));
            Assert.That(input.Handled, Is.True);
            Assert.That(view.Rows.SelectedItem, Is.SameAs(selected));
        });
    }

    [Test]
    public void EnterWithoutSubscribersStillMarksTheEventHandled()
    {
        var view = View();
        var input = KeyEvent();
        view.Rows.RaiseEvent(input);
        Assert.That(input.Handled, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void EnterWithoutSelectionLeavesTheEventAndRowsAlone(bool empty)
    {
        var view = View(empty: empty);
        view.Rows.SelectedIndex = -1;
        var rows = view.DisplayedRows.ToArray();
        var requests = 0;
        view.PropertiesRequested += (_, _) => requests++;
        var input = KeyEvent();
        view.Rows.RaiseEvent(input);
        Assert.Multiple(() =>
        {
            Assert.That(requests, Is.Zero);
            Assert.That(input.Handled, Is.False);
            Assert.That(view.Rows.SelectedItem, Is.Null);
            Assert.That(view.DisplayedRows, Is.EqualTo(rows));
        });
    }

    [Test]
    public void EnterOnAMissingPinnedRowDoesNotRequestEmptyDetails()
    {
        var view = View();
        view.TogglePin(Owner);
        view.Update(Frame(), [], false);
        view.Rows.SelectedIndex = 0;
        Assert.That(view.DisplayedRows.Single().Connections, Is.Empty);
        var requests = 0;
        view.PropertiesRequested += (_, _) => requests++;
        var input = KeyEvent();
        view.Rows.RaiseEvent(input);
        Assert.Multiple(() =>
        {
            Assert.That(requests, Is.Zero);
            Assert.That(input.Handled, Is.False);
            Assert.That(view.DisplayedRows.Single().Pinned, Is.True);
        });
    }

    [TestCase(Key.Space)]
    [TestCase(Key.Escape)]
    public void OtherKeysDoNotOpenPropertiesOrConsumeThePreviewEvent(Key key)
    {
        var view = View();
        var selected = view.Rows.SelectedItem;
        var requests = 0;
        view.PropertiesRequested += (_, _) => requests++;
        var input = KeyEvent(key);
        view.Rows.RaiseEvent(input);
        Assert.Multiple(() =>
        {
            Assert.That(requests, Is.Zero);
            Assert.That(input.Handled, Is.False);
            Assert.That(view.Rows.SelectedItem, Is.SameAs(selected));
        });
    }

    [TestCase(Key.Enter)]
    [TestCase(Key.Space)]
    public void ARealRowButtonKeepsItsOwnKeyboardPreview(Key key)
    {
        var view = View();
        Layout(view);
        var button = Descendants(view.Rows).OfType<Button>().Single();
        var requests = 0;
        view.PropertiesRequested += (_, _) => requests++;
        var input = KeyEvent(key);
        button.RaiseEvent(input);
        Assert.Multiple(() =>
        {
            Assert.That(input.OriginalSource, Is.SameAs(button));
            Assert.That(requests, Is.Zero);
            Assert.That(input.Handled, Is.False);
            Assert.That(view.DisplayedRows.Single().Pinned, Is.False);
        });
    }

    [Test]
    public void EnterFromANonButtonRowChildStillOpensProperties()
    {
        var view = View();
        Layout(view);
        var label = Descendants(view.Rows).OfType<TextBlock>().Single(text => text.Text == "Viewer");
        var details = new List<string>();
        view.PropertiesRequested += (_, text) => details.Add(text);
        var input = KeyEvent();
        label.RaiseEvent(input);
        Assert.Multiple(() =>
        {
            Assert.That(input.OriginalSource, Is.SameAs(label));
            Assert.That(details, Is.EqualTo(new[] { ExpectedDetails() }));
            Assert.That(input.Handled, Is.True);
        });
    }

    [Test]
    public void SubscriberFailurePropagatesBeforeTheHandledAssignment()
    {
        var view = View();
        var input = KeyEvent();
        var failure = new InvalidOperationException("Owned subscriber failure");
        view.PropertiesRequested += (_, _) => throw failure;
        var actual = Assert.Throws<InvalidOperationException>(() => view.Rows.RaiseEvent(input));
        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(input.Handled, Is.False);
        });
    }

    [Test]
    public void ASubscriberCanClearSelectionWithoutRetargetingTheRequest()
    {
        var view = View();
        var requests = new List<(string Name, string Details)>();
        view.PropertiesRequested += (name, details) =>
        {
            view.Rows.SelectedIndex = -1;
            requests.Add((name, details));
        };
        var input = KeyEvent();
        view.Rows.RaiseEvent(input);
        Assert.Multiple(() =>
        {
            Assert.That(requests, Is.EqualTo(new[] { ("Viewer", ExpectedDetails()) }));
            Assert.That(view.Rows.SelectedItem, Is.Null);
            Assert.That(input.Handled, Is.True);
        });
    }

    [Test]
    public void AnAlreadyHandledPreviewIsNotDeliveredToTheRowHandler()
    {
        var view = View();
        var requests = 0;
        view.PropertiesRequested += (_, _) => requests++;
        var input = KeyEvent();
        input.Handled = true;
        view.Rows.RaiseEvent(input);
        Assert.Multiple(() =>
        {
            Assert.That(requests, Is.Zero);
            Assert.That(input.Handled, Is.True);
        });
    }

    private static void Layout(ConnectionsView view)
    {
        view.Measure(new Size(1000, 700));
        view.Arrange(new Rect(0, 0, 1000, 700));
        view.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class WindowlessInputSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
