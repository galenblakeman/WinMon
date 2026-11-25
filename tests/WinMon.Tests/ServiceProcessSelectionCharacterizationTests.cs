using System.Windows;
using System.Windows.Controls;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ServiceProcessSelectionCharacterizationTests
{
    private ServicesView _view = null!;
    private bool _addedCaptionStyle;
    private readonly List<string> _copies = [];
    private readonly List<(string Name, string Details)> _properties = [];
    private ListBox Rows => (ListBox)_view.FindName("Rows");
    private ServiceDisplayRow[] Shown => Rows.Items.Cast<ServiceDisplayRow>().ToArray();
    private TextBlock Summary => (TextBlock)_view.FindName("Summary");
    private Button Button(string name) => (Button)_view.FindName(name);

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (!app.Resources.Contains("CaptionButton"))
        { app.Resources["CaptionButton"] = new Style(typeof(Button)); _addedCaptionStyle = true; }
        _view = new ServicesView { TopServiceCount = 0 };
        _view.RestoreFilters(0, 0, true);
        _view.CopyRequested += text => _copies.Add(text);
        _view.PropertiesRequested += (name, details) => _properties.Add((name, details));
        _view.ActionRequested += (_, _, _) => Assert.Fail("Worker selection must never request a native service action.");
    }

    [TearDown]
    public void TearDown()
    {
        _view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        if (_addedCaptionStyle) Application.Current.Resources.Remove("CaptionButton");
        _addedCaptionStyle = false;
        _copies.Clear();
        _properties.Clear();
    }

    [TestCase("collapsed")]
    [TestCase("filtered")]
    [TestCase("host-only")]
    public void HiddenWorkerSelectionUsesFreshDetailsAndRestoresItsVisibleSelection(string hiding)
    {
        var original = Worker("worker-old");
        var first = Frame(1, original);
        SelectWorker(first);
        HideWorker(hiding, first.Groups[0].Key);
        Assert.That(Rows.SelectedItem, Is.Null);
        var current = Worker("worker-current", cpu: 60);
        var next = Frame(2, current);
        _view.Update(next, [first, next], false);
        Assert.That(Rows.SelectedItem, Is.Null);
        AssertCurrentDetails(current.Name, "Agent");
        ShowWorker(hiding, next.Groups[0].Key);
        var selected = Rows.SelectedItem as ServiceDisplayRow;
        Assert.That(selected, Is.Not.Null);
        Assert.That(selected!.Key, Is.EqualTo("process:200:2000"));
        Assert.That(selected.Process, Is.SameAs(current));
    }

    [TestCase("missing")]
    [TestCase("reused-pid")]
    public void HiddenWorkerSelectionClearsWhenItsProcessLifetimeIsNoLongerPresent(string partition)
    {
        var first = Frame(1, Worker("worker-old"));
        SelectWorker(first);
        HideWorker("collapsed", first.Groups[0].Key);
        var next = Frame(2, partition == "missing" ? null : Worker("replacement-worker", created: 3000));
        _view.Update(next, [first, next], false);
        Assert.That(Rows.SelectedItem, Is.Null);
        Assert.That(Summary.Text, Is.EqualTo(Strings.SelectService));
        Assert.That(Button("CopyDetails").IsEnabled, Is.False);
        Assert.That(Button("Properties").IsEnabled, Is.False);
        Click("CopyDetails");
        Click("Properties");
        Assert.That(_copies, Is.Empty);
        Assert.That(_properties, Is.Empty);
        AssertServiceControlsDisabled();
    }

    [Test]
    public void HiddenWorkerSelectionFollowsItsLifetimeIntoAnotherServiceGroup()
    {
        var first = Frame(1, Worker("worker-old"));
        SelectWorker(first);
        HideWorker("filtered", first.Groups[0].Key);
        var current = Worker("worker-current");
        var next = Frame(2, null);
        var otherHost = ServiceRankingTests.P(300, 3000, 20, 200, 3000);
        next = next with
        {
            Groups = [.. next.Groups, new("host:300:3000", [new("other", "Other Agent", 4, 300, 1)], otherHost, [otherHost, current])]
        };
        _view.Update(next, [first, next], false);
        AssertCurrentDetails(current.Name, "Other Agent");
        ((TextBox)_view.FindName("Search")).Clear();
        _view.ExpandGroup("host:300:3000");
        Assert.That(((ServiceDisplayRow)Rows.SelectedItem).Process, Is.SameAs(current));
    }

    [Test]
    public void UnavailableFrameDisablesHiddenWorkerDetailsAndAvailabilityRestoresTheSameLifetime()
    {
        var first = Frame(1, Worker("worker-old"));
        SelectWorker(first);
        HideWorker("collapsed", first.Groups[0].Key);
        var current = Worker("worker-current");
        var unavailable = Frame(2, current) with { Available = false };
        _view.Update(unavailable, [first, unavailable], false);
        Assert.That(Summary.Text, Is.EqualTo(Strings.SelectService));
        Assert.That(Button("Properties").IsEnabled, Is.False);
        Assert.That(Button("CopyDetails").IsEnabled, Is.False);
        var available = unavailable with { Available = true };
        _view.Update(available, [first, available], false);
        AssertCurrentDetails(current.Name, "Agent");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SharedHostSelectionKeepsFreshDetailsOnlyWhileTheOriginalHostLifetimeExists(bool reusedPid)
    {
        var host = ServiceRankingTests.P(100, 1000, 10, 100, 1000) with { Name = "host-old" };
        ServiceObservation[] services = [new("agent", "Agent", 4, 100, 1), new("peer", "Peer", 4, 100, 1)];
        var first = Frame(1, null) with { Groups = [new("host:100:1000", services, host, [host])] };
        _view.Update(first, [first], false);
        Rows.SelectedItem = Shown.Single();
        HideWorker("filtered", first.Groups[0].Key);
        var current = host with { Name = "host-current", CreationTicks = reusedPid ? 3000 : 1000 };
        var next = Frame(2, null) with
        {
            Groups = [new($"host:100:{current.CreationTicks}", services, current, [current])]
        };
        _view.Update(next, [first, next], false);
        if (reusedPid)
        {
            Assert.That(Summary.Text, Is.EqualTo(Strings.SelectService));
            Assert.That(Button("CopyDetails").IsEnabled, Is.False);
            Assert.That(Button("Properties").IsEnabled, Is.False);
            Click("CopyDetails");
            Click("Properties");
            Assert.That(_copies, Is.Empty);
            Assert.That(_properties, Is.Empty);
            AssertServiceControlsDisabled();
        }
        else
        {
            AssertCurrentDetails(Strings.Format(Strings.SharedServiceHost, current.Name, 100), "Agent", 100, "host-old");
            Assert.That(_copies.Single(), Does.Contain("Peer (peer)"));
        }
    }

    private void SelectWorker(ServiceFrame frame)
    {
        _view.Update(frame, [frame], false);
        _view.ExpandGroup(frame.Groups[0].Key);
        Rows.SelectedItem = Shown.Single(row => row.Key == "process:200:2000");
        Assert.That(Summary.Text, Is.EqualTo("worker-old"));
        AssertServiceControlsDisabled();
    }

    private void HideWorker(string hiding, string group)
    {
        switch (hiding)
        {
            case "collapsed":
                var content = (Border)Rows.ItemTemplate.LoadContent();
                content.DataContext = Shown.Single(row => row.Key == group);
                var panel = ((StackPanel)content.Child).Children.OfType<Grid>().Single();
                panel.Children.OfType<DockPanel>().Single().Children.OfType<Button>().First()
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                break;
            case "filtered": ((TextBox)_view.FindName("Search")).Text = "no fixture service matches this"; break;
            case "host-only": _view.RestoreFilters(0, 0, false); break;
            default: throw new ArgumentOutOfRangeException(nameof(hiding));
        }
    }

    private void ShowWorker(string hiding, string group)
    {
        if (hiding == "collapsed") _view.ExpandGroup(group);
        else if (hiding == "filtered") ((TextBox)_view.FindName("Search")).Clear();
        else _view.RestoreFilters(0, 0, true);
    }

    private void AssertCurrentDetails(string name, string service, int processId = 200, string oldName = "worker-old")
    {
        Assert.That(Summary.Text, Is.EqualTo(name));
        Assert.That(Button("Properties").IsEnabled, Is.True);
        Assert.That(Button("CopyDetails").IsEnabled, Is.True);
        Click("CopyDetails");
        Click("Properties");
        Assert.That(_copies.Single(), Does.StartWith(name + Environment.NewLine).And.Contain(service).And.Contain($"PID {processId}"));
        Assert.That(_copies.Single(), Does.Not.Contain(oldName));
        Assert.That(_properties.Single().Name, Is.EqualTo(name));
        Assert.That(_properties.Single().Details, Is.EqualTo(_copies.Single()));
        AssertServiceControlsDisabled();
    }

    private void AssertServiceControlsDisabled()
    {
        foreach (var name in new[] { "Start", "Stop", "Restart" })
        {
            Assert.That(Button(name).IsEnabled, Is.False);
            Click(name);
        }
    }

    private void Click(string name) => Button(name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static ProcessUsage Worker(string name, long created = 2000, double cpu = 5) =>
        ServiceRankingTests.P(200, created, cpu, 300, 2000) with { Name = name };
    private static ServiceFrame Frame(int second, ProcessUsage? worker)
    {
        var host = ServiceRankingTests.P(100, 1000, 10, 100, 1000);
        return worker is null ? ServiceRankingTests.Frame(second, host) : ServiceRankingTests.Frame(second, host, worker);
    }
}
