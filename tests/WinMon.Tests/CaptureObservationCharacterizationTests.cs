using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;
using System.Windows.Threading;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration")]
public sealed class CaptureObservationCharacterizationTests
{
    private readonly Dictionary<object, object?> _resources = [];
    private MainWindow _window = null!;
    private string? _temporaryFolder;
    private SynchronizationContext? _previousContext;

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (System.Collections.DictionaryEntry entry in app.Resources) _resources.Add(entry.Key, entry.Value);
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) app.Resources[key] = new Style();
        if (!app.Resources.Contains("TitleBarButtonText"))
            app.Resources["TitleBarButtonText"] = new DataTemplate();
        if (!app.Resources.Contains(typeof(Button)))
            app.Resources[typeof(Button)] = new Style(typeof(Button));
        _window = new MainWindow(true);
        _window.Loaded -= _window.OnLoaded;
        _window.ApplyTheme(false, false);
        _previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        _temporaryFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinMon.CaptureObservation." + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_temporaryFolder);
        _window.Preferences = _window.Preferences with { AutoCapture = new WinMon.Preferences.AutoCapturePreferences { Enabled = true, Notify = false, Folder = System.IO.Path.Combine(_temporaryFolder, "reports"), CooldownMinutes = 1, Rules = [new(CaptureMetric.Cpu, true, 90, 3), new(CaptureMetric.Memory, false, 90, 3)] } };
    }

    [TearDown]
    public void TearDown()
    {
        WaitForReports();
        if (_window is not null)
        {
            foreach (var view in new FrameworkElement[] { _window.UsageView, _window.Processes, _window.ServiceView, _window.ConnectionView })
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            _window.CloseForFixture();
        }
        var resources = Application.Current.Resources;
        foreach (var key in resources.Keys.Cast<object>().Where(key => !_resources.ContainsKey(key)).ToArray())
            resources.Remove(key);
        foreach (var (key, value) in _resources) resources[key] = value;
        _resources.Clear();
        SynchronizationContext.SetSynchronizationContext(_previousContext);
        if (_temporaryFolder is not null)
        {
            System.IO.Directory.Delete(_temporaryFolder, recursive: true);
            _temporaryFolder = null;
        }
    }

    private void WaitForReports()
    {
        if (_window is null || _window.CaptureMarkers.All(marker => marker.ReportPath is not null || marker.WriteFailed)) return;
        var frame = new DispatcherFrame();
        var timedOut = false;
        DispatcherHookEventHandler completed = (_, _) =>
        {
            if (_window.CaptureMarkers.All(marker => marker.ReportPath is not null || marker.WriteFailed)) frame.Continue = false;
        };
        var deadline = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(10) };
        deadline.Tick += (_, _) => { timedOut = true; frame.Continue = false; };
        _window.Dispatcher.Hooks.OperationCompleted += completed;
        deadline.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { deadline.Stop(); _window.Dispatcher.Hooks.OperationCompleted -= completed; }
        Assert.That(timedOut, Is.False, "Owned capture writes did not complete on their dispatcher");
    }

    private static MonitorSample Sample(int second, double? cpu = 95, bool gap = false) =>
        new(DateTimeOffset.Parse("2026-10-24T14:03:12Z").AddSeconds(second), cpu, [], 0, gap,
            (1000L + second) * System.Diagnostics.Stopwatch.Frequency)
        { IntervalSeconds = 1 };

    private MonitorSample Feed(int second, double? cpu = 95, bool gap = false)
    {
        var sample = Sample(second, cpu, gap);
        _window.Session.Record(sample);
        _window.ObserveForCaptures(sample);
        WaitForReports();
        return sample;
    }

    private void Duration(int seconds) => _window.Preferences = _window.Preferences with
    {
        AutoCapture = _window.Preferences.AutoCapture with
        {
            Rules = [new(CaptureMetric.Cpu, true, 90, seconds), new(CaptureMetric.Memory, false, 90, seconds)]
        }
    };

    private void AssertReports(int count)
    {
        WaitForReports();
        Assert.That(_window.CaptureMarkers.Count, Is.EqualTo(count));
        foreach (var marker in _window.CaptureMarkers)
        {
            Assert.Multiple(() =>
            {
                Assert.That(marker.WriteFailed, Is.False);
                Assert.That(marker.ReportPath, Is.Not.Null);
                Assert.That(System.IO.File.Exists(marker.ReportPath), Is.True);
                Assert.That(System.IO.Path.GetFullPath(marker.ReportPath!).StartsWith(
                    System.IO.Path.GetFullPath(_temporaryFolder!) + System.IO.Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase), Is.True);
                Assert.That(System.IO.File.ReadLines(marker.ReportPath!).First(), Is.EqualTo(CaptureReports.FirstLine(marker.ReportId)));
            });
        }
        Assert.That(_window.IsVisible, Is.False);
        Assert.That(new WindowInteropHelper(_window).Handle, Is.EqualTo(IntPtr.Zero));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DisabledObservationIgnoresNullAndHighSamplesWithoutCreatingReports(bool nullSample)
    {
        _window.Preferences = _window.Preferences with { AutoCapture = _window.Preferences.AutoCapture with { Enabled = false } };

        Assert.DoesNotThrow(() => _window.ObserveForCaptures(nullSample ? null! : Sample(0)));

        Assert.Multiple(() =>
        {
            Assert.That(_window.CaptureMarkers, Is.Empty);
            Assert.That(System.IO.Directory.Exists(_window.Preferences.AutoCapture.Folder), Is.False);
        });
    }

    [Test]
    public void EnabledNullObservationPropagatesTheExistingArgumentExceptionBeforeWriting()
    {
        Assert.That(Assert.Throws<ArgumentNullException>(() => _window.ObserveForCaptures(null!))!.ParamName, Is.EqualTo("full"));
        Assert.That(_window.CaptureMarkers, Is.Empty);
        Assert.That(System.IO.Directory.Exists(_window.Preferences.AutoCapture.Folder), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EmptyOrDisabledRuleSetsNeverCreateAReport(bool includeDisabled)
    {
        _window.Preferences = _window.Preferences with
        {
            AutoCapture = _window.Preferences.AutoCapture with { Rules = includeDisabled ? [new(CaptureMetric.Cpu, false, 90, 1)] : [] }
        };

        for (var second = 0; second < 4; second++) Feed(second, 99);

        Assert.That(_window.CaptureMarkers, Is.Empty);
        Assert.That(System.IO.Directory.Exists(_window.Preferences.AutoCapture.Folder), Is.False);
    }

    [TestCase("Preferences")]
    [TestCase("EqualRules")]
    [TestCase("Folder")]
    public void ValueEqualSettingsKeepThePartialRunAndUseTheCurrentReportFolder(string change)
    {
        var first = Feed(0, 95);
        var peak = Feed(1, 99);
        Assert.That(_window.CaptureMarkers, Is.Empty);
        var original = _window.Preferences.AutoCapture;
        _window.Preferences = _window.Preferences with
        {
            ChartRangeSeconds = 600,
            AutoCapture = change switch
            {
                "EqualRules" => original with { Rules = original.Rules.Select(rule => rule with { }).ToArray() },
                "Folder" => original with { Folder = System.IO.Path.Combine(_temporaryFolder!, "moved") },
                _ => original with { }
            }
        };

        var fired = Feed(2, 96);

        AssertReports(1);
        var marker = _window.CaptureMarkers.Single();
        Assert.Multiple(() =>
        {
            Assert.That(marker.Fired.Fired, Is.SameAs(fired));
            Assert.That(marker.Fired.Peak, Is.SameAs(peak));
            Assert.That(marker.Fired.PeakValue, Is.EqualTo(99));
            Assert.That(marker.Fired.Run, Is.EqualTo(new[] { first.TotalCpuPercent!.Value, 99d, 96 }));
            Assert.That(System.IO.Path.GetDirectoryName(marker.ReportPath), Is.EqualTo(_window.Preferences.AutoCapture.Folder).IgnoreCase);
        });
    }

    [Test]
    public void EqualRuleClonesPreserveArmingAndCooldownUntilTheExactBoundary()
    {
        Duration(1);
        Feed(0);
        AssertReports(1);
        var settings = _window.Preferences.AutoCapture;
        _window.Preferences = _window.Preferences with { AutoCapture = settings with { Rules = settings.Rules.Select(rule => rule with { }).ToArray() } };
        Feed(1, 96);
        Assert.That(_window.CaptureMarkers.Count, Is.EqualTo(1), "Saving equal rules must not re-arm a high run");
        Feed(2, 0);
        _window.Preferences = _window.Preferences with { AutoCapture = _window.Preferences.AutoCapture with { } };
        Feed(3, 97);
        Assert.That(_window.CaptureMarkers.Count, Is.EqualTo(1), "Saving equal rules must not clear cooldown");
        for (var second = 4; second < 60; second++) Feed(second, 0);

        var secondFiring = Feed(60, 98);

        AssertReports(2);
        Assert.That(_window.CaptureMarkers[1].Fired.Fired, Is.SameAs(secondFiring));
    }

    [TestCase("Cooldown")]
    [TestCase("Threshold")]
    [TestCase("Duration")]
    [TestCase("DisabledRule")]
    public void ChangedRuleValuesOrCooldownStartANewPendingRun(string change)
    {
        Feed(0, 95);
        Feed(1, 96);
        var settings = _window.Preferences.AutoCapture;
        _window.Preferences = _window.Preferences with
        {
            AutoCapture = settings with
            {
                CooldownMinutes = change == "Cooldown" ? 2 : 1,
                Rules = settings.Rules.Select(rule => change switch
                {
                    "Threshold" when rule.Metric == CaptureMetric.Cpu => rule with { Threshold = 91 },
                    "Duration" when rule.Metric == CaptureMetric.Cpu => rule with { Seconds = 2 },
                    "DisabledRule" when rule.Metric == CaptureMetric.Memory => rule with { Threshold = 80 },
                    _ => rule with { }
                }).ToArray()
            }
        };
        var required = change == "Duration" ? 2 : 3;
        for (var index = 0; index < required; index++)
        {
            Feed(2 + index, 97 + index);
            Assert.That(_window.CaptureMarkers.Count, Is.EqualTo(index == required - 1 ? 1 : 0));
        }

        AssertReports(1);
        Assert.That(_window.CaptureMarkers[0].Fired.Run, Is.EqualTo(new[] { 97d, 98, 99 }.Take(required)));
    }

    [Test]
    public void AnObservedDisableBreaksTheRunBeforeReenabling()
    {
        Duration(2);
        Feed(0, 95);
        _window.Preferences = _window.Preferences with { AutoCapture = _window.Preferences.AutoCapture with { Enabled = false } };
        Feed(1, 99);
        _window.Preferences = _window.Preferences with { AutoCapture = _window.Preferences.AutoCapture with { Enabled = true } };
        Feed(2, 97);
        Assert.That(_window.CaptureMarkers, Is.Empty);

        Feed(3, 98);

        AssertReports(1);
        Assert.That(_window.CaptureMarkers[0].Fired.Run, Is.EqualTo(new[] { 97d, 98 }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingReadingsAndGapsBreakPendingRuns(bool gap)
    {
        Duration(2);
        Feed(0);
        Feed(1, gap ? 99 : null, gap);
        Feed(2, 96);
        Assert.That(_window.CaptureMarkers, Is.Empty);

        Feed(3, 97);

        AssertReports(1);
        Assert.That(_window.CaptureMarkers[0].Fired.Run, Is.EqualTo(new[] { 96d, 97 }));
    }

    [Test]
    public void SimultaneousMachineRulesKeepTheirOrderAndWriteDistinctOwnedReports()
    {
        _window.Preferences = _window.Preferences with
        {
            AutoCapture = _window.Preferences.AutoCapture with { Rules = [new(CaptureMetric.Cpu, true, 90, 1), new(CaptureMetric.Memory, true, 90, 1)] }
        };
        var full = Sample(0) with { Resources = new ResourceSample { TotalMemoryBytes = 100, UsedMemoryBytes = 95 } };
        _window.Session.Record(full);

        _window.ObserveForCaptures(full);
        WaitForReports();

        AssertReports(2);
        Assert.Multiple(() =>
        {
            Assert.That(_window.CaptureMarkers.Select(marker => marker.Fired.Rule.Metric), Is.EqualTo(new[] { CaptureMetric.Cpu, CaptureMetric.Memory }));
            Assert.That(_window.CaptureMarkers.Select(marker => marker.ReportId).Distinct().Count(), Is.EqualTo(2));
            Assert.That(_window.CaptureMarkers.Select(marker => marker.ReportPath).Distinct().Count(), Is.EqualTo(2));
            Assert.That(CaptureReports.Find(_window.Preferences.AutoCapture.Folder!).Count, Is.EqualTo(2));
        });
    }

    private string BlockFolder(string name)
    {
        var path = System.IO.Path.Combine(_temporaryFolder!, name);
        System.IO.File.WriteAllText(path, "owned blocker");
        _window.Preferences = _window.Preferences with { AutoCapture = _window.Preferences.AutoCapture with { Folder = path } };
        _window.UpdateFooter.Visibility = Visibility.Collapsed;
        _window.UpdateNowButton.Visibility = Visibility.Collapsed;
        return path;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FailedWritesKeepTheirCaptureAndOnlyShowANoticeWhileOpen(bool closing)
    {
        Duration(1);
        var blocked = BlockFolder("blocked");
        _window.IsClosing = closing;

        Feed(0);

        var marker = _window.CaptureMarkers.Single();
        Assert.Multiple(() =>
        {
            Assert.That(marker.WriteFailed, Is.True);
            Assert.That(marker.ReportPath, Is.Null);
            Assert.That(System.IO.File.ReadAllText(blocked), Is.EqualTo("owned blocker"));
            Assert.That(_window.UpdateFooter.Visibility, Is.EqualTo(closing ? Visibility.Collapsed : Visibility.Visible));
            Assert.That(_window.OwnedWindows.Count, Is.Zero);
            if (!closing) Assert.That(_window.UpdateStatusText.Text, Is.EqualTo(Strings.Format(Strings.AutoCaptureWriteFailed, blocked)));
        });
    }

    [Test]
    public void RepeatedWriteFailuresDoNotRearmOrReplaceTheFirstErrorNotice()
    {
        Duration(1);
        var first = BlockFolder("first-blocked");
        Feed(0);
        var notice = _window.UpdateStatusText.Text;
        Assert.That(notice, Is.EqualTo(Strings.Format(Strings.AutoCaptureWriteFailed, first)));
        var second = System.IO.Path.Combine(_temporaryFolder!, "second-blocked");
        System.IO.File.WriteAllText(second, "second blocker");
        _window.Preferences = _window.Preferences with { AutoCapture = _window.Preferences.AutoCapture with { Folder = second } };
        Feed(1, 96);
        Assert.That(_window.CaptureMarkers.Count, Is.EqualTo(1));
        for (var time = 2; time < 60; time++) Feed(time, 0);

        Feed(60, 97);

        Assert.Multiple(() =>
        {
            Assert.That(_window.CaptureMarkers.Count, Is.EqualTo(2));
            Assert.That(_window.CaptureMarkers.All(marker => marker.WriteFailed && marker.ReportPath is null), Is.True);
            Assert.That(_window.UpdateStatusText.Text, Is.EqualTo(notice));
            Assert.That(System.IO.File.ReadAllText(second), Is.EqualTo("second blocker"));
        });
    }
}
