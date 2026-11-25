using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class SnapshotCopyCharacterizationTests
{
    private static readonly DateTimeOffset Start = new(2026, 2, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProcessUsage Owner = new(987651, 100, "historic-process", 12, 4096, 1024)
        { AppName = "Historic app", ImagePath = @"C:\fixture\historic.exe", HasHadAppWindow = true };
    private static readonly ProcessUsage Other = new(987652, 200, "other-process", 5, 2048, 512)
        { AppName = "Other app", ImagePath = @"C:\fixture\other.exe" };
    private readonly Dictionary<object, object?> _resources = [];
    private MainWindow _window = null!;

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (System.Collections.DictionaryEntry entry in app.Resources) _resources.Add(entry.Key, entry.Value);
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) app.Resources[key] = new Style();
        if (!app.Resources.Contains("TitleBarButtonText")) app.Resources["TitleBarButtonText"] = new DataTemplate();
        if (!app.Resources.Contains(typeof(Button))) app.Resources[typeof(Button)] = new Style(typeof(Button));
        _window = new MainWindow(true);
        _window.Loaded -= _window.OnLoaded;
        _window.ApplyTheme(false, false);
        _window.Preferences = _window.Preferences with { ConnectionTrafficSeconds = 0 };
    }

    [TearDown]
    public void TearDown()
    {
        if (_window is not null)
        {
            _window.SnapshotCopiedTimer.Stop();
            foreach (var view in new FrameworkElement[] { _window.UsageView, _window.Processes, _window.ServiceView, _window.ConnectionView })
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            _window.CloseForFixture();
        }
        var resources = Application.Current.Resources;
        foreach (var key in resources.Keys.Cast<object>().Where(key => !_resources.ContainsKey(key)).ToArray()) resources.Remove(key);
        foreach (var (key, value) in _resources) resources[key] = value;
        _resources.Clear();
    }

    private static MonitorSample Sample(int second, params ProcessUsage[] owners) =>
        new(Start.AddSeconds(second), second, owners, 0)
        {
            MonotonicTimestamp = (1000L + second) * Stopwatch.Frequency,
            IntervalSeconds = 1, IntervalCpuTicks = 10_000_000,
            Resources = new ResourceSample { DiskName = "C:", UsedMemoryBytes = 4096, TotalMemoryBytes = 8192 }
        };

    private static NetworkConnectionsFrame Endpoints(params ProcessUsage[] owners) => new(
        owners.Select((owner, index) => new NetworkConnection(owner.ProcessId, owner.CreationTicks, TransportProtocol.Tcp,
            new IPEndPoint(IPAddress.Loopback, 41000 + index), new IPEndPoint(IPAddress.Parse("192.0.2.1"), 443), TcpState.Established)).ToArray(),
        NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success);

    private MonitorSample Seed(string tab = "Processes", string network = "traffic", params ProcessUsage[] owners)
    {
        if (owners.Length == 0) owners = [Owner, Other];
        var selected = Sample(10, owners);
        var futureOwner = Owner with { ProcessId = 987699, Name = "future-only", AppName = "Future only", ImagePath = @"C:\fixture\future.exe" };
        var future = Sample(20, futureOwner);
        var services = new ServiceFrame(selected,
            [new ServiceGroup("host:987651:100", [new("historic-service", "Historic service", 4, Owner.ProcessId, 1)], Owner, [Owner])]);
        var counters = network is "traffic" or "ranked"
            ? new ProcessNetworkFrame(selected.MonotonicTimestamp, Stopwatch.Frequency, ProcessNetworkQuality.None,
                owners.Select((owner, index) => new ProcessNetworkReading(owner.ProcessId, owner.CreationTicks,
                    1000 + index * 100, 500, 100 + index * 10, 50)).ToArray())
            : null;
        var endpoints = network is "missing" or "no-capture" ? null : Endpoints(owners);
        if (network == "ranked")
        {
            var available = endpoints!;
            endpoints = available with { Connections = [.. available.Connections,
                new(Owner.ProcessId, Owner.CreationTicks, TransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, 42000))] };
        }
        _window.Session.Record(selected, network == "no-capture" ? null : selected, services, endpoints, counters);
        _window.Session.Record(future, future, connections: Endpoints(futureOwner));
        _window.SelectedSample = selected;
        switch (tab)
        {
            case "Services": _window.ServicesTab.IsChecked = true; break;
            case "Usage": _window.UsageTab.IsChecked = true; break;
            case "Connections": _window.ConnectionsTab.IsChecked = true; break;
            default: _window.ProcessesTab.IsChecked = true; break;
        }
        _window.RefreshVisiblePresentation();
        _window.ConnectionView.Load(new ConnectionListSettings(TrafficSeconds: 0));
        Assert.That(_window.SnapshotReport?.Sample, Is.SameAs(selected));
        return selected;
    }

    [Test]
    public void InvalidCaptureGuardsDoNotInvokeTheCallbackOrChangeFeedback(
        [Values("no-report", "live", "equal-copy", "other", "expired")] string state,
        [Values(false, true)] bool timerRunning)
    {
        if (state != "no-report")
        {
            var sample = Seed();
            switch (state)
            {
                case "live": _window.SelectedSample = null; break;
                case "equal-copy": _window.SelectedSample = sample with { }; break;
                case "other": _window.SelectedSample = _window.Session.Latest; break;
                case "expired": _window.Session.History.Resize(1); break;
            }
        }
        object caption = new object();
        _window.SnapshotCopyButton.Content = caption;
        if (timerRunning) _window.SnapshotCopiedTimer.Start();
        var report = _window.SnapshotReport;
        var calls = 0;

        _window.CopySnapshot(_ => calls++);

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.Zero);
            Assert.That(_window.SnapshotCopyButton.Content, Is.SameAs(caption));
            Assert.That(_window.SnapshotCopiedTimer.IsEnabled, Is.EqualTo(timerRunning));
            Assert.That(_window.SnapshotReport, Is.SameAs(report));
        });
    }

    [Test]
    public void HistoricalCopyUsesTheSelectedReportAndAppendsOnlyTheActiveTab(
        [Values("Processes", "Services", "Usage", "Connections")] string tab,
        [Values("no-capture", "missing", "endpoints", "traffic")] string network)
    {
        var sample = Seed(tab, network);
        var prefix = SnapshotText.Format(_window.SnapshotReport!);
        var serviceSuffix = Environment.NewLine + _window.ServiceView.CaptureReport();
        var usageSuffix = Environment.NewLine + _window.UsageView.CaptureReport(_window.CurrentUsageFrame());
        var copied = new List<string>();
        _window.SnapshotCopyButton.Content = "before copy";

        _window.CopySnapshot(text =>
        {
            Assert.That(_window.SnapshotCopyButton.Content, Is.EqualTo("before copy"));
            Assert.That(_window.SnapshotCopiedTimer.IsEnabled, Is.False);
            copied.Add(text);
        });

        Assert.That(copied, Has.Count.EqualTo(1));
        var result = copied[0];
        var connections = ConnectionSection(result);
        Assert.Multiple(() =>
        {
            Assert.That(result, Does.StartWith(prefix));
            Assert.That(result, Does.Contain("2026-02-11 12:00:10 +00:00"));
            Assert.That(result, Does.Not.Contain("Future only").And.Not.Contain("future-only"));
            Assert.That(_window.SelectedSample, Is.SameAs(sample));
            Assert.That(_window.SnapshotCopyButton.Content, Is.EqualTo(Strings.Copied));
            Assert.That(_window.SnapshotCopiedTimer.IsEnabled, Is.True);
            Assert.That(_window.SnapshotCopiedTimer.Interval, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(connections, Does.Contain(network is "missing" or "no-capture" ? Strings.ConnectionsHistoryUnavailable : "127.0.0.1:41000"));
            if (network is "endpoints" or "traffic")
                Assert.That(connections, Does.Contain(Strings.ConnectionsTrafficPeriod + ": " + Strings.ConnectionsSinceStart));
            if (tab == "Services") Assert.That(result, Does.EndWith(serviceSuffix));
            else if (tab == "Usage") Assert.That(result, Does.EndWith(usageSuffix));
            else Assert.That(result, Does.Not.EndWith(serviceSuffix).And.Not.EndWith(usageSuffix));
        });
    }

    [Test]
    public void RepeatedCopyAndExplicitResetKeepOneTimerAndRestoreTheBoundCaption()
    {
        Seed();
        var timer = _window.SnapshotCopiedTimer;
        var copied = new List<string>();
        _window.CopySnapshot(copied.Add);
        _window.CopySnapshot(copied.Add);
        Assert.Multiple(() =>
        {
            Assert.That(copied, Has.Count.EqualTo(2));
            Assert.That(copied[1], Is.EqualTo(copied[0]));
            Assert.That(_window.SnapshotCopiedTimer, Is.SameAs(timer));
            Assert.That(timer.IsEnabled, Is.True);
            Assert.That(_window.SnapshotCopyButton.GetBindingExpression(ContentControl.ContentProperty), Is.Null);
        });
        _window.ResetSnapshotCopyCaption();
        Assert.Multiple(() =>
        {
            Assert.That(timer.IsEnabled, Is.False);
            Assert.That(_window.SnapshotCopyButton.Content, Is.EqualTo(Strings.CopySnapshot));
            Assert.That(_window.SnapshotCopyButton.GetBindingExpression(ContentControl.ContentProperty)!.ParentBinding.Path.Path,
                Is.EqualTo("[CopySnapshot]"));
        });
    }

    [Test]
    public void OtherCallbackExceptionsPropagateWithoutChangingPriorFeedbackAndAllowRetry(
        [Values("invalid-operation", "cancelled")] string kind,
        [Values(false, true)] bool previousCopy)
    {
        Seed();
        if (previousCopy) _window.CopySnapshot(_ => { });
        var caption = _window.SnapshotCopyButton.Content;
        var error = kind == "cancelled" ? (Exception)new OperationCanceledException("owned copy cancellation") :
            new InvalidOperationException("owned callback failure");
        var calls = 0;
        var actual = Assert.Catch(() => _window.CopySnapshot(text =>
        {
            Assert.That(text, Does.Contain(Strings.ConnectionsDetails));
            calls++;
            throw error;
        }));
        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(error));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(_window.SnapshotCopyButton.Content, Is.SameAs(caption));
            Assert.That(_window.SnapshotCopiedTimer.IsEnabled, Is.EqualTo(previousCopy));
        });
        _window.CopySnapshot(_ => calls++);
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(_window.SnapshotCopyButton.Content, Is.EqualTo(Strings.Copied));
    }

    [Test, Category("Integration")]
    public void ExternalCallbackFailuresWarnResetFeedbackShowTheNoticeAndAllowRetry(
        [Values("external", "com")] string kind,
        [Values(false, true)] bool previousCopy)
    {
        Seed();
        if (previousCopy) _window.CopySnapshot(_ => { });
        var error = kind == "com" ? new COMException("owned clipboard failure") : new ExternalException("owned clipboard failure");
        using var warnings = new StringWriter();
        using var listener = new TextWriterTraceListener(warnings);
        Trace.Listeners.Add(listener);
        try
        {
            WithNotice(() => _window.CopySnapshot(_ => throw error), notice =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(notice.Title, Is.EqualTo(Strings.CopySnapshot));
                    Assert.That(Descendants(notice).OfType<TextBlock>().Select(block => block.Text),
                        Does.Contain(Strings.TheClipboardIsUnavailableTryCopyAgain));
                    Assert.That(_window.SnapshotCopyButton.Content, Is.EqualTo(Strings.CopySnapshot));
                    Assert.That(_window.SnapshotCopiedTimer.IsEnabled, Is.False);
                    Assert.That(_window.SnapshotCopyButton.GetBindingExpression(ContentControl.ContentProperty), Is.Not.Null);
                });
            });
            listener.Flush();
            Assert.That(warnings.ToString(), Does.Contain("Could not copy snapshot: owned clipboard failure"));
        }
        finally { Trace.Listeners.Remove(listener); }
        var calls = 0;
        _window.CopySnapshot(_ => calls++);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(_window.SnapshotCopyButton.Content, Is.EqualTo(Strings.Copied));
    }

    [TestCase("search"), TestCase("path-search"), TestCase("apps"), TestCase("pinned-only"), TestCase("trigger")]
    public void ConnectionSectionUsesSearchAndAppFiltersKeepsUnpinnedRowsAndIncludesTheTrigger(string mode)
    {
        var sample = Seed("Connections", "traffic");
        _window.ConnectionView.Load(new ConnectionListSettings(AppsOnly: mode == "apps", PinnedOnly: mode == "pinned-only", TrafficSeconds: 0));
        _window.ConnectionView.Search.Text = mode switch
        {
            "search" => "Historic app", "path-search" => "historic.exe", "trigger" => "unmatched-search", _ => ""
        };
        if (mode == "trigger")
            _window.AddCaptureMarkerForFixture(new MainWindow.CaptureMarker(
                new CaptureFired(new(CaptureMetric.Process, true, 90, 1), sample, sample, 95, [95], Owner),
                Guid.Parse("00000000-0000-0000-0000-000000000002"), "owned trigger"));
        string result = "";

        _window.CopySnapshot(text => result = ConnectionSection(text));

        Assert.Multiple(() =>
        {
            Assert.That(result.Contains("127.0.0.1:41000", StringComparison.Ordinal), Is.EqualTo(mode != "path-search"));
            Assert.That(result.Contains("127.0.0.1:41001", StringComparison.Ordinal), Is.EqualTo(mode == "pinned-only"));
            if (mode == "trigger") Assert.That(result, Does.Contain(Strings.CaptureReportExtraTrigger));
            Assert.That(_window.SnapshotCopyButton.Content, Is.EqualTo(Strings.Copied));
        });
    }

    [Test]
    public void ConnectionReportUsesItsCaptureLimitInsteadOfTheViewsTopCount()
    {
        var owners = Enumerable.Range(0, 12).Select(index => Owner with
        {
            ProcessId = 987700 + index, CreationTicks = 100 + index,
            Name = $"owner{index:00}", AppName = $"Owner {index:00}", ImagePath = $@"C:\fixture\owner{index:00}.exe"
        }).ToArray();
        Seed("Connections", "endpoints", owners);
        _window.ConnectionView.Load(new ConnectionListSettings(TopCount: 5, Sort: ConnectionSort.Tcp, TrafficSeconds: 0));
        string result = "";
        _window.CopySnapshot(text => result = ConnectionSection(text));
        var endpoints = result.Split(Environment.NewLine).Where(line => line.Contains("\tTCP\t", StringComparison.Ordinal)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(_window.ConnectionView.Settings.TopCount, Is.EqualTo(5));
            Assert.That(endpoints, Has.Length.EqualTo(10));
            Assert.That(result, Does.Contain("127.0.0.1:41009").And.Not.Contain("127.0.0.1:41010"));
        });
    }

    [TestCase("tcp"), TestCase("receive")]
    public void ConnectionOrderingAndTrafficPeriodFollowTheCurrentViewSettings(string choice)
    {
        var sort = choice == "tcp" ? ConnectionSort.Tcp : ConnectionSort.Receive;
        Seed("Connections", "ranked");
        _window.ConnectionView.Load(new ConnectionListSettings(Sort: sort, TrafficSeconds: 60));
        string result = "";
        _window.CopySnapshot(text => result = ConnectionSection(text));
        var first = result.IndexOf("127.0.0.1:41000", StringComparison.Ordinal);
        var second = result.IndexOf("127.0.0.1:41001", StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.GreaterThanOrEqualTo(0));
            Assert.That(second, Is.GreaterThanOrEqualTo(0));
            Assert.That(first < second, Is.EqualTo(sort == ConnectionSort.Tcp), "TCP count and receive-rate orders differ in this fixture.");
            Assert.That(result, Does.Contain(Strings.ConnectionsTrafficPeriod + ": " + Strings.Format(Strings.GaugePeakWindowMinutes, 1)));
        });
    }

    private static string ConnectionSection(string text)
    {
        var index = text.IndexOf(Strings.ConnectionsDetails, StringComparison.Ordinal);
        Assert.That(index, Is.GreaterThanOrEqualTo(0));
        return text[index..];
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Descendants(child)) yield return nested;
    }

    private static void WithNotice(Action command, Action<MessageDialog> inspect)
    {
        var existing = Application.Current.Windows.Cast<Window>().ToHashSet();
        Exception? failure = null;
        var seen = 0;
        var operation = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var windows = Application.Current.Windows.Cast<Window>().Where(window => !existing.Contains(window)).ToArray();
            try
            {
                Assert.That(windows, Has.Length.EqualTo(1));
                Assert.That(windows[0], Is.TypeOf<MessageDialog>());
                seen++;
                inspect((MessageDialog)windows[0]);
            }
            catch (Exception ex) { failure = ex; }
            finally { foreach (var window in windows) window.Close(); }
        }));
        try { command(); }
        finally
        {
            operation.Abort();
            foreach (var window in Application.Current.Windows.Cast<Window>().Where(window => !existing.Contains(window)).ToArray()) window.Close();
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.That(seen, Is.EqualTo(1));
    }
}
