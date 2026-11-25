using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration"), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class UsageSortingCharacterizationTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 14, 0, 0, TimeSpan.Zero);
    private UsageView _view = null!;

    [SetUp]
    public void SetUp() => _view = new UsageView();

    [TearDown]
    public void TearDown() => _view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

    private TextBlock[] Headers => [_view.CpuHeader, _view.IoHeader, _view.GpuHeader, _view.ProcessesHeader, _view.LastActiveHeader];
    private string[] Labels => [Strings.UsageCpuTime, Strings.IO, Strings.UsageGpuTime, Strings.ProcessesTab, Strings.UsageLastActive];

    private static AppUsage App(string key, string name, long cpu, double io, double gpu, int lifetimes, int minute, bool isApp = true) =>
        new(key, name, "", "", "", cpu, io, gpu, lifetimes, 0, isApp, new SampleKey(0, Start.AddMinutes(minute)));

    private static UsageFrame Frame() => new([
        App("a", "candidate A", 30, 10, 1, 1, 1),
        App("b", "candidate B", 20, 30, 0, 3, 2),
        App("c", "candidate C", 10, 20, 3, 2, 3),
        App("background", "candidate background", 1000, 1000, 1000, 1000, 9, false),
        App("nonmatch", "excluded application", 1000, 1000, 1000, 1000, 9)
    ], 10000, 0, Start, Start.AddMinutes(10), false);

    private MouseButtonEventArgs Click(int index)
    {
        var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent };
        Headers[index].RaiseEvent(e);
        return e;
    }

    private string[] RowKeys => _view.Rows.Items.Cast<UsageDisplayRow>().Select(row => row.Key).ToArray();

    [TestCase(0, "a,b,c")]
    [TestCase(1, "b,c,a")]
    [TestCase(2, "c,a,b")]
    [TestCase(3, "b,c,a")]
    [TestCase(4, "c,b,a")]
    public void EachRankHeaderPublishesItsSortAndReordersRowsWithoutChangingFiltersOrSelection(int sortIndex, string expected)
    {
        var sort = (UsageSort)sortIndex;
        _view.Load(new UsageListSettings(5, sort == UsageSort.CpuTime ? UsageSort.LastActive : UsageSort.CpuTime, true));
        _view.Search.Text = "candidate";
        _view.Update(Frame());
        var rows = _view.Rows.Items.Cast<UsageDisplayRow>().ToDictionary(row => row.Key);
        _view.Rows.SelectedItem = rows["b"];
        var changes = new List<UsageListSettings>();
        _view.SettingsChanged += changes.Add;
        var e = Click((int)sort);
        Assert.Multiple(() =>
        {
            Assert.That(e.Handled, Is.True);
            Assert.That(_view.Settings, Is.EqualTo(new UsageListSettings(5, sort, true)));
            Assert.That(changes, Is.EqualTo(new[] { new UsageListSettings(5, sort, true) }));
            Assert.That(_view.Search.Text, Is.EqualTo("candidate"));
            Assert.That(RowKeys, Is.EqualTo(expected.Split(',')));
            Assert.That(_view.Rows.SelectedItem, Is.SameAs(rows["b"]));
            foreach (var row in _view.Rows.Items.Cast<UsageDisplayRow>()) Assert.That(row, Is.SameAs(rows[row.Key]));
            for (var i = 0; i < Headers.Length; i++)
            {
                Assert.That(Headers[i].Text, Is.EqualTo(Labels[i] + (i == (int)sort ? " ▾" : "")));
                Assert.That(Headers[i].FontWeight, Is.EqualTo(i == (int)sort ? FontWeights.SemiBold : FontWeights.Normal));
            }
        });
    }

    [Test]
    public void ClickingTheActiveRankHeaderConsumesInputWithoutReversingOrderOrPublishingAnotherChange()
    {
        _view.Load(new UsageListSettings(0, UsageSort.Io, false));
        _view.Update(Frame());
        var rows = _view.Rows.Items.Cast<UsageDisplayRow>().ToArray();
        _view.Rows.SelectedItem = rows[^1];
        var changes = 0;
        _view.SettingsChanged += _ => changes++;
        var first = Click((int)UsageSort.Io);
        var second = Click((int)UsageSort.Io);
        Assert.Multiple(() =>
        {
            Assert.That(first.Handled, Is.True);
            Assert.That(second.Handled, Is.True);
            Assert.That(changes, Is.Zero);
            Assert.That(_view.Settings, Is.EqualTo(new UsageListSettings(0, UsageSort.Io, false)));
            Assert.That(_view.Rows.Items.Cast<UsageDisplayRow>().ToArray(), Is.EqualTo(rows));
            Assert.That(_view.Rows.SelectedItem, Is.SameAs(rows[^1]));
        });
    }

    [Test]
    public void RankChosenBeforeTheFirstFrameAppliesToThatFrameAndSurvivesLaterRefreshes()
    {
        _view.Load(new UsageListSettings(5, UsageSort.CpuTime, true));
        _view.Search.Text = "candidate";
        var changes = new List<UsageListSettings>();
        _view.SettingsChanged += changes.Add;
        var e = Click((int)UsageSort.GpuTime);
        Assert.Multiple(() =>
        {
            Assert.That(e.Handled, Is.True);
            Assert.That(_view.Rows.Items, Is.Empty);
            Assert.That(changes, Is.EqualTo(new[] { new UsageListSettings(5, UsageSort.GpuTime, true) }));
        });
        _view.Update(Frame());
        Assert.That(RowKeys, Is.EqualTo(new[] { "c", "a", "b" }));
        var changed = Frame() with { Apps = Frame().Apps.Select(app => app.Key == "b" ? app with { GpuSeconds = 100 } : app).ToArray() };
        _view.Update(changed);
        Assert.Multiple(() =>
        {
            Assert.That(RowKeys, Is.EqualTo(new[] { "b", "c", "a" }));
            Assert.That(_view.Settings, Is.EqualTo(new UsageListSettings(5, UsageSort.GpuTime, true)));
            Assert.That(_view.Search.Text, Is.EqualTo("candidate"));
            Assert.That(changes, Has.Count.EqualTo(1));
        });
    }
}
