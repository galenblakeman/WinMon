using System.Net;
using System.Windows;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ConnectionRowsCharacterizationTests
{
    private readonly List<object> _addedResources = [];
    private ConnectionsView _view = null!;
    private static readonly ProcessUsage Owner = new(int.MaxValue - 20, 100, "fixture", 0);

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var key in new[] { "RangeChip", "ActionBarCollapseButton", "ActionBarButton", "ActionBarLastButton" })
            if (!app.Resources.Contains(key)) { app.Resources[key] = new Style(); _addedResources.Add(key); }
        _view = new ConnectionsView();
        _view.Load(new ConnectionListSettings(0, ConnectionSort.Tcp));
    }

    [TearDown]
    public void TearDown()
    {
        _view?.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
        _addedResources.Clear();
    }

    private static NetworkConnection Endpoint(ProcessUsage owner) =>
        new(owner.ProcessId, owner.CreationTicks, TransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, 1234));

    private static NetworkConnectionsFrame Frame(params ProcessUsage[] owners) =>
        new(owners.Select(Endpoint).ToArray(), new(true), new(true));

    private static SavedProcessPin Pin(ProcessUsage owner, bool grouped = false) =>
        new(owner.ProcessId, owner.CreationTicks, owner.Name, owner.ImagePath, owner.AppName, owner.HasAppWindow, grouped)
        { LifetimeDomain = owner.LifetimeDomain };

    [TestCase("exact", true)]
    [TestCase("pid", false)]
    [TestCase("creation", false)]
    [TestCase("domain", false)]
    [TestCase("missing-path", false)]
    public void IndividualPinPromotionRequiresTheExactLifetimeAndAnExecutable(string partition, bool promoted)
    {
        _view.RestorePins([Pin(Owner)]);
        var known = Owner with { ImagePath = @"C:\Fixtures\known.exe", AppName = "Known app", HasAppWindow = true };
        known = partition switch
        {
            "pid" => known with { ProcessId = Owner.ProcessId - 1 },
            "creation" => known with { CreationTicks = Owner.CreationTicks + 1 },
            "domain" => known with { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds },
            "missing-path" => known with { ImagePath = "" },
            _ => known
        };
        var notifications = 0;
        _view.PinsChanged += () => notifications++;
        _view.Update(Frame(known), [known], false);
        var saved = _view.SavedPins.Single();
        Assert.That(saved.Grouped, Is.EqualTo(promoted));
        Assert.That(saved.ImagePath, Is.EqualTo(promoted ? known.ImagePath : ""));
        Assert.That(saved.AppName, Is.EqualTo(promoted ? known.AppName : ""));
        Assert.That(notifications, Is.EqualTo(promoted ? 1 : 0));
        _view.Update(Frame(known), [known], false);
        Assert.That(notifications, Is.EqualTo(promoted ? 1 : 0), "Unchanged metadata must not publish another promotion.");
    }

    [Test]
    public void ThrowingPinObserverSeesPromotedPinsBeforeRowsAndCanResumeOnTheNextUpdate()
    {
        _view.RestorePins([Pin(Owner)]);
        var oldRow = _view.DisplayedRows.Single();
        var oldDetail = oldRow.Detail;
        var known = Owner with { ImagePath = @"C:\Fixtures\known.exe", AppName = "Known app" };
        var failure = new InvalidOperationException("observer fixture");
        var calls = 0;
        void Observer()
        {
            calls++;
            Assert.That(_view.SavedPins.Single().Grouped, Is.True);
            Assert.That(_view.SavedPins.Single().ImagePath, Is.EqualTo(known.ImagePath));
            Assert.That(_view.DisplayedRows.Single(), Is.SameAs(oldRow));
            Assert.That(oldRow.Detail, Is.EqualTo(oldDetail));
            throw failure;
        }
        _view.PinsChanged += Observer;
        Assert.That(Assert.Throws<InvalidOperationException>(() => _view.Update(Frame(known), [known], false)),
            Is.SameAs(failure));
        Assert.That(_view.DisplayedRows.Single(), Is.SameAs(oldRow));
        _view.PinsChanged -= Observer;
        _view.Update(Frame(known), [known], false);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(_view.DisplayedRows.Single().Process, Is.EqualTo(known));
        Assert.That(_view.DisplayedRows.Single().Pinned, Is.True);
        Assert.That(_view.DisplayedRows.Single().Connections, Has.Count.EqualTo(1));
    }

    [Test]
    public void HistoricalUpdatePromotesPinsWithoutInventingDisconnectedHistoricalRows()
    {
        _view.RestorePins([Pin(Owner)]);
        var known = Owner with { ImagePath = @"C:\Fixtures\known.exe" };
        var notifications = 0;
        _view.PinsChanged += () => notifications++;
        _view.Update(Frame(), [known], true);
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(_view.SavedPins.Single().Grouped, Is.True);
        Assert.That(_view.DisplayedRows, Is.Empty);
        Assert.That(_view.PinSummary.Visibility, Is.EqualTo(Visibility.Collapsed));
        Assert.That(_view.PinnedCountLabel.Text, Is.EqualTo(Strings.Format(Strings.PinnedProcessCount, 1)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NullFrameRetainsCachedRowWhileAnObservedEmptyFrameEvictsIt(bool observedEmpty)
    {
        _view.Update(Frame(Owner), [Owner], false);
        var original = _view.DisplayedRows.Single();
        _view.Update(observedEmpty ? Frame() : null, [], false);
        Assert.That(_view.DisplayedRows, Is.Empty);
        _view.Update(Frame(Owner), [Owner], false);
        Assert.That(ReferenceEquals(_view.DisplayedRows.Single(), original), Is.EqualTo(!observedEmpty));
        Assert.That(_view.DisplayedRows.Single().Connections, Has.Count.EqualTo(1));
    }

    [Test]
    public void FilteringRetainsTheSelectedIdentityAndItsRowForRestoration()
    {
        var other = Owner with { ProcessId = Owner.ProcessId - 1, CreationTicks = 101, Name = "second" };
        _view.Update(Frame(Owner, other), [Owner, other], false);
        _view.SelectConnectionForFixture(Owner.ProcessId, Owner.CreationTicks);
        var selected = _view.Rows.SelectedItem;
        _view.Search.Text = "second";
        Assert.That(_view.Rows.SelectedItem, Is.Null);
        Assert.That(_view.DisplayedRows.Single().Process, Is.EqualTo(other));
        _view.Search.Clear();
        Assert.That(_view.Rows.SelectedItem, Is.SameAs(selected));
        Assert.That(_view.SelectedProcessForFixture, Is.EqualTo(Owner));
    }

    [TestCase("outside-top", 11, true)]
    [TestCase("inside-top", 0, true)]
    [TestCase("missing", -1, true)]
    [TestCase("live", 11, false)]
    [TestCase("search", 11, true)]
    [TestCase("apps-only", 11, true)]
    public void HistoricalTriggerAppendsOnceEvenWhenExcludedByTheVisibleFilters(string partition, int triggerIndex, bool historical)
    {
        var owners = Enumerable.Range(0, 12).Select(i => Owner with
        {
            ProcessId = Owner.ProcessId - i, CreationTicks = 100 + i, Name = $"fixture-{i}",
            ImagePath = $@"C:\Fixtures\fixture-{i}.exe", HasAppWindow = false
        }).ToArray();
        var frame = new NetworkConnectionsFrame(owners.SelectMany((owner, i) => Enumerable.Repeat(Endpoint(owner), 12 - i))
            .ToArray(), new(true), new(true));
        _view.Load(new ConnectionListSettings(5, ConnectionSort.Tcp, partition == "apps-only", true));
        _view.RestorePins([Pin(owners[^1], true)]);
        if (partition == "search") _view.Search.Text = "no-match";
        _view.Update(frame, owners, historical, captureTrigger: triggerIndex < 0 ? Owner with
            { ProcessId = 12345, CreationTicks = 321 } : owners[triggerIndex]);
        var expected = partition switch
        {
            "search" or "apps-only" or "live" => new[] { owners[^1] },
            "outside-top" => owners.Take(10).Append(owners[^1]).ToArray(),
            _ => owners.Take(10).ToArray()
        };
        Assert.That(_view.DisplayedRows.Select(row => row.Process), Is.EqualTo(expected));
        Assert.That(_view.DisplayedRows.Select(row => row.Key).Distinct().Count(), Is.EqualTo(expected.Length));
        Assert.That(_view.DisplayedRows.Where(row => row.Pinned).Select(row => row.Process),
            Is.EqualTo(expected.Where(owner => owner == owners[^1])));
        Assert.That(_view.DisplayedRows.All(row => row.RankedLabel.Length == 0), Is.True);
        Assert.That(_view.TopCount.IsEnabled, Is.EqualTo(!historical));
        Assert.That(_view.PinnedOnly.IsEnabled, Is.EqualTo(!historical));
    }

    [TestCase("missing", false, false)]
    [TestCase("missing", false, true)]
    [TestCase("missing", true, false)]
    [TestCase("missing", true, true)]
    [TestCase("unavailable", false, false)]
    [TestCase("unavailable", false, true)]
    [TestCase("unavailable", true, false)]
    [TestCase("unavailable", true, true)]
    [TestCase("partial", false, false)]
    [TestCase("partial", false, true)]
    [TestCase("partial", true, false)]
    [TestCase("partial", true, true)]
    [TestCase("full", false, false)]
    [TestCase("full", false, true)]
    [TestCase("full", true, false)]
    [TestCase("full", true, true)]
    public void NoticePrecedenceKeepsHistoricalAndPinnedEmptyStatesDistinct(string availability, bool historical, bool pinned)
    {
        if (pinned) _view.RestorePins([Pin(Owner)]);
        var frame = availability == "missing" ? null : new NetworkConnectionsFrame([], new(availability != "unavailable"),
            new(availability == "full"));
        _view.Update(frame, [], historical);
        var rows = pinned && !historical ? 1 : 0;
        var expected = availability switch
        {
            "missing" => historical ? Strings.ConnectionsHistoryUnavailable : Strings.ConnectionsLoading,
            "unavailable" => Strings.ConnectionsUnavailable,
            "partial" => historical
                ? string.Join(Environment.NewLine, Strings.ConnectionsHistoryNote, Strings.ConnectionsPartial)
                : Strings.ConnectionsPartial,
            _ => historical ? Strings.ConnectionsHistoryNote : rows == 0 ? Strings.ConnectionsEmpty : ""
        };
        Assert.That(_view.DisplayedRows, Has.Count.EqualTo(rows));
        Assert.That(_view.Notice.Text, Is.EqualTo(expected));
        Assert.That(_view.Notice.Visibility, Is.EqualTo(rows == 0 && expected.Length > 0 ? Visibility.Visible : Visibility.Collapsed));
        if (rows == 0) return;
        var row = _view.DisplayedRows.Single();
        Assert.That(row.Detail, Is.EqualTo(availability is "partial" or "full" ? Strings.ConnectionsNoObservedEndpoints : Strings.Unavailable));
        Assert.That(row.Tcp, Is.EqualTo(availability is "partial" or "full" ? "0" : Strings.MissingReading));
        Assert.That(row.Udp, Is.EqualTo(row.Tcp));
        Assert.That(row.Graph.Visibility, Is.EqualTo(Visibility.Hidden));
    }
}
