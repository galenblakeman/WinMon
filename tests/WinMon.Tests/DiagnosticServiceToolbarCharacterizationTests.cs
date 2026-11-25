using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private const string ServiceOrderFailure = "Service filters must follow Search, Workers, Pinned only, Status order.";
    private const string ServicePinFailure = "Service pin buttons must remain visible at narrow widths.";
    private const string ServicePartsFailure = "Service toolbar controls overlap or extend beyond the available width.";
    private const string ServiceActionsFailure = "Service identity and action buttons must remain visible without overlap.";

    [TestCase(620), TestCase(900), TestCase(1400), Category("Integration")]
    public void ServiceToolbarDiagnosticAcceptsOriginalResponsiveLayoutsWithoutChangingThem(double width)
    {
        using var fixture = new ServiceToolbarDiagnostic(_directory, width);
        var before = fixture.Snapshot();
        Assert.DoesNotThrow(() => fixture.Check(fixture.View));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
        Assert.That(fixture.Pin.IsVisible, Is.True);
    }

    [TestCase("worker", -1.0001, true), TestCase("worker", -1, false), TestCase("worker", -0.9999, false)]
    [TestCase("pinned", -1.0001, true), TestCase("pinned", -1, false), TestCase("pinned", -0.9999, false)]
    [TestCase("status", -1.0001, true), TestCase("status", -1, false), TestCase("status", -0.9999, false)]
    [Category("Integration")]
    public void ServiceToolbarDiagnosticPreservesFilterOrderTolerance(string part, double offset, bool rejected)
    {
        using var fixture = new ServiceToolbarDiagnostic(_directory);
        var previous = part == "worker" ? fixture.View.SearchBorder : part == "pinned" ? (FrameworkElement)fixture.View.Workers : fixture.View.PinnedOnlyCheckBox;
        var control = part == "worker" ? fixture.View.Workers : part == "pinned" ? (FrameworkElement)fixture.View.PinnedOnlyCheckBox : fixture.View.StatusFilter;
        fixture.Move(control, fixture.Bounds(previous).Right + offset - fixture.Bounds(control).Left, 0);
        Assert.That(fixture.Bounds(control).Left - fixture.Bounds(previous).Right, Is.EqualTo(offset).Within(1e-8));
        fixture.Verify(rejected, ServiceOrderFailure);
    }

    [TestCase("width", 19.9999, true), TestCase("width", 20, false), TestCase("width", 20.0001, false)]
    [TestCase("left", -0.0001, true), TestCase("left", 0, false), TestCase("left", 0.0001, false)]
    [TestCase("right", -0.0001, false), TestCase("right", 0, false), TestCase("right", 0.0001, true)]
    [Category("Integration")]
    public void ServiceToolbarDiagnosticPreservesPinMinimumWidthAndContainment(string edge, double value, bool rejected)
    {
        using var fixture = new ServiceToolbarDiagnostic(_directory);
        if (edge == "width")
        {
            fixture.Pin.Width = value;
            fixture.Flush();
            Assert.That(fixture.Pin.ActualWidth, Is.EqualTo(value).Within(1e-8));
        }
        else
        {
            var before = fixture.Bounds(fixture.Pin);
            var dx = edge == "left" ? value - before.Left : fixture.View.ActualWidth + value - before.Right;
            fixture.Move(fixture.Pin, dx, 0);
            Assert.That(edge == "left" ? fixture.Bounds(fixture.Pin).Left : fixture.Bounds(fixture.Pin).Right - fixture.View.ActualWidth,
                Is.EqualTo(value).Within(1e-8));
        }
        fixture.Verify(rejected, ServicePinFailure);
    }

    [TestCase("left", -1.0001, true), TestCase("left", -1, false), TestCase("left", -0.9999, false)]
    [TestCase("right", 0.9999, false), TestCase("right", 1, false), TestCase("right", 1.0001, true)]
    [TestCase("overlap-width", 0.9999, false), TestCase("overlap-width", 1, false), TestCase("overlap-width", 1.0001, true)]
    [TestCase("overlap-height", 0.9999, false), TestCase("overlap-height", 1, false), TestCase("overlap-height", 1.0001, true)]
    [Category("Integration")]
    public void ServiceToolbarDiagnosticPreservesToolbarBoundsAndOverlapTolerance(string edge, double value, bool rejected)
    {
        using var fixture = new ServiceToolbarDiagnostic(_directory);
        var control = edge == "right" ? fixture.View.ServiceFilters : fixture.View.RankingControls;
        var bounds = fixture.Bounds(control);
        if (edge == "left") fixture.Move(control, value - bounds.Left, 0);
        else if (edge == "right") fixture.Move(control, fixture.View.ActualWidth + value - bounds.Right, 0);
        else
        {
            var search = fixture.Bounds(fixture.View.SearchBorder);
            var width = edge == "overlap-width" ? value : 2;
            var dy = edge == "overlap-height" ? search.Bottom - value - bounds.Top : 0;
            fixture.Move(control, search.Left + width - bounds.Right, dy);
            var overlap = Rect.Intersect(fixture.Bounds(control), fixture.Bounds(fixture.View.SearchBorder));
            Assert.That(edge == "overlap-width" ? overlap.Width : overlap.Height, Is.EqualTo(value).Within(1e-8));
        }
        fixture.Verify(rejected, ServicePartsFailure);
    }

    [TestCase("identity-width", 19.9999, true), TestCase("identity-width", 20, false), TestCase("identity-width", 20.0001, false)]
    [TestCase("summary-width", 11.9999, true), TestCase("summary-width", 12, false), TestCase("summary-width", 12.0001, false)]
    [TestCase("summary-height", 0.9999, true), TestCase("summary-height", 1, false), TestCase("summary-height", 1.0001, false)]
    [TestCase("identity-left", -1.0001, true), TestCase("identity-left", -1, false), TestCase("identity-left", -0.9999, false)]
    [TestCase("actions-right", 0.9999, false), TestCase("actions-right", 1, false), TestCase("actions-right", 1.0001, true)]
    [TestCase("overlap-width", 0.9999, false), TestCase("overlap-width", 1, false), TestCase("overlap-width", 1.0001, true)]
    [TestCase("overlap-height", 0.9999, false), TestCase("overlap-height", 1, false), TestCase("overlap-height", 1.0001, true)]
    [Category("Integration")]
    public void ServiceToolbarDiagnosticPreservesIdentitySummaryAndActionBoundaries(string rule, double value, bool rejected)
    {
        using var fixture = new ServiceToolbarDiagnostic(_directory);
        var identity = fixture.View.SelectionIdentity;
        var bounds = fixture.Bounds(identity);
        switch (rule)
        {
            case "identity-width": identity.Width = value; fixture.Flush(); Assert.That(identity.ActualWidth, Is.EqualTo(value).Within(1e-8)); break;
            case "summary-width":
                // Keep the intrinsic ellipsis width below the diagnostic boundary being supplied.
                fixture.View.Summary.FontSize = 8;
                fixture.View.Summary.Width = value;
                fixture.Flush();
                Assert.That(fixture.View.Summary.ActualWidth, Is.EqualTo(value).Within(1e-8));
                break;
            case "summary-height":
                // Supply a real sub-DIP line height, while isolating the height rule from width.
                fixture.View.Summary.FontSize = 0.5;
                fixture.View.Summary.Width = 20;
                fixture.View.Summary.Height = value;
                fixture.Flush();
                Assert.That(fixture.View.Summary.ActualHeight, Is.EqualTo(value).Within(1e-8));
                Assert.That(fixture.View.Summary.ActualWidth, Is.GreaterThanOrEqualTo(12));
                break;
            case "identity-left": fixture.Move(identity, value - bounds.Left, 0); break;
            case "actions-right": fixture.Move(fixture.View.ActionControls, fixture.View.ActualWidth + value - fixture.Bounds(fixture.View.ActionControls).Right, 0); break;
            default:
                var actions = fixture.Bounds(fixture.View.ActionControls);
                var width = rule == "overlap-width" ? value : 2;
                var dy = rule == "overlap-height" ? actions.Bottom - value - bounds.Top : 0;
                fixture.Move(identity, actions.Left + width - bounds.Right, dy);
                var overlap = Rect.Intersect(fixture.Bounds(identity), fixture.Bounds(fixture.View.ActionControls));
                Assert.That(rule == "overlap-width" ? overlap.Width : overlap.Height, Is.EqualTo(value).Within(1e-8));
                break;
        }
        fixture.Verify(rejected, ServiceActionsFailure);
    }

    [Test, Category("Integration")]
    public void ServiceToolbarDiagnosticPreservesTheOriginalFailureWhenNoPinIsVisible()
    {
        using var fixture = new ServiceToolbarDiagnostic(_directory);
        fixture.Pin.Visibility = Visibility.Collapsed;
        fixture.Flush();
        Assert.That(fixture.Pin.IsVisible, Is.False);
        var error = Assert.Throws<InvalidOperationException>(() => fixture.Check(fixture.View));
        Assert.That(error!.Message, Is.EqualTo("Sequence contains no matching element"));
    }

    [Test, Category("Integration")]
    public void ServiceToolbarDiagnosticPreservesTheOriginalDetachedFilterFailure()
    {
        using var fixture = new ServiceToolbarDiagnostic(_directory);
        fixture.View.ServiceFilters.Children.Remove(fixture.View.StatusFilter);
        var error = Assert.Throws<InvalidOperationException>(() => fixture.Check(fixture.View));
        Assert.That(error!.Message, Is.EqualTo("The specified Visual is not an ancestor of this Visual."));
    }

    [Test, Category("Integration")]
    public void ServiceToolbarDiagnosticPreservesTheOriginalNullViewFailure()
    {
        using var fixture = new ServiceToolbarDiagnostic(_directory);
        Assert.Throws<NullReferenceException>(() => fixture.Check(null!));
    }

    private sealed class ServiceToolbarDiagnostic : IDisposable
    {
        private readonly UpdateUi _ui;
        private readonly Window? _window;
        internal ServicesView View { get; }
        internal Button Pin { get; }
        internal Action<ServicesView> Check { get; }

        internal ServiceToolbarDiagnostic(string directory, double width = 1400)
        {
            _ui = new UpdateUi(directory, true);
            try
            {
                _ui.Window.ApplyTheme(false, false);
                View = new ServicesView();
                View.ActionRequested += (_, _, _) => throw new AssertionException("Diagnostic geometry must never request a service action.");
                var group = new ServiceGroup("service:owned", [new("owned", "Owned fixture service", 1, 0, 1)], null, []);
                var frame = new ServiceFrame(new MonitorSample(DateTimeOffset.UnixEpoch, 0, [], 0), [group]);
                View.Update(frame, [frame], false);
                _window = new Window
                {
                    Content = View, Width = width, Height = 500, ShowInTaskbar = false, ShowActivated = false,
                    Opacity = 0, Title = "Owned service diagnostic geometry fixture"
                };
                _window.Show();
                Flush();
                Pin = Descendants(View).OfType<Button>().Single(button => button.Name == "ServicePin" && button.IsVisible);
                Check = (typeof(WindowDiagnostics).GetMethod("VerifyServicesScenarioToolbar", BindingFlags.Static | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Missing service scenario toolbar validation"))
                    .CreateDelegate<Action<ServicesView>>();
                Assert.That(View.IsVisible, Is.True);
                Assert.DoesNotThrow(() => Check(View), "Establish a valid original responsive layout before injecting a boundary.");
            }
            catch
            {
                _window?.Close();
                View?.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                _ui.Dispose();
                throw;
            }
        }

        internal void Flush()
        {
            View.UpdateLayout();
            FinishWindowCheck(View.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
        }
        internal Rect Bounds(FrameworkElement element) => element.TransformToAncestor(View).TransformBounds(new Rect(element.RenderSize));
        internal void Move(FrameworkElement element, double dx, double dy)
        {
            var before = Bounds(element);
            element.RenderTransform = new TranslateTransform(dx, dy);
            Flush();
            Assert.That(Bounds(element).Left, Is.EqualTo(before.Left + dx).Within(1e-8));
            Assert.That(Bounds(element).Top, Is.EqualTo(before.Top + dy).Within(1e-8));
        }
        internal Rect[] Snapshot() => new FrameworkElement[] { View.RankingControls, View.SearchBorder, View.ServiceFilters,
            View.Workers, View.PinnedOnlyCheckBox, View.StatusFilter, Pin, View.SelectionIdentity, View.Summary, View.ActionControls }.Select(Bounds).ToArray();
        internal void Verify(bool rejected, string message)
        {
            var before = Snapshot();
            if (rejected) Assert.That(Assert.Throws<InvalidOperationException>(() => Check(View))!.Message, Is.EqualTo(message));
            else Assert.DoesNotThrow(() => Check(View));
            Assert.That(Snapshot(), Is.EqualTo(before));
        }
        private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
        public void Dispose()
        {
            _window?.Close();
            View.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert.That(_ui.Window.Session.HasLiveSample, Is.False);
            _ui.Dispose();
        }
    }
}
