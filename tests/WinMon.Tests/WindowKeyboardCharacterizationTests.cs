using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class WindowKeyboardCharacterizationTests
{
    private readonly Dictionary<object, object?> _resources = [];
    private MainWindow _window = null!;
    private static readonly MonitorSample Historical = new(DateTimeOffset.UnixEpoch, 20, [], 0);

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (DictionaryEntry entry in app.Resources) _resources.Add(entry.Key, entry.Value);
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) app.Resources[key] = new Style();
        if (!app.Resources.Contains("TitleBarButtonText")) app.Resources["TitleBarButtonText"] = new DataTemplate();
        if (!app.Resources.Contains(typeof(Button))) app.Resources[typeof(Button)] = new Style(typeof(Button));
        _window = new MainWindow(true) { ShowInTaskbar = false, Width = 1000, Height = 840 };
        _window.Loaded -= _window.OnLoaded;
        _window.ApplyTheme();
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
    }

    private static KeyEventArgs Input(Key key, bool handled = false) =>
        new(Keyboard.PrimaryDevice, new WindowlessInputSource(), 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent, Handled = handled };

    private TextBox Search(string tab) => (TextBox)(tab switch
    {
        "Services" => _window.ServiceView.FindName("Search"),
        "Usage" => _window.UsageView.FindName("Search"),
        "Connections" => _window.ConnectionView.FindName("Search"),
        _ => _window.Processes.FindName("ProcessSearchBox")
    });

    private void SelectTab(string tab)
    {
        var control = tab switch
        {
            "Services" => _window.ServicesTab,
            "Usage" => _window.UsageTab,
            "Connections" => _window.ConnectionsTab,
            _ => _window.ProcessesTab
        };
        control.IsChecked = true;
    }

    private void FillSearches()
    {
        foreach (var tab in new[] { "Processes", "Services", "Usage", "Connections" })
            Search(tab).Text = "owned " + tab;
    }

    private void AssertSearches(string? cleared = null)
    {
        foreach (var tab in new[] { "Processes", "Services", "Usage", "Connections" })
            Assert.That(Search(tab).Text, Is.EqualTo(tab == cleared ? "" : "owned " + tab), tab);
    }

    private void ShowAndFocus(TextBox search)
    {
        _window.Show();
        _window.Activate();
        _window.UpdateLayout();
        Assert.That(Keyboard.Focus(search), Is.SameAs(search), "The characterization requires actual owned search focus");
        Assert.That(search.IsKeyboardFocusWithin, Is.True);
    }

    [Test]
    public void UnrelatedKeysPreserveSearchSelectionCollapsedPanelsAndHandledState(
        [Values(Key.None, Key.A, Key.Enter, Key.Tab, Key.Space, Key.Left)] Key key,
        [Values(false, true)] bool selected, [Values(false, true)] bool handled)
    {
        FillSearches();
        _window.SetActivityPanelCollapsed(true);
        _window.SetCompactView(true);
        _window.SelectedSample = selected ? Historical : null;
        var preferences = _window.Preferences;
        var input = Input(key, handled);

        _window.RaiseEvent(input);

        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.EqualTo(handled));
            Assert.That(_window.SelectedSample, Is.SameAs(selected ? Historical : null));
            Assert.That(_window.Preferences, Is.SameAs(preferences));
            Assert.That(_window.CompactMode, Is.True);
            Assert.That(_window.Preferences.ActivityPanelCollapsed, Is.True);
            AssertSearches();
        });
    }

    [Test]
    public void EscapeWithoutSearchFocusReturnsOnlyHistoricalSelectionsToLive(
        [Values("Processes", "Services", "Usage", "Connections")] string tab,
        [Values(false, true)] bool selected)
    {
        SelectTab(tab);
        FillSearches();
        _window.SelectedSample = selected ? Historical : null;
        var input = Input(Key.Escape);

        _window.RaiseEvent(input);

        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.EqualTo(selected));
            Assert.That(_window.SelectedSample, Is.Null);
            AssertSearches();
        });
    }

    [Test]
    public void EscapeClearsOnlyTheFocusedActiveSearchBeforeReturningToLive(
        [Values("Processes", "Services", "Usage", "Connections")] string tab,
        [Values(false, true)] bool selected, [Values(false, true)] bool nonempty)
    {
        SelectTab(tab);
        FillSearches();
        var search = Search(tab);
        if (!nonempty) search.Clear();
        ShowAndFocus(search);
        _window.SelectedSample = selected ? Historical : null;
        var input = Input(Key.Escape);

        _window.RaiseEvent(input);

        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.EqualTo(nonempty || selected));
            Assert.That(_window.SelectedSample, Is.SameAs(nonempty && selected ? Historical : null));
            AssertSearches(tab);
        });
    }

    [Test]
    public void EscapeTreatsWhitespaceAsNonemptySearchAndRetainsHistory(
        [Values("Processes", "Services", "Usage", "Connections")] string tab)
    {
        SelectTab(tab);
        FillSearches();
        var search = Search(tab);
        search.Text = " ";
        ShowAndFocus(search);
        _window.SelectedSample = Historical;
        var input = Input(Key.Escape);

        _window.RaiseEvent(input);

        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.True);
            Assert.That(_window.SelectedSample, Is.SameAs(Historical));
            AssertSearches(tab);
        });
    }

    [Test]
    public void AlreadyHandledPreviewEscapeDoesNotRunTheWindowBinding(
        [Values("Processes", "Services", "Usage", "Connections")] string tab,
        [Values(false, true)] bool selected)
    {
        SelectTab(tab);
        FillSearches();
        ShowAndFocus(Search(tab));
        _window.SelectedSample = selected ? Historical : null;
        var input = Input(Key.Escape, true);

        _window.RaiseEvent(input);

        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.True);
            Assert.That(_window.SelectedSample, Is.SameAs(selected ? Historical : null));
            AssertSearches();
        });
    }

    [Test]
    public void ProcessDropdownsPreventEscapeFromLeavingHistoryOnEveryTab(
        [Values("ProcessCountSelector", "ProcessSortSelector")] string selector,
        [Values("Processes", "Services", "Usage", "Connections")] string tab)
    {
        _window.Show();
        _window.UpdateLayout();
        // Realize process selectors before switching tabs; WPF defers opening an unloaded selector.
        SelectTab(tab);
        var dropdown = (ComboBox)_window.Processes.FindName(selector);
        dropdown.IsDropDownOpen = true;
        _window.SelectedSample = Historical;
        var input = Input(Key.Escape);
        try
        {
            Assert.That(_window.Processes.HasOpenDropDown, Is.True);
            _window.RaiseEvent(input);
            Assert.Multiple(() =>
            {
                Assert.That(input.Handled, Is.False);
                Assert.That(_window.SelectedSample, Is.SameAs(Historical));
                Assert.That(dropdown.IsDropDownOpen, Is.True);
            });
        }
        finally { dropdown.IsDropDownOpen = false; }
    }

    [Test]
    public void ConnectionsDropdownsPreventEscapeFromLeavingConnectionsHistory(
        [Values("TopCount", "Rank", "TrafficPeriod")] string selector)
    {
        SelectTab("Connections");
        _window.Show();
        _window.UpdateLayout();
        var dropdown = (ComboBox)_window.ConnectionView.FindName(selector);
        dropdown.IsDropDownOpen = true;
        _window.SelectedSample = Historical;
        var input = Input(Key.Escape);
        try
        {
            Assert.That(_window.ConnectionView.HasOpenDropDown, Is.True);
            _window.RaiseEvent(input);
            Assert.Multiple(() =>
            {
                Assert.That(input.Handled, Is.False);
                Assert.That(_window.SelectedSample, Is.SameAs(Historical));
                Assert.That(dropdown.IsDropDownOpen, Is.True);
            });
        }
        finally { dropdown.IsDropDownOpen = false; }
    }

    [Test]
    public void ReturningToLiveWithEscapeDoesNotExpandCompactOrCollapsedActivity()
    {
        _window.SetActivityPanelCollapsed(true);
        _window.SetCompactView(true);
        _window.SelectedSample = Historical;
        var input = Input(Key.Escape);

        _window.RaiseEvent(input);

        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.True);
            Assert.That(_window.SelectedSample, Is.Null);
            Assert.That(_window.CompactMode, Is.True);
            Assert.That(_window.Preferences.ActivityPanelCollapsed, Is.True);
        });
    }

    [Test]
    public void ClearingSearchNotifiesBeforeHandlingAndRetainsReentrantHistoryChanges(
        [Values("Processes", "Services", "Usage", "Connections")] string tab)
    {
        SelectTab(tab);
        var search = Search(tab);
        search.Text = "owned search";
        ShowAndFocus(search);
        _window.SelectedSample = Historical;
        var replacement = new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(1), 30, [], 0);
        var input = Input(Key.Escape);
        var notifications = 0;
        search.TextChanged += (_, _) =>
        {
            Assert.That(input.Handled, Is.False);
            notifications++;
            _window.SelectedSample = replacement;
        };

        _window.RaiseEvent(input);

        Assert.Multiple(() =>
        {
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(search.Text, Is.Empty);
            Assert.That(input.Handled, Is.True);
            Assert.That(_window.SelectedSample, Is.SameAs(replacement));
        });
    }

    [Test]
    public void SearchNotificationExceptionsPropagateBeforeTheKeyIsMarkedHandled(
        [Values("Processes", "Services", "Usage", "Connections")] string tab)
    {
        SelectTab(tab);
        var search = Search(tab);
        search.Text = "owned search";
        ShowAndFocus(search);
        _window.SelectedSample = Historical;
        var input = Input(Key.Escape);
        var failure = new InvalidOperationException("owned observer failure");
        search.TextChanged += (_, _) => throw failure;

        Assert.That(Assert.Throws<InvalidOperationException>(() => _window.RaiseEvent(input)), Is.SameAs(failure));

        Assert.Multiple(() =>
        {
            Assert.That(search.Text, Is.Empty);
            Assert.That(input.Handled, Is.False);
            Assert.That(_window.SelectedSample, Is.SameAs(Historical));
        });
    }

    [Test]
    public void ReturningToLiveImmediatelyRefreshesTheVisibleHistoryStatus(
        [Values("Processes", "Services", "Usage", "Connections")] string tab)
    {
        _window.Session.Record(Historical, Historical);
        var latest = new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(1), 30, [], 0);
        _window.Session.Record(latest, latest);
        SelectTab(tab);
        _window.Show();
        _window.UpdateLayout();
        _window.SelectedSample = Historical;
        _window.RefreshPresentation();
        Assert.That(_window.ChartTooltipStatus.Text, Is.EqualTo(Strings.SystemResourcesInspectingSnapshot));
        var input = Input(Key.Escape);

        _window.RaiseEvent(input);

        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.True);
            Assert.That(_window.SelectedSample, Is.Null);
            Assert.That(_window.ChartTooltipStatus.Text, Is.EqualTo(Strings.ChartTypes));
        });
    }

    [Test]
    public void ControlFFocusesAndSelectsTheActiveSearchAfterExpandingPanelsWithoutChangingHistory(
        [Values("Processes", "Services", "Usage", "Connections")] string tab,
        [Values(false, true)] bool compact, [Values(false, true)] bool collapsed,
        [Values(false, true)] bool selected)
    {
        SelectTab(tab);
        FillSearches();
        _window.Show();
        _window.UpdateLayout();
        _window.SetActivityPanelCollapsed(collapsed);
        _window.SetCompactView(compact);
        _window.SelectedSample = selected ? Historical : null;
        var input = Input(Key.F);

        _window.DispatchWindowKeyForFixture(input, ModifierKeys.Control);

        Assert.Multiple(() =>
        {
            var search = Search(tab);
            Assert.That(input.Handled, Is.True);
            Assert.That(_window.CompactMode, Is.False);
            Assert.That(_window.Preferences.CompactMode, Is.False);
            Assert.That(_window.Preferences.ActivityPanelCollapsed, Is.False);
            Assert.That(_window.SelectedSample, Is.SameAs(selected ? Historical : null));
            Assert.That(search.IsKeyboardFocusWithin, Is.True);
            Assert.That(search.SelectionStart, Is.Zero);
            Assert.That(search.SelectionLength, Is.EqualTo(search.Text.Length));
            AssertSearches();
        });
    }

    [TestCase(ModifierKeys.None)]
    [TestCase(ModifierKeys.Shift)]
    [TestCase(ModifierKeys.Alt)]
    [TestCase(ModifierKeys.Windows)]
    [TestCase(ModifierKeys.Control | ModifierKeys.Shift)]
    [TestCase(ModifierKeys.Control | ModifierKeys.Alt)]
    [TestCase(ModifierKeys.Control | ModifierKeys.Windows)]
    [TestCase(ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Windows)]
    [TestCase((ModifierKeys)16)]
    public void FindRequiresExactlyControlAndLeavesOtherModifierCombinationsUntouched(ModifierKeys modifiers)
    {
        SelectTab("Services");
        FillSearches();
        _window.Show();
        _window.UpdateLayout();
        var search = Search("Services");
        search.Select(2, 3);
        _window.SetActivityPanelCollapsed(true);
        _window.SetCompactView(true);
        _window.SelectedSample = Historical;
        var preferences = _window.Preferences;
        var input = Input(Key.F);

        _window.DispatchWindowKeyForFixture(input, modifiers);

        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.False);
            Assert.That(_window.Preferences, Is.SameAs(preferences));
            Assert.That(_window.CompactMode, Is.True);
            Assert.That(_window.Preferences.ActivityPanelCollapsed, Is.True);
            Assert.That(_window.SelectedSample, Is.SameAs(Historical));
            Assert.That(search.SelectionStart, Is.EqualTo(2));
            Assert.That(search.SelectionLength, Is.EqualTo(3));
            AssertSearches();
        });
    }

    [Test]
    public void ControlDoesNotTurnOtherKeysIntoFind(
        [Values(Key.None, Key.A, Key.Enter, Key.Tab, Key.System)] Key key)
    {
        FillSearches();
        _window.SetActivityPanelCollapsed(true);
        _window.SetCompactView(true);
        _window.SelectedSample = Historical;
        var preferences = _window.Preferences;
        var input = Input(key);

        _window.DispatchWindowKeyForFixture(input, ModifierKeys.Control);

        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.False);
            Assert.That(_window.Preferences, Is.SameAs(preferences));
            Assert.That(_window.SelectedSample, Is.SameAs(Historical));
            AssertSearches();
        });
    }

    [TestCase(ModifierKeys.Control)]
    [TestCase(ModifierKeys.Shift)]
    [TestCase(ModifierKeys.Alt)]
    [TestCase(ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Windows)]
    public void EscapeReturnsToLiveRegardlessOfObservedModifiers(ModifierKeys modifiers)
    {
        _window.SelectedSample = Historical;
        var input = Input(Key.Escape);

        _window.DispatchWindowKeyForFixture(input, modifiers);

        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.True);
            Assert.That(_window.SelectedSample, Is.Null);
        });
    }

    [Test]
    public void ControlFFocusesAnEmptySearchAndStillRetainsTheHistoricalSelection(
        [Values("Processes", "Services", "Usage", "Connections")] string tab)
    {
        SelectTab(tab);
        FillSearches();
        Search(tab).Clear();
        _window.Show();
        _window.UpdateLayout();
        _window.SelectedSample = Historical;
        var input = Input(Key.F);

        _window.DispatchWindowKeyForFixture(input, ModifierKeys.Control);

        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.True);
            Assert.That(Search(tab).IsKeyboardFocusWithin, Is.True);
            Assert.That(Search(tab).SelectionLength, Is.Zero);
            Assert.That(_window.SelectedSample, Is.SameAs(Historical));
            AssertSearches(tab);
        });
    }

    [Test]
    public void FocusNotificationsSeeExpandedPanelsBeforeHandlingAndMayChangeLaterState(
        [Values("Processes", "Services", "Usage", "Connections")] string tab)
    {
        SelectTab(tab);
        FillSearches();
        _window.Show();
        _window.UpdateLayout();
        _window.SetActivityPanelCollapsed(true);
        _window.SetCompactView(true);
        _window.SelectedSample = Historical;
        var replacement = new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(1), 30, [], 0);
        var input = Input(Key.F);
        var notifications = 0;
        var search = Search(tab);
        search.GotKeyboardFocus += (_, _) =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(input.Handled, Is.False);
                Assert.That(_window.CompactMode, Is.False);
                Assert.That(_window.Preferences.ActivityPanelCollapsed, Is.False);
            });
            notifications++;
            _window.SelectedSample = replacement;
            _window.SetActivityPanelCollapsed(true);
        };

        _window.DispatchWindowKeyForFixture(input, ModifierKeys.Control);

        Assert.Multiple(() =>
        {
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(input.Handled, Is.True);
            Assert.That(_window.SelectedSample, Is.SameAs(replacement));
            Assert.That(_window.Preferences.ActivityPanelCollapsed, Is.True);
            Assert.That(search.SelectionLength, Is.EqualTo(search.Text.Length));
        });
    }

    [Test]
    public void FocusNotificationExceptionsPropagateAfterExpansionBeforeHandling(
        [Values("Processes", "Services", "Usage", "Connections")] string tab)
    {
        SelectTab(tab);
        _window.Show();
        _window.UpdateLayout();
        _window.SetActivityPanelCollapsed(true);
        _window.SetCompactView(true);
        _window.SelectedSample = Historical;
        var input = Input(Key.F);
        var failure = new InvalidOperationException("owned focus observer failure");
        Search(tab).GotKeyboardFocus += (_, _) => throw failure;

        Assert.That(Assert.Throws<InvalidOperationException>(() =>
            _window.DispatchWindowKeyForFixture(input, ModifierKeys.Control)), Is.SameAs(failure));

        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.False);
            Assert.That(_window.CompactMode, Is.False);
            Assert.That(_window.Preferences.ActivityPanelCollapsed, Is.False);
            Assert.That(_window.SelectedSample, Is.SameAs(Historical));
        });
    }

    [Test]
    public void FindFallsBackToProcessSearchWhenNoActivityTabIsChecked()
    {
        FillSearches();
        _window.Show();
        _window.UpdateLayout();
        _window.ProcessesTab.IsChecked = false;
        var input = Input(Key.F);

        _window.DispatchWindowKeyForFixture(input, ModifierKeys.Control);

        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.True);
            Assert.That(Search("Processes").IsKeyboardFocusWithin, Is.True);
            Assert.That(Search("Processes").SelectionLength, Is.EqualTo(Search("Processes").Text.Length));
            Assert.That(new[] { _window.ProcessesTab, _window.ServicesTab, _window.UsageTab, _window.ConnectionsTab }
                .Any(tab => tab.IsChecked == true), Is.False);
            AssertSearches();
        });
    }

    private sealed class WindowlessInputSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
