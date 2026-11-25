using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class GuideTargetCharacterizationTests
{
    private readonly List<object> _addedResources = [];
    private MainWindow _window = null!;

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) { app.Resources[key] = new Style(); _addedResources.Add(key); }
        if (!app.Resources.Contains("TitleBarButtonText"))
        { app.Resources["TitleBarButtonText"] = new DataTemplate(); _addedResources.Add("TitleBarButtonText"); }
        if (!app.Resources.Contains(typeof(Button)))
        { app.Resources[typeof(Button)] = new Style(typeof(Button)); _addedResources.Add(typeof(Button)); }
        _window = new MainWindow(true);
        _window.Loaded -= _window.OnLoaded;
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
        foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
        _addedResources.Clear();
    }

    private static IEnumerable<TestCaseData> KnownTargets()
    {
        foreach (var (topic, name) in new[]
                 {
                     ("Charts", "ChartTypeSelector"), ("Processes", "ProcessesTab"), ("Services", "ServicesTab"),
                     ("Usage", "UsageTab"), ("Connections", "ConnectionsTab"), ("Export", "SnapshotModeButton"),
                     ("Automatic", "CapturesButton"), ("Compact", "CompactViewButton"), ("Walkthrough", "ChartBody")
                 })
        {
            yield return new TestCaseData(topic, name, false).SetName("StoredTopicKeepsTarget_" + topic + "_Normal");
            yield return new TestCaseData(topic, name, true).SetName("StoredTopicKeepsTarget_" + topic + "_HiddenDisabled");
        }
    }

    [TestCaseSource(nameof(KnownTargets))]
    public void StoredTopicsReturnTheExistingControlWithoutNavigation(string topic, string name, bool hidden)
    {
        var target = _window.FindName(name) as FrameworkElement;
        Assert.That(target, Is.Not.Null, "Expected control must exist in the actual window namescope");
        if (hidden)
        {
            _window.SetActivityPanelCollapsed(true);
            _window.SetChartPanelCollapsed(true);
            _window.SetCompactView(true);
            target!.Visibility = Visibility.Collapsed;
            target.IsEnabled = false;
        }
        var historical = new MonitorSample(DateTimeOffset.UnixEpoch, 20, [], 0);
        _window.SelectedSample = historical;
        var preferences = _window.Preferences;
        var focus = Keyboard.FocusedElement;
        var checkedTabs = new[] { _window.ProcessesTab.IsChecked, _window.ServicesTab.IsChecked,
            _window.UsageTab.IsChecked, _window.ConnectionsTab.IsChecked };

        var result = _window.GuideTarget(topic);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.SameAs(target));
            Assert.That(_window.GuideTarget(topic), Is.SameAs(result));
            Assert.That(_window.Preferences, Is.SameAs(preferences));
            Assert.That(_window.SelectedSample, Is.SameAs(historical));
            Assert.That(Keyboard.FocusedElement, Is.SameAs(focus));
            Assert.That(new[] { _window.ProcessesTab.IsChecked, _window.ServicesTab.IsChecked,
                _window.UsageTab.IsChecked, _window.ConnectionsTab.IsChecked }, Is.EqualTo(checkedTabs));
            Assert.That(_window.IsVisible, Is.False);
            Assert.That(new WindowInteropHelper(_window).Handle, Is.EqualTo(IntPtr.Zero));
            if (hidden)
            {
                Assert.That(result!.Visibility, Is.EqualTo(Visibility.Collapsed));
                Assert.That(result.IsEnabled, Is.False);
            }
        });
    }

    private static IEnumerable<TestCaseData> UnknownTopics()
    {
        foreach (var (id, value) in new (string, string?)[]
                 {
                     ("Null", null), ("Empty", ""), ("Blank", " "), ("Lowercase", "charts"),
                     ("LeadingSpace", " Charts"), ("TrailingSpace", "Charts "), ("Typo", "Chartz"),
                     ("Prefix", "ChartsAndMore"), ("OtherButton", "Settings"), ("EmbeddedNull", "Charts\0"),
                     ("LoneHighSurrogate", "\uD800"), ("FormFeed", "\f"), ("Fullwidth", "Ｃｈａｒｔｓ"),
                     ("LeadingTab", "\tCharts"), ("TrailingNewline", "Charts\n")
                 })
            yield return new TestCaseData(new object?[] { value }).SetName("UnknownTopicReturnsNoTarget_" + id);
        foreach (var topic in new[] { "Charts", "Processes", "Services", "Usage", "Connections", "Export",
                     "Automatic", "Compact", "Walkthrough" })
            yield return new TestCaseData(topic.ToUpperInvariant()).SetName("UnknownTopicReturnsNoTarget_Upper_" + topic);
    }

    [TestCaseSource(nameof(UnknownTopics))]
    public void UnknownTopicsReturnNoTargetWithoutNormalizingOrCreatingAWindow(string? topic)
    {
        var preferences = _window.Preferences;
        Assert.Multiple(() =>
        {
            Assert.That(_window.GuideTarget(topic!), Is.Null);
            Assert.That(_window.Preferences, Is.SameAs(preferences));
            Assert.That(_window.IsVisible, Is.False);
            Assert.That(new WindowInteropHelper(_window).Handle, Is.EqualTo(IntPtr.Zero));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("missing")]
    public void UnknownShowMeRequestsLeaveCollapsedPanelsCompactModeAndHistoryUntouched(string? topic)
    {
        _window.SetActivityPanelCollapsed(true);
        _window.SetChartPanelCollapsed(true);
        _window.SetCompactView(true);
        var historical = new MonitorSample(DateTimeOffset.UnixEpoch, 20, [], 0);
        _window.SelectedSample = historical;
        var preferences = _window.Preferences;
        var focus = Keyboard.FocusedElement;

        _window.ShowGuideTarget(topic!);

        Assert.Multiple(() =>
        {
            Assert.That(_window.Preferences, Is.SameAs(preferences));
            Assert.That(_window.SelectedSample, Is.SameAs(historical));
            Assert.That(Keyboard.FocusedElement, Is.SameAs(focus));
            Assert.That(_window.CompactMode, Is.True);
            Assert.That(_window.Preferences.ActivityPanelCollapsed, Is.True);
            Assert.That(_window.Preferences.ChartPanelCollapsed, Is.True);
            Assert.That(_window.IsVisible, Is.False);
            Assert.That(new WindowInteropHelper(_window).Handle, Is.EqualTo(IntPtr.Zero));
        });
    }
}
