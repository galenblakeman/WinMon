using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[SetUICulture("en")]
public sealed class ServicesViewTests
{
    [Test]
    public void AllShowsMoreThanFiftyGroupsAndKeepsFiltersAndCountEvents()
    {
        var groups = Enumerable.Range(1, 75).Select(i =>
        {
            var host = ServiceRankingTests.P(i, 100, i, i * 1_000_000, (ulong)i);
            return new ServiceGroup($"host:{i}:100", [new($"s{i}", $"Service {i}", 4, i, 1)], host, [host]);
        }).ToArray();
        var frame = ServiceRankingTests.Frame(1, groups[0].Host!) with { Groups = groups };
        var view = new ServicesView();
        view.Update(frame, [frame], false);
        var rows = (ListBox)view.FindName("Rows");
        Assert.That(rows.Items.Count, Is.EqualTo(20));
        var counts = new List<int>();
        view.TopServiceCountChanged += counts.Add;
        ((ComboBox)view.FindName("TopCount")).SelectedItem = TopCountChoices.All;
        Assert.That(rows.Items.Count, Is.EqualTo(75));
        Assert.That(((ServiceDisplayRow)rows.Items[0]).Service?.Name, Is.EqualTo("s75"));
        ((ComboBox)view.FindName("StatusFilter")).SelectedIndex = 2;
        Assert.That(rows.Items.Count, Is.Zero);
        ((ComboBox)view.FindName("StatusFilter")).SelectedIndex = 0;
        Assert.That(rows.Items.Count, Is.EqualTo(75));
        view.TopServiceCount = 5;
        Assert.That(rows.Items.Count, Is.EqualTo(5));
        Assert.That(counts, Is.EqualTo(new[] { 0, 5 }));
    }

