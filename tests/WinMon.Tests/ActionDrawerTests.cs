using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, Apartment(ApartmentState.STA), SetUICulture("en")]
public sealed class ActionDrawerTests
{
    private static ProcessDetailsSnapshot Snapshot => new(new(123, 456, "example.exe", 2) { ImagePath = @"C:\Apps\example.exe" }, false, DateTimeOffset.Now, false, true, "");

    [Test]
    public void CollapsingLeavesAStripNamingTheSelectionAndRestoresTheSameBar()
    {
        var (drawer, bar) = Drawer();
        bar.Update(Snapshot, true, true);
        drawer.Summarize(bar.Title, bar.Detail, true);
        var expanded = Height(drawer);
        drawer.SetCollapsed(true);
        var collapsed = Height(drawer);
        Assert.Multiple(() =>
        {
            Assert.That(bar.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(drawer.Peek.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(drawer.PeekTitle, Is.EqualTo("example.exe"));
            Assert.That(collapsed, Is.GreaterThan(0).And.LessThan(expanded - 20));
        });
        bar.Update(Snapshot with { Process = Snapshot.Process with { Name = "other.exe" } }, true, true);
        drawer.Summarize(bar.Title, bar.Detail, true);
        Assert.That(drawer.PeekTitle, Is.EqualTo("other.exe"));
        drawer.SetCollapsed(false);
        Assert.Multiple(() =>
        {
            Assert.That(bar.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(drawer.Peek.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(Height(drawer), Is.EqualTo(expanded));
        });
    }

    [Test]
    public void ChevronCommandCollapsesAndShowActionsRestoresReportingEachChange()
    {
        var (drawer, bar) = Drawer();
        var changes = new List<bool>();
        drawer.CollapsedChanged += changes.Add;
        Assert.That(ActionDrawer.CollapseCommand.CanExecute(null, bar), Is.True);
        ActionDrawer.CollapseCommand.Execute(null, bar);
        Assert.Multiple(() =>
        {
            Assert.That(drawer.Collapsed, Is.True);
            Assert.That(bar.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(drawer.ShowButton.Content, Is.EqualTo(Strings.ShowActions));
            Assert.That(ActionDrawer.CollapseCommand.CanExecute(null, bar), Is.False);
        });
        drawer.ShowButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.That(bar.Visibility, Is.EqualTo(Visibility.Visible));
        ActionDrawer.CollapseCommand.Execute(null, bar);
        Assert.That(changes, Is.EqualTo(new[] { true, false, true }));
        // Restoring the saved state is not a user change.
        drawer.SetCollapsed(false);
        Assert.That(changes, Has.Count.EqualTo(3));
        Assert.That(bar.Visibility, Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void NoSelectionShowsThePromptInTheStrip()
    {
        var (drawer, bar) = Drawer();
        bar.Update(null, false, false);
        drawer.Summarize(bar.Title, bar.Detail, false);
        drawer.SetCollapsed(true);
        Assert.Multiple(() =>
        {
            Assert.That(drawer.PeekTitle, Is.EqualTo(Strings.SelectProcess));
            Assert.That(drawer.Peek.Visibility, Is.EqualTo(Visibility.Visible));
        });
    }

    [Test]
    public void HiddenStateSurvivesSelectionChangesAndActionsWorkAfterRestoring()
    {
        var (drawer, bar) = Drawer();
        bar.Update(Snapshot, true, true);
        drawer.SetCollapsed(true);
        bar.Update(Snapshot with { Process = Snapshot.Process with { ProcessId = 999 } }, true, true);
        Assert.That(drawer.Collapsed, Is.True);
        ProcessDetailsSnapshot? captured = null;
        bar.ActionRequested += (_, snapshot) => captured = snapshot;
        drawer.SetCollapsed(false);
        LogicalButtons(bar).Single(b => Equals(b.Content, Strings.CopyDetails))
            .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.That(captured?.Process.ProcessId, Is.EqualTo(999));
    }

    private static (ActionDrawer Drawer, ProcessActionBar Bar) Drawer()
    {
        var bar = new ProcessActionBar();
        return (new ActionDrawer { Bar = bar }, bar);
    }

    private static double Height(UIElement element)
    {
        // Layout carries a child's new size up to the drawer; a bare Measure returns the cached size.
        element.UpdateLayout();
        element.Measure(new Size(800, double.PositiveInfinity));
        return element.DesiredSize.Height;
    }

    private static IEnumerable<Button> LogicalButtons(DependencyObject element)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            if (child is Button button) yield return button;
            else foreach (var nested in LogicalButtons(child)) yield return nested;
        }
    }
}
