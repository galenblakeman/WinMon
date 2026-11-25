using System.Collections;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Monitoring;
using Forms = System.Windows.Forms;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration"),
 SetCulture("en-US"), SetUICulture("en-US")]
public sealed class WindowStateCharacterizationTests
{
    private readonly Dictionary<object, object?> _resources = [];
    private MainWindow _window = null!;
    private Forms.NotifyIcon? _tray;
    private Window? _other;
    private static readonly MonitorSample First = new(DateTimeOffset.UnixEpoch, 12, [], 0);
    private static readonly MonitorSample Second = new(DateTimeOffset.UnixEpoch.AddSeconds(1), 34, [], 0);

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
        _window.Session.Record(First);
    }

    [TearDown]
    public void TearDown()
    {
        _other?.Close();
        _other = null;
        if (_window is not null)
        {
            _window.Tray = null;
            foreach (var view in new FrameworkElement[] { _window.UsageView, _window.Processes, _window.ServiceView, _window.ConnectionView })
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            _window.CloseForFixture();
        }
        _tray?.Dispose();
        _tray = null;
        var resources = Application.Current.Resources;
        foreach (var key in resources.Keys.Cast<object>().Where(key => !_resources.ContainsKey(key)).ToArray())
            resources.Remove(key);
        foreach (var (key, value) in _resources) resources[key] = value;
        _resources.Clear();
    }

    private void ConfigureTray(bool enabled, bool attached)
    {
        _window.Preferences = _window.Preferences with { MinimizeToTray = enabled };
        if (attached) _window.Tray = _tray = new Forms.NotifyIcon { Visible = false };
    }

    private void Show()
    {
        _window.Show();
        _window.Activate();
        _window.UpdateLayout();
        Assert.That(_window.IsVisible, Is.True);
        Assert.That(_window.SamplingTask, Is.Null);
    }

    [Test]
    public void StateChangesBeforeTheFirstShowRestoreNormalWithoutPresentingSamplesEarly(
        [Values(WindowState.Normal, WindowState.Maximized)] WindowState remembered)
    {
        var notifications = 0;
        _window.StateChanged += (_, _) => notifications++;
        _window.WindowState = WindowState.Maximized;
        _window.WindowState = remembered;
        _window.WindowState = WindowState.Minimized;
        Assert.That(_window.PresentedSample, Is.Null);
        Assert.That(notifications, Is.Zero, "Before the native window exists WPF defers state notifications");

        _window.ShowRequested();

        Assert.Multiple(() =>
        {
            Assert.That(_window.WindowState, Is.EqualTo(WindowState.Normal));
            Assert.That(_window.IsVisible, Is.True);
            Assert.That(_window.PresentedSample, Is.SameAs(First));
        });
    }

    [Test]
    public void HiddenExistingWindowKeepsItsLastVisibleRestoreStateWithoutPresentingThePendingSample(
        [Values(WindowState.Normal, WindowState.Maximized)] WindowState remembered)
    {
        Show();
        _window.WindowState = remembered;
        _window.Hide();
        _window.Session.Record(Second);
        var notifications = 0;
        _window.StateChanged += (_, _) => notifications++;
        _window.WindowState = remembered == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        _window.WindowState = WindowState.Minimized;
        Assert.That(_window.PresentedSample, Is.SameAs(First));
        Assert.That(notifications, Is.Zero, "Hidden native windows also defer these state notifications");

        _window.ShowRequested();

        Assert.That(_window.WindowState, Is.EqualTo(remembered));
        Assert.That(_window.IsVisible, Is.True);
        Assert.That(_window.PresentedSample, Is.SameAs(Second));
    }

    [Test]
    public void VisibleStateChangesRefreshTheNewestSampleAndPreserveTheRestoreState(
        [Values(WindowState.Normal, WindowState.Maximized)] WindowState remembered)
    {
        Show();
        _window.WindowState = remembered == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        _window.Session.Record(Second);
        _window.WindowState = remembered;
        Assert.That(_window.PresentedSample, Is.SameAs(Second));
        _window.WindowState = WindowState.Minimized;

        _window.ShowRequested();

        Assert.That(_window.WindowState, Is.EqualTo(remembered));
        Assert.That(_window.PresentedSample, Is.SameAs(Second));
    }

    [Test]
    public void MinimizingHidesTheWindowOnlyWithBothTrayPrerequisites(
        [Values(false, true)] bool enabled, [Values(false, true)] bool attached)
    {
        ConfigureTray(enabled, attached);
        Show();
        _window.Session.Record(Second);

        _window.WindowState = WindowState.Minimized;

        Assert.Multiple(() =>
        {
            Assert.That(_window.IsVisible, Is.EqualTo(!(enabled && attached)));
            Assert.That(_window.WindowState, Is.EqualTo(WindowState.Minimized));
            Assert.That(_window.PresentedSample, Is.SameAs(First), "Minimizing must not draw the pending sample");
            Assert.That(_tray?.Visible ?? false, Is.False, "The fixture must not publish a system tray icon");
        });
        _window.ShowRequested();
        Assert.That(_window.IsVisible, Is.True);
        Assert.That(_window.WindowState, Is.EqualTo(WindowState.Normal));
        Assert.That(_window.PresentedSample, Is.SameAs(Second));
    }

    [Test]
    public void ClosingStateChangesDoNotHideOrRefreshTheWindow(
        [Values(WindowState.Minimized, WindowState.Maximized)] WindowState next)
    {
        ConfigureTray(true, true);
        Show();
        _window.Session.Record(Second);
        _window.IsClosing = true;

        _window.WindowState = next;

        Assert.That(_window.IsVisible, Is.True);
        Assert.That(_window.PresentedSample, Is.SameAs(First));
    }

    [Test]
    public void ShowRequestsDuringClosingLeaveVisibilityAndStateAlone([Values(false, true)] bool visible)
    {
        if (visible) Show();
        _window.IsClosing = true;

        _window.ShowRequested();

        Assert.That(_window.IsVisible, Is.EqualTo(visible));
        Assert.That(_window.WindowState, Is.EqualTo(WindowState.Normal));
    }

    [Test]
    public void AnActiveWindowShowRequestTogglesMinimizationAndRestoresItsPreviousState(
        [Values(WindowState.Normal, WindowState.Maximized)] WindowState remembered,
        [Values(false, true)] bool tray)
    {
        ConfigureTray(tray, tray);
        _window.WindowState = remembered;
        Show();
        Assert.That(_window.IsActive, Is.True, "This case requires actual window activation");

        _window.ShowRequested();

        Assert.That(_window.WindowState, Is.EqualTo(WindowState.Minimized));
        Assert.That(_window.IsVisible, Is.EqualTo(!tray));
        _window.ShowRequested();
        Assert.That(_window.WindowState, Is.EqualTo(remembered));
        Assert.That(_window.IsVisible, Is.True);
        Assert.That(_window.IsActive, Is.True);
    }

    [Test]
    public void AnInactiveVisibleWindowShowRequestActivatesWithoutMinimizing()
    {
        Show();
        _other = new Window { Width = 240, Height = 160, ShowInTaskbar = false, Title = "Window activation fixture" };
        _other.Show();
        _other.Activate();
        Assert.That(_other.IsActive, Is.True);
        Assert.That(_window.IsActive, Is.False);

        _window.ShowRequested();

        Assert.That(_window.IsActive, Is.True);
        Assert.That(_window.WindowState, Is.EqualTo(WindowState.Normal));
    }

    [Test]
    public void AShowRequestReturnsActivationToTheOwnedWindow()
    {
        Show();
        _other = new Window { Owner = _window, Width = 240, Height = 160, ShowInTaskbar = false, Title = "Owned window fixture" };
        _other.Show();
        _other.Activate();
        Assert.That(_other.IsActive, Is.True);
        Assert.That(_window.IsActive, Is.False);

        _window.ShowRequested();

        Assert.That(_other.IsActive, Is.True);
        Assert.That(_window.WindowState, Is.EqualTo(WindowState.Normal));
        Assert.That(_window.IsVisible, Is.True);
    }
}
