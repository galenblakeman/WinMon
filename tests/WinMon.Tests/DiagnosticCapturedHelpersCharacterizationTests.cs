using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Diagnostics;
using WinMon.Monitoring;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private const string InvestigationFailure = "Chart investigation actions overlap or leave the toolbar at minimum width.";

    [Test]
    public void InvestigationDiagnosticAcceptsSeparatedControlsWithoutChangingTheirBounds()
    {
        using var fixture = new InvestigationDiagnostic(_directory);
        fixture.Verify(false);
    }

    [TestCase("left", -0.0001, true), TestCase("left", 0, false), TestCase("left", 0.0001, false)]
    [TestCase("right", -0.0001, false), TestCase("right", 0, false), TestCase("right", 0.0001, true)]
    public void InvestigationDiagnosticPreservesExactHeaderContainment(string edge, double value, bool rejected)
    {
        using var fixture = new InvestigationDiagnostic(_directory, edge == "right" ? 2800 : 2400);
        var x = edge == "left" ? value : fixture.Header.ActualWidth + value - fixture.Actions.ActualWidth;
        fixture.Place(fixture.Actions, x, 0);
        Assert.That(edge == "left" ? fixture.Bounds(fixture.Actions).Left
            : fixture.Bounds(fixture.Actions).Right - fixture.Header.ActualWidth, Is.EqualTo(value).Within(1e-8));
        fixture.Verify(rejected);
    }

    [TestCase("type", -0.0001, false), TestCase("type", 0, true), TestCase("type", 0.0001, true)]
    [TestCase("range", -0.0001, false), TestCase("range", 0, true), TestCase("range", 0.0001, true)]
    public void InvestigationDiagnosticPreservesInclusiveRectangleContactWithEitherSelector(string selector, double overlap, bool rejected)
    {
        using var fixture = new InvestigationDiagnostic(_directory);
        var target = selector == "type" ? fixture.Type : fixture.Range;
        fixture.Place(fixture.Actions, fixture.Bounds(target).Left + overlap - fixture.Actions.ActualWidth, 0);
        Assert.That(fixture.Bounds(fixture.Actions).Right - fixture.Bounds(target).Left, Is.EqualTo(overlap).Within(1e-8));
        fixture.Verify(rejected);
    }

    [TestCase(-0.0001, false), TestCase(0, true), TestCase(0.0001, true)]
    public void InvestigationDiagnosticPreservesVerticalContactAsIntersection(double overlap, bool rejected)
    {
        using var fixture = new InvestigationDiagnostic(_directory);
        fixture.Place(fixture.Type, 500, 30);
        fixture.Place(fixture.Actions, 500, 30 + overlap - fixture.Actions.ActualHeight);
        Assert.That(fixture.Bounds(fixture.Actions).Bottom - fixture.Bounds(fixture.Type).Top, Is.EqualTo(overlap).Within(1e-8));
        fixture.Verify(rejected);
    }

    [TestCase("actions"), TestCase("type"), TestCase("range")]
    public void InvestigationDiagnosticPreservesDetachedControlGeometryFailures(string control)
    {
        using var fixture = new InvestigationDiagnostic(_directory);
        var detached = control == "actions" ? fixture.Actions : control == "type" ? fixture.Type : fixture.Range;
        fixture.Canvas.Children.Remove(detached);
        var error = Assert.Throws<InvalidOperationException>(fixture.Check);
        Assert.That(error!.Message, Is.EqualTo("The specified Visual is not an ancestor of this Visual."));
        Assert.That(detached.Parent, Is.Null);
    }

    [Test]
    public void ConnectionPinDiagnosticReturnsNullForAnEmptyVisualTree()
    {
        using var ui = new UpdateUi(_directory, true);
        var find = ConnectionsScenarioPinFinder(PinOwner());
        var root = new Grid();
        Assert.That(find(root), Is.Null);
        Assert.That(root.Children, Is.Empty);
    }

    [TestCase("not-button"), TestCase("null-context"), TestCase("wrong-context")]
    [TestCase("no-owner"), TestCase("different-pid"), TestCase("different-lifetime")]
    public void ConnectionPinDiagnosticRejectsNonOwnersAndDifferentProcessLifetimes(string partition)
    {
        using var ui = new UpdateUi(_directory, true);
        var owner = PinOwner();
        var find = ConnectionsScenarioPinFinder(owner);
        var candidateOwner = partition switch
        {
            "different-pid" => owner with { ProcessId = owner.ProcessId - 1 },
            "different-lifetime" => owner with { CreationTicks = owner.CreationTicks + 1 },
            "no-owner" => null,
            _ => owner
        };
        var context = partition == "null-context" ? null : partition == "wrong-context" ? new object() : PinRow(candidateOwner);
        FrameworkElement candidate = partition == "not-button" ? new Border() : new Button();
        candidate.DataContext = context;
        var root = new Grid();
        root.Children.Add(candidate);

        Assert.That(find(root), Is.Null);

        Assert.That(root.Children.Count, Is.EqualTo(1));
        Assert.That(candidate.DataContext, Is.SameAs(context));
    }

    [TestCase("same"), TestCase("renamed"), TestCase("hidden"), TestCase("disabled"), TestCase("other-button-name")]
    public void ConnectionPinDiagnosticMatchesIdentityWithoutAddingNameVisibilityOrEnabledRules(string partition)
    {
        using var ui = new UpdateUi(_directory, true);
        var owner = PinOwner();
        var find = ConnectionsScenarioPinFinder(owner);
        var candidate = new Button
        {
            Name = partition == "other-button-name" ? "OwnedOtherAction" : "OwnedPin",
            DataContext = PinRow(partition == "renamed" ? owner with { Name = "renamed owner", CpuPercent = 99 } : owner),
            Visibility = partition == "hidden" ? Visibility.Collapsed : Visibility.Visible,
            IsEnabled = partition != "disabled"
        };

        Assert.That(find(candidate), Is.SameAs(candidate));

        Assert.That(candidate.Name, Is.EqualTo(partition == "other-button-name" ? "OwnedOtherAction" : "OwnedPin"));
        Assert.That(candidate.Visibility, Is.EqualTo(partition == "hidden" ? Visibility.Collapsed : Visibility.Visible));
        Assert.That(candidate.IsEnabled, Is.EqualTo(partition != "disabled"));
    }

    [Test]
    public void ConnectionPinDiagnosticKeepsDepthFirstOrderAndFallsBackToTheNextMatchingBranch()
    {
        using var ui = new UpdateUi(_directory, true);
        var owner = PinOwner();
        var find = ConnectionsScenarioPinFinder(owner);
        var first = new Button { DataContext = PinRow(owner) };
        var later = new Button { DataContext = PinRow(owner) };
        var firstBranch = new Border { Child = first };
        var root = new Grid();
        root.Children.Add(new Border { Child = new Button { DataContext = new object() } });
        root.Children.Add(firstBranch);
        root.Children.Add(later);

        Assert.That(find(root), Is.SameAs(first));
        Assert.That(firstBranch.Child, Is.SameAs(first));
        firstBranch.Child = null;
        Assert.That(find(root), Is.SameAs(later));
        Assert.That(root.Children.Count, Is.EqualTo(3));
    }

    [Test]
    public void ConnectionPinDiagnosticPreservesTheOriginalNullTreeFailure()
    {
        using var ui = new UpdateUi(_directory, true);
        var find = ConnectionsScenarioPinFinder(PinOwner());
        Assert.Throws<ArgumentNullException>(() => find(null!));
    }

    private static ProcessUsage PinOwner() => new(int.MaxValue - 40, 100, "owned diagnostic process", 0);
    private static ConnectionDisplayRow PinRow(ProcessUsage? owner)
    {
        // As in existing row tests, finish the lifetime-checked icon lookup before observing the row.
        if (owner is not null) FinishWindowCheck(ProcessIconProvider.GetAsync(owner));
        var row = new ConnectionDisplayRow("owned-row");
        row.UpdateGroup(new("owned-row", [], owner is null ? [] : [owner], owner), false,
            ConnectionsView.NetworkSummary.Disabled, owner is null ? "owned unavailable process" : null);
        return row;
    }

    private static Func<DependencyObject, Button?> ConnectionsScenarioPinFinder(ProcessUsage owner)
    {
        var find = (typeof(WindowDiagnostics).GetMethod("FindConnectionsScenarioPinButton", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing Connections scenario pin search"))
            .CreateDelegate<Func<DependencyObject, ProcessUsage, Button?>>();
        return parent => find(parent, owner);
    }

    private static T CapturedDiagnostic<T>(string parent, string local, MainWindow window, ProcessUsage? process = null) where T : Delegate
    {
        var method = typeof(WindowDiagnostics).GetNestedTypes(BindingFlags.NonPublic)
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Single(value => value.Name.StartsWith($"<{parent}>g__{local}|", StringComparison.Ordinal));
        var closure = Activator.CreateInstance(method.DeclaringType!, nonPublic: true)!;
        foreach (var field in method.DeclaringType!.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType == typeof(MainWindow)) field.SetValue(closure, window);
            else if (field.FieldType == typeof(ProcessUsage)) field.SetValue(closure, process);
        }
        return (T)method.CreateDelegate(typeof(T), closure);
    }

    private sealed class InvestigationDiagnostic : IDisposable
    {
        private readonly UpdateUi _ui;
        internal Border Header => _ui.Window.ResourceHeader;
        internal FrameworkElement Actions => _ui.Window.ChartInvestigationActions;
        internal FrameworkElement Type => _ui.Window.ChartTypeSelector;
        internal FrameworkElement Range => _ui.Window.RangeSelector;
        internal Canvas Canvas { get; } = new() { Height = 50 };
        private readonly double _width;
        internal Action Check { get; }

        internal InvestigationDiagnostic(string directory, double width = 2400)
        {
            _ui = new UpdateUi(directory, true);
            _width = width;
            Canvas.Width = width;
            try
            {
                foreach (var control in new[] { Actions, Type, Range })
                {
                    if (control.Parent is Panel panel) panel.Children.Remove(control);
                    else if (control.Parent is Decorator decorator) decorator.Child = null;
                    else throw new AssertionException("Unexpected owned header control parent.");
                    control.MinWidth = control.MinHeight = 0;
                    control.Margin = new Thickness();
                    // Preserve child content while supplying separated, real container extents.
                    control.Width = 400;
                    control.Height = 20;
                    Canvas.Children.Add(control);
                }
                Header.Child = Canvas;
                Header.Padding = Header.Margin = Header.BorderThickness = new Thickness();
                Header.Width = width;
                Header.Height = 50;
                Header.UseLayoutRounding = false;
                SetPosition(Actions, 0, 0);
                SetPosition(Type, 500, 0);
                SetPosition(Range, 1400, 0);
                Flush();
                var check = typeof(WindowDiagnostics).GetMethod("VerifyAutoCaptureScenarioToolbar",
                    BindingFlags.NonPublic | BindingFlags.Static)!.CreateDelegate<Action<MainWindow>>();
                Check = () => check(_ui.Window);
                Assert.That(Header.ActualWidth, Is.EqualTo(width));
                Assert.That(Bounds(Actions), Is.EqualTo(new Rect(0, 0, 400, 20)));
                Assert.That(Bounds(Type), Is.EqualTo(new Rect(500, 0, 400, 20)));
                Assert.That(Bounds(Range), Is.EqualTo(new Rect(1400, 0, 400, 20)));
            }
            catch { _ui.Dispose(); throw; }
        }
        private static void SetPosition(UIElement control, double x, double y)
        {
            System.Windows.Controls.Canvas.SetLeft(control, x);
            System.Windows.Controls.Canvas.SetTop(control, y);
        }
        private void Flush()
        {
            Header.Measure(new Size(_width, 50));
            Header.Arrange(new Rect(0, 0, _width, 50));
            Header.UpdateLayout();
        }
        internal void Place(FrameworkElement control, double x, double y)
        {
            SetPosition(control, x, y);
            Flush();
            Assert.That(Bounds(control).Left, Is.EqualTo(x).Within(1e-8));
            Assert.That(Bounds(control).Top, Is.EqualTo(y).Within(1e-8));
        }
        internal Rect Bounds(FrameworkElement control) => control.TransformToAncestor(Header).TransformBounds(new Rect(control.RenderSize));
        internal void Verify(bool rejected)
        {
            var before = new[] { Bounds(Actions), Bounds(Type), Bounds(Range) };
            if (rejected) Assert.That(Assert.Throws<InvalidOperationException>(Check)!.Message, Is.EqualTo(InvestigationFailure));
            else Assert.DoesNotThrow(Check);
            Assert.That(new[] { Bounds(Actions), Bounds(Type), Bounds(Range) }, Is.EqualTo(before));
        }
        public void Dispose()
        {
            Assert.That(_ui.Window.Session.HasLiveSample, Is.False);
            _ui.Dispose();
        }
    }
}
