using System.Net;
using System.Windows;
using System.Windows.Controls;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ProcessNavigationCharacterizationTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch.AddDays(1);
    private static readonly ProcessUsage Process = new(8123, 100, "example.exe", 10)
        { ImagePath = @"C:\Apps\example.exe" };
    private readonly List<object> _addedResources = [];
    private MainWindow? _window;

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key))
            {
                app.Resources[key] = new Style();
                _addedResources.Add(key);
            }
        if (!app.Resources.Contains("TitleBarButtonText"))
        {
            app.Resources["TitleBarButtonText"] = new DataTemplate();
            _addedResources.Add("TitleBarButtonText");
        }
        if (!app.Resources.Contains(typeof(Button)))
        {
            app.Resources[typeof(Button)] = new Style(typeof(Button));
            _addedResources.Add(typeof(Button));
        }
    }

    [TearDown]
    public void TearDown()
    {
        _window?.CloseForFixture();
        _window = null;
        foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
        _addedResources.Clear();
    }

    private MainWindow Window(bool diagnostic = false)
    {
        _window = new MainWindow(diagnostic);
        _window.Loaded -= _window.OnLoaded;
        return _window;
    }

    private static MonitorSample Sample(int second = 0, params ProcessUsage[] processes) =>
        new(Start.AddSeconds(second), 20, processes, 0) { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 };

    private static ProcessDetailsSnapshot Snapshot(bool historical = false, MonitorSample? sample = null,
        bool grouped = false, ProcessUsage? process = null) =>
        new(process ?? Process, grouped, sample?.Timestamp, historical, true, "", sample is null ? null : SampleKey.Of(sample));

    private static NetworkConnection Connection(int pid = 8123, long? ticks = 100) =>
        new(pid, ticks, TransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, 1234));

    private static NetworkConnectionsFrame Frame(params NetworkConnection[] connections) =>
        new(connections, NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success);

    private static bool Can(MainWindow window, string target, ProcessDetailsSnapshot snapshot) =>
        window.Processes.CanNavigate!(target, snapshot);

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void LiveAndHistoricalSnapshotsCannotCrossTheSelectedMode(bool selected, bool historical)
    {
        var window = Window();
        var sample = Sample(0, Process);
        window.Session.Record(sample, sample, connections: Frame(Connection()));
        window.SelectedSample = selected ? sample : null;
        Assert.That(Can(window, "connections", Snapshot(historical, sample)), Is.False);
    }

    [TestCase("missing")]
    [TestCase("timestamp")]
    [TestCase("monotonic")]
    [TestCase("expired")]
    public void HistoricalNavigationRequiresTheExactRetainedSample(string mismatch)
    {
        var window = Window();
        var sample = Sample(0, Process);
        window.Session.Record(sample, sample, connections: Frame(Connection()));
        window.SelectedSample = mismatch == "expired" ? Sample(1, Process) : sample;
        var snapshot = Snapshot(true, window.SelectedSample);
        snapshot = mismatch switch
        {
            "missing" => snapshot with { Sample = null },
            "timestamp" => snapshot with { Sample = SampleKey.Of(sample with { Timestamp = sample.Timestamp.AddTicks(1) }) },
            "monotonic" => snapshot with { Sample = SampleKey.Of(sample with { MonotonicTimestamp = 1 }) },
            _ => snapshot
        };
        Assert.That(Can(window, "connections", snapshot), Is.False);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void UsageNavigationMatchesExecutableIdentityInTheSelectedPeriod(bool historical, bool present)
    {
        var window = Window();
        var observed = Process with { ImagePath = present ? @"c:\apps\EXAMPLE.EXE" : @"C:\Other\example.exe" };
        var sample = Sample(0, observed);
        window.Session.Record(sample, sample);
        window.SelectedSample = historical ? sample : null;
        Assert.That(Can(window, "usage", Snapshot(historical, sample)), Is.EqualTo(present));
        Assert.That(window.SelectedSample, Is.SameAs(historical ? sample : null));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("Connections")]
    [TestCase("unknown")]
    public void UnknownTargetsDoNotBorrowAvailableConnections(string target)
    {
        var window = Window();
        var sample = Sample();
        window.Session.Record(sample, sample, connections: Frame(Connection()));
        Assert.That(Can(window, target, Snapshot()), Is.False);
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void PathlessRowsCanNavigateOnlyByAnUngroupedLifetime(bool grouped, bool expected)
    {
        var window = Window();
        var sample = Sample();
        window.Session.Record(sample, sample, connections: Frame(Connection()));
        Assert.That(Can(window, "connections", Snapshot(grouped: grouped, process: Process with { ImagePath = "" })),
            Is.EqualTo(expected));
    }

    [TestCase("missing")]
    [TestCase("unavailable")]
    [TestCase("empty")]
    public void AbsentOrUnavailableConnectionsDoNotEnableNavigation(string kind)
    {
        var window = Window();
        if (kind != "missing")
        {
            var frame = kind == "empty" ? Frame() : new NetworkConnectionsFrame([Connection()], new(false), new(false));
            window.Session.Record(Sample(), connections: frame);
        }
        Assert.That(Can(window, "connections", Snapshot()), Is.False);
    }

    [TestCase(8123, 100L, true)]
    [TestCase(8123, 101L, false)]
    [TestCase(8124, 100L, false)]
    [TestCase(8123, null, false)]
    public void ConnectionsRequireBothPidAndCreationToken(int pid, long? ticks, bool expected)
    {
        var window = Window();
        window.Session.Record(Sample(), connections: Frame(Connection(pid, ticks)));
        Assert.That(Can(window, "connections", Snapshot()), Is.EqualTo(expected));
    }

    [TestCase("matching", true)]
    [TestCase("different", false)]
    [TestCase("missing", false)]
    [TestCase("empty-snapshot", false)]
    public void ExecutableGroupsIncludeOnlyOwnersWithMatchingNonemptyPaths(string kind, bool expected)
    {
        var window = Window();
        var owner = Process with { ProcessId = 9000, ImagePath = kind switch
        {
            "different" => @"C:\Other\example.exe", "missing" => "", _ => @"c:\APPS\EXAMPLE.EXE"
        } };
        var sample = Sample(0, owner);
        window.Session.Record(sample, sample, connections: Frame(Connection(9000)));
        var process = kind == "empty-snapshot" ? Process with { ImagePath = "" } : Process;
        Assert.That(Can(window, "connections", Snapshot(process: process)), Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HistoricalConnectionsUseTheAlignedCaptureInsteadOfTheLatestFrame(bool diagnostic)
    {
        var window = Window(diagnostic);
        var past = Sample(0, Process);
        window.Session.Record(past, past, connections: Frame(Connection()));
        var latest = Sample(1);
        window.Session.Record(latest, latest, connections: Frame());
        if (diagnostic) window.SetConnectionsForFixture(Frame());
        window.SelectedSample = past;
        Assert.That(Can(window, "connections", Snapshot(true, past)), Is.True);
        window.SelectedSample = latest;
        Assert.That(Can(window, "connections", Snapshot(true, latest)), Is.False);
    }

    [Test]
    public void AHistoricalSampleWithoutConnectionsCannotBorrowTheLiveCapture()
    {
        var window = Window();
        var past = Sample();
        window.Session.Record(past);
        window.Session.Record(Sample(1), connections: Frame(Connection()));
        window.SelectedSample = past;
        Assert.That(Can(window, "connections", Snapshot(true, past)), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DiagnosticLiveConnectionsUseTheSuppliedFrameAndLatestFullOwners(bool ownersPresent)
    {
        var window = Window(true);
        var owner = Process with { ProcessId = 9000 };
        var sample = Sample(0, owner);
        window.Session.Record(sample, ownersPresent ? sample : null, connections: Frame());
        window.SetConnectionsForFixture(Frame(Connection(9000)));
        Assert.That(Can(window, "connections", Snapshot(grouped: true)), Is.EqualTo(ownersPresent));
        window.SetConnectionsForFixture(Frame(Connection()));
        Assert.That(Can(window, "connections", Snapshot()), Is.True, "The selected process identity is always included.");
    }

    [Test]
    public void PartialAddressFamilyAvailabilityStillAllowsAnExactLifetime()
    {
        var window = Window();
        window.Session.Record(Sample(), connections: new([Connection()], new(false), NetworkAddressFamilyStatus.Success));
        Assert.That(Can(window, "connections", Snapshot()), Is.True);
    }

    [Test]
    public void HistoricalUsageDoesNotBorrowAnExecutableFirstSeenLater()
    {
        var window = Window();
        var past = Sample();
        window.Session.Record(past, past);
        var latest = Sample(1, Process);
        window.Session.Record(latest, latest);
        Assert.That(Can(window, "usage", Snapshot()), Is.True);
        window.SelectedSample = past;
        Assert.That(Can(window, "usage", Snapshot(true, past)), Is.False);
    }

    [Test]
    public void MatchingNumbersFromAnotherLifetimeDomainCannotIdentifyAWindowsConnection()
    {
        var window = Window();
        window.Session.Record(Sample(), connections: Frame(Connection()));
        var process = Process with { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds };
        Assert.That(Can(window, "connections", Snapshot(process: process)), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AvailabilityFlagDoesNotOverrideAnExistingNavigationIdentity(bool historical)
    {
        var window = Window();
        var sample = Sample(0, Process);
        window.Session.Record(sample, sample, connections: Frame(Connection()));
        window.SelectedSample = historical ? sample : null;
        var snapshot = Snapshot(historical, sample) with { Available = false };
        Assert.Multiple(() =>
        {
            Assert.That(Can(window, "usage", snapshot), Is.True);
            Assert.That(Can(window, "connections", snapshot), Is.True);
        });
    }

    private static void PrepareConnectionOwnerButton(MainWindow window, params ProcessUsage[] members)
    {
        window.ConnectionsTab.IsChecked = true;
        window.Processes.Load(new Controls.ProcessListSettings(5, ProcessSort.Cpu, false, true, true), []);
        window.Processes.ProcessSearchBox.Text = "preserved search";
        window.ConnectionView.Update(Frame(members.Select(member => Connection(member.ProcessId, member.CreationTicks)).ToArray()),
            members, window.SelectedSample is not null);
        window.ConnectionView.Rows.SelectedItem = window.ConnectionView.DisplayedRows.Single();
        Assert.That(window.ConnectionView.ShowProcess.IsEnabled, Is.True);
    }

    private static void ClickConnectionOwner(MainWindow window) =>
        window.ConnectionView.ShowProcess.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void ConnectionOwnerButtonKeepsLiveAndHistoricalGroupOrPidNavigation(bool historical, bool hasPath)
    {
        var window = Window();
        var process = Process with { ImagePath = hasPath ? Process.ImagePath : "" };
        var sample = Sample(0, process);
        window.Session.Record(sample, sample);
        window.SelectedSample = historical ? sample : null;
        PrepareConnectionOwnerButton(window, process);

        ClickConnectionOwner(window);

        Assert.Multiple(() =>
        {
            Assert.That(window.ProcessesTab.IsChecked, Is.True);
            Assert.That(window.ConnectionsTab.IsChecked, Is.False);
            Assert.That(window.Processes.AppsOnlyCheckBox.IsChecked, Is.False);
            Assert.That(window.Processes.PinnedOnlyCheckBox.IsChecked, Is.False);
            Assert.That(window.Processes.GroupedChip.IsChecked, Is.EqualTo(hasPath));
            Assert.That(window.Processes.IndividualChip.IsChecked, Is.EqualTo(!hasPath));
            Assert.That(window.Processes.ProcessSearchBox.Text, Is.EqualTo(hasPath ? "" : "8123"));
            Assert.That(window.Processes.Selection.HasSelection, Is.EqualTo(hasPath));
            if (hasPath) Assert.That(window.Processes.Selection.Contains(process, true), Is.True);
            Assert.That(window.SelectedSample, Is.SameAs(historical ? sample : null));
            Assert.That(window.Session.LatestFull, Is.SameAs(sample));
            Assert.That(sample.Processes.Single(), Is.SameAs(process));
        });
    }

    [Test]
    public void ConnectionOwnerButtonUsesSampleOrderAndCurrentOwnerDetailsInsteadOfMemberOrder()
    {
        var window = Window();
        var first = Process with { ImagePath = @"C:\Current\first.exe" };
        var second = Process with { ProcessId = 8124, CreationTicks = 200, ImagePath = @"C:\Current\second.exe" };
        var sample = Sample(0, first, second);
        window.Session.Record(sample, sample);
        var members = new[] { second with { ImagePath = Process.ImagePath }, first with { ImagePath = Process.ImagePath } };
        PrepareConnectionOwnerButton(window, members);

        ClickConnectionOwner(window);

        Assert.Multiple(() =>
        {
            Assert.That(window.ProcessesTab.IsChecked, Is.True);
            Assert.That(window.Processes.Selection.Contains(first, true), Is.True);
            Assert.That(window.Processes.Selection.Contains(second, true), Is.False);
            Assert.That(window.Processes.Selection.Contains(members[0], true), Is.False);
            Assert.That(members.Select(member => member.ProcessId), Is.EqualTo(new[] { 8124, 8123 }));
            Assert.That(sample.Processes.Select(process => process.ProcessId), Is.EqualTo(new[] { 8123, 8124 }));
        });
    }

    [Test]
    public void ConnectionOwnerButtonPrefersTheSelectedSampleOverTheLatestFullOwner()
    {
        var window = Window();
        var pastOwner = Process with { ImagePath = @"C:\Past\owner.exe" };
        var latestOwner = Process with { ImagePath = @"C:\Latest\owner.exe" };
        var past = Sample(0, pastOwner);
        var latest = Sample(1, latestOwner);
        window.Session.Record(past, past);
        window.Session.Record(latest, latest);
        window.SelectedSample = past;
        PrepareConnectionOwnerButton(window, latestOwner);

        ClickConnectionOwner(window);

        Assert.Multiple(() =>
        {
            Assert.That(window.Processes.Selection.Contains(pastOwner, true), Is.True);
            Assert.That(window.Processes.Selection.Contains(latestOwner, true), Is.False);
            Assert.That(window.SelectedSample, Is.SameAs(past));
            Assert.That(window.Session.LatestFull, Is.SameAs(latest));
        });
    }

    [TestCase("no-full")]
    [TestCase("empty-full")]
    [TestCase("different-pid")]
    [TestCase("different-creation")]
    [TestCase("different-domain")]
    [TestCase("selected-empty")]
    public void StaleConnectionOwnerButtonsLeaveTheCurrentViewUntouchedWhenNoLifetimeMatches(string replacement)
    {
        var window = Window();
        var original = Sample(0, Process);
        window.Session.Record(original, original);
        PrepareConnectionOwnerButton(window, Process);
        window.Processes.Selection.Select(Process, false);
        if (replacement == "selected-empty") window.SelectedSample = Sample(1);
        else
        {
            var changed = replacement switch
            {
                "different-pid" => Process with { ProcessId = 8124 },
                "different-creation" => Process with { CreationTicks = 101 },
                "different-domain" => Process with { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds },
                _ => null
            };
            var next = changed is null ? Sample(1) : Sample(1, changed);
            window.Session.Record(next, replacement == "no-full" ? null : next);
        }
        Assert.That(window.ConnectionView.ShowProcess.IsEnabled, Is.True, "No row refresh has occurred since eligibility was observed.");

        ClickConnectionOwner(window);

        Assert.Multiple(() =>
        {
            Assert.That(window.ConnectionsTab.IsChecked, Is.True);
            Assert.That(window.ProcessesTab.IsChecked, Is.False);
            Assert.That(window.Processes.ProcessSearchBox.Text, Is.EqualTo("preserved search"));
            Assert.That(window.Processes.AppsOnlyCheckBox.IsChecked, Is.True);
            Assert.That(window.Processes.PinnedOnlyCheckBox.IsChecked, Is.True);
            Assert.That(window.Processes.GroupedChip.IsChecked, Is.False);
            Assert.That(window.Processes.Selection.Contains(Process, false), Is.True);
        });
    }
}
