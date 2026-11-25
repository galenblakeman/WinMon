using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ServiceRefreshCharacterizationTests
{
    private ServicesView _view = null!;
    private ListBox Rows => (ListBox)_view.FindName("Rows");
    private ServiceDisplayRow[] Shown => Rows.Items.Cast<ServiceDisplayRow>().ToArray();

    [SetUp]
    public void SetUp()
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        _view = new ServicesView { TopServiceCount = 0 };
        _view.RestoreFilters(0, 0, true);
        _view.ActionRequested += (_, _, _) => Assert.Fail("Refresh must not request a native service action.");
    }

    [TearDown]
    public void TearDown() => _view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

    private static ServiceFrame Frame(int second, params ServiceGroup[] groups) =>
        ServiceRankingTests.Frame(second, ServiceRankingTests.P(1, 100, 0, 0, 0)) with { Groups = groups };

    private static ServiceGroup Dedicated(int pid, string name, double cpu) =>
        new($"host:{pid}:1000", [new(name.ToLowerInvariant(), name, 4, pid, 1)],
            ServiceRankingTests.P(pid, 1000, cpu, 100, 1000), [ServiceRankingTests.P(pid, 1000, cpu, 100, 1000)]);

    [TestCase("collecting")]
    [TestCase("history-missing")]
    [TestCase("unavailable")]
    [TestCase("empty")]
    [TestCase("pinned-empty")]
    [TestCase("busy")]
    [TestCase("error")]
    [TestCase("empty-error")]
    public void NoticePrecedenceKeepsErrorsBusyHistoryAvailabilityAndFiltersDistinct(string partition)
    {
        _view.RestorePins(["missing", "another"]);
        _view.PinnedOnly = partition == "pinned-empty";
        ServiceFrame? frame = partition is "collecting" or "history-missing" or "busy" or "error" or "empty-error"
            ? null : Frame(1) with { Available = partition != "unavailable" };
        _view.Update(frame, frame is null ? [] : [frame], partition == "history-missing");
        if (partition is "busy" or "error" or "empty-error")
            _view.SetBusy(true, partition == "error" ? "fixture failure" : partition == "empty-error" ? "" : null);
        var expected = partition switch
        {
            "collecting" => Strings.ServicesCollecting,
            "history-missing" => Strings.ServiceHistoryExpired,
            "unavailable" => Strings.ServicesUnavailable,
            "empty" => Strings.NoServicesMatch,
            "pinned-empty" => Strings.NoPinnedServicesMatch,
            "busy" => Strings.ServiceActionInProgress,
            "error" => "fixture failure",
            "empty-error" => "",
            _ => throw new ArgumentOutOfRangeException(nameof(partition))
        };
        Assert.Multiple(() =>
        {
            Assert.That(((TextBlock)_view.FindName("Notice")).Text, Is.EqualTo(expected));
            Assert.That(((TextBlock)_view.FindName("Notice")).Visibility,
                Is.EqualTo(expected.Length == 0 ? Visibility.Collapsed : Visibility.Visible));
            Assert.That(((TextBlock)_view.FindName("PinSummary")).Text,
                Is.EqualTo(Strings.Format(Strings.PinnedServiceCount, 2)));
            Assert.That(((TextBlock)_view.FindName("PinSummary")).Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(Shown, Is.Empty);
            Assert.That(((Button)_view.FindName("Stop")).IsEnabled, Is.False);
        });
    }

    public static IEnumerable<TestCaseData> ExpandedCases()
    {
        yield return new TestCaseData(true, false, 0, true, Array.Empty<int>()).SetName("SharedSingleHost_HostOnly");
        yield return new TestCaseData(true, true, 0, true, Array.Empty<int>()).SetName("SharedSingleHost_Workers");
        yield return new TestCaseData(true, false, 2, true, new[] { 100 }).SetName("SharedWithWorkers_HostOnly");
        yield return new TestCaseData(true, true, 2, true, new[] { 100, 102, 101 }).SetName("SharedWithWorkers_All");
        yield return new TestCaseData(false, false, 2, true, new[] { 100 }).SetName("DedicatedWithWorkers_HostOnly");
        yield return new TestCaseData(false, true, 2, true, new[] { 100, 102, 101 }).SetName("DedicatedWithWorkers_All");
        yield return new TestCaseData(true, false, 2, false, Array.Empty<int>()).SetName("HostlessShared_HostOnly");
        yield return new TestCaseData(true, true, 2, false, new[] { 102, 101 }).SetName("HostlessShared_Workers");
    }

    [TestCaseSource(nameof(ExpandedCases))]
    public void ExpandedChildrenKeepServiceOrderHostFirstWorkerRankAndSingleSharedHostDeduplication(
        bool shared, bool workers, int workerCount, bool hasHost, int[] expectedProcessIds)
    {
        var host = hasHost ? ServiceRankingTests.P(100, 1000, 1, 100, 1000) : null;
        var processes = new List<ProcessUsage>();
        if (workerCount > 0) processes.Add(ServiceRankingTests.P(101, 1000, 3, 100, 1000));
        if (host is not null) processes.Add(host);
        if (workerCount > 1) processes.Add(ServiceRankingTests.P(102, 1000, 8, 100, 1000));
        ServiceObservation[] services = shared
            ? [new("z", "Zulu", 4, 100, 1), new("a", "Alpha", 4, 100, 1)]
            : [new("a", "Alpha", 4, 100, 1)];
        var group = new ServiceGroup("fixture", services, host, processes);
        var frame = Frame(1, group);
        _view.RestoreFilters(0, 0, workers);
        _view.Update(frame, [frame], false);
        var header = Shown.Single();
        _view.ExpandGroup(group.Key);
        string[] expected = [group.Key, .. shared ? new[] { "fixture:service:a", "fixture:service:z" } : [],
            .. expectedProcessIds.Select(pid => $"process:{pid}:1000")];
        Assert.That(Shown.Select(row => row.Key), Is.EqualTo(expected));
        Assert.That(Shown[0], Is.SameAs(header));
        Assert.That(header.Graph.Visibility, Is.EqualTo(hasHost ? Visibility.Visible : Visibility.Hidden));
        foreach (var child in Shown.Skip(1))
        {
            Assert.That(child.Graph.Visibility, Is.EqualTo(child.Service is not null ? Visibility.Hidden : Visibility.Visible));
            if (child.Service is not null)
            {
                Assert.That(child.Process, Is.SameAs(host));
                Assert.That((child.Cpu, child.Memory, child.Io),
                    Is.EqualTo((Strings.MissingReading, Strings.MissingReading, Strings.MissingReading)));
            }
        }
    }

    [Test]
    public void RankChangesMoveExistingRowsWithoutResetReplacingGraphsOrNotifyingOnRepeat()
    {
        var first = Frame(1, Dedicated(100, "Alpha", 30), Dedicated(200, "Beta", 20), Dedicated(300, "Gamma", 10));
        _view.Update(first, [first], false);
        var original = Shown.ToDictionary(row => row.Key);
        var graphs = Shown.ToDictionary(row => row.Key, row => row.Graph);
        var events = new List<(NotifyCollectionChangedAction, int, int)>();
        ((INotifyCollectionChanged)Rows.ItemsSource).CollectionChanged += (_, args) =>
            events.Add((args.Action, args.NewStartingIndex, args.OldStartingIndex));
        var next = Frame(2, Dedicated(100, "Alpha", 1), Dedicated(200, "Beta", 30), Dedicated(300, "Gamma", 20));
        _view.Update(next, [next], false);
        Assert.That(Shown.Select(row => row.Key), Is.EqualTo(new[] { "host:200:1000", "host:300:1000", "host:100:1000" }));
        Assert.That(events, Is.EqualTo(new[]
            { (NotifyCollectionChangedAction.Move, 0, 1), (NotifyCollectionChangedAction.Move, 1, 2) }));
        foreach (var row in Shown)
        {
            Assert.That(row, Is.SameAs(original[row.Key]));
            Assert.That(row.Graph, Is.SameAs(graphs[row.Key]));
        }
        events.Clear();
        _view.Update(next, [next], false);
        Assert.That(events, Is.Empty);
    }

    [Test]
    public void ThrowingCollectionObserverSeesInsertionAndFinallyRestoresSelectionHandling()
    {
        var source = (INotifyCollectionChanged)Rows.ItemsSource;
        var failure = new InvalidOperationException("collection observer fixture");
        var calls = 0;
        void Observer(object? sender, NotifyCollectionChangedEventArgs args)
        {
            calls++;
            Assert.That(args.Action, Is.EqualTo(NotifyCollectionChangedAction.Add));
            Assert.That(Shown.Single().Key, Is.EqualTo("host:100:1000"));
            throw failure;
        }
        source.CollectionChanged += Observer;
        var frame = Frame(1, Dedicated(100, "Alpha", 10));
        try
        {
            Assert.That(Assert.Throws<InvalidOperationException>(() => _view.Update(frame, [frame], false)), Is.SameAs(failure));
        }
        finally { source.CollectionChanged -= Observer; }
        Assert.That(calls, Is.EqualTo(1));
        Rows.SelectedItem = null;
        Rows.SelectedItem = Shown.Single();
        Assert.That(((TextBlock)_view.FindName("Summary")).Text, Is.EqualTo("Alpha"));
        Assert.That(((Button)_view.FindName("Stop")).IsEnabled, Is.True);
        _view.Update(frame, [frame], false);
        Assert.That(Shown, Has.Length.EqualTo(1));
        Assert.That(((TextBlock)_view.FindName("Notice")).Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void ThrowingRowObserverLeavesCollectionOrderAndCanRetryWithTheSameRows()
    {
        var first = Frame(1, Dedicated(100, "Alpha", 30), Dedicated(200, "Beta", 20));
        _view.Update(first, [first], false);
        var alpha = Shown[0];
        var beta = Shown[1];
        var next = Frame(2, Dedicated(100, "Alpha", 1), Dedicated(200, "Beta", 40));
        var failure = new InvalidOperationException("row observer fixture");
        void Observer(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(ServiceDisplayRow.Cpu)) return;
            Assert.That(alpha.Group, Is.SameAs(next.Groups[0]));
            throw failure;
        }
        alpha.PropertyChanged += Observer;
        try
        {
            Assert.That(Assert.Throws<InvalidOperationException>(() => _view.Update(next, [next], false)), Is.SameAs(failure));
        }
        finally { alpha.PropertyChanged -= Observer; }
        Assert.That(Shown, Is.EqualTo(new[] { alpha, beta }));
        _view.Update(next, [next], false);
        Assert.That(Shown, Is.EqualTo(new[] { beta, alpha }));
        Rows.SelectedItem = alpha;
        Assert.That(((TextBlock)_view.FindName("Summary")).Text, Is.EqualTo("Alpha"));
    }

    [Test]
    public void ExpansionSurvivesCurrentAbsenceButExpiresWhenItsLifetimeLeavesHistory()
    {
        var first = Frame(1, Dedicated(100, "Alpha", 10));
        var empty = Frame(2);
        _view.Update(first, [first], false);
        _view.ExpandAll();
        Assert.That(Shown, Has.Length.EqualTo(2));
        _view.Update(empty, [first, empty], false);
        Assert.That(Shown, Is.Empty);
        _view.Update(first, [first], true);
        Assert.That(Shown.Select(row => row.Key), Is.EqualTo(new[] { "host:100:1000", "process:100:1000" }));
        _view.Update(empty, [empty], false);
        _view.Update(first, [first], false);
        Assert.That(Shown.Select(row => row.Key), Is.EqualTo(new[] { "host:100:1000" }));
    }
}
