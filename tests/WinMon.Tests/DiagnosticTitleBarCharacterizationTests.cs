using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using NUnit.Framework;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, Category("Integration")]
    public void TitleBarDiagnosticAcceptsAnOwnedWindowWithoutChangingBoundsOrStartingSampling()
    {
        using var fixture = new MetricCaptureDiagnostic(_directory);
        var window = fixture.Window;
        var before = (window.Left, window.Top, window.Width, window.Height, window.CompactMode, window.Topmost);
        var check = typeof(WindowDiagnostics).GetMethod("ValidateTitleBar", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action>(new WindowDiagnostics(window));
        Assert.DoesNotThrow(check);
        Assert.That((window.Left, window.Top, window.Width, window.Height, window.CompactMode, window.Topmost), Is.EqualTo(before));
        Assert.That(window.Session.HasLiveSample, Is.False);
    }

    [TestCase("title"), TestCase("pin"), Category("Integration")]
    public void TitleBarDiagnosticPreservesNativeTitleAndPinHitRequirements(string input)
    {
        using var fixture = new TitleBarDiagnostic(_directory);
        if (input == "title") WindowChrome.SetIsHitTestVisibleInChrome(fixture.Window.ViewTitle, true);
        else WindowChrome.SetIsHitTestVisibleInChrome(fixture.Window.PinButton, false);
        fixture.Reject("The title must drag the window, while Pin must receive client input.");
    }

    [TestCase("info"), TestCase("settings"), TestCase("export"), Category("Integration")]
    public void TitleBarDiagnosticPreservesClientInputForEveryTitleAction(string name)
    {
        using var fixture = new TitleBarDiagnostic(_directory);
        var action = fixture.Action(name);
        WindowChrome.SetIsHitTestVisibleInChrome(action, false);
        fixture.Reject($"{action.Content} must receive client input in the title bar.");
    }

    [TestCase("info"), TestCase("settings"), TestCase("export"), Category("Integration")]
    public void TitleBarDiagnosticRejectsActionsOutsideTheTitleBar(string name)
    {
        using var fixture = new TitleBarDiagnostic(_directory);
        var action = fixture.Action(name);
        fixture.MoveToProcessToolbar(action);
        Assert.That(fixture.Window.TitleBarArea.IsAncestorOf(action), Is.False);
        fixture.Reject($"{action.Content} must receive client input in the title bar.");
    }

    [TestCase("top", -0.001, true), TestCase("top", 0, false), TestCase("top", 0.001, false)]
    [TestCase("bottom", -0.001, false), TestCase("bottom", 0, false), TestCase("bottom", 0.001, true)]
    [TestCase("right", -0.001, false), TestCase("right", 0, false), TestCase("right", 0.001, true)]
    [TestCase("height", 27.999, true), TestCase("height", 28, false), TestCase("height", 28.001, false)]
    [Category("Integration")]
    public void TitleBarDiagnosticPreservesActionContainmentAndMinimumHeight(string edge, double value, bool rejected)
    {
        using var fixture = new TitleBarDiagnostic(_directory);
        var window = fixture.Window;
        var action = edge == "right" ? window.ExportButton : window.InfoButton;
        if (edge is "top" or "bottom")
        {
            action.Height = 28;
            fixture.Layout();
        }
        if (edge == "right")
        {
            window.TitleBarArea.Width = 600;
            window.TitleBarArea.HorizontalAlignment = HorizontalAlignment.Left;
            fixture.Layout();
        }
        var before = fixture.Bounds(action);
        if (edge == "height") action.Height = value;
        else
        {
            var dx = edge == "right" ? window.TitleBarArea.ActualWidth - before.Right + value : 0;
            var dy = edge == "top" ? value - before.Top
                : edge == "bottom" ? window.TitleBarArea.ActualHeight - before.Bottom + value : 0;
            action.RenderTransform = new TranslateTransform(dx, dy);
        }
        fixture.Layout();
        var bounds = fixture.Bounds(action);
        var actual = edge switch { "top" => bounds.Top, "bottom" => bounds.Bottom - window.TitleBarArea.ActualHeight,
            "right" => bounds.Right - window.TitleBarArea.ActualWidth, _ => action.ActualHeight };
        Assert.That(actual, Is.EqualTo(value).Within(1e-8));
        fixture.Verify(rejected, $"{action.Content} does not fit fully inside the title bar.");
    }

    [TestCase(-0.001, true), TestCase(0, false), TestCase(0.001, false)]
    [TestCase(15.999, false), TestCase(16, false), TestCase(16.001, true), Category("Integration")]
    public void TitleBarDiagnosticPreservesTheVersionToInfoGap(double gap, bool rejected)
    {
        using var fixture = new TitleBarDiagnostic(_directory);
        var window = fixture.Window;
        var titleRight = fixture.Bounds(window.ViewTitle).Right;
        var shift = titleRight + gap - fixture.Bounds(window.InfoButton).Left;
        foreach (var action in new[] { window.InfoButton, window.SettingsButton, window.ExportButton })
            action.RenderTransform = new TranslateTransform(shift, 0);
        fixture.Layout();
        Assert.That(fixture.Bounds(window.InfoButton).Left - titleRight, Is.EqualTo(gap).Within(1e-8));
        fixture.Verify(rejected, "Info and Settings must follow the version in the title bar.");
    }

    [TestCase(-0.001, true), TestCase(0, false), TestCase(0.001, false), Category("Integration")]
    public void TitleBarDiagnosticPreservesExactSettingsContact(double gap, bool rejected)
    {
        using var fixture = new TitleBarDiagnostic(_directory);
        var window = fixture.Window;
        window.SettingsButton.RenderTransform = new TranslateTransform(
            fixture.Bounds(window.InfoButton).Right + gap - fixture.Bounds(window.SettingsButton).Left, 0);
        fixture.Layout();
        Assert.That(fixture.Bounds(window.SettingsButton).Left - fixture.Bounds(window.InfoButton).Right,
            Is.EqualTo(gap).Within(1e-8));
        fixture.Verify(rejected, "Info and Settings must follow the version in the title bar.");
    }

    [TestCase("range"), TestCase("mode"), Category("Integration")]
    public void TitleBarDiagnosticPreservesResourceHeaderAncestry(string name)
    {
        using var fixture = new TitleBarDiagnostic(_directory);
        var element = name == "range" ? (FrameworkElement)fixture.Window.RangeSelector : fixture.Window.ChartModeSwitch;
        fixture.MoveToProcessToolbar(element);
        Assert.That(fixture.Window.ResourceHeader.IsAncestorOf(element), Is.False);
        fixture.VerifyHeader(true);
    }

    [TestCase("range", 24.999, false), TestCase("range", 25, false), TestCase("range", 25.001, true)]
    [TestCase("resource", 43.999, false), TestCase("resource", 44, false), TestCase("resource", 44.001, true)]
    [Category("Integration")]
    public void TitleBarDiagnosticPreservesChartHeaderHeightBoundaries(string name, double height, bool rejected)
    {
        using var fixture = new TitleBarDiagnostic(_directory);
        FrameworkElement element = name == "range" ? fixture.Window.RangeSelector : fixture.Window.ResourceHeader;
        element.Height = height;
        fixture.Layout();
        Assert.That(element.ActualHeight, Is.EqualTo(height).Within(1e-8));
        fixture.VerifyHeader(rejected);
    }

    [TestCase("pin", 43.999, false), TestCase("pin", 44, false), TestCase("pin", 44.001, true)]
    [TestCase("process", 77.999, false), TestCase("process", 78, false), TestCase("process", 78.001, true)]
    [TestCase("separate", 111.999, false), TestCase("separate", 112, false), TestCase("separate", 112.001, true)]
    [Category("Integration")]
    public void TitleBarDiagnosticPreservesEachProcessHeaderHeightLimit(string mode, double height, bool rejected)
    {
        using var fixture = new TitleBarDiagnostic(_directory);
        var window = fixture.Window;
        window.InlinePinToolbar = mode == "pin";
        window.InlineProcessToolbar = mode != "separate";
        window.Processes.ArrangeToolbar(window.InlineProcessToolbar, window.InlinePinToolbar);
        window.Processes.ProcessHeader.Height = height;
        fixture.Layout();
        Assert.That(window.Processes.ProcessHeader.ActualHeight, Is.EqualTo(height).Within(1e-8));
        fixture.VerifyHeader(rejected);
    }

    [TestCase("resize"), TestCase("caption"), Category("Integration")]
    public void TitleBarDiagnosticPreservesNativeResizeAndCaptionRequirements(string name)
    {
        using var fixture = new TitleBarDiagnostic(_directory);
        var chrome = WindowChrome.GetWindowChrome(fixture.Window);
        if (name == "resize") chrome.ResizeBorderThickness = new Thickness(0);
        else chrome.UseAeroCaptionButtons = false;
        fixture.Reject(name == "resize" ? "The integrated title bar lost the left resize border."
            : "Native Minimize, Maximize and Close caption buttons must remain available.");
    }

    private sealed class TitleBarDiagnostic : IDisposable
    {
        private readonly MetricCaptureDiagnostic _fixture;
        private readonly Action _check;
        internal MainWindow Window => _fixture.Window;
        internal TitleBarDiagnostic(string directory)
        {
            _fixture = new MetricCaptureDiagnostic(directory);
            try
            {
                Window.Width = 1400;
                Layout();
                _check = typeof(WindowDiagnostics).GetMethod("ValidateTitleBar", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .CreateDelegate<Action>(new WindowDiagnostics(Window));
                Assert.DoesNotThrow(_check, "The unmodified owned window must satisfy the original validator.");
            }
            catch { _fixture.Dispose(); throw; }
        }

        internal Button Action(string name) => name == "info" ? Window.InfoButton
            : name == "settings" ? Window.SettingsButton : Window.ExportButton;
        internal void Layout() => Window.UpdateLayout();
        internal Rect Bounds(FrameworkElement element) => element.TransformToAncestor(Window.TitleBarArea)
            .TransformBounds(new Rect(element.RenderSize));
        internal void MoveToProcessToolbar(FrameworkElement element)
        {
            ((Panel)element.Parent).Children.Remove(element);
            Grid.SetRow(element, 1);
            Grid.SetColumn(element, 0);
            element.VerticalAlignment = VerticalAlignment.Center;
            element.HorizontalAlignment = HorizontalAlignment.Left;
            Window.Processes.ProcessToolbarGrid.Children.Add(element);
            Layout();
        }
        internal void Reject(string message)
        {
            Layout();
            var error = Assert.Throws<InvalidOperationException>(_check);
            Assert.That(error!.Message, Is.EqualTo(message));
            Assert.That(Window.Session.HasLiveSample, Is.False);
        }
        internal void Verify(bool rejected, string message)
        {
            if (rejected) Reject(message);
            else Assert.DoesNotThrow(_check);
        }
        internal void VerifyHeader(bool rejected) => Verify(rejected,
            $"The live chart/process headers did not stay compact: range={Window.RangeSelector.ActualHeight}, resource={Window.ResourceHeader.ActualHeight}, process={Window.Processes.ProcessHeader.ActualHeight}.");
        public void Dispose() => _fixture.Dispose();
    }
}
