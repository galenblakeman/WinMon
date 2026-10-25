using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, Apartment(ApartmentState.STA), SetUICulture("en")]
public sealed class ProcessActionBarTests
{
    private static ProcessDetailsSnapshot Snapshot => new(new(123, 456, "example.exe", 2) { ImagePath = @"C:\Apps\example.exe" }, false, DateTimeOffset.Now, false, true, "");

    [Test]
    public void MoreMenuKeepsCapturedTargetWhenSelectionChanges()
    {
        var bar = new ProcessActionBar(); var original = Snapshot;
        bar.Update(original, true, true);
        var menu = bar.CreateMoreMenu()!;
        Assert.That(menu.Items.OfType<MenuItem>().Any(i => Equals(i.Header, Strings.CopyDetails)), Is.False);
        ProcessDetailsSnapshot? captured = null;
        bar.ActionRequested += (_, snapshot) => captured = snapshot;
        var current = original with { Process = original.Process with { ProcessId = 999 } };
        bar.Update(current, true, true);
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(captured, Is.SameAs(original));
        }
        Buttons(bar).Single(b => Equals(b.Content, Strings.CopyDetails)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(captured, Is.SameAs(current));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void HistoricalOrUnavailableSelectionDisablesEndAndDump(bool historical, bool unavailable)
    {
        var bar = new ProcessActionBar();
        bar.Update(Snapshot with { Historical = historical, Available = !unavailable }, true, true);
        Assert.That(Buttons(bar).Single(b => Equals(b.Content, Strings.EndProcess)).IsEnabled, Is.False);
        Assert.That(bar.CreateMoreMenu()!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, Strings.CreateDumpFile + "…")).IsEnabled, Is.False);
        Assert.That(Buttons(bar).Single(b => Equals(b.Content, Strings.Properties)).IsEnabled, Is.True);
    }

    [TestCase(560)]
    [TestCase(1000)]
    public void ActionButtonsStayInsideBar(double width)
    {
        var bar = new ProcessActionBar(); bar.Update(Snapshot, true, true);
        bar.Measure(new Size(width, double.PositiveInfinity)); bar.Arrange(new Rect(0, 0, width, bar.DesiredSize.Height)); bar.UpdateLayout();
        foreach (var button in Buttons(bar))
        {
            if (button.Visibility == Visibility.Collapsed) continue;
            var bounds = button.TransformToAncestor(bar).TransformBounds(new Rect(button.RenderSize));
            Assert.That(bounds.Right, Is.LessThanOrEqualTo(width));
            Assert.That(bounds.Bottom, Is.LessThanOrEqualTo(bar.ActualHeight));
        }
    }

    private static IEnumerable<Button> Buttons(DependencyObject element)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            if (child is Button button) yield return button;
            else foreach (var nested in Buttons(child)) yield return nested;
        }
    }
}
