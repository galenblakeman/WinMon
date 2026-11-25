using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, Category("Integration"), NonParallelizable, Apartment(ApartmentState.STA),
 SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ServiceMenuRoutingCharacterizationTests
{
    private ServicesView _view = null!;
    private Window? _window;
    private bool _addedCaptionStyle;
    private ListBox Rows => (ListBox)_view.FindName("Rows");

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (!app.Resources.Contains("CaptionButton"))
        {
            app.Resources["CaptionButton"] = new Style(typeof(Button));
            _addedCaptionStyle = true;
        }
        _view = new ServicesView();
        _view.ActionRequested += (_, _, _) => Assert.Fail("Menu routing must not execute a native service action.");
    }

    [TearDown]
    public void TearDown()
    {
        if (_view?.FindName("Rows") is ListBox { ContextMenu: { } menu }) menu.IsOpen = false;
        _window?.Close();
        _window = null;
        _view?.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        if (_addedCaptionStyle) Application.Current.Resources.Remove("CaptionButton");
        _addedCaptionStyle = false;
    }

    private static ServiceGroup Group(string name) =>
        new("service:" + name, [new(name, "Fixture " + name, 1, 0, 1)], null, []);

    private static ServiceFrame Frame(params string[] names) =>
        new(new MonitorSample(DateTimeOffset.UnixEpoch.AddDays(1), 0, [], 0), names.Select(Group).ToArray());

    private void Show(ServiceFrame frame, bool historical = false)
    {
        _view.Update(frame, [frame], historical);
        _window = new Window { Content = _view, Width = 960, Height = 620, ShowInTaskbar = false,
            Title = "Service menu routing fixture" };
        _window.Show();
        _window.Activate();
        _view.UpdateLayout();
        Assert.That(Rows.Focus(), Is.True, "The owned fixture must establish real keyboard focus.");
    }

    private KeyEventArgs OpenViaApps(UIElement source)
    {
        var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(_window!)!, 0, Key.Apps)
            { RoutedEvent = Keyboard.KeyUpEvent, Source = source };
        InputManager.Current.ProcessInput(input);
        return input;
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

    private ListBoxItem Container(int index) =>
        (ListBoxItem)Rows.ItemContainerGenerator.ContainerFromIndex(index);

    private static void Copy(ContextMenu menu) =>
        menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.CopyDetails))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    [Test]
    public void KeyboardMenuReplacesItsPlaceholderOnTheFirstInvocation()
    {
        Show(Frame("alpha", "beta"));
        Rows.SelectedIndex = 0;
        var row = Rows.SelectedItem;
        var placeholder = Rows.ContextMenu;
        var input = OpenViaApps(Rows);
        Assert.That(input.Handled, Is.True);
        Assert.That(Rows.ContextMenu, Is.Not.SameAs(placeholder));
        Assert.That(Rows.ContextMenu.IsOpen, Is.True);
        Assert.That(Rows.SelectedItem, Is.SameAs(row));
        Assert.That(Rows.ContextMenu.Items.OfType<MenuItem>().Select(item => item.Header), Is.EqualTo(new[]
        {
            Strings.ViewServiceProcess, Strings.CopyDetails, Strings.StartService, Strings.StopService, Strings.RestartService
        }));
    }

    [TestCase("list")]
    [TestCase("row")]
    [TestCase("pin-button")]
    public void KeyboardMenuCapturesItsRoutedOwnerWhenSelectionLaterChanges(string sourceKind)
    {
        Show(Frame("alpha", "beta"));
        Rows.SelectedIndex = 0;
        var expected = sourceKind == "list" ? "alpha" : "beta";
        UIElement source = sourceKind switch
        {
            "row" => Container(1),
            "pin-button" => Children<Button>(Container(1)).Single(button => button.Visibility == Visibility.Visible),
            _ => Rows
        };
        string? copied = null;
        _view.CopyRequested += text => copied = text;
        Assert.That(OpenViaApps(source).Handled, Is.True);
        var menu = Rows.ContextMenu;
        Assert.That(menu.IsOpen, Is.True);
        Assert.That(((ServiceDisplayRow)Rows.SelectedItem).Service!.Name, Is.EqualTo(expected));
        menu.IsOpen = false;
        Rows.SelectedIndex = expected == "alpha" ? 1 : 0;
        Copy(menu);
        Assert.That(copied, Does.Contain("Fixture " + expected + " (" + expected + ")"));
        Assert.That(copied, Does.Not.Contain(expected == "alpha" ? "Fixture beta" : "Fixture alpha"));
    }

    [TestCase("empty")]
    [TestCase("no-selection")]
    [TestCase("unavailable")]
    public void KeyboardMenuSuppressesItsPlaceholderWithoutAnAvailableSelectedRow(string partition)
    {
        var frame = partition == "empty" ? Frame() : Frame("alpha");
        if (partition == "unavailable") frame = frame with { Available = false };
        Show(frame);
        Rows.SelectedIndex = partition == "unavailable" ? 0 : -1;
        var placeholder = Rows.ContextMenu;
        _view.CopyRequested += _ => Assert.Fail("A suppressed menu must not publish details.");
        Assert.That(OpenViaApps(Rows).Handled, Is.True);
        Assert.That(Rows.ContextMenu, Is.SameAs(placeholder));
        Assert.That(placeholder.IsOpen, Is.False);
        Assert.That(placeholder.Items, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HistoricalKeyboardMenuKeepsDetailsButDisablesLiveServiceControls(bool historical)
    {
        Show(Frame("alpha"), historical);
        Rows.SelectedIndex = 0;
        Assert.That(OpenViaApps(Rows).Handled, Is.True);
        var items = Rows.ContextMenu.Items.OfType<MenuItem>().ToArray();
        Assert.That(items.Select(item => item.IsEnabled),
            Is.EqualTo(new[] { false, true, !historical, false, false }));
        Rows.ContextMenu.IsOpen = false;
        string? copied = null;
        _view.CopyRequested += text => copied = text;
        Copy(Rows.ContextMenu);
        Assert.That(copied, Does.Contain("Fixture alpha (alpha)"));
    }

    [Test]
    public void InvalidContainerContextDoesNotBorrowTheSelectedServiceForItsMenu()
    {
        Show(Frame("alpha"));
        Rows.SelectedIndex = 0;
        var container = Container(0);
        container.DataContext = "not a service row";
        var placeholder = Rows.ContextMenu;
        Assert.That(OpenViaApps(container).Handled, Is.True);
        Assert.That(Rows.ContextMenu, Is.SameAs(placeholder));
        Assert.That(placeholder.IsOpen, Is.False);
        Assert.That(((ServiceDisplayRow)Rows.SelectedItem).Service!.Name, Is.EqualTo("alpha"));
    }

    [Test]
    public void ReopeningTheKeyboardMenuReplacesItsPreviousSelectionSnapshot()
    {
        Show(Frame("alpha", "beta"));
        Rows.SelectedIndex = 0;
        OpenViaApps(Rows);
        var first = Rows.ContextMenu;
        first.IsOpen = false;
        Rows.SelectedIndex = 1;
        Assert.That(OpenViaApps(Rows).Handled, Is.True);
        var second = Rows.ContextMenu;
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second.IsOpen, Is.True);
        second.IsOpen = false;
        string? copied = null;
        _view.CopyRequested += text => copied = text;
        Copy(second);
        Assert.That(copied, Does.Contain("Fixture beta (beta)").And.Not.Contain("Fixture alpha"));
        Copy(first);
        Assert.That(copied, Does.Contain("Fixture alpha (alpha)").And.Not.Contain("Fixture beta"));
    }

    [Test]
    public void ThrowingMenuObserverSeesTheReplacementBeforeOpeningAndCanRetry()
    {
        Show(Frame("alpha"));
        Rows.SelectedIndex = 0;
        var placeholder = Rows.ContextMenu;
        var failure = new InvalidOperationException("menu observer fixture");
        var notifications = 0;
        void Observer(object sender, ContextMenuEventArgs args)
        {
            notifications++;
            Assert.That(Rows.ContextMenu, Is.Not.SameAs(placeholder));
            Assert.That(Rows.ContextMenu.IsOpen, Is.False);
            throw failure;
        }
        Rows.ContextMenuOpening += Observer;
        Assert.That(Assert.Throws<InvalidOperationException>(() => OpenViaApps(Rows)), Is.SameAs(failure));
        Assert.That(notifications, Is.EqualTo(1));
        Rows.ContextMenuOpening -= Observer;
        Assert.That(OpenViaApps(Rows).Handled, Is.True);
        Assert.That(Rows.ContextMenu.IsOpen, Is.True);
    }
}
