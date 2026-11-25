using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using MonCore.Monitoring;
using NUnit.Framework;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class GuideNavigationCharacterizationTests
{
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
        if (!app.Resources.Contains("TitleBarButtonText"))
            app.Resources["TitleBarButtonText"] = new DataTemplate();
        if (!app.Resources.Contains(typeof(Button)))
            app.Resources[typeof(Button)] = new Style(typeof(Button));
        _window = new MainWindow(true);
        _window.Loaded -= _window.OnLoaded;
    }

    [TearDown]
    public void TearDown()
    {
        if (_window is not null)
        {
            foreach (var topic in Topics)
                _window.GuideTarget(topic)!.BeginAnimation(UIElement.OpacityProperty, null);
            foreach (var view in new FrameworkElement[] { _window.UsageView, _window.Processes, _window.ServiceView, _window.ConnectionView })
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            _window.CloseForFixture();
        }
        var resources = Application.Current.Resources;
        foreach (var key in resources.Keys.Cast<object>().Where(key => !_resources.ContainsKey(key)).ToArray())
            resources.Remove(key);
        foreach (var (key, value) in _resources) resources[key] = value;
        _resources.Clear();
    }

    private static readonly string[] Topics =
        ["Charts", "Processes", "Services", "Usage", "Connections", "Export", "Automatic", "Compact", "Walkthrough"];

    private static IEnumerable<TestCaseData> NavigationStates()
    {
        foreach (var topic in Topics)
        foreach (var compact in new[] { false, true })
        foreach (var collapsed in new[] { false, true })
            yield return new TestCaseData(topic, compact, collapsed)
                .SetName($"GuideNavigation_{topic}_Compact{compact}_Collapsed{collapsed}");
    }

    private RadioButton[] Tabs() =>
        [_window.ProcessesTab, _window.ServicesTab, _window.UsageTab, _window.ConnectionsTab];

    private int ActivityIndex(string topic) => topic switch
    {
        "Processes" => 0, "Services" => 1, "Usage" => 2, "Connections" => 3, _ => -1
    };

    [TestCaseSource(nameof(NavigationStates))]
    public void KnownTopicsRevealOnlyTheirPanelAndPreserveHistoryAndOpacityBase(
        string topic, bool compact, bool collapsed)
    {
        _window.ServicesTab.IsChecked = true;
        _window.SetActivityPanelCollapsed(collapsed);
        _window.SetChartPanelCollapsed(collapsed);
        _window.SetCompactView(compact);
        var historical = new MonitorSample(DateTimeOffset.UnixEpoch, 20, [], 0);
        _window.SelectedSample = historical;
        var target = _window.GuideTarget(topic)!;
        target.Opacity = 0.61;
        var animate = SystemParameters.ClientAreaAnimation;

        _window.ShowGuideTarget(topic);

        var activityIndex = ActivityIndex(topic);
        var expectedTab = activityIndex < 0 ? 1 : activityIndex;
        Assert.Multiple(() =>
        {
            Assert.That(_window.CompactMode, Is.EqualTo(topic == "Compact" && compact));
            Assert.That(_window.Preferences.CompactMode, Is.EqualTo(_window.CompactMode));
            Assert.That(_window.Preferences.ActivityPanelCollapsed, Is.EqualTo(activityIndex < 0 && collapsed));
            Assert.That(_window.Preferences.ChartPanelCollapsed,
                Is.EqualTo(topic is not ("Charts" or "Walkthrough") && collapsed));
            Assert.That(Tabs().Select(tab => tab.IsChecked == true),
                Is.EqualTo(Enumerable.Range(0, 4).Select(index => index == expectedTab)));
            Assert.That(_window.Preferences.SelectedActivityTab, Is.EqualTo(expectedTab));
            Assert.That(_window.SelectedSample, Is.SameAs(historical));
            Assert.That(target.GetAnimationBaseValue(UIElement.OpacityProperty), Is.EqualTo(0.61));
            Assert.That(DependencyPropertyHelper.GetValueSource(target, UIElement.OpacityProperty).IsAnimated,
                Is.EqualTo(animate));
            Assert.That(_window.IsVisible, Is.False);
            Assert.That(new WindowInteropHelper(_window).Handle, Is.EqualTo(IntPtr.Zero));
        });
    }

    [TestCase("Charts")]
    [TestCase("Processes")]
    [TestCase("Services")]
    [TestCase("Usage")]
    [TestCase("Connections")]
    [TestCase("Export")]
    [TestCase("Automatic")]
    [TestCase("Compact")]
    [TestCase("Walkthrough")]
    public void KnownTargetsRequestScrollingAfterExpansionBeforeFocusAndAnimation(string topic)
    {
        _window.ShowInTaskbar = false;
        _window.ApplyTheme();
        _window.Show();
        _window.Activate();
        _window.SetActivityPanelCollapsed(true);
        _window.SetChartPanelCollapsed(true);
        _window.SetCompactView(true);
        var beforeFocus = Keyboard.FocusedElement;
        var target = _window.GuideTarget(topic)!;
        var requests = 0;
        target.RequestBringIntoView += (_, args) =>
        {
            if (!ReferenceEquals(args.TargetObject, target)) return;
            requests++;
            if (requests != 1) return;
            Assert.Multiple(() =>
            {
                Assert.That(_window.CompactMode, Is.EqualTo(topic == "Compact"));
                Assert.That(_window.Preferences.ActivityPanelCollapsed, Is.EqualTo(ActivityIndex(topic) < 0));
                Assert.That(_window.Preferences.ChartPanelCollapsed, Is.EqualTo(topic is not ("Charts" or "Walkthrough")));
                Assert.That(Keyboard.FocusedElement, Is.SameAs(beforeFocus));
                Assert.That(DependencyPropertyHelper.GetValueSource(target, UIElement.OpacityProperty).IsAnimated, Is.False);
            });
        };

        _window.ShowGuideTarget(topic);

        Assert.That(requests, Is.GreaterThanOrEqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(target.IsKeyboardFocusWithin, Is.EqualTo(topic is not ("Export" or "Automatic" or "Walkthrough")));
            if (topic == "Export") Assert.That(target.IsEnabled, Is.False, "No samples are available in this fixture");
            if (topic == "Automatic") Assert.That(target.Visibility, Is.EqualTo(Visibility.Collapsed));
            if (topic == "Walkthrough") Assert.That(target.Focusable, Is.False);
        });
    }

    [Test]
    public void ScrollingFailurePropagatesAfterExpansionBeforeFocusAndAnimation()
    {
        _window.SetCompactView(true);
        _window.SetChartPanelCollapsed(true);
        var target = _window.ChartTypeSelector;
        var focus = Keyboard.FocusedElement;
        var failure = new InvalidOperationException("owned scrolling failure");
        target.RequestBringIntoView += (_, args) =>
        {
            if (!ReferenceEquals(args.TargetObject, target)) return;
            Assert.That(_window.CompactMode, Is.False);
            Assert.That(_window.Preferences.ChartPanelCollapsed, Is.False);
            throw failure;
        };

        Assert.That(Assert.Throws<InvalidOperationException>(() => _window.ShowGuideTarget("Charts")), Is.SameAs(failure));
        Assert.Multiple(() =>
        {
            Assert.That(Keyboard.FocusedElement, Is.SameAs(focus));
            Assert.That(DependencyPropertyHelper.GetValueSource(target, UIElement.OpacityProperty).IsAnimated, Is.False);
        });
    }

    [TestCase("Export")]
    [TestCase("Automatic")]
    public void AvailableToolbarTargetsReceiveFocusWithoutActivatingTheirAction(string topic)
    {
        _window.ShowInTaskbar = false;
        _window.ApplyTheme();
        _window.Show();
        _window.Activate();
        var target = _window.GuideTarget(topic)!;
        target.IsEnabled = true;
        target.Visibility = Visibility.Visible;
        var selected = new MonitorSample(DateTimeOffset.UnixEpoch, 20, [], 0);
        _window.SelectedSample = selected;
        var clicked = false;
        target.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent,
            new RoutedEventHandler((_, _) => clicked = true));

        _window.ShowGuideTarget(topic);

        Assert.Multiple(() =>
        {
            Assert.That(target.IsKeyboardFocusWithin, Is.True);
            Assert.That(clicked, Is.False);
            Assert.That(_window.SelectedSample, Is.SameAs(selected));
            Assert.That(_window.OwnedWindows.Count, Is.Zero);
        });
    }

    [Test]
    public void ScrollingReentrancyRetainsReplacementHistoryAndPanelState()
    {
        _window.SetChartPanelCollapsed(true);
        var replacement = new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(1), 30, [], 0);
        var target = _window.ChartTypeSelector;
        var requested = false;
        target.RequestBringIntoView += (_, args) =>
        {
            if (!ReferenceEquals(args.TargetObject, target)) return;
            requested = true;
            Assert.That(_window.Preferences.ChartPanelCollapsed, Is.False);
            _window.SetChartPanelCollapsed(true);
            _window.SelectedSample = replacement;
            target.IsEnabled = false;
        };

        _window.ShowGuideTarget("Charts");

        Assert.Multiple(() =>
        {
            Assert.That(requested, Is.True);
            Assert.That(_window.Preferences.ChartPanelCollapsed, Is.True);
            Assert.That(_window.SelectedSample, Is.SameAs(replacement));
            Assert.That(target.IsEnabled, Is.False);
            Assert.That(target.IsKeyboardFocusWithin, Is.False);
            Assert.That(DependencyPropertyHelper.GetValueSource(target, UIElement.OpacityProperty).IsAnimated,
                Is.EqualTo(SystemParameters.ClientAreaAnimation));
        });
    }
}
