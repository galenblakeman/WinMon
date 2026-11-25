using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, Category("Integration"), NonParallelizable, Apartment(ApartmentState.STA),
 SetCulture("en-US"), SetUICulture("en-US")]
public sealed class UsageMenuRoutingCharacterizationTests
{
    private UsageView _view = null!;
    private Window? _window;
    private bool _addedCaptionStyle;
    private ListBox Rows => (ListBox)_view.FindName("Rows");
    private static readonly DateTimeOffset Start = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (!app.Resources.Contains("CaptionButton"))
        {
            app.Resources["CaptionButton"] = new Style(typeof(Button));
            _addedCaptionStyle = true;
        }
        _view = new UsageView();
        _view.CanShowInProcesses = _ => true;
        _view.PropertiesRequested += (_, _) => Assert.Fail("Menu routing must not open a properties window.");
        _view.ResetRequested += () => Assert.Fail("Menu routing must not reset usage.");
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

    private static AppUsage App(string name, int seconds = 60) =>
        new(name, "fixture-" + name, "", "Fixture " + name, "Fixture publisher",
            TimeSpan.FromSeconds(seconds).Ticks, 0, 12, 3, 2, true, null);

    private static UsageFrame Frame(params AppUsage[] apps) =>
        new(apps, TimeSpan.FromSeconds(300).Ticks, 0, Start, Start.AddMinutes(5), true)
            { Notice = "original frame" };

    private void Show(UsageFrame? frame)
    {
        if (frame is not null) _view.Update(frame);
        _window = new Window { Content = _view, Width = 960, Height = 620, ShowInTaskbar = false,
            Title = "Usage menu routing fixture" };
        _window.Show();
        _window.Activate();
        _view.UpdateLayout();
        Assert.That(Rows.Focus(), Is.True, "The owned fixture must establish actual keyboard focus.");
    }

