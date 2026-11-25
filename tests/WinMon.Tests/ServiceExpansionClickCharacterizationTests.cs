using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ServiceExpansionClickCharacterizationTests
{
    private ServicesView _view = null!;
    private bool _addedCaptionStyle;
    private ListBox Rows => (ListBox)_view.FindName("Rows");
    private string[] Keys => Rows.Items.Cast<ServiceDisplayRow>().Select(row => row.Key).ToArray();

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (!app.Resources.Contains("CaptionButton"))
        {
            app.Resources["CaptionButton"] = new Style(typeof(Button));
            _addedCaptionStyle = true;
        }
        _view = new ServicesView { TopServiceCount = 0 };
        _view.RestoreFilters(0, 0, true);
        _view.ActionRequested += (_, _, _) => Assert.Fail("Expansion must not request a native service action.");
        var groups = new[] { Group(100, "Alpha"), Group(200, "Beta") };
        var frame = ServiceRankingTests.Frame(1, ServiceRankingTests.P(1, 100, 0, 0, 0)) with { Groups = groups };
        _view.Update(frame, [frame], false);
        Assert.That(Keys, Is.EqualTo(new[] { "host:100:1000", "host:200:1000" }));
    }

    [TearDown]
    public void TearDown()
    {
        _view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        if (_addedCaptionStyle) Application.Current.Resources.Remove("CaptionButton");
        _addedCaptionStyle = false;
    }

    private static ServiceGroup Group(int pid, string name)
    {
        var host = ServiceRankingTests.P(pid, 1000, 1, 100, 1000);
        return new($"host:{pid}:1000", [new(name, name, 4, pid, 1)], host, [host]);
    }

    [TestCase("null-sender")]
    [TestCase("ordinary-object")]
    [TestCase("missing-context")]
    [TestCase("wrong-context")]
    public void InvalidExpansionSendersLeaveRowsAndSelectionUntouched(string partition)
    {
        var selected = Rows.Items[0];
        Rows.SelectedItem = selected;
        var original = Rows.Items.Cast<object>().ToArray();
        object? sender = partition switch
        {
            "null-sender" => null,
            "ordinary-object" => new object(),
            "missing-context" => new Button(),
            "wrong-context" => new Button { DataContext = "not a service row" },
            _ => throw new ArgumentOutOfRangeException(nameof(partition))
        };
        // Malformed routed-event senders cannot be produced by the row template.
        typeof(ServicesView).GetMethod("OnExpand", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(_view, [sender, new RoutedEventArgs(Button.ClickEvent)]);
        Assert.That(Rows.Items.Cast<object>(), Is.EqualTo(original));
        Assert.That(Rows.SelectedItem, Is.SameAs(selected));
    }

    [Test]
    public void TemplateClicksToggleOnlyTheirGroupAndReuseItsHeader()
    {
        var alpha = (ServiceDisplayRow)Rows.Items[0];
        var beta = (ServiceDisplayRow)Rows.Items[1];
        Click(alpha);
        Assert.That(Keys, Is.EqualTo(new[] { alpha.Key, "process:100:1000", beta.Key }));
        Assert.That(Rows.Items[0], Is.SameAs(alpha));
        Click(beta);
        Assert.That(Keys, Is.EqualTo(new[] { alpha.Key, "process:100:1000", beta.Key, "process:200:1000" }));
        Click(alpha);
        Assert.That(Keys, Is.EqualTo(new[] { alpha.Key, beta.Key, "process:200:1000" }));
        Assert.That(Rows.Items[0], Is.SameAs(alpha));
        Assert.That(Rows.Items[1], Is.SameAs(beta));
        Click(beta);
        Assert.That(Keys, Is.EqualTo(new[] { alpha.Key, beta.Key }));
        Click(alpha);
        Assert.That(Keys, Is.EqualTo(new[] { alpha.Key, "process:100:1000", beta.Key }));
    }

    private void Click(ServiceDisplayRow row)
    {
        var content = (FrameworkElement)Rows.ItemTemplate.LoadContent();
        content.DataContext = row;
        var panel = ((StackPanel)((Border)content).Child).Children.OfType<Grid>().Single();
        var dock = panel.Children.OfType<DockPanel>().Single();
        var button = dock.Children.OfType<Button>().First();
        Assert.That(button.DataContext, Is.SameAs(row));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
