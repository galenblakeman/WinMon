using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private const string ConnectionRowFailure = "Connections controls must fit on a single row at minimum width.";
    private const string ConnectionTotalsFailure = "Activity totals must stay on one line.";
    private const string ConnectionOrderFailure = "App only and Pinned only must precede the period selector and match the Processes filter style.";
    private const string ConnectionPinsFailure = "Pinned count must fit beside Process / PID in the first column header.";

    [TestCase(false), TestCase(true)]
    public void ConnectionToolbarDiagnosticAcceptsOwnedAlignedControlsAndBothPinSummaryStates(bool pins)
    {
        using var fixture = new ConnectionToolbarDiagnostic(_directory, pins);
        fixture.Verify(null);
    }

    [TestCase("width", 0, true), TestCase("width", 0.0001, false)]
    [TestCase("left", -0.0001, true), TestCase("left", 0, false), TestCase("left", 0.0001, false)]
    [TestCase("right", -0.0001, false), TestCase("right", 0, false), TestCase("right", 0.0001, true)]
    [TestCase("center", -1.0001, true), TestCase("center", -1, true), TestCase("center", -0.9999, false)]
    [TestCase("center", 0.9999, false), TestCase("center", 1, true), TestCase("center", 1.0001, true)]
    public void ConnectionToolbarDiagnosticPreservesPositiveWidthContainmentAndStrictCenterTolerance(string rule, double value, bool rejected)
    {
        using var fixture = new ConnectionToolbarDiagnostic(_directory);
        if (rule == "width")
        {
            fixture.View.SearchBorder.Child = null;
            fixture.View.SearchBorder.Padding = fixture.View.SearchBorder.BorderThickness = new Thickness();
            fixture.View.SearchBorder.Width = value;
            fixture.Flush();
            Assert.That(fixture.View.SearchBorder.ActualWidth, Is.EqualTo(value).Within(1e-8));
        }
        else if (rule == "left") fixture.Place(fixture.View.TopCount, value, 0);
        else if (rule == "right") fixture.Place(fixture.View.TrafficPeriod,
            fixture.View.ActualWidth + value - fixture.View.TrafficPeriod.ActualWidth, 0);
        else
        {
            fixture.Place(fixture.View.Rank, 180, value);
            var center = fixture.Bounds(fixture.View.TopCount).Top + fixture.View.TopCount.ActualHeight / 2;
            var moved = fixture.Bounds(fixture.View.Rank);
            Assert.That(moved.Top + moved.Height / 2 - center, Is.EqualTo(value).Within(1e-8));
        }
        fixture.Verify(rejected ? ConnectionRowFailure : null);
    }

    [TestCase("plain", false), TestCase("lf", true), TestCase("cr", false), TestCase("crlf", true)]
    public void ConnectionToolbarDiagnosticPreservesTheOriginalLfOnlyTotalsRule(string kind, bool rejected)
    {
        using var fixture = new ConnectionToolbarDiagnostic(_directory);
        fixture.Window.ConnectionVolumeValue.Text = kind switch
        {
            "plain" => "owned total", "lf" => "owned\ntotal", "cr" => "owned\rtotal", _ => "owned\r\ntotal"
        };
        fixture.Verify(rejected ? ConnectionTotalsFailure : null);
    }

    [TestCase("apps-pinned", -0.0001, false), TestCase("apps-pinned", 0, false), TestCase("apps-pinned", 0.0001, true)]
    [TestCase("pinned-period", -0.0001, false), TestCase("pinned-period", 0, false), TestCase("pinned-period", 0.0001, true)]
    public void ConnectionToolbarDiagnosticAllowsFilterContactButRejectsReversedOrder(string pair, double overlap, bool rejected)
    {
        using var fixture = new ConnectionToolbarDiagnostic(_directory);
        if (pair == "apps-pinned")
        {
            fixture.Place(fixture.View.AppsOnly, fixture.Bounds(fixture.View.PinnedOnly).Left + overlap - fixture.View.AppsOnly.ActualWidth, 0);
            Assert.That(fixture.Bounds(fixture.View.AppsOnly).Right - fixture.Bounds(fixture.View.PinnedOnly).Left,
                Is.EqualTo(overlap).Within(1e-8));
        }
        else
        {
            fixture.Place(fixture.View.TrafficPeriod, fixture.Bounds(fixture.View.PinnedOnly).Right - overlap, 0);
            Assert.That(fixture.Bounds(fixture.View.PinnedOnly).Right - fixture.Bounds(fixture.View.TrafficPeriod).Left,
                Is.EqualTo(overlap).Within(1e-8));
        }
        fixture.Verify(rejected ? ConnectionOrderFailure : null);
    }

    [TestCase(11.9999, true), TestCase(12, false), TestCase(12.0001, true)]
    public void ConnectionToolbarDiagnosticRetainsItsExactFilterFontSize(double size, bool rejected)
    {
        using var fixture = new ConnectionToolbarDiagnostic(_directory);
        fixture.View.AppsOnly.FontSize = size;
        fixture.Flush();
        Assert.That(fixture.View.AppsOnly.FontSize, Is.EqualTo(size));
        fixture.Verify(rejected ? ConnectionOrderFailure : null);
    }

    [TestCase(Visibility.Hidden), TestCase(Visibility.Collapsed)]
    public void ConnectionToolbarDiagnosticSkipsDetachedPinGeometryWhenTheSummaryIsNotVisible(Visibility visibility)
    {
        using var fixture = new ConnectionToolbarDiagnostic(_directory, true);
        fixture.View.PinSummary.Visibility = visibility;
        fixture.Pins.Children.Remove(fixture.View.PinSummary);
        fixture.Verify(null);
        Assert.That(fixture.View.PinSummary.Parent, Is.Null);
    }

    [TestCase("left", -0.0001, true), TestCase("left", 0, true), TestCase("left", 0.0001, false)]
    [TestCase("right", -0.0001, false), TestCase("right", 0, false), TestCase("right", 0.0001, true)]
    [TestCase("top", -0.0001, true), TestCase("top", 0, false), TestCase("top", 0.0001, false)]
    [TestCase("bottom", -0.0001, false), TestCase("bottom", 0, false), TestCase("bottom", 0.0001, true)]
    public void ConnectionToolbarDiagnosticPreservesVisiblePinColumnBoundaries(string edge, double value, bool rejected)
    {
        using var fixture = new ConnectionToolbarDiagnostic(_directory, true);
        var x = edge switch
        {
            "left" => value,
            "right" => fixture.View.ConnectionHeaders.ColumnDefinitions[0].ActualWidth + value - fixture.View.PinSummary.ActualWidth,
            _ => 10
        };
        var y = edge switch
        {
            "top" => value,
            "bottom" => fixture.View.ConnectionHeaders.ActualHeight + value - fixture.View.PinSummary.ActualHeight,
            _ => 0
        };
        fixture.PlacePin(x, y);
        fixture.Verify(rejected ? ConnectionPinsFailure : null);
    }

    [Test]
    public void ConnectionToolbarDiagnosticPreservesTheOriginalDetachedControlFailure()
    {
        using var fixture = new ConnectionToolbarDiagnostic(_directory);
        fixture.Canvas.Children.Remove(fixture.View.SearchBorder);
        var error = Assert.Throws<InvalidOperationException>(fixture.Check);
        Assert.That(error!.Message, Is.EqualTo("The specified Visual is not an ancestor of this Visual."));
    }

    [TestCase("row"), TestCase("totals"), TestCase("order")]
    public void ConnectionToolbarDiagnosticRetainsFirstFailurePrecedenceWithoutMutatingAnyInput(string stage)
    {
        using var fixture = new ConnectionToolbarDiagnostic(_directory, true);
        fixture.PlacePin(0, 0);
        fixture.View.AppsOnly.FontSize = 13;
        if (stage != "order") fixture.Window.ConnectionVolumeValue.Text = "owned\ninvalid";
        if (stage == "row") fixture.Place(fixture.View.TopCount, -1, 0);
        fixture.Flush();
        fixture.Verify(stage == "row" ? ConnectionRowFailure : stage == "totals" ? ConnectionTotalsFailure : ConnectionOrderFailure);
    }

    private sealed class ConnectionToolbarDiagnostic : IDisposable
    {
        private readonly UpdateUi _ui;
        internal MainWindow Window => _ui.Window;
        internal ConnectionsView View => Window.ConnectionView;
        internal FrameworkElement[] Controls { get; }
        internal Canvas Canvas { get; } = new() { Width = 1200, Height = 160 };
        internal Canvas Pins { get; } = new() { Width = 300, Height = 30 };
        internal Action Check { get; }

        internal ConnectionToolbarDiagnostic(string directory, bool pins = false)
        {
            _ui = new UpdateUi(directory, true);
            try
            {
                Controls = [View.TopCount, View.Rank, View.SearchBorder, View.AppsOnly, View.PinnedOnly, View.TrafficPeriod];
                foreach (var control in Controls)
                {
                    Detach(control);
                    control.MinWidth = control.MinHeight = 0;
                    control.Width = 160;
                    control.Height = 40;
                    control.Margin = new Thickness();
                    Canvas.Children.Add(control);
                }
                Detach(View.PinSummary);
                Detach(View.ConnectionHeaders);
                View.ConnectionHeaders.Children.Clear();
                View.ConnectionHeaders.ColumnDefinitions.Clear();
                View.ConnectionHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                View.ConnectionHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
                View.ConnectionHeaders.RowDefinitions.Clear();
                View.ConnectionHeaders.MinWidth = 0;
                View.ConnectionHeaders.Width = 300;
                View.ConnectionHeaders.Height = 30;
                View.ConnectionHeaders.Margin = new Thickness();
                Grid.SetColumnSpan(Pins, 2);
                View.ConnectionHeaders.Children.Add(Pins);
                View.PinSummary.MinWidth = View.PinSummary.MinHeight = 0;
                View.PinSummary.Margin = new Thickness();
                View.PinSummary.Width = 60;
                View.PinSummary.Height = 20;
                View.PinSummary.Visibility = pins ? Visibility.Visible : Visibility.Collapsed;
                Pins.Children.Add(View.PinSummary);
                Canvas.Children.Add(View.ConnectionHeaders);
                SetPosition(View.ConnectionHeaders, 0, 60);
                for (var i = 0; i < Controls.Length; i++) SetPosition(Controls[i], i * 180, 0);
                SetPosition(View.PinSummary, 10, 0);
                View.AppsOnly.FontSize = 12;
                Window.ConnectionVolumeValue.Text = "owned total";
                View.Content = Canvas;
                View.Visibility = Visibility.Visible;
                View.UseLayoutRounding = false;
                View.Width = 1200;
                View.Height = 160;
                var verify = (typeof(WindowDiagnostics).GetMethod("VerifyConnectionsScenarioToolbar", BindingFlags.Static | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Missing Connections scenario toolbar validator"))
                    .CreateDelegate<Action<MainWindow>>();
                Check = () => verify(Window);
                Flush();
                Assert.That(View.ActualWidth, Is.EqualTo(1200));
                for (var i = 0; i < Controls.Length; i++) Assert.That(Bounds(Controls[i]), Is.EqualTo(new Rect(i * 180, 0, 160, 40)));
                Assert.That(View.ConnectionHeaders.ColumnDefinitions[0].ActualWidth, Is.EqualTo(120));
            }
            catch { _ui.Dispose(); throw; }
        }

        private static void Detach(FrameworkElement element)
        {
            if (element.Parent is Panel panel) panel.Children.Remove(element);
            else if (element.Parent is Decorator decorator) decorator.Child = null;
            else if (element.Parent is ContentControl content) content.Content = null;
            else throw new AssertionException("Unexpected owned connection control parent.");
        }
        private static void SetPosition(UIElement element, double x, double y)
        {
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
        }
        internal void Flush()
        {
            View.Measure(new Size(1200, 160));
            View.Arrange(new Rect(0, 0, 1200, 160));
            View.UpdateLayout();
        }
        internal Rect Bounds(FrameworkElement control) => control.TransformToAncestor(View).TransformBounds(new Rect(control.RenderSize));
        internal void Place(FrameworkElement control, double x, double y)
        {
            SetPosition(control, x, y);
            Flush();
            Assert.That(Bounds(control).Left, Is.EqualTo(x).Within(1e-8));
            Assert.That(Bounds(control).Top, Is.EqualTo(y).Within(1e-8));
        }
        internal void PlacePin(double x, double y)
        {
            SetPosition(View.PinSummary, x, y);
            Flush();
            var bounds = View.PinSummary.TransformToAncestor(View.ConnectionHeaders).TransformBounds(new Rect(View.PinSummary.RenderSize));
            Assert.That(bounds, Is.EqualTo(new Rect(x, y, 60, 20)));
        }
        internal void Verify(string? error)
        {
            var before = Snapshot();
            if (error is null) Assert.DoesNotThrow(Check);
            else Assert.That(Assert.Throws<InvalidOperationException>(Check)!.Message, Is.EqualTo(error));
            Assert.That(Snapshot(), Is.EqualTo(before));
        }
        private object[] Snapshot() => Controls.Select(control => (object)Bounds(control)).Concat(new object[]
        {
            View.PinSummary.Visibility == Visibility.Visible
                ? View.PinSummary.TransformToAncestor(View.ConnectionHeaders).TransformBounds(new Rect(View.PinSummary.RenderSize)) : Rect.Empty,
            Window.ConnectionVolumeValue.Text, View.AppsOnly.FontSize, View.PinSummary.Visibility
        }).ToArray();
        public void Dispose()
        {
            Assert.That(Window.Session.HasLiveSample, Is.False);
            _ui.Dispose();
        }
    }
}
