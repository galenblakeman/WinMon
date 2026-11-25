using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Diagnostics;
using DrawingRectangle = System.Drawing.Rectangle;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, Category("Integration")]
    public void NativeExampleCapturePreservesUnshownWindowForegroundFailureWithoutCreatingAFile()
    {
        using var fixture = new NativeCaptureWindow(show: false);
        var path = Path.Combine(_directory, "unshown.png");
        var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(CaptureNativeExample(fixture.Window, path)));
        Assert.That(error!.Message, Is.EqualTo("Example window must be foreground to capture its native caption."));
        Assert.That(fixture.Handle, Is.EqualTo(nint.Zero));
        Assert.That(fixture.Window.IsVisible, Is.False);
        Assert.That(File.Exists(path), Is.False);
    }

    [TestCase(false), TestCase(true), Category("Integration")]
    public void NativeExampleCapturePreCancellationPreservesVisibilityAndWritesNothing(bool shown)
    {
        using var fixture = new NativeCaptureWindow(show: shown);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var path = Path.Combine(_directory, "cancelled.png");
        Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(CaptureNativeExample(fixture.Window, path, cancellation.Token)));
        Assert.That(fixture.Window.IsVisible, Is.EqualTo(shown));
        Assert.That(File.Exists(path), Is.False);
    }

    [Test, Category("Integration")]
    public void NativeExampleCaptureCancellationDuringCompositionRetainsTheCallerToken()
    {
        using var fixture = new NativeCaptureWindow();
        using var cancellation = new CancellationTokenSource();
        var path = Path.Combine(_directory, "composition-cancelled.png");
        var capture = CaptureNativeExample(fixture.Window, path, cancellation.Token);
        var signalled = false;
        fixture.Window.Dispatcher.BeginInvoke(() => { signalled = true; cancellation.Cancel(); }, DispatcherPriority.Background);
        var error = Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(capture));
        Assert.That(signalled, Is.True);
        Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public void NativeExampleCapturePreservesTheOriginalNullWindowArgumentFailure()
    {
        var error = Assert.Throws<ArgumentNullException>(() => FinishWindowCheck(CaptureNativeExample(null!, "unused.png")));
        Assert.That(error!.ParamName, Is.EqualTo("window"));
    }

    [Test, Category("Integration")]
    public void NativeFrameCapturePreservesOwnedBackgroundWindowDimensions()
    {
        using var fixture = new MetricCaptureDiagnostic(_directory);
        // The fixture is shown with ShowActivated=false. Do not request foreground activation:
        // this verifies PrintWindow while preserving the original composed-capture guard.
        FinishWindowCheck(fixture.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render).Task);
        var handle = new WindowInteropHelper(fixture.Window).Handle;
        Assert.That(ReadCaptureForeground(), Is.Not.EqualTo(handle));
        var bounds = NativeCaptureBounds(fixture.Window);
        var path = Path.Combine(_directory, "frame.png");
        FrameCapture(fixture.Window)(path);
        AssertNativePng(path, bounds, "frame-background.png");
        Assert.That(fixture.Window.Session.HasLiveSample, Is.False);
    }

    [Test, Category("Integration")]
    public void NativeFrameCapturePreservesTheUnshownWindowBoundsFailure()
    {
        using var fixture = new UpdateUi(_directory, true);
        var path = Path.Combine(_directory, "unshown-frame.png");
        var error = Assert.Throws<InvalidOperationException>(() => FrameCapture(fixture.Window)(path));
        Assert.That(error!.Message, Is.EqualTo("Window bounds unavailable."));
        Assert.That(File.Exists(path), Is.False);
        Assert.That(fixture.Window.Session.HasLiveSample, Is.False);
    }

    [TestCase("missing-folder"), TestCase("locked-file"), Category("Integration")]
    public void NativeFrameCaptureWriteFailuresPreserveExistingDataAndReleaseFileHandles(string failure)
    {
        using var fixture = new MetricCaptureDiagnostic(_directory);
        Assert.That(ReadCaptureForeground(), Is.Not.EqualTo(new WindowInteropHelper(fixture.Window).Handle));
        var path = Path.Combine(failure == "missing-folder" ? Path.Combine(_directory, "missing") : _directory, "failure.png");
        if (failure == "locked-file") File.WriteAllText(path, "owned native image sentinel");
        using var locked = failure == "locked-file" ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
        if (failure == "missing-folder") Assert.Throws<DirectoryNotFoundException>(() => FrameCapture(fixture.Window)(path));
        else Assert.Throws<ExternalException>(() => FrameCapture(fixture.Window)(path));
        AssertNativeFailureFile(path, failure, locked);
        Assert.That(fixture.Window.Session.HasLiveSample, Is.False);
    }

    private static Task CaptureNativeExample(Window window, string path, CancellationToken token = default) =>
        typeof(WindowDiagnostics).GetMethod("CaptureExampleWindowAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Window, string, CancellationToken, Task>>()(window, path, token);

    private static Action<string> FrameCapture(MainWindow window) => typeof(WindowDiagnostics)
        .GetMethod("CaptureWindowFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
        .CreateDelegate<Action<string>>(new WindowDiagnostics(window));

    private static nint ReadCaptureForeground() => typeof(WindowDiagnostics)
        .GetMethod("GetForegroundWindow", BindingFlags.Static | BindingFlags.NonPublic)!
        .CreateDelegate<Func<nint>>()();

    private static DrawingRectangle NativeCaptureBounds(Window window)
    {
        Assert.That(ReadPositionRect(new WindowInteropHelper(window).Handle, out var bounds), Is.True);
        return DrawingRectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }

    private static void AssertNativeFailureFile(string path, string failure, FileStream? locked)
    {
        if (failure == "locked-file")
        {
            Assert.That(File.ReadAllText(path), Is.EqualTo("owned native image sentinel"));
            locked!.Dispose();
            using var reopened = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.That(reopened.Length, Is.GreaterThan(0));
        }
        else Assert.That(Directory.Exists(Path.GetDirectoryName(path)), Is.False);
    }

    private static void AssertNativePng(string path, DrawingRectangle bounds, string artifact)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
        Assert.That(frame.PixelWidth, Is.EqualTo(bounds.Width));
        Assert.That(frame.PixelHeight, Is.EqualTo(bounds.Height));
        var directory = TestContext.Parameters.Get("NativeCaptureArtifactDirectory", "");
        if (directory.Length == 0) return;
        Directory.CreateDirectory(directory);
        using var copy = File.Create(Path.Combine(directory, artifact));
        stream.Position = 0;
        stream.CopyTo(copy);
    }

    private sealed class NativeCaptureWindow : IDisposable
    {
        private readonly SynchronizationContext? _context = SynchronizationContext.Current;
        internal Window Window { get; }
        internal nint Handle => new WindowInteropHelper(Window).Handle;
        internal NativeCaptureWindow(bool show = true)
        {
            _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Window = new Window
            {
                Width = 320, Height = 200, Left = SystemParameters.WorkArea.Left + 40,
                Top = SystemParameters.WorkArea.Top + 40, ShowInTaskbar = false, Topmost = true,
                Title = "Owned native capture fixture",
                Content = new Border { Background = Brushes.Lime,
                    Child = new TextBlock { Text = "Owned native capture marker", VerticalAlignment = VerticalAlignment.Top } }
            };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Window.Dispatcher));
            if (!show) return;
            Window.Show();
            Window.Activate();
            FinishWindowCheck(Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render).Task);
        }
        public void Dispose()
        {
            Window.Close();
            SynchronizationContext.SetSynchronizationContext(_context);
        }
    }
}