    private KeyEventArgs OpenViaApps(UIElement source)
    {
        var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(_window!)!, 0, Key.Apps)
            { RoutedEvent = Keyboard.KeyUpEvent, Source = source };
        InputManager.Current.ProcessInput(input);
        return input;
    }

    private ListBoxItem Container(int index) => (ListBoxItem)Rows.ItemContainerGenerator.ContainerFromIndex(index);

    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in Children<T>(child)) yield return descendant;
        }
    }

    private static MenuItem Entry(ContextMenu menu, string header) =>
        menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header));

    private static void Click(ContextMenu menu, string header) =>
        Entry(menu, header).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    [Test]
    public void FirstKeyboardMenuReplacesAndOpensTheOriginalUsagePlaceholder()
    {
        Show(Frame(App("alpha")));
        Rows.SelectedIndex = 0;
        var placeholder = Rows.ContextMenu;

        Assert.That(OpenViaApps(Rows).Handled, Is.True);

        Assert.That(Rows.ContextMenu, Is.Not.SameAs(placeholder));
        Assert.That(Rows.ContextMenu.IsOpen, Is.True);
        Assert.That(Rows.ContextMenu.Items.OfType<MenuItem>().Select(item => item.Header), Is.EqualTo(new[]
            { Strings.UsageShowInProcesses, Strings.CopyDetails, Strings.OpenFileLocation }));
    }

    [TestCase("list")]
    [TestCase("row")]
    [TestCase("descendant")]
    public void RoutedUsageOwnerControlsCopyAndNavigationWithoutChangingTheSelection(string sourceKind)
    {
        var alpha = App("alpha");
        var beta = App("beta", 30);
        Show(Frame(alpha, beta));
        Rows.SelectedIndex = 0;
        var selected = Rows.SelectedItem;
        var expected = sourceKind == "list" ? alpha : beta;
        UIElement source = sourceKind switch
        {
            "row" => Container(1),
            "descendant" => Children<TextBlock>(Container(1)).First(),
            _ => Rows
        };
        string? copied = null;
        AppUsage? navigated = null;
        _view.CopyRequested += text => copied = text;
        _view.ShowInProcessesRequested += app => navigated = app;

        Assert.That(OpenViaApps(source).Handled, Is.True);

        var menu = Rows.ContextMenu;
        Assert.That(menu.IsOpen, Is.True);
        Assert.That(Rows.SelectedItem, Is.SameAs(selected));
        menu.IsOpen = false;
        Rows.SelectedIndex = 1;
        Click(menu, Strings.CopyDetails);
        Click(menu, Strings.UsageShowInProcesses);
        Assert.That(copied, Does.StartWith(expected.AppName + Environment.NewLine));
        Assert.That(navigated, Is.SameAs(expected));
        Assert.That(Entry(menu, Strings.OpenFileLocation).IsEnabled, Is.False);
    }

    [TestCase("uninitialized")]
    [TestCase("empty")]
    [TestCase("no-selection")]
    public void MissingUsageSelectionSuppressesThePlaceholderWithoutEligibilityOrCopy(string partition)
    {
        Show(partition == "uninitialized" ? null : partition == "empty" ? Frame() : Frame(App("alpha")));
        Rows.SelectedIndex = -1;
        _view.CanShowInProcesses = _ => throw new AssertionException("A suppressed menu must not query navigation.");
        _view.CopyRequested += _ => Assert.Fail("A suppressed menu must not publish details.");
        var placeholder = Rows.ContextMenu;

        Assert.That(OpenViaApps(Rows).Handled, Is.True);

        Assert.That(Rows.ContextMenu, Is.SameAs(placeholder));
        Assert.That(placeholder.IsOpen, Is.False);
        Assert.That(placeholder.Items, Is.Empty);
    }

    [TestCase("wrong-type")]
    [TestCase("empty-app")]
    public void InvalidUsageContainerContextCannotBorrowTheSelectedApp(string partition)
    {
        Show(Frame(App("alpha")));
        Rows.SelectedIndex = 0;
        var selected = Rows.SelectedItem;
        var container = Container(0);
        container.DataContext = partition == "wrong-type" ? "not a usage row" : new UsageDisplayRow("empty");
        var placeholder = Rows.ContextMenu;

        Assert.That(OpenViaApps(container).Handled, Is.True);

        Assert.That(Rows.ContextMenu, Is.SameAs(placeholder));
        Assert.That(placeholder.IsOpen, Is.False);
        Assert.That(Rows.SelectedItem, Is.SameAs(selected));
    }

    [Test]
    public void AValidRetainedRowCannotOpenAMenuAfterItsUsageFrameIsCleared()
    {
        var app = App("alpha");
        Show(Frame(app));
        Rows.SelectedIndex = 0;
        var retained = (UsageDisplayRow)Rows.SelectedItem;
        _view.Update(null!);
        Rows.ItemsSource = new[] { retained };
        _view.UpdateLayout();
        Rows.SelectedIndex = 0;
        Assert.That(((UsageDisplayRow)Rows.SelectedItem).App, Is.SameAs(app));
        var placeholder = Rows.ContextMenu;
        _view.CanShowInProcesses = _ => throw new AssertionException("A cleared frame must suppress eligibility.");

        Assert.That(OpenViaApps(Rows).Handled, Is.True);

        Assert.That(Rows.ContextMenu, Is.SameAs(placeholder));
        Assert.That(placeholder.IsOpen, Is.False);
    }

    [Test]
    public void ArchivedAndUnavailableFramesKeepTheExistingRowMenuAndRunningCountPolicy(
        [Values(false, true)] bool archived, [Values(false, true)] bool available)
    {
        Show(Frame(App("alpha")) with { Archived = archived, Available = available });
        Rows.SelectedIndex = 0;
        string? copied = null;
        _view.CopyRequested += text => copied = text;

        Assert.That(OpenViaApps(Rows).Handled, Is.True);

        var menu = Rows.ContextMenu;
        Assert.That(menu.Items.OfType<MenuItem>().Select(item => item.IsEnabled), Is.EqualTo(new[] { true, true, false }));
        menu.IsOpen = false;
        Click(menu, Strings.CopyDetails);
        Assert.That(copied, Does.Contain($"{Strings.ProcessesTab}: 3 ({(archived ? Strings.MissingReading : "2")})"));
        Assert.That(copied, Does.Contain("original frame"));
    }

    [Test]
    public void AggregateUsageMenuDisablesNavigationWithoutQueryingEligibility()
    {
        Show(Frame(App("aggregate") with { Key = UsageLedger.OtherAppsKey }));
        Rows.SelectedIndex = 0;
        _view.CanShowInProcesses = _ => throw new AssertionException("An aggregate has no process-navigation decision.");

        Assert.That(OpenViaApps(Rows).Handled, Is.True);

        Assert.That(Rows.ContextMenu.Items.OfType<MenuItem>().Select(item => item.IsEnabled),
            Is.EqualTo(new[] { false, true, false }));
    }

    [Test]
    public void ReopeningUsageMenuTakesANewSnapshotWhileThePreviousMenuRetainsItsAppAndPeriod()
    {
        var alpha = App("alpha");
        var beta = App("beta", 30);
        Show(Frame(alpha, beta));
        Rows.SelectedIndex = 0;
        OpenViaApps(Rows);
        var first = Rows.ContextMenu;
        first.IsOpen = false;
        _view.Update(Frame(alpha with { CpuTicks = TimeSpan.FromMinutes(2).Ticks }, beta) with
            { Since = Start.AddDays(1), Latest = Start.AddDays(1).AddMinutes(5), Notice = "replacement frame" });
        Rows.SelectedIndex = 1;

        Assert.That(OpenViaApps(Rows).Handled, Is.True);

        var second = Rows.ContextMenu;
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second.IsOpen, Is.True);
        second.IsOpen = false;
        string? copied = null;
        _view.CopyRequested += text => copied = text;
        Click(second, Strings.CopyDetails);
        Assert.That(copied, Does.StartWith("Fixture beta" + Environment.NewLine).And.Contain("replacement frame"));
        Click(first, Strings.CopyDetails);
        Assert.That(copied, Does.StartWith("Fixture alpha" + Environment.NewLine).And.Contain("original frame"));
        Assert.That(copied, Does.Contain($"{Strings.UsageCpuTime}: 0:01:00").And.Not.Contain("replacement frame"));
    }

    [Test]
    public void ThrowingUsageEligibilityLeavesThePlaceholderIntactAndTheNextRequestCanRetry()
    {
        Show(Frame(App("alpha")));
        Rows.SelectedIndex = 0;
        var placeholder = Rows.ContextMenu;
        var failure = new InvalidOperationException("usage eligibility fixture");
        _view.CanShowInProcesses = _ => throw failure;

        Assert.That(Assert.Throws<InvalidOperationException>(() => OpenViaApps(Rows)), Is.SameAs(failure));

        Assert.That(Rows.ContextMenu, Is.SameAs(placeholder));
        Assert.That(placeholder.IsOpen, Is.False);
        _view.CanShowInProcesses = _ => false;
        Assert.That(OpenViaApps(Rows).Handled, Is.True);
        Assert.That(Rows.ContextMenu.IsOpen, Is.True);
        Assert.That(Entry(Rows.ContextMenu, Strings.UsageShowInProcesses).IsEnabled, Is.False);
    }

    [Test]
    public void ThrowingUsageMenuObserverSeesTheReplacementBeforeOpeningAndCanRetry()
    {
        Show(Frame(App("alpha")));
        Rows.SelectedIndex = 0;
        var placeholder = Rows.ContextMenu;
        var failure = new InvalidOperationException("usage menu observer fixture");
        var notifications = 0;
        void Observe(object sender, ContextMenuEventArgs args)
        {
            notifications++;
            Assert.That(Rows.ContextMenu, Is.Not.SameAs(placeholder));
            Assert.That(Rows.ContextMenu.IsOpen, Is.False);
            throw failure;
        }
        Rows.ContextMenuOpening += Observe;
        try { Assert.That(Assert.Throws<InvalidOperationException>(() => OpenViaApps(Rows)), Is.SameAs(failure)); }
        finally { Rows.ContextMenuOpening -= Observe; }
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(OpenViaApps(Rows).Handled, Is.True);
        Assert.That(Rows.ContextMenu.IsOpen, Is.True);
    }
}
