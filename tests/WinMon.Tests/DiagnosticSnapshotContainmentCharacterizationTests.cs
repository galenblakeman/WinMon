using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test]
    public void SnapshotDiagnosticAcceptsContainedActionsWithoutChangingGeometry()
    {
        using var fixture = new SnapshotContainmentDiagnostic(_directory);
        fixture.Verify(false);
        fixture.Verify(false);
    }

    [TestCase("left", -0.001, true), TestCase("left", 0, false), TestCase("left", 0.001, false)]
    [TestCase("right", -0.001, false), TestCase("right", 0, false), TestCase("right", 0.001, true)]
    [TestCase("top", -0.001, true), TestCase("top", 0, false), TestCase("top", 0.001, false)]
    [TestCase("bottom", -0.001, false), TestCase("bottom", 0, false), TestCase("bottom", 0.001, true)]
    public void SnapshotDiagnosticPreservesInclusivePlotContainment(string edge, double offset, bool rejected)
    {
        using var fixture = new SnapshotContainmentDiagnostic(_directory);
        fixture.MoveToEdge(fixture.Panel, fixture.Plot, edge, offset);
        // Move the actions with the panel, preserving all later guards.
        var panel = fixture.Bounds(fixture.Panel);
        fixture.Place(fixture.Live, panel.Left + 10, panel.Top + 10);
        fixture.Place(fixture.Copy, panel.Left + 100, panel.Top + 10);
        fixture.Verify(rejected);
    }

    [TestCase("live", "left", -0.001, true), TestCase("live", "left", 0, false), TestCase("live", "left", 0.001, false)]
    [TestCase("live", "right", -0.001, false), TestCase("live", "right", 0, false), TestCase("live", "right", 0.001, true)]
    [TestCase("live", "top", -0.001, true), TestCase("live", "top", 0, false), TestCase("live", "top", 0.001, false)]
    [TestCase("live", "bottom", -0.001, false), TestCase("live", "bottom", 0, false), TestCase("live", "bottom", 0.001, true)]
    [TestCase("copy", "left", -0.001, true), TestCase("copy", "left", 0, false), TestCase("copy", "left", 0.001, false)]
    [TestCase("copy", "right", -0.001, false), TestCase("copy", "right", 0, false), TestCase("copy", "right", 0.001, true)]
    [TestCase("copy", "top", -0.001, true), TestCase("copy", "top", 0, false), TestCase("copy", "top", 0.001, false)]
    [TestCase("copy", "bottom", -0.001, false), TestCase("copy", "bottom", 0, false), TestCase("copy", "bottom", 0.001, true)]
    public void SnapshotDiagnosticPreservesInclusiveActionContainment(string action, string edge, double offset, bool rejected)
    {
        using var fixture = new SnapshotContainmentDiagnostic(_directory);
        fixture.MoveToEdge(action == "live" ? fixture.Live : fixture.Copy, fixture.Bounds(fixture.Panel), edge, offset);
        fixture.Verify(rejected);
    }

    [TestCase("live", 23.999, true), TestCase("live", 24, false), TestCase("live", 24.001, false)]
    [TestCase("copy", 23.999, true), TestCase("copy", 24, false), TestCase("copy", 24.001, false)]
    public void SnapshotDiagnosticPreservesTheExactMinimumActionHeight(string action, double height, bool rejected)
    {
        using var fixture = new SnapshotContainmentDiagnostic(_directory);
        var control = action == "live" ? fixture.Live : fixture.Copy;
        control.Height = height;
        fixture.Layout();
        Assert.That(control.ActualHeight, Is.EqualTo(height).Within(1e-8));
        fixture.Verify(rejected);
    }

    [TestCase("panel"), TestCase("live"), TestCase("copy")]
    public void SnapshotDiagnosticPreservesDetachedVisualFailures(string element)
    {
        using var fixture = new SnapshotContainmentDiagnostic(_directory);
        FrameworkElement control = element == "panel" ? fixture.Panel : element == "live" ? fixture.Live : fixture.Copy;
        fixture.Layer.Children.Remove(control);
        var error = Assert.Throws<InvalidOperationException>(fixture.Check);
        Assert.That(error!.Message, Is.EqualTo("The specified Visual is not an ancestor of this Visual."));
        Assert.That(control.Parent, Is.Null);
    }

    private sealed class SnapshotContainmentDiagnostic : IDisposable
    {
        private readonly UpdateUi _ui;
        internal Canvas Layer => _ui.Window.SnapshotLayer;
        internal Border Panel => _ui.Window.SnapshotPanel;
        internal Button Live => _ui.Window.LiveButton;
        internal Button Copy => _ui.Window.SnapshotCopyButton;
        internal Rect Plot => _ui.Window.OverallGraph.PlotBounds;
        internal Action Check { get; }

        internal SnapshotContainmentDiagnostic(string directory)
        {
            _ui = new UpdateUi(directory, true);
            try
            {
                var graph = _ui.Window.OverallGraph;
                graph.Measure(new Size(800, 400));
                graph.Arrange(new Rect(0, 0, 800, 400));
                graph.UpdateLayout();
                Assert.That(Plot.Width, Is.GreaterThan(400));
                Assert.That(Plot.Height, Is.GreaterThan(200));
                foreach (var button in new[] { Live, Copy })
                {
                    ((Panel)button.Parent).Children.Remove(button);
                    button.Style = null;
                    button.Content = null;
                    button.MinWidth = button.MinHeight = 0;
                    button.Margin = new Thickness(0);
                    button.Width = 60;
                    button.Height = 24;
                }
                Panel.Child = null;
                Panel.Padding = Panel.BorderThickness = new Thickness(0);
                Panel.Margin = new Thickness(0);
                Panel.Width = 200;
                Panel.Height = 100;
                Layer.Children.Clear();
                Layer.Children.Add(Panel);
                Layer.Children.Add(Live);
                Layer.Children.Add(Copy);
                Layer.Visibility = Visibility.Visible;
                Layer.UseLayoutRounding = false;
                Place(Panel, Plot.Left + 20, Plot.Top + 20);
                Place(Live, Plot.Left + 30, Plot.Top + 30);
                Place(Copy, Plot.Left + 120, Plot.Top + 30);
                var check = (typeof(WindowDiagnostics).GetMethod("AssertDesignSnapshotContained",
                    BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException("Missing design snapshot validation"))
                    .CreateDelegate<Action<MainWindow>>();
                Check = () => check(_ui.Window);
            }
            catch { _ui.Dispose(); throw; }
        }

        internal Rect Bounds(FrameworkElement element) => element.TransformToAncestor(Layer).TransformBounds(new Rect(element.RenderSize));

        internal void Layout()
        {
            Layer.Measure(new Size(800, 400));
            Layer.Arrange(new Rect(0, 0, 800, 400));
            Layer.UpdateLayout();
        }

        internal void Place(FrameworkElement element, double x, double y)
        {
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
            Layout();
        }

        internal void MoveToEdge(FrameworkElement element, Rect container, string edge, double offset)
        {
            var x = edge == "left" ? container.Left + offset : edge == "right" ? container.Right - element.ActualWidth + offset : container.Left + 10;
            var y = edge == "top" ? container.Top + offset : edge == "bottom" ? container.Bottom - element.ActualHeight + offset : container.Top + 10;
            Place(element, x, y);
            var bounds = Bounds(element);
            var actual = edge switch { "left" => bounds.Left - container.Left, "right" => bounds.Right - container.Right,
                "top" => bounds.Top - container.Top, _ => bounds.Bottom - container.Bottom };
            Assert.That(actual, Is.EqualTo(offset).Within(1e-8));
        }

        internal void Verify(bool rejected)
        {
            var before = new[] { Bounds(Panel), Bounds(Live), Bounds(Copy), Plot };
            if (rejected)
            {
                var error = Assert.Throws<InvalidOperationException>(Check);
                Assert.That(error!.Message, Is.EqualTo($"Snapshot panel or live action escaped the chart: {before[0]}, button {before[1]}, plot {before[3]}."));
            }
            else Assert.DoesNotThrow(Check);
            Assert.That(new[] { Bounds(Panel), Bounds(Live), Bounds(Copy), Plot }, Is.EqualTo(before));
        }

        public void Dispose() => _ui.Dispose();
    }
}
