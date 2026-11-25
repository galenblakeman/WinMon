using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Diagnostics;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase("Dark", 840, false), TestCase("Light", 700, true), Category("Integration")]
    public void UpdateFooterDiagnosticPreservesFourCapturesAndRestoresOwnedPreferences(string theme, double height, bool previousFooter)
    {
        using var fixture = new UpdateFooterDiagnostic(_directory, theme, height, previousFooter);
        FinishWindowCheck(fixture.Start());
        var expected = new[] { "update-footer-dark.png", "update-footer-light.png", "update-footer-short.png", "update-progress.png" };
        Assert.That(Directory.GetFiles(_directory, "*.png").Select(Path.GetFileName), Is.EquivalentTo(expected));
        foreach (var file in expected) fixture.AssertPng(file, $"{theme}-{height:0}-{file}");
        fixture.AssertRestored();
        Assert.That(fixture.Window.UpdateStatusText.Text, Is.EqualTo(Strings.Format(Strings.DownloadingUpdate, 42)));
        Assert.That(fixture.Window.CancelUpdateButton.Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(fixture.Window.DismissUpdateButton.Visibility, Is.EqualTo(Visibility.Collapsed));
        Assert.That(fixture.Window.UpdateNowButton.Visibility, Is.EqualTo(Visibility.Collapsed));
        Assert.That(fixture.Window.ReleaseNotesButton.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [TestCase("status", 59.999, true), TestCase("status", 60, false), TestCase("status", 60.001, false)]
    [TestCase("button", 49.999, true), TestCase("button", 50, false), TestCase("button", 50.001, false)]
    [TestCase("footer", 29.999, true), TestCase("footer", 30, false), TestCase("footer", 30.001, false)]
    [Category("Integration")]
    public void UpdateFooterDiagnosticPreservesExactControlSizeThresholds(string control, double value, bool rejected)
    {
        using var fixture = new UpdateFooterDiagnostic(_directory);
        if (control == "status") fixture.Window.UpdateStatusText.Width = value;
        if (control == "button") fixture.Window.UpdateNowButton.Width = value;
        if (control == "footer") fixture.Window.UpdateFooter.Height = value;
        if (rejected) fixture.Reject("The update footer controls do not fit the compact window.");
        else FinishWindowCheck(fixture.Start());
        Assert.That(fixture.ObservedSizes.Select(size => control == "status" ? size.Status : control == "button" ? size.Button : size.Footer),
            Has.Some.EqualTo(value).Within(1e-8), "The supplied boundary must be realized by actual WPF geometry.");
        fixture.AssertRestored();
    }

    [TestCase(-0.001, false), TestCase(0, false), TestCase(0.001, true), Category("Integration")]
    public void UpdateFooterDiagnosticPreservesProcessOverlapToleranceWhenRowsFit(double offset, bool rejected)
    {
        using var fixture = new UpdateFooterDiagnostic(_directory);
        var fits = false;
        var overlap = double.NaN;
        fixture.OnFirstFooterShown(() =>
        {
            var window = fixture.Window;
            var content = (Grid)window.ActivityPanel.Parent;
            fits = content.RowDefinitions.Sum(row => row.ActualHeight) <= window.FullView.ActualHeight - content.Margin.Top - content.Margin.Bottom + .5;
            window.Processes.RenderTransform = new TranslateTransform(0,
                fixture.Bounds(window.UpdateFooter).Top + .5 + offset - fixture.Bounds(window.Processes).Bottom);
            window.UpdateLayout();
            overlap = fixture.Bounds(window.Processes).Bottom - fixture.Bounds(window.UpdateFooter).Top;
        });
        if (rejected)
        {
            var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Start()));
            Assert.That(error!.Message, Does.StartWith("The update footer covers the process list: rows="));
        }
        else FinishWindowCheck(fixture.Start());
        fixture.FinishQueued();
        Assert.That(fits, Is.True);
        Assert.That(overlap, Is.EqualTo(.5 + offset).Within(1e-8));
        fixture.AssertRestored();
    }

    [Test, Category("Integration")]
    public void UpdateFooterDiagnosticPreservesIntentionalOverlayWhenRowsExceedVisibleHeight()
    {
        using var fixture = new UpdateFooterDiagnostic(_directory);
        var content = (Grid)fixture.Window.ActivityPanel.Parent;
        content.RowDefinitions[0].MinHeight = 2000;
        var overflows = false;
        fixture.OnFirstFooterShown(() => overflows = content.RowDefinitions.Sum(row => row.ActualHeight) >
            fixture.Window.FullView.ActualHeight - content.Margin.Top - content.Margin.Bottom + .5);
        FinishWindowCheck(fixture.Start());
        fixture.FinishQueued();
        Assert.That(overflows, Is.True);
        fixture.AssertRestored();
    }

    [TestCase("top", -0.001, true), TestCase("top", 0, false), TestCase("top", 0.001, false)]
    [TestCase("bottom", -0.001, false), TestCase("bottom", 0, false), TestCase("bottom", 0.001, true)]
    [Category("Integration")]
    public void UpdateFooterDiagnosticPreservesShortWindowTopAndBottomTolerance(string edge, double offset, bool rejected)
    {
        using var fixture = new UpdateFooterDiagnostic(_directory);
        var observed = new List<double>();
        fixture.AdjustShortFooter(fixture.Window.MinHeight, edge, offset, observed);
        if (rejected) fixture.Reject($"The update footer is not visible in a {fixture.Window.MinHeight}-DIP window.");
        else FinishWindowCheck(fixture.Start());
        Assert.That(observed, Has.Some.EqualTo(edge == "top" ? offset : .5 + offset).Within(1e-8));
        fixture.AssertRestored();
    }

    [TestCase("top"), TestCase("bottom"), Category("Integration")]
    public void UpdateFooterDiagnosticPreservesVisibilityFailureAtTheFirstShortHeight(string edge)
    {
        using var fixture = new UpdateFooterDiagnostic(_directory);
        var observed = new List<double>();
        fixture.AdjustShortFooter(300, edge, edge == "top" ? -1 : 1, observed);
        fixture.Reject("The update footer is not visible in a 300-DIP window.");
        Assert.That(observed, Is.Not.Empty);
        fixture.AssertRestored();
    }

    [TestCase("update-footer-dark.png"), TestCase("update-footer-light.png")]
    [TestCase("update-footer-short.png"), TestCase("update-progress.png"), Category("Integration")]
    public void UpdateFooterDiagnosticWriteFailuresPreserveExistingDataAndRestorePresentation(string file)
    {
        using var fixture = new UpdateFooterDiagnostic(_directory, "Light", 700, true);
        var path = Path.Combine(_directory, file);
        File.WriteAllText(path, "owned update footer sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Throws<IOException>(() => FinishWindowCheck(fixture.Start()));
        Assert.That(File.ReadAllText(path), Is.EqualTo("owned update footer sentinel"));
        fixture.AssertRestored();
    }

    [Test, Category("Integration")]
    public void UpdateFooterDiagnosticMissingOutputFolderRetainsTheOriginalErrorAndRestoresPresentation()
    {
        using var fixture = new UpdateFooterDiagnostic(_directory);
        var output = Path.Combine(_directory, "missing");
        Assert.Throws<DirectoryNotFoundException>(() => FinishWindowCheck(fixture.Start(output)));
        Assert.That(Directory.Exists(output), Is.False);
        fixture.AssertRestored();
    }

    [Test, Category("Integration")]
    public void UpdateFooterDiagnosticPreCancellationRestoresPresentationWithoutWritingFiles()
    {
        using var fixture = new UpdateFooterDiagnostic(_directory, "Light", 700, true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(fixture.Start(token: cancellation.Token)));
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
        fixture.AssertRestored();
    }

    private sealed class UpdateFooterDiagnostic : IDisposable
    {
        private readonly MetricCaptureDiagnostic _fixture;
        private readonly string _directory;
        private readonly MonitorPreferences _preferences;
        private readonly double _height;
        private readonly Color _surface;
        private readonly List<Action> _cleanup = [];
        private readonly List<Task> _queued = [];
        internal MainWindow Window => _fixture.Window;
        internal List<(double Status, double Button, double Footer)> ObservedSizes { get; } = [];
        internal UpdateFooterDiagnostic(string directory, string theme = "Dark", double height = 840, bool previousFooter = false)
        {
            _fixture = new MetricCaptureDiagnostic(directory);
            try
            {
                _directory = directory;
                Window.Preferences = Window.Preferences with { Theme = theme };
                Window.ApplyTheme(false, false);
                Window.Height = height;
                if (previousFooter) Window.ShowUpdateStatus("owned previous footer", true);
                Window.UpdateLayout();
                _preferences = Window.Preferences;
                _height = Window.Height;
                _surface = ((SolidColorBrush)Window.FindResource("Surface")).Color;
                SizeChangedEventHandler observe = (_, _) =>
                {
                    if (Window.UpdateFooter.Visibility == Visibility.Visible)
                        ObservedSizes.Add((Window.UpdateStatusText.ActualWidth, Window.UpdateNowButton.ActualWidth,
                            Window.UpdateFooter.ActualHeight));
                };
                foreach (var element in new FrameworkElement[] { Window.UpdateStatusText, Window.UpdateNowButton, Window.UpdateFooter })
                {
                    element.SizeChanged += observe;
                    _cleanup.Add(() => element.SizeChanged -= observe);
                }
            }
            catch { _fixture.Dispose(); throw; }
        }
        internal Task Start(string? output = null, CancellationToken token = default) => typeof(WindowDiagnostics)
            .GetMethod("CaptureUpdateDiagnosticsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, CancellationToken, Task>>(new WindowDiagnostics(Window))(output ?? _directory, token);
        internal void Cleanup(Action action) => _cleanup.Add(action);
        internal void OnFirstFooterShown(Action action)
        {
            var queued = false;
            DependencyPropertyChangedEventHandler changed = (_, e) =>
            {
                if (e.NewValue is not true || queued) return;
                queued = true;
                _queued.Add(Window.Dispatcher.InvokeAsync(action, DispatcherPriority.Render).Task);
            };
            Window.UpdateFooter.IsVisibleChanged += changed;
            Cleanup(() => Window.UpdateFooter.IsVisibleChanged -= changed);
        }
        internal void FinishQueued()
        {
            foreach (var task in _queued) FinishWindowCheck(task);
        }
        internal void AdjustShortFooter(double height, string edge, double offset, List<double> observed)
        {
            SizeChangedEventHandler adjust = (_, _) =>
            {
                if (Math.Abs(Window.Height - height) > 1e-8) return;
                Window.UpdateFooter.UseLayoutRounding = false;
                Window.UpdateFooter.VerticalAlignment = edge == "top" ? VerticalAlignment.Top : VerticalAlignment.Bottom;
                Window.UpdateFooter.Margin = new Thickness(20, 0, 20, edge == "top" ? 8 : 0);
                Window.UpdateFooter.RenderTransform = new TranslateTransform(0, edge == "top" ? offset : .5 + offset);
            };
            EventHandler measure = (_, _) =>
            {
                if (Window.UpdateFooter.Visibility != Visibility.Visible || Math.Abs(Window.Height - height) > 1e-8) return;
                var footer = Bounds(Window.UpdateFooter);
                var full = Bounds(Window.FullView);
                observed.Add(edge == "top" ? footer.Top - full.Top : footer.Bottom - full.Bottom);
            };
            Window.SizeChanged += adjust;
            Window.UpdateFooter.LayoutUpdated += measure;
            Cleanup(() => Window.SizeChanged -= adjust);
            Cleanup(() => Window.UpdateFooter.LayoutUpdated -= measure);
        }
        internal void Reject(string message)
        {
            var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(Start()));
            Assert.That(error!.Message, Is.EqualTo(message));
        }
        internal Rect Bounds(FrameworkElement element) => element.TransformToAncestor(Window).TransformBounds(new Rect(element.RenderSize));
        internal void AssertRestored()
        {
            Assert.That(Window.Preferences, Is.SameAs(_preferences));
            Assert.That(Window.Height, Is.EqualTo(_height));
            Assert.That(Window.UpdateFooter.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(((SolidColorBrush)Window.FindResource("Surface")).Color, Is.EqualTo(_surface));
            Assert.That(Window.Session.HasLiveSample, Is.False);
        }
        internal void AssertPng(string file, string artifact)
        {
            using var stream = File.Open(Path.Combine(_directory, file), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
            Assert.That(frame.PixelWidth, Is.GreaterThan(0));
            Assert.That(frame.PixelHeight, Is.GreaterThan(0));
            Assert.That(frame.DpiX, Is.EqualTo(96).Within(.1));
            var directory = TestContext.Parameters.Get("UpdateFooterArtifactDirectory", "");
            if (directory.Length == 0) return;
            Directory.CreateDirectory(directory);
            using var copy = File.Create(Path.Combine(directory, artifact));
            stream.Position = 0;
            stream.CopyTo(copy);
        }
        public void Dispose()
        {
            foreach (var cleanup in _cleanup.AsEnumerable().Reverse()) cleanup();
            _fixture.Dispose();
        }
    }
}
