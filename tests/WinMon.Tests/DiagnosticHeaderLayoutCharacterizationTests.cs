using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase(false), TestCase(true)]
    public void HeaderDiagnosticAcceptsFullWidthAndPreservesVerticalOnlyClipping(bool verticalClip)
    {
        using var fixture = new HeaderDiagnostic(_directory);
        if (verticalClip)
        {
            fixture.Header.Height = 40;
            fixture.Arrange();
            fixture.Container.Arrange(new Rect(0, 0, HeaderDiagnostic.Width, 20));
            var clip = LayoutInformation.GetLayoutClip(fixture.Container);
            Assert.That(clip, Is.Not.Null);
            Assert.That(fixture.LocalBounds.Bottom, Is.GreaterThan(clip.Bounds.Bottom));
        }
        var before = fixture.Snapshot();

        Assert.DoesNotThrow(() => fixture.Check(fixture.Header));

        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [TestCase(-0.5001, true), TestCase(-0.5, false), TestCase(-0.4999, false)]
    [TestCase(0.4999, false), TestCase(0.5, false), TestCase(0.5001, true)]
    public void HeaderDiagnosticRetainsItsInclusiveFullWidthTolerance(double delta, bool rejected)
    {
        using var fixture = new HeaderDiagnostic(_directory);
        fixture.Header.Width = HeaderDiagnostic.Width + delta;
        fixture.Arrange();
        Assert.That(fixture.RootBounds.Right, Is.EqualTo(HeaderDiagnostic.Width + delta).Within(1e-8));
        var before = fixture.Snapshot();

        if (rejected)
            Assert.That(Assert.Throws<InvalidOperationException>(() => fixture.Check(fixture.Header))!.Message,
                Is.EqualTo(fixture.SpanFailure));
        else Assert.DoesNotThrow(() => fixture.Check(fixture.Header));

        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [TestCase(-0.5001, true), TestCase(-0.5, false), TestCase(-0.4999, false)]
    [TestCase(0.4999, false), TestCase(0.5, false), TestCase(0.5001, true)]
    public void HeaderDiagnosticRetainsItsInclusiveHorizontalPositionTolerance(double offset, bool rejected)
    {
        using var fixture = new HeaderDiagnostic(_directory);
        fixture.Header.RenderTransform = new TranslateTransform(offset, 0);
        fixture.Arrange();
        Assert.That(fixture.RootBounds.Left, Is.EqualTo(offset).Within(1e-8));
        var before = fixture.Snapshot();

        if (rejected)
            Assert.That(Assert.Throws<InvalidOperationException>(() => fixture.Check(fixture.Header))!.Message,
                Is.EqualTo(fixture.SpanFailure));
        else Assert.DoesNotThrow(() => fixture.Check(fixture.Header));

        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [TestCase("left", 0.4999, false), TestCase("left", 0.5, false), TestCase("left", 0.5001, true)]
    [TestCase("right", 0.4999, false), TestCase("right", 0.5, false), TestCase("right", 0.5001, true)]
    public void HeaderDiagnosticDetectsAncestorLayoutClippingWithoutChangingTheRootSpan(string edge, double overflow, bool rejected)
    {
        using var fixture = new HeaderDiagnostic(_directory);
        fixture.Container.HorizontalAlignment = edge == "left" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        var slot = new Rect(0, 0, HeaderDiagnostic.Width - overflow, 40);
        fixture.Container.Arrange(slot);
        var offset = fixture.RootBounds.Left;
        fixture.Container.RenderTransform = new TranslateTransform(-offset, 0);
        fixture.Container.Arrange(slot);
        var clip = LayoutInformation.GetLayoutClip(fixture.Container);
        Assert.That(clip, Is.Not.Null);
        Assert.That(fixture.Container.ClipToBounds, Is.False);
        Assert.That(fixture.RootBounds.Left, Is.EqualTo(0).Within(1e-8));
        Assert.That(fixture.RootBounds.Right, Is.EqualTo(HeaderDiagnostic.Width).Within(1e-8));
        var clipped = edge == "left" ? clip.Bounds.Left - fixture.LocalBounds.Left
            : fixture.LocalBounds.Right - clip.Bounds.Right;
        Assert.That(clipped, Is.EqualTo(overflow).Within(1e-8), "Use a real WPF ancestor layout clip for this boundary.");
        var before = fixture.Snapshot();

        if (rejected)
            Assert.That(Assert.Throws<InvalidOperationException>(() => fixture.Check(fixture.Header))!.Message,
                Is.EqualTo($"Header OwnedHeader is clipped horizontally at height {fixture.Window.Height} by Border."));
        else Assert.DoesNotThrow(() => fixture.Check(fixture.Header));

        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public void HeaderDiagnosticPreservesTheDetachedVisualFailure()
    {
        using var fixture = new HeaderDiagnostic(_directory);
        fixture.Container.Child = null;

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Check(fixture.Header));

        Assert.That(error!.Message, Is.EqualTo("The specified Visual is not an ancestor of this Visual."));
        Assert.That(fixture.Header.Parent, Is.Null);
    }

    [Test]
    public void HeaderDiagnosticPreservesTheOriginalNullHeaderFailure()
    {
        using var fixture = new HeaderDiagnostic(_directory);
        var before = fixture.Snapshot();

        Assert.Throws<NullReferenceException>(() => fixture.Check(null!));

        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    private sealed class HeaderDiagnostic : IDisposable
    {
        internal const double Width = 200;
        private readonly UpdateUi _ui;
        internal MainWindow Window => _ui.Window;
        internal Border Header { get; } = new()
        {
            Name = "OwnedHeader", Width = Width, Height = 20,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };
        internal Border Container { get; }
        internal Action<FrameworkElement> Check { get; }
        internal Rect RootBounds => Header.TransformToAncestor(Window.FullView).TransformBounds(new Rect(Header.RenderSize));
        internal Rect LocalBounds => Header.TransformToAncestor(Container).TransformBounds(new Rect(Header.RenderSize));
        internal string SpanFailure => $"Header OwnedHeader does not span the full view at height {Window.Height}: {RootBounds}.";

        internal HeaderDiagnostic(string directory)
        {
            _ui = new UpdateUi(directory, true);
            Window.FullView.Width = Width;
            Window.FullView.Height = 100;
            Container = new Border
            {
                Child = Header, MinWidth = Width, MinHeight = 40,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
            };
            Window.FullView.Child = Container;
            var method = typeof(WindowDiagnostics).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(value => value.Name.StartsWith("<CaptureActivityPanelAsync>g__CheckHeader|", StringComparison.Ordinal));
            Check = method.CreateDelegate<Action<FrameworkElement>>(new WindowDiagnostics(Window));
            Arrange();
            Assert.That(Window.FullView.ActualWidth, Is.EqualTo(Width));
            Assert.That(RootBounds, Is.EqualTo(new Rect(0, 0, Width, 20)));
        }

        internal void Arrange()
        {
            Window.FullView.Measure(new Size(Width, 100));
            Window.FullView.Arrange(new Rect(0, 0, Width, 100));
            Window.FullView.UpdateLayout();
        }
        internal (Rect Bounds, Rect Clip, double Width) Snapshot() =>
            (RootBounds, LayoutInformation.GetLayoutClip(Container)?.Bounds ?? Rect.Empty, Window.FullView.ActualWidth);
        public void Dispose()
        {
            Assert.That(Window.Session.HasLiveSample, Is.False);
            _ui.Dispose();
        }
    }
}
