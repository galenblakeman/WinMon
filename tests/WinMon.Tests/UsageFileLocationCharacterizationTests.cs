using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration"), SetUICulture("en-US")]
public sealed class UsageFileLocationCharacterizationTests
{
    private readonly List<string> _addedResources = [];
    private UsageView _view = null!;
    private Window _window = null!;
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch.AddDays(1);

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var (key, value) in new (string, object)[]
                 { ("CaptionButton", new Style(typeof(Button))), ("Surface", Brushes.White), ("Text", Brushes.Black) })
            if (!app.Resources.Contains(key)) { app.Resources[key] = value; _addedResources.Add(key); }
        _view = new UsageView();
        _window = new Window { Content = _view, Width = 900, Height = 600, ShowInTaskbar = false,
            Title = "Usage file-location fixture" };
        _window.Show();
    }

    [TearDown]
    public void TearDown()
    {
        _window?.Close();
        _view?.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
        _addedResources.Clear();
    }

    private static AppUsage App(string path) => new("fixture-app", "fixture.exe", path, "Fixture app",
        "Fixture publisher", TimeSpan.FromSeconds(60).Ticks, 0, 12, 3, 2, true, null);

    private void Select(AppUsage app, bool archived = false)
    {
        // Finish icon loading before binding the row so this fixture leaves no deferred icon update.
        _ = ProcessIconProvider.GetForPathAsync(app.ImagePath).GetAwaiter().GetResult();
        _view.Update(new UsageFrame([app], TimeSpan.FromSeconds(300).Ticks, 0, Start, Start.AddMinutes(5), true)
            { Archived = archived });
        _view.Rows.SelectedItem = _view.Rows.Items.Cast<UsageDisplayRow>().Single();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Descendants(child)) yield return nested;
    }

    private void Invoke(bool expectNotice)
    {
        var existing = Application.Current.Windows.Cast<Window>().ToHashSet();
        DispatcherOperation? watch = null;
        Exception? failure = null;
        var count = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Send, _window.Dispatcher)
            { Interval = TimeSpan.FromSeconds(10) };
        MessageDialog[] Notices() => Application.Current.Windows.OfType<MessageDialog>()
            .Where(notice => !existing.Contains(notice)).ToArray();
        void Watch()
        {
            foreach (var notice in Notices())
            {
                count++;
                try
                {
                    Assert.That(expectNotice, Is.True, "An ineligible selection opened a file-location notice");
                    Assert.That(notice.Owner, Is.SameAs(_window));
                    Assert.That(notice.Title, Is.EqualTo(Strings.OpenFileLocation));
                    Assert.That(Descendants((DependencyObject)notice.Content).OfType<TextBlock>().Select(text => text.Text),
                        Is.EqualTo(new[] { Strings.ProcessFileMissing }));
                    var close = Descendants((DependencyObject)notice.Content).OfType<Button>().Single();
                    Assert.That(close.Content, Is.EqualTo(Strings.Close));
                    Assert.That(close.IsDefault && close.IsCancel, Is.True);
                    close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception error) { failure ??= error; }
                finally { if (notice.IsVisible) notice.Close(); }
            }
            watch = _window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Watch));
        }
        timer.Tick += (_, _) =>
        {
            failure ??= new AssertionException("File-location notice exceeded the owned dialog bound");
            foreach (var notice in Notices()) notice.Close();
        };
        try
        {
            watch = _window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Watch));
            timer.Start();
            _view.OpenFileLocation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (failure is not null) throw failure;
            Assert.That(count, Is.EqualTo(expectNotice ? 1 : 0));
            Assert.That(Notices(), Is.Empty);
        }
        finally
        {
            timer.Stop();
            watch?.Abort();
            foreach (var notice in Notices()) notice.Close();
        }
    }

    [TestCase("none")]
    [TestCase("uninitialized")]
    [TestCase("empty")]
    [TestCase("null")]
    [TestCase("other")]
    [TestCase("filtered")]
    public void StaleFileLocationActionsIgnoreSelectionsWithoutAnEligibleExecutable(string kind)
    {
        var missing = Path.Combine(Path.GetTempPath(), "WinMon-location-" + Guid.NewGuid().ToString("N"), "missing.exe");
        Assert.That(File.Exists(missing), Is.False);
        if (kind is "uninitialized" or "null")
        {
            var row = new UsageDisplayRow("fixture-app");
            if (kind == "null")
                Assert.Throws<NullReferenceException>(() => row.Update(App(null!), 1, Start));
            _view.Rows.ItemsSource = new[] { row };
            _view.Rows.SelectedItem = row;
        }
        else if (kind != "none")
        {
            Select(App(kind == "empty" ? "" : missing) with
                { Key = kind == "other" ? UsageLedger.OtherAppsKey : "fixture-app" });
            if (kind == "filtered") _view.Search.Text = "no matching fixture";
        }
        var selection = _view.Rows.SelectedItem;
        var rows = _view.Rows.Items.Cast<object>().ToArray();
        var search = _view.Search.Text;
        Invoke(false);
        Assert.That(_view.Rows.SelectedItem, Is.SameAs(selection));
        Assert.That(_view.Rows.Items.Cast<object>(), Is.EqualTo(rows));
        Assert.That(_view.Search.Text, Is.EqualTo(search));
    }

    [TestCase("missing", false)]
    [TestCase("missing", true)]
    [TestCase("whitespace", false)]
    [TestCase("nul", false)]
    public void MissingFileLocationShowsOneLocalizedNoticeWithoutChangingTheSelectedUsage(string kind, bool archived)
    {
        var path = kind switch
        {
            "whitespace" => " \t ", "nul" => "\0",
            _ => Path.Combine(Path.GetTempPath(), "WinMon-location-" + Guid.NewGuid().ToString("N"), "missing.exe")
        };
        Assert.That(File.Exists(path), Is.False, "These cases must never reach Explorer launch");
        var app = App(path);
        Select(app, archived);
        var row = (UsageDisplayRow)_view.Rows.SelectedItem;
        var selectedName = _view.SelectionName.Text;
        Assert.That(_view.OpenFileLocation.IsEnabled, Is.True);
        Invoke(true);
        Assert.That(_view.Rows.SelectedItem, Is.SameAs(row));
        Assert.That(row.App, Is.SameAs(app));
        Assert.That(_view.SelectionName.Text, Is.EqualTo(selectedName));
        Assert.That(_view.OpenFileLocation.IsEnabled, Is.True);
        Assert.That(_window.IsEnabled, Is.True);
        Assert.That(File.Exists(path), Is.False);
    }
}
