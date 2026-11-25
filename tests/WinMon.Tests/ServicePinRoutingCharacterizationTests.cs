using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ServicePinRoutingCharacterizationTests
{
    private ServicesView _view = null!;
    private Window? _window;
    private readonly List<object> _addedResources = [];
    private ListBox Rows => (ListBox)_view.FindName("Rows");

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (!app.Resources.Contains("CaptionButton"))
        {
            app.Resources["CaptionButton"] = new Style(typeof(Button));
            _addedResources.Add("CaptionButton");
        }
        _view = new ServicesView();
        _view.ActionRequested += (_, _, _) => Assert.Fail("Pin routing must not request a native service action.");
    }

    [TearDown]
    public void TearDown()
    {
        _window?.Close();
        _window = null;
        _view?.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
        _addedResources.Clear();
    }

    private static ServiceGroup Group(string key, params string[] names) =>
        new(key, names.Select(name => new ServiceObservation(name, "Fixture " + name, 1, 0, 1)).ToArray(), null, []);

    private static ServiceFrame Frame(params ServiceGroup[] groups) =>
        new(new MonitorSample(DateTimeOffset.UnixEpoch.AddDays(1), 0, [], 0), groups);

    private void Update(ServiceFrame frame, bool historical = false)
    {
        _view.Update(frame, [frame], historical);
        Layout();
    }

    private void Layout()
    {
        _view.Measure(new Size(960, 620));
        _view.Arrange(new Rect(0, 0, 960, 620));
        _view.UpdateLayout();
    }

    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in Children<T>(child)) yield return descendant;
        }
    }

    private Button PinButton(ServiceDisplayRow row)
    {
        var container = Rows.ItemContainerGenerator.ContainerFromItem(row) as ListBoxItem;
        Assert.That(container, Is.Not.Null, "The real row template must be generated.");
        return Children<Button>(container!).Single(button => ReferenceEquals(button.DataContext, row) &&
            AutomationProperties.GetName(button) == row.PinAccessibleName);
    }

    private static RoutedEventArgs Click(Button button)
    {
        var input = new RoutedEventArgs(Button.ClickEvent);
        button.RaiseEvent(input);
        return input;
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void PinClickTogglesTheWholeServiceGroupBeforePublishingChanges(bool historical, bool shared)
    {
        var names = shared ? new[] { "alpha", "beta" } : new[] { "alpha" };
        Update(Frame(Group("service:fixture", names)), historical);
        var row = (ServiceDisplayRow)Rows.Items[0];
        var button = PinButton(row);
        var notifications = 0;
        _view.PinsChanged += () =>
        {
            notifications++;
            var pinned = notifications == 1;
            Assert.That(_view.SavedPins, Is.EqualTo(pinned ? names : []));
            Assert.That(((ServiceDisplayRow)Rows.Items[0]).Pinned, Is.EqualTo(pinned));
            Assert.That(((TextBlock)_view.FindName("PinSummary")).Visibility,
                Is.EqualTo(pinned ? Visibility.Visible : Visibility.Collapsed));
        };
        Assert.That(Click(button).Handled, Is.True);
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(Rows.Items[0], Is.SameAs(row));
        Assert.That(Click(button).Handled, Is.True);
        Assert.That(notifications, Is.EqualTo(2));
        Assert.That(_view.SavedPins, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PinClickOnAnExpandedSharedServiceLeavesItsSiblingUnpinned(bool historical)
    {
        Update(Frame(Group("service:fixture", "alpha", "beta")), historical);
        _view.ExpandGroup("service:fixture");
        Layout();
        var beta = Rows.Items.Cast<ServiceDisplayRow>().Single(row => row.Service?.Name == "beta");
        var alpha = Rows.Items.Cast<ServiceDisplayRow>().Single(row => row.Service?.Name == "alpha");
        Assert.That(Click(PinButton(beta)).Handled, Is.True);
        Assert.That(_view.SavedPins, Is.EqualTo(new[] { "beta" }));
        Assert.That(beta.Pinned, Is.True);
        Assert.That(alpha.Pinned, Is.False);
        Assert.That(((ServiceDisplayRow)Rows.Items[0]).Pinned, Is.True);
        Assert.That(Click(PinButton(beta)).Handled, Is.True);
        Assert.That(_view.SavedPins, Is.Empty);
    }

    [TestCase("missing")]
    [TestCase("wrong-type")]
    public void PinClickWithMissingOrWrongDataContextIsHandledWithoutChangingPins(string partition)
    {
        Update(Frame(Group("service:alpha", "alpha")));
        var row = (ServiceDisplayRow)Rows.Items[0];
        var button = PinButton(row);
        button.DataContext = partition == "missing" ? null : "not a service row";
        _view.PinsChanged += () => Assert.Fail("Invalid pin context must not publish a change.");
        Assert.That(Click(button).Handled, Is.True);
        Assert.That(_view.SavedPins, Is.Empty);
        Assert.That(row.Pinned, Is.False);
    }

    [Test]
    public void PinClickFromADetachedRowCannotRetargetTheNewFrame()
    {
        Update(Frame(Group("service:alpha", "alpha")));
        var button = PinButton((ServiceDisplayRow)Rows.Items[0]);
        Update(Frame(Group("service:beta", "beta")));
        _view.PinsChanged += () => Assert.Fail("A detached row cannot pin its replacement.");
        Assert.That(Click(button).Handled, Is.True);
        Assert.That(_view.SavedPins, Is.Empty);
        Assert.That(((ServiceDisplayRow)Rows.Items[0]).Service!.Name, Is.EqualTo("beta"));
        Assert.That(((ServiceDisplayRow)Rows.Items[0]).Pinned, Is.False);
    }

    [Test]
    public void UnpinningInPinnedOnlyRemovesTheRowWithoutChangingSavedOrder()
    {
        Update(Frame(Group("service:alpha", "alpha"), Group("service:beta", "beta")));
        _view.RestorePins(["alpha", "beta"]);
        _view.PinnedOnly = true;
        Layout();
        var alpha = (ServiceDisplayRow)Rows.Items[0];
        var beta = (ServiceDisplayRow)Rows.Items[1];
        var button = PinButton(alpha);
        Assert.That(button.IsKeyboardFocusWithin, Is.False);
        var changes = 0;
        _view.PinsChanged += () => changes++;
        Assert.That(Click(button).Handled, Is.True);
        Assert.That(_view.SavedPins, Is.EqualTo(new[] { "beta" }));
        Assert.That(Rows.Items, Has.Count.EqualTo(1));
        Assert.That(Rows.Items[0], Is.SameAs(beta));
        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void ThrowingPinSubscriberKeepsTheRefreshedPinButDoesNotMarkTheClickHandled()
    {
        Update(Frame(Group("service:alpha", "alpha")));
        var row = (ServiceDisplayRow)Rows.Items[0];
        var button = PinButton(row);
        var input = new RoutedEventArgs(Button.ClickEvent);
        var failure = new InvalidOperationException("pin observer fixture");
        _view.PinsChanged += () => throw failure;
        Assert.That(Assert.Throws<InvalidOperationException>(() => button.RaiseEvent(input)), Is.SameAs(failure));
        Assert.That(input.Handled, Is.False);
        Assert.That(_view.SavedPins, Is.EqualTo(new[] { "alpha" }));
        Assert.That(row.Pinned, Is.True);
        Assert.That(((TextBlock)_view.FindName("PinSummary")).Visibility, Is.EqualTo(Visibility.Visible));
    }

    [TestCase(false)]
    [TestCase(true)]
    [Category("Integration")]
    public void KeyboardPinClickKeepsFocusOrMovesItToPinnedOnlyWhenTheRowDisappears(bool removed)
    {
        Update(Frame(Group("service:alpha", "alpha")));
        if (removed)
        {
            _view.RestorePins(["alpha"]);
            _view.PinnedOnly = true;
        }
        _window = new Window { Content = _view, Width = 960, Height = 620, ShowInTaskbar = false,
            Title = "Service pin routing fixture" };
        _window.Show();
        _window.Activate();
        _view.UpdateLayout();
        var row = (ServiceDisplayRow)Rows.Items[0];
        var button = PinButton(row);
        Assert.That(button.Focus(), Is.True, "The owned fixture must establish real keyboard focus.");
        Assert.That(button.IsKeyboardFocusWithin, Is.True);
        Assert.That(Click(button).Handled, Is.True);
        Assert.That(_view.SavedPins, Is.EqualTo(removed ? Array.Empty<string>() : ["alpha"]));
        if (removed)
        {
            Assert.That(Rows.Items, Is.Empty);
            Assert.That(((CheckBox)_view.FindName("PinnedOnlyCheckBox")).IsKeyboardFocused, Is.True);
        }
        else
        {
            Assert.That(Rows.Items[0], Is.SameAs(row));
            Assert.That(button.IsKeyboardFocusWithin, Is.True);
        }
    }
}
