using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase(1400), TestCase(1600)]
    public void ProcessToolbarDiagnosticAcceptsRealInlineLayoutsWithoutChangingThem(double width)
    {
        using var fixture = new ProcessToolbarDiagnostic(_directory, width);
        var before = fixture.Snapshot();

        Assert.DoesNotThrow(fixture.Check);

        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
        Assert.That(fixture.Controls.All(control => Grid.GetRow(control) == 0), Is.True);
        Assert.That(fixture.Window.Processes.ProcessSearchBorder.ActualWidth,
            Is.GreaterThanOrEqualTo(fixture.Window.Processes.ProcessSearchBorder.MinWidth));
    }

    [Test]
    public void ProcessToolbarDiagnosticDisabledGuardIgnoresInvalidRowsAndMinimumWidth()
    {
        using var fixture = new ProcessToolbarDiagnostic(_directory);
        fixture.Window.InlinePinToolbar = false;
        Grid.SetRow(fixture.Controls[0], 1);
        fixture.Window.Processes.ProcessSearchBorder.MinWidth = fixture.Window.Processes.ProcessSearchBorder.ActualWidth + 1;
        var before = fixture.Snapshot();

        Assert.DoesNotThrow(fixture.Check);

        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
        Assert.That(fixture.Window.InlinePinToolbar, Is.False);
    }

    [Test]
    public void ProcessToolbarDiagnosticRejectsWrappingWithTheOriginalControlAndWidthMessage()
    {
        using var fixture = new ProcessToolbarDiagnostic(_directory);
        var control = fixture.Controls[0];
        Grid.SetRow(control, 1);
        var before = fixture.Snapshot();

        var error = Assert.Throws<InvalidOperationException>(fixture.Check);

        Assert.That(error!.Message, Is.EqualTo(fixture.LayoutFailure(control)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [TestCase("left", -0.5001, true), TestCase("left", -0.5, false), TestCase("left", -0.4999, false)]
    [TestCase("overlap", -0.5001, true), TestCase("overlap", -0.5, false), TestCase("overlap", -0.4999, false)]
    [TestCase("right", 0.4999, false), TestCase("right", 0.5, false), TestCase("right", 0.5001, true)]
    [TestCase("top", -0.5001, true), TestCase("top", -0.5, false), TestCase("top", -0.4999, false)]
    [TestCase("bottom", 0.4999, false), TestCase("bottom", 0.5, false), TestCase("bottom", 0.5001, true)]
    public void ProcessToolbarDiagnosticKeepsItsInclusiveHalfPixelLayoutTolerance(string edge, double offset, bool rejected)
    {
        using var fixture = new ProcessToolbarDiagnostic(_directory);
        var control = edge == "overlap" ? fixture.Controls[1] : edge == "right" ? fixture.Controls[^1] : fixture.Controls[0];
        var bounds = fixture.Bounds(control);
        var dx = edge switch
        {
            "left" => offset - bounds.Left,
            "overlap" => fixture.Bounds(fixture.Controls[0]).Right + offset - bounds.Left,
            "right" => fixture.Grid.ActualWidth + offset - bounds.Right,
            _ => 0
        };
        var dy = edge switch
        {
            "top" => offset - bounds.Top,
            "bottom" => fixture.Grid.ActualHeight + offset - bounds.Bottom,
            _ => 0
        };
        control.RenderTransform = new TranslateTransform(dx, dy);
        fixture.Window.Processes.UpdateLayout();
        var moved = fixture.Bounds(control);
        Assert.That(moved.Left, Is.EqualTo(bounds.Left + dx).Within(1e-8), "Apply the fixture transform before testing diagnostic bounds.");
        Assert.That(moved.Top, Is.EqualTo(bounds.Top + dy).Within(1e-8));
        var before = fixture.Snapshot();

        if (rejected)
            Assert.That(Assert.Throws<InvalidOperationException>(fixture.Check)!.Message, Is.EqualTo(fixture.LayoutFailure(control)));
        else Assert.DoesNotThrow(fixture.Check);

        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public void ProcessToolbarDiagnosticRetainsItsMinimumSearchWidthCheckForAStaleLayout()
    {
        using var fixture = new ProcessToolbarDiagnostic(_directory);
        var search = fixture.Window.Processes.ProcessSearchBorder;
        // A property invalidation need not have completed a layout pass when diagnostics read it.
        search.MinWidth = search.ActualWidth + 1;
        var before = fixture.Snapshot();

        var error = Assert.Throws<InvalidOperationException>(fixture.Check);

        Assert.That(error!.Message, Is.EqualTo("The compact process toolbar lost its minimum search width."));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public void ProcessToolbarDiagnosticRetainsTheOriginalGeometryFailureForADetachedControl()
    {
        using var fixture = new ProcessToolbarDiagnostic(_directory);
        var search = fixture.Window.Processes.ProcessSearchBorder;
        fixture.Grid.Children.Remove(search);

        var error = Assert.Throws<InvalidOperationException>(fixture.Check);

        Assert.That(error!.Message, Is.EqualTo("The specified Visual is not an ancestor of this Visual."));
        Assert.That(search.Parent, Is.Null);
        Assert.That(fixture.Grid.Children.Contains(search), Is.False);
    }

    private sealed class ProcessToolbarDiagnostic : IDisposable
    {
        private readonly UpdateUi _ui;
        internal MainWindow Window => _ui.Window;
        internal Grid Grid => Window.Processes.ProcessToolbarGrid;
        internal FrameworkElement[] Controls { get; }
        internal Action Check { get; }

        internal ProcessToolbarDiagnostic(string directory, double width = 1600)
        {
            _ui = new UpdateUi(directory, true);
            Window.ApplyTheme(false, false);
            Window.InlinePinToolbar = true;
            Window.Processes.ArrangeToolbar(true, true);
            Controls = [Window.Processes.ProcessRankingControls, Window.Processes.ProcessSearchBorder,
                Window.Processes.AppsOnlyCheckBox, Window.Processes.PinnedOnlyCheckBox, Window.Processes.ProcessGroupingControls];
            Window.Processes.Measure(new Size(width, 300));
            Window.Processes.Arrange(new Rect(0, 0, width, 300));
            Window.Processes.UpdateLayout();
            Check = typeof(WindowDiagnostics).GetMethod("ValidateProcessToolbar", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action>(new WindowDiagnostics(Window));
            Assert.That(Grid.ActualWidth, Is.GreaterThan(0));
            Assert.That(Grid.ActualHeight, Is.GreaterThan(0));
            Assert.That(Controls.All(control => control.ActualWidth > 0), Is.True);
        }

        internal Rect Bounds(FrameworkElement control) => control.TransformToAncestor(Grid).TransformBounds(new Rect(control.RenderSize));
        internal (string Name, int Row, Rect Bounds, double MinimumWidth)[] Snapshot() => Controls.Select(control =>
            (control.Name, System.Windows.Controls.Grid.GetRow(control), Bounds(control), control.MinWidth)).ToArray();
        internal string LayoutFailure(FrameworkElement control) =>
            $"Process toolbar control {control.Name} overlaps, clips, or wraps at width {Grid.ActualWidth}.";
        public void Dispose()
        {
            Assert.That(Window.Session.HasLiveSample, Is.False);
            _ui.Dispose();
        }
    }
}