    [Test]
    public void HiddenActionBarReclaimsSpaceAndPreservesSelectionAcrossRefresh()
    {
        var view = new ServicesView();
        var bar = (Border)view.FindName("ActionBar");
        Assert.That(bar.Visibility, Is.EqualTo(Visibility.Visible));
        var frame = SelectionFrame(1, 80);
        view.Update(frame, [frame], false);
        var rows = (ListBox)view.FindName("Rows");
        rows.SelectedItem = rows.Items.Cast<ServiceDisplayRow>().Single(r => r.Service?.Name == "agent");
        bar.Measure(new Size(1000, double.PositiveInfinity));
        var originalHeight = bar.DesiredSize.Height;
        Assert.That(bar.Visibility, Is.EqualTo(Visibility.Visible));
        view.ActionsHidden = true;
        frame = SelectionFrame(2, 70);
        view.Update(frame, [frame], false);
        bar.Measure(new Size(1000, double.PositiveInfinity));
        Assert.Multiple(() =>
        {
            Assert.That(bar.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(bar.DesiredSize.Height, Is.Zero);
            Assert.That(((TextBlock)view.FindName("Summary")).Text, Is.EqualTo("Build Agent"));
        });
        view.ActionsHidden = false;
        bar.Measure(new Size(1000, double.PositiveInfinity));
        Assert.That(bar.Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(bar.DesiredSize.Height, Is.EqualTo(originalHeight));
        string? copied = null;
        view.CopyRequested += text => copied = text;
        ((Button)view.FindName("CopyDetails")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(copied, Does.Contain("Build Agent (agent)"));
        rows.SelectedItem = null;
        Assert.That(bar.Visibility, Is.EqualTo(Visibility.Visible));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NoSelectionRespectsVisibilityPreferenceAndDisablesActions(bool hidden)
    {
        var view = new ServicesView { ActionsHidden = hidden };
        view.Update(null, [], false);
        var bar = (Border)view.FindName("ActionBar");
        bar.Measure(new Size(1000, double.PositiveInfinity));
        Assert.Multiple(() =>
        {
            Assert.That(bar.Visibility, Is.EqualTo(hidden ? Visibility.Collapsed : Visibility.Visible));
            Assert.That(bar.DesiredSize.Height, hidden ? Is.Zero : Is.GreaterThan(0));
            Assert.That(((TextBlock)view.FindName("Summary")).Text, Is.EqualTo(WinMon.Resources.Strings.SelectService));
            foreach (var name in new[] { "Properties", "CopyDetails", "Start", "Stop", "Restart" })
                Assert.That(((Button)view.FindName(name)).IsEnabled, Is.False, name);
        });
        view.ActionsHidden = !hidden;
        Assert.That(bar.Visibility, Is.EqualTo(hidden ? Visibility.Visible : Visibility.Collapsed));
    }

    [Test]
    public void RankingHeaders_SwitchOrderAndKeepDropdownAndIndicatorInSync()
    {
        var groups = new[] {
            ServiceRankingTests.Frame(1, ServiceRankingTests.P(1, 100, 90, 1, 1)).Groups[0],
            ServiceRankingTests.Frame(1, ServiceRankingTests.P(2, 200, 1, 2, 900)).Groups[0],
            ServiceRankingTests.Frame(1, ServiceRankingTests.P(3, 300, 2, 900, 2)).Groups[0] };
        var frame = ServiceRankingTests.Frame(1, groups[0].Host!) with { Groups = groups };
        var view = new ServicesView(); view.Update(frame, [frame], false);
        var rows = (ListBox)view.FindName("Rows");
        var sort = (ComboBox)view.FindName("Sort");
        var headers = new[] { "CpuHeader", "MemoryHeader", "IoHeader" }.Select(name => (TextBlock)view.FindName(name)).ToArray();
        foreach (var index in new[] { 1, 2, 0 })
        {
            headers[index].RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            Assert.That(sort.SelectedIndex, Is.EqualTo(index));
            Assert.That(((ServiceDisplayRow)rows.Items[0]).Group.Host!.ProcessId, Is.EqualTo(index + 1));
            Assert.That(headers.Select(h => h.Text.EndsWith('▾')), Is.EqualTo(Enumerable.Range(0, 3).Select(i => i == index)));
            Assert.That(headers[index].FontWeight, Is.EqualTo(FontWeights.SemiBold));
        }
        sort.SelectedIndex = 2;
        Assert.That(headers[2].Text.EndsWith('▾'), Is.True);
        Assert.That(headers.Take(2).Any(h => h.Text.EndsWith('▾')), Is.False);
        Assert.That(((ServiceDisplayRow)rows.Items[0]).Group.Host!.ProcessId, Is.EqualTo(3));
    }

    [Test]
    public void NameSort_IsAvailableForAllAndRestoresMetricSortForFiniteTop()
    {
        var groups = new[]
        {
            new ServiceGroup("service:z", [new("z", "Zulu", 4, 0, 1)], null, []),
            new ServiceGroup("service:a", [new("a", "Alpha", 4, 0, 1)], null, []),
            new ServiceGroup("service:b", [new("b", "Bravo", 4, 0, 1)], null, [])
        };
        var frame = ServiceRankingTests.Frame(1, ServiceRankingTests.P(1, 100, 1, 1, 1)) with { Groups = groups };
        var view = new ServicesView();
        view.Update(frame, [frame], false);
        var sort = (ComboBox)view.FindName("Sort");
        var rows = (ListBox)view.FindName("Rows");
        Assert.That(sort.Items.Count, Is.EqualTo(3));
        ((ComboBox)view.FindName("TopCount")).SelectedItem = TopCountChoices.All;
        Assert.That(sort.Items.Cast<string>().Last(), Is.EqualTo(WinMon.Resources.Strings.ServiceName));
        sort.SelectedIndex = 3;
        Assert.That(rows.Items.Cast<ServiceDisplayRow>().Select(r => r.Service?.Name),
            Is.EqualTo(new[] { "a", "b", "z" }));
        view.TopServiceCount = 5;
        Assert.That(sort.Items.Count, Is.EqualTo(3));
        Assert.That(sort.SelectedIndex, Is.EqualTo(0));
    }

    [Test]
    public void ContextMenu_SelectsRowAndCapturesTargetsAcrossSelectionChanges()
    {
        var view = new ServicesView(); var frame = SelectionFrame(1, 80);
        view.Update(frame, [frame], false);
        var rows = (ListBox)view.FindName("Rows");
        var target = rows.Items.Cast<ServiceDisplayRow>().Single(r => r.Service?.Name == "agent");
        var menu = view.CreateServiceMenu(target)!;
        Assert.That(rows.SelectedItem, Is.SameAs(target));
        var items = menu.Items.OfType<MenuItem>().ToArray();
        Assert.That(items.Select(i => i.Header), Is.EqualTo(new[] { WinMon.Resources.Strings.ViewServiceProcess,
            WinMon.Resources.Strings.CopyDetails, WinMon.Resources.Strings.StartService, WinMon.Resources.Strings.StopService, WinMon.Resources.Strings.RestartService }));
        Assert.That(items.All(i => i.Icon is System.Windows.Shapes.Path), Is.True);
        Assert.That(items.Select(i => i.IsEnabled), Is.EqualTo(new[] { true, true, false, true, true }));
        rows.SelectedIndex = 1;
        ProcessUsage? process = null; ServiceObservation? service = null; string? copy = null;
        view.ProcessRequested += p => process = p;
        view.ActionRequested += (s, _, _) => service = s;
        view.CopyRequested += text => copy = text;
        items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        items[4].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.That(process, Is.SameAs(frame.Groups[0].Host));
        Assert.That(service?.Name, Is.EqualTo("agent"));
        Assert.That(copy, Does.Contain("Build Agent (agent)"));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void ContextMenu_DisablesServiceControlsForHistoryOrBusy(bool historical, bool busy)
    {
        var view = new ServicesView(); var frame = SelectionFrame(1, 80);
        view.Update(frame, [frame], historical); view.SetBusy(busy);
        var row = ((ListBox)view.FindName("Rows")).Items.Cast<ServiceDisplayRow>().First();
        var items = view.CreateServiceMenu(row)!.Items.OfType<MenuItem>().ToArray();
        Assert.That(items.Take(2).All(i => i.IsEnabled), Is.True);
        Assert.That(items.Skip(2).All(i => !i.IsEnabled), Is.True);
    }

    [Test]
    public void ContextMenu_RejectsHostReplacementOrUnavailableDataAfterOpening()
    {
        var view = new ServicesView(); var frame = SelectionFrame(1, 80);
        view.Update(frame, [frame], false);
        var row = ((ListBox)view.FindName("Rows")).Items.Cast<ServiceDisplayRow>().First();
        var restart = view.CreateServiceMenu(row)!.Items.OfType<MenuItem>().Last();
        var count = 0; view.ActionRequested += (_, _, _) => count++;
        var replacement = SelectionFrame(2, 80, created: 999);
        view.Update(replacement, [replacement], false);
        restart.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.That(count, Is.Zero);
        view.Update(null, [], false);
        restart.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.That(count, Is.Zero);
    }

    [Test]
    public void ContextMenu_WorkerAndSharedHostHaveNoServiceControls()
    {
        var view = new ServicesView(); var frame = SelectionFrame(1, 80);
        var group = frame.Groups[0];
        group = group with { Services = [.. group.Services, new("second", "Second Service", 4, 10, 1)] };
        frame = frame with { Groups = [group] };
        view.Update(frame, [frame], false); view.ExpandAll();
        var rows = (ListBox)view.FindName("Rows");
        var host = rows.Items.Cast<ServiceDisplayRow>().First();
        Assert.That(view.CreateServiceMenu(host)!.Items.OfType<MenuItem>().Skip(2).Any(i => i.IsEnabled), Is.False);
        var service = rows.Items.Cast<ServiceDisplayRow>().Single(r => r.Service?.Name == "agent");
        Assert.That(view.CreateServiceMenu(service)!.Items.OfType<MenuItem>().Last().IsEnabled, Is.True);
        frame = SelectionFrame(2, 80);
        view.Update(frame, [frame], false); view.ExpandAll();
        var worker = rows.Items.Cast<ServiceDisplayRow>().Single(r => r.Key == "process:10:100");
        Assert.That(view.CreateServiceMenu(worker)!.Items.OfType<MenuItem>().Skip(2).Any(i => i.IsEnabled), Is.False);
    }

    [Test]
    public void ActionBar_KeepsOffRankService_AndUsesFreshStatusAndHostForActions()
    {
        var view = new ServicesView();
        ((ComboBox)view.FindName("TopCount")).SelectedItem = 5;
        var first = SelectionFrame(1, 80);
        view.Update(first, [first], false);
        var rows = (ListBox)view.FindName("Rows");
        rows.SelectedIndex = 0;
        var history = new[] { first }.Concat(Enumerable.Range(2, 6).Select(i => SelectionFrame(i, 0))).ToArray();
        view.Update(history[^1], history, false);
        Assert.That(rows.Items.Cast<ServiceDisplayRow>().Any(r => r.Service?.Name == "agent"), Is.False);
        Assert.That(rows.SelectedItem, Is.Null);
        Assert.That(((TextBlock)view.FindName("Summary")).Text, Is.EqualTo("Build Agent"));
        string? copied = null;
        view.CopyRequested += text => copied = text;
        ((Button)view.FindName("CopyDetails")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(copied, Does.Contain("Build Agent (agent)").And.Contain("CPU: 0.0%"));
        Assert.That(copied, Does.Contain($"PID {history[^1].Groups[0].Host!.ProcessId}"));
        ServiceObservation? actedService = null;
        ProcessUsage? actedHost = null;
        view.ActionRequested += (service, host, _) => { actedService = service; actedHost = host; };
        ((Button)view.FindName("Restart")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(actedService?.Name, Is.EqualTo("agent"));
        Assert.That(actedHost, Is.SameAs(history[^1].Groups[0].Host));

        var stopped = SelectionFrame(8, 0, state: 1);
        view.Update(stopped, [stopped], false);
        Assert.That(((TextBlock)view.FindName("SelectionStatus")).Text, Is.EqualTo(WinMon.Resources.Strings.ServiceStopped));
        Assert.That(((Button)view.FindName("Stop")).IsEnabled, Is.False);
        Assert.That(((Button)view.FindName("Start")).IsEnabled, Is.True);
        ((Button)view.FindName("Start")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(actedService?.State, Is.EqualTo(1));
        Assert.That(actedHost, Is.Null);
        ((Button)view.FindName("CopyDetails")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(copied, Does.Contain(WinMon.Resources.Strings.ServiceStopped));

        var restarted = SelectionFrame(9, 0, pid: 99, created: 900);
        view.Update(restarted, [restarted], false);
        ((Button)view.FindName("Restart")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(actedHost?.ProcessId, Is.EqualTo(99));
        Assert.That(actedHost?.CreationTicks, Is.EqualTo(900));
        string? propertyName = null, propertyDetails = null;
        view.PropertiesRequested += (name, details) => (propertyName, propertyDetails) = (name, details);
        ((Button)view.FindName("Properties")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(propertyName, Is.EqualTo("Build Agent"));
        Assert.That(propertyDetails, Does.Contain("Build Agent (agent)"));
    }

    [Test]
    public void OffRankSelection_DisablesDuringHistoryAndUnavailableData_AndRestoresVisibleSelection()
    {
        var view = new ServicesView();
        ((ComboBox)view.FindName("TopCount")).SelectedItem = 5;
        var first = SelectionFrame(1, 80);
        view.Update(first, [first], false);
        var rows = (ListBox)view.FindName("Rows");
        rows.SelectedIndex = 0;
        var hidden = SelectionFrame(2, 0);
        view.Update(hidden, [hidden], true);
        Assert.That(rows.SelectedItem, Is.Null);
        Assert.That(((Button)view.FindName("Stop")).IsEnabled, Is.False);
        var actions = 0;
        view.ActionRequested += (_, _, _) => actions++;
        ((Button)view.FindName("Stop")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        view.Update(null, [], false);
        ((Button)view.FindName("Stop")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(actions, Is.Zero);
        view.Update(hidden, [hidden], false);
        Assert.That(((Button)view.FindName("Stop")).IsEnabled, Is.True);
        view.SetBusy(true);
        ((Button)view.FindName("Stop")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(actions, Is.Zero);
        view.SetBusy(false);
        ((ComboBox)view.FindName("TopCount")).SelectedItem = 20;
        Assert.That(((ServiceDisplayRow)rows.SelectedItem).Service?.Name, Is.EqualTo("agent"));
        rows.SelectedItem = rows.Items.Cast<ServiceDisplayRow>().First(r => r.Service?.Name == "other20");
        Assert.That(((TextBlock)view.FindName("Summary")).Text, Is.EqualTo("Other 20"));
        view.Update(hidden with { Groups = [] }, [], false);
        Assert.That(((Button)view.FindName("Stop")).IsEnabled, Is.False);
        Assert.That(((TextBlock)view.FindName("Summary")).Text, Is.EqualTo(WinMon.Resources.Strings.SelectService));
    }

    [Test]
    public void SharedServiceSelection_SurvivesOffRankHost_AndTargetsOnlyTheSelectedService()
    {
        var view = new ServicesView();
        ((ComboBox)view.FindName("TopCount")).SelectedItem = 5;
        var first = Shared(SelectionFrame(1, 80));
        view.Update(first, [first], false);
        view.ExpandGroup(first.Groups[0].Key);
        var rows = (ListBox)view.FindName("Rows");
        rows.SelectedItem = rows.Items.Cast<ServiceDisplayRow>().First(r => r.Service?.Name == "peer");
        var hidden = Shared(SelectionFrame(2, 0));
        view.Update(hidden, [hidden], false);
        Assert.That(rows.SelectedItem, Is.Null);
        Assert.That(((TextBlock)view.FindName("Summary")).Text, Is.EqualTo("Peer service"));
        ServiceObservation? target = null;
        view.ActionRequested += (service, _, _) => target = service;
        ((Button)view.FindName("Stop")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(target?.Name, Is.EqualTo("peer"));

        static ServiceFrame Shared(ServiceFrame frame) => frame with
        {
            Groups = [frame.Groups[0] with { Services = [frame.Groups[0].Services[0], new("peer", "Peer service", 4, 10, 1)] }, .. frame.Groups.Skip(1)]
        };
    }

    [Test]
    public void ProcessSelection_DoesNotFollowAReusedPid()
    {
        var view = new ServicesView();
        var first = SelectionFrame(1, 80);
        view.Update(first, [first], false);
        view.ExpandGroup(first.Groups[0].Key);
        var rows = (ListBox)view.FindName("Rows");
        rows.SelectedItem = rows.Items.Cast<ServiceDisplayRow>().First(r => r.Key == "process:10:100");
        var replacement = SelectionFrame(2, 80, created: 900);
        view.Update(replacement, [replacement], false);
        Assert.That(((Button)view.FindName("Properties")).IsEnabled, Is.False);
        Assert.That(((TextBlock)view.FindName("Summary")).Text, Is.EqualTo(WinMon.Resources.Strings.SelectService));
    }

    private static ServiceFrame SelectionFrame(int second, double cpu, uint state = 4, int pid = 10, long created = 100)
    {
        var host = state == 1 ? null : ServiceRankingTests.P(pid, created, cpu, cpu * 100, 100);
        var agent = new ServiceGroup(host is null ? "service:agent" : $"host:{pid}:{created}",
            [new("agent", "Build Agent", state, host?.ProcessId ?? 0, 1)], host, host is null ? [] : [host]);
        var others = Enumerable.Range(20, 5).Select(i =>
        {
            var process = ServiceRankingTests.P(i, 200, 10, 100, 100);
            return new ServiceGroup($"host:{i}:200", [new($"other{i}", $"Other {i}", 4, i, 1)], process, [process]);
        });
        return ServiceRankingTests.Frame(second, ServiceRankingTests.P(1, 100, 0, 0, 0)) with { Groups = [agent, .. others] };
    }

    [Test]
    public void TopLimit_CountsHostsOnly_AndFiltersBeforeTakingTheTopRows()
    {
        var groups = Enumerable.Range(1, 27).Select(i =>
        {
            var host = ServiceRankingTests.P(i, 100, i, i * 1_000_000, (ulong)i);
            var worker = ServiceRankingTests.P(100 + i, 200, i, i * 1_000_000, (ulong)i);
            return new ServiceGroup($"host:{i}:100", [new($"s{i}", $"Service {i}", i == 1 ? 1u : 4u, i, 1)], host, [host, worker]);
        }).ToArray();
        var frame = ServiceRankingTests.Frame(1, groups[0].Host!) with { Groups = groups };
        var view = new ServicesView();
        view.Update(frame, [frame], false);
        var rows = (ListBox)view.FindName("Rows");
        var count = (ComboBox)view.FindName("TopCount");
        Assert.That(count.SelectedItem, Is.EqualTo(20));
        Assert.That(rows.Items.Count, Is.EqualTo(20));
        Assert.That(rows.Items.Cast<ServiceDisplayRow>().Last().Group.Host!.ProcessId, Is.EqualTo(8));
        view.ExpandGroup("host:27:100");
        Assert.That(rows.Items.Count, Is.EqualTo(22), "Two child processes do not consume the host limit.");
        count.SelectedItem = 5;
        Assert.That(rows.Items.Count, Is.EqualTo(7));
        ((TextBox)view.FindName("Search")).Text = "s1";
        Assert.That(rows.Items.Cast<ServiceDisplayRow>().All(r => r.Group.Services[0].Name.StartsWith("s1")), Is.True);
        ((ComboBox)view.FindName("StatusFilter")).SelectedIndex = 2;
        Assert.That(rows.Items.Count, Is.EqualTo(1), "A stopped service below the original limit is still found.");
        Assert.That(((ServiceDisplayRow)rows.Items[0]).Group.Services[0].Name, Is.EqualTo("s1"));
        ((TextBox)view.FindName("Search")).Clear();
        ((ComboBox)view.FindName("StatusFilter")).SelectedIndex = 0;
        count.SelectedItem = 50;
        Assert.That(rows.Items.Count, Is.EqualTo(29));
    }

    [Test]
    public void Rankings_UseFiveSecondCpuAndIoAveragesForOrderAndDisplayedValues()
    {
        var frames = Enumerable.Range(1, 5).Select(i =>
        {
            var first = ServiceRankingTests.Frame(i, ServiceRankingTests.P(1, 100, i == 5 ? 0 : 10, i == 5 ? 0 : 5_000_000, 100));
            var second = ServiceRankingTests.Frame(i, ServiceRankingTests.P(2, 200, i == 5 ? 20 : 0, i == 5 ? 10_000_000 : 0, 200));
            return first with { Groups = [first.Groups[0], second.Groups[0]] };
        }).ToArray();
        var view = new ServicesView();
        view.Update(frames[^1], frames, false);
        var list = (ListBox)view.FindName("Rows");
        Assert.That(((ServiceDisplayRow)list.Items[0]).Group.Host!.ProcessId, Is.EqualTo(1));
        Assert.That(((ServiceDisplayRow)list.Items[0]).Cpu, Is.EqualTo(WinMon.Resources.Strings.Format(WinMon.Resources.Strings.PercentOneDecimal, 8d)));
        ((ComboBox)view.FindName("Sort")).SelectedIndex = 2;
        Assert.That(((ServiceDisplayRow)list.Items[0]).Group.Host!.ProcessId, Is.EqualTo(1));
        Assert.That(((ServiceDisplayRow)list.Items[0]).Io, Is.EqualTo(4d.ToString("N2")));
    }

    private static ServiceFrame Frame()
    {
        var host = new ProcessUsage(10, 100, "host", 2, IoBytesPerSecond: 100) { PrivateBytes = 100 };
        var worker = new ProcessUsage(11, 200, "worker", 3, IoBytesPerSecond: 200) { PrivateBytes = 200, ParentProcessId = 10 };
        return new ServiceTree().Capture(new MonitorSample(DateTimeOffset.UtcNow, 5, [host, worker], 0),
            new ServiceCatalog(1000, [new("agent", "Build Agent", 4, 10, 1)]));
    }

    [Test]
    public void Tree_WorkerToggleChangesTotalsAndChildren_SelectionSurvivesRefresh()
    {
        var view = new ServicesView();
        var frame = Frame();
        view.Update(frame, [frame], false);
        view.ExpandAll();
        var list = (ListBox)view.FindName("Rows");
        Assert.That(list.Items.Count, Is.EqualTo(3));
        var group = (ServiceDisplayRow)list.Items[0];
        Assert.That(group.Cpu, Does.Contain("5"));
        list.SelectedItem = group;
        view.Update(frame, [frame], false);
        Assert.That(list.SelectedItem, Is.SameAs(group));
        ((CheckBox)view.FindName("Workers")).IsChecked = false;
        Assert.That(list.Items.Count, Is.EqualTo(2));
        Assert.That(group.Cpu, Does.Contain("2"));
    }

    [Test]
    public void ExitedWorkerRow_IsNotKeptForReuseWhileItsHostRuns()
    {
        var view = new ServicesView();
        var frame = Frame();
        view.Update(frame, [frame], false);
        view.ExpandAll();
        var host = new ProcessUsage(10, 100, "host", 2, IoBytesPerSecond: 100) { PrivateBytes = 100 };
        var exited = new ServiceTree().Capture(new MonitorSample(DateTimeOffset.UtcNow, 5, [host], 0),
            new ServiceCatalog(1000, [new("agent", "Build Agent", 4, 10, 1)]));

        view.Update(exited, [frame, exited], false);

        var spares = (Dictionary<string, ServiceDisplayRow>)typeof(ServicesView)
            .GetField("_spareRows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
        Assert.Multiple(() =>
        {
            Assert.That(((ListBox)view.FindName("Rows")).Items.Cast<ServiceDisplayRow>().Select(r => r.Key),
                Has.None.EqualTo("process:11:200"));
            Assert.That(spares.Keys, Has.None.StartsWith("process:"));
        });
    }

    [Test]
    public void Selection_ServiceActionsAreDisabledForHistoryAndWhileBusy()
    {
        var view = new ServicesView();
        var frame = Frame();
        view.Update(frame, [frame], false);
        var list = (ListBox)view.FindName("Rows");
        list.SelectedIndex = 0;
        var stop = (Button)view.FindName("Stop");
        Assert.That(stop.IsEnabled, Is.True);
        view.Update(frame, [frame], true);
        Assert.That(stop.IsEnabled, Is.False);
        view.Update(frame, [frame], false);
        view.SetBusy(true);
        Assert.That(stop.IsEnabled, Is.False);
        view.SetBusy(false);
        Assert.That(stop.IsEnabled, Is.True);
        view.Update(null, [], false);
        Assert.That(stop.IsEnabled, Is.False);
    }

    [Test]
    public void Filters_SearchWorkerPidKeepsGroupContext_StoppedFilterExcludesRunningServices()
    {
        var view = new ServicesView();
        var frame = Frame();
        view.Update(frame, [frame], false);
        var list = (ListBox)view.FindName("Rows");
        ((TextBox)view.FindName("Search")).Text = "11";
        Assert.That(list.Items.Count, Is.EqualTo(1));
        ((ComboBox)view.FindName("StatusFilter")).SelectedIndex = 2;
        Assert.That(list.Items, Is.Empty);
    }

    [Test]
    public void SharedHost_ChildrenExposeServiceActionsButNoIndividualNumbersOrGraph()
    {
        var view = new ServicesView();
        var frame = Frame();
        var group = frame.Groups[0] with { Services = [new("one", "One", 4, 10, 1), new("two", "Two", 4, 10, 1)] };
        frame = frame with { Groups = [group] };
        view.Update(frame, [frame], false);
        view.ExpandAll();
        var list = (ListBox)view.FindName("Rows");
        list.SelectedIndex = 0;
        Assert.That(((Button)view.FindName("Stop")).IsEnabled, Is.False);
        var service = list.Items.Cast<ServiceDisplayRow>().First(r => r.Service?.Name == "one");
        list.SelectedItem = service;
        Assert.Multiple(() =>
        {
            Assert.That(((Button)view.FindName("Stop")).IsEnabled, Is.True);
            Assert.That(service.Cpu, Is.EqualTo(WinMon.Resources.Strings.MissingReading));
            Assert.That(service.Graph.Visibility, Is.EqualTo(Visibility.Hidden));
        });
    }
}
