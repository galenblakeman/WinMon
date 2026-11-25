using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class CapturePresentationCharacterizationTests
{
    private readonly Dictionary<object, object?> _resources = [];
    private MainWindow _window = null!;
    private string? _temporaryFolder;

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
    }

    [TearDown]
    public void TearDown()
    {
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
        if (_temporaryFolder is not null)
        {
            System.IO.Directory.Delete(_temporaryFolder, recursive: true);
            _temporaryFolder = null;
        }
    }

    [Test]
    public void EmptyCaptureStateClearsStaleMarkersAndKeepsDisabledMetadataUntouched(
        [Values(false, true)] bool enabled, [Values(false, true)] bool staleMarkers,
        [Values(false, true)] bool staleMetadata)
    {
        _window.Preferences = _window.Preferences with
        {
            AutoCapture = _window.Preferences.AutoCapture with { Enabled = enabled }
        };
        IReadOnlyList<ChartMarker> beforeMarkers = staleMarkers
            ? new[] { new ChartMarker(1, "CpuAccent", "old marker", false, new object()) }
            : Array.Empty<ChartMarker>();
        _window.OverallGraph.Markers = beforeMarkers;
        object content = staleMetadata ? new object() : new string(Strings.AutoCaptureShort.ToCharArray());
        object tooltip = staleMetadata ? new object() : new string(Strings.AutoCaptureNone.ToCharArray());
        var accessibleName = staleMetadata ? "owned stale name" : Strings.AutoCaptureNone;
        _window.CapturesButton.Content = content;
        _window.CapturesButton.ToolTip = tooltip;
        AutomationProperties.SetName(_window.CapturesButton, accessibleName);
        _window.CaptureBanner.Visibility = Visibility.Visible;

        _window.RefreshVisiblePresentation();

        Assert.Multiple(() =>
        {
            Assert.That(_window.CaptureMarkers, Is.Empty);
            Assert.That(_window.OverallGraph.Markers, Is.Empty);
            Assert.That(_window.CapturesButton.Visibility, Is.EqualTo(enabled ? Visibility.Visible : Visibility.Collapsed));
            Assert.That(_window.CaptureBanner.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(_window.CapturesButton.Content, enabled ? Is.EqualTo(Strings.AutoCaptureShort) : Is.SameAs(content));
            Assert.That(_window.CapturesButton.ToolTip, enabled ? Is.EqualTo(Strings.AutoCaptureNone) : Is.SameAs(tooltip));
            Assert.That(AutomationProperties.GetName(_window.CapturesButton), Is.EqualTo(enabled ? Strings.AutoCaptureNone : accessibleName));
            if (!staleMarkers && !enabled) Assert.That(_window.OverallGraph.Markers, Is.SameAs(beforeMarkers));
            if (!staleMetadata)
            {
                Assert.That(_window.CapturesButton.Content, Is.SameAs(content));
                Assert.That(_window.CapturesButton.ToolTip, Is.SameAs(tooltip));
            }
        });
        var refreshedContent = _window.CapturesButton.Content;
        var refreshedTooltip = _window.CapturesButton.ToolTip;

        _window.RefreshVisiblePresentation();

        Assert.Multiple(() =>
        {
            Assert.That(_window.CapturesButton.Content, Is.SameAs(refreshedContent));
            Assert.That(_window.CapturesButton.ToolTip, Is.SameAs(refreshedTooltip));
        });
    }

    private static MonitorSample Sample(int second) => new(DateTimeOffset.UnixEpoch.AddSeconds(second), 20, [], 0)
    {
        MonotonicTimestamp = (1000L + second) * System.Diagnostics.Stopwatch.Frequency,
        IntervalSeconds = 1
    };

    private static MainWindow.CaptureMarker Marker(int fired, int peak, string rule = "owned CPU rule") =>
        new(new CaptureFired(new CaptureRuleSetting(CaptureMetric.Cpu, true, 90, 1),
                Sample(fired), Sample(peak), 97, [95, 97]),
            Guid.Parse("00000000-0000-0000-0000-000000000001"), rule);

    private void RecordHistory()
    {
        _window.Session.Record(Sample(10));
        _window.Session.Record(Sample(20));
    }

    [Test]
    public void MarkersExpireStrictlyBeforeTheOldestFiringMoment(
        [Values(9, 10, 11)] int fired, [Values(false, true)] bool enabled)
    {
        RecordHistory();
        _window.Preferences = _window.Preferences with
        {
            AutoCapture = _window.Preferences.AutoCapture with { Enabled = enabled }
        };
        var marker = Marker(fired, 10);
        _window.AddCaptureMarkerForFixture(marker);
        _window.SelectedSample = Sample(10);

        _window.RefreshVisiblePresentation();

        var retained = fired >= 10;
        Assert.Multiple(() =>
        {
            Assert.That(_window.CaptureMarkers.Count, Is.EqualTo(retained ? 1 : 0));
            Assert.That(_window.OverallGraph.Markers.Count, Is.EqualTo(retained ? 1 : 0));
            Assert.That(_window.CaptureBanner.Visibility, Is.EqualTo(retained ? Visibility.Visible : Visibility.Collapsed));
            Assert.That(_window.CapturesButton.Visibility, Is.EqualTo(enabled || retained ? Visibility.Visible : Visibility.Collapsed));
            Assert.That(_window.CapturesButton.Content, Is.EqualTo(retained ? Strings.Format(Strings.AutoCaptureShortCount, 1) : Strings.AutoCaptureShort));
            if (retained) Assert.That(_window.CaptureMarkers[0], Is.SameAs(marker));
        });
    }

    [Test]
    public void EmptyHistoryPrunesMarkersEvenWhenTheirPeakWasSelected([Values(false, true)] bool enabled)
    {
        _window.Preferences = _window.Preferences with
        {
            AutoCapture = _window.Preferences.AutoCapture with { Enabled = enabled }
        };
        _window.AddCaptureMarkerForFixture(Marker(20, 10));
        _window.SelectedSample = Sample(10);

        _window.RefreshVisiblePresentation();

        Assert.Multiple(() =>
        {
            Assert.That(_window.CaptureMarkers, Is.Empty);
            Assert.That(_window.OverallGraph.Markers, Is.Empty);
            Assert.That(_window.CaptureBanner.Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [TestCase(9, false)]
    [TestCase(10, true)]
    [TestCase(11, true)]
    [TestCase(21, true)]
    public void MarkerTooltipUsesPeakAgeWithoutRequiringAnExactRecordedSample(int peak, bool hint)
    {
        RecordHistory();
        var marker = Marker(20, peak);
        _window.AddCaptureMarkerForFixture(marker);

        _window.RefreshVisiblePresentation();

        var graph = _window.OverallGraph.Markers.Single();
        Assert.Multiple(() =>
        {
            Assert.That(graph.Tag, Is.SameAs(marker));
            Assert.That(graph.Moment, Is.EqualTo(marker.FiredKey.Moment));
            Assert.That(graph.BrushKey, Is.EqualTo("CpuAccent"));
            Assert.That(graph.Selected, Is.False);
            Assert.That(graph.Tooltip, Does.StartWith("owned CPU rule\n"));
            Assert.That(graph.Tooltip, Does.EndWith("\n" + (hint ? Strings.AutoCaptureMarkerHint : Strings.AutoCapturePeakGone)));
            Assert.That(graph.Tooltip, Is.EqualTo("owned CPU rule\n" + Strings.Format(Strings.AutoCaptureMarkerTooltip,
                marker.FiredKey.Timestamp, Strings.Format(Strings.PercentOneDecimal, 97), marker.Peak.Timestamp) +
                "\n" + (hint ? Strings.AutoCaptureMarkerHint : Strings.AutoCapturePeakGone)));
            Assert.That(_window.CaptureBanner.Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [TestCase("Live")]
    [TestCase("Exact")]
    [TestCase("DifferentTimestamp")]
    [TestCase("DifferentMonotonic")]
    public void SelectionRequiresTheWholeSampleKeyAndChoosesTheLastMatchingMarker(string selection)
    {
        RecordHistory();
        var first = Marker(11, 10, "first rule");
        var last = Marker(12, 10, "last rule");
        _window.AddCaptureMarkerForFixture(first);
        _window.AddCaptureMarkerForFixture(last);
        var sample = Sample(10);
        _window.SelectedSample = selection switch
        {
            "Live" => null,
            "DifferentTimestamp" => sample with { Timestamp = sample.Timestamp.AddTicks(1) },
            "DifferentMonotonic" => sample with { MonotonicTimestamp = sample.MonotonicTimestamp + 1 },
            _ => sample
        };

        _window.RefreshVisiblePresentation();

        Assert.Multiple(() =>
        {
            Assert.That(_window.OverallGraph.Markers.Select(marker => marker.Tag), Is.EqualTo(new[] { first, last }));
            Assert.That(_window.OverallGraph.Markers.Select(marker => marker.Selected), Is.EqualTo(new[] { false, selection == "Exact" }));
            Assert.That(_window.CaptureBanner.Visibility, Is.EqualTo(selection == "Exact" ? Visibility.Visible : Visibility.Collapsed));
            Assert.That(_window.CapturesButton.Content, Is.EqualTo(Strings.Format(Strings.AutoCaptureShortCount, 2)));
            Assert.That(_window.CapturesButton.ToolTip, Is.EqualTo(Strings.Format(Strings.AutoCaptureCount, 2)));
            Assert.That(AutomationProperties.GetName(_window.CapturesButton), Is.EqualTo(Strings.Format(Strings.AutoCaptureCount, 2)));
            if (selection == "Exact")
            {
                Assert.That(_window.CaptureBannerText.Text, Is.EqualTo(Strings.Format(Strings.AutoCaptureBanner, "last rule")));
                Assert.That(_window.CaptureBannerText.ToolTip, Is.EqualTo(_window.CaptureBannerText.Text));
                Assert.That(_window.CaptureBannerMarker.Fill, Is.SameAs(_window.FindResource("CpuAccent")));
            }
        });
    }

    [TestCase("Pending", false)]
    [TestCase("Failed", false)]
    [TestCase("Missing", false)]
    [TestCase("MissingFailed", false)]
    [TestCase("Exists", true)]
    [TestCase("ExistsFailed", true)]
    [TestCase("Empty", false)]
    [TestCase("Directory", false)]
    public void SelectedReportAvailabilityPreservesFailureAndPendingPrecedence(string state, bool exists)
    {
        RecordHistory();
        _temporaryFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinMon.CapturePresentation." + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_temporaryFolder);
        var path = System.IO.Path.Combine(_temporaryFolder, "owned-report.txt");
        if (exists) System.IO.File.WriteAllText(path, "owned report");
        var marker = Marker(20, 10);
        marker.WriteFailed = state is "Failed" or "MissingFailed" or "ExistsFailed";
        marker.ReportPath = state switch
        {
            "Pending" or "Failed" => null,
            "Empty" => "",
            "Directory" => _temporaryFolder,
            _ => path
        };
        _window.AddCaptureMarkerForFixture(marker);
        _window.SelectedSample = Sample(10);

        _window.RefreshVisiblePresentation();

        var expected = exists ? path : marker.WriteFailed ? Strings.AutoCaptureReportFailed :
            state == "Pending" ? Strings.AutoCaptureReportPending : Strings.AutoCaptureReportMissing;
        Assert.Multiple(() =>
        {
            Assert.That(_window.OpenReportButton.IsEnabled, Is.EqualTo(exists));
            Assert.That(_window.OpenReportButton.ToolTip, Is.EqualTo(expected));
            Assert.That(_window.CaptureBanner.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(_window.OwnedWindows.Count, Is.Zero);
        });
    }

    [Test]
    public void SelectedCaptureTracksReportCompletionAndRemovalWithoutReplacingHistory()
    {
        RecordHistory();
        var marker = Marker(20, 10);
        var selected = Sample(10);
        _window.AddCaptureMarkerForFixture(marker);
        _window.SelectedSample = selected;
        _window.RefreshVisiblePresentation();
        Assert.That(_window.OpenReportButton.ToolTip, Is.EqualTo(Strings.AutoCaptureReportPending));

        _temporaryFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinMon.CapturePresentation." + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_temporaryFolder);
        var path = System.IO.Path.Combine(_temporaryFolder, "completed.txt");
        System.IO.File.WriteAllText(path, "owned report");
        marker.ReportPath = path;
        _window.RefreshVisiblePresentation();
        Assert.Multiple(() =>
        {
            Assert.That(_window.OpenReportButton.IsEnabled, Is.True);
            Assert.That(_window.OpenReportButton.ToolTip, Is.EqualTo(path));
        });

        System.IO.File.Delete(path);
        _window.RefreshVisiblePresentation();
        Assert.Multiple(() =>
        {
            Assert.That(_window.OpenReportButton.IsEnabled, Is.False);
            Assert.That(_window.OpenReportButton.ToolTip, Is.EqualTo(Strings.AutoCaptureReportMissing));
        });

        marker.WriteFailed = true;
        _window.RefreshVisiblePresentation();
        Assert.Multiple(() =>
        {
            Assert.That(_window.OpenReportButton.ToolTip, Is.EqualTo(Strings.AutoCaptureReportFailed));
            Assert.That(_window.SelectedSample, Is.SameAs(selected));
            Assert.That(_window.CaptureBanner.Visibility, Is.EqualTo(Visibility.Visible));
        });

        _window.SelectedSample = null;
        _window.RefreshVisiblePresentation();
        Assert.Multiple(() =>
        {
            Assert.That(_window.CaptureBanner.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(_window.OverallGraph.Markers.Single().Selected, Is.False);
        });
    }
}
