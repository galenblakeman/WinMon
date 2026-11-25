using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Diagnostics;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using Forms = System.Windows.Forms;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, Category("Integration")]
    public void PositionDiagnosticPreservesRealSettlingUserChoiceAndFinalCapture()
    {
        using var fixture = new PositionDiagnostic(_directory);
        var original = fixture.Bounds();
        FinishWindowCheck(fixture.Start());
        var final = fixture.Bounds();
        Assert.That(Math.Abs(final.Left - original.Left), Is.LessThanOrEqualTo(2));
        Assert.That(Math.Abs(final.Top - original.Top), Is.LessThanOrEqualTo(2));
        Assert.That(final.Size, Is.EqualTo(original.Size));
        fixture.AssertSavedPosition(final);
        fixture.AssertPng();
        Assert.That(fixture.Timer.Interval, Is.EqualTo(TimeSpan.FromSeconds(3)));
        Assert.That(fixture.Timer.IsEnabled, Is.False);
        Assert.That(fixture.Window.Session.HasLiveSample, Is.False);
    }

    [Test, Category("Integration")]
    public void PositionDiagnosticPreservesTheUnshownWindowBoundsFailure()
    {
        using var fixture = new UpdateUi(_directory, true);
        var error = Assert.Throws<InvalidOperationException>(() =>
            FinishWindowCheck(PositionDiagnosticCapture(fixture.Window)(_directory, default)));
        Assert.That(error!.Message, Is.EqualTo("Window bounds unavailable."));
        Assert.That(File.Exists(Path.Combine(_directory, "position-recovery.png")), Is.False);
        Assert.That(fixture.Window.Session.HasLiveSample, Is.False);
    }

    [Test, Category("Integration")]
    public void PositionDiagnosticPreCancellationRetainsItsOriginalMoveAndQueuedRecovery()
    {
        using var fixture = new PositionDiagnostic(_directory);
        var original = fixture.Bounds();
        fixture.Cancel();
        var error = Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(fixture.Start()));
        Assert.That(error!.CancellationToken, Is.EqualTo(fixture.Token));
        Assert.That(fixture.Bounds().Left, Is.Not.EqualTo(original.Left));
        fixture.Flush();
        Assert.That(fixture.Timer.IsEnabled, Is.True, "The original diagnostic does not cancel its separately queued recovery timer.");
        Assert.That(File.Exists(Path.Combine(_directory, "position-recovery.png")), Is.False);
    }

    [Test, Category("Integration")]
    public void PositionDiagnosticPreservesTheEarlyReturnToOriginalPositionFailure()
    {
        using var fixture = new PositionDiagnostic(_directory);
        var original = fixture.Bounds();
        var left = fixture.Window.Left;
        var queued = false;
        Task? adjustment = null;
        EventHandler changed = (_, _) =>
        {
            if (queued) return;
            queued = true;
            adjustment = fixture.Window.Dispatcher.InvokeAsync(() => fixture.Window.Left = left).Task;
        };
        fixture.Window.LocationChanged += changed;
        fixture.Cleanup(() => fixture.Window.LocationChanged -= changed);
        fixture.Reject("Display recovery moved the window before monitors settled.");
        Assert.That(queued, Is.True);
        Assert.That(adjustment, Is.Not.Null);
        FinishWindowCheck(adjustment!);
        Assert.That(fixture.Bounds().Left, Is.EqualTo(original.Left));
    }

    [TestCase("x", 2, false), TestCase("y", 2, false)]
    [TestCase("x", 3, true), TestCase("y", 3, true)]
    [TestCase("x", -3, true), TestCase("y", -3, true), Category("Integration")]
    public void PositionDiagnosticPreservesTheTwoNativePixelRestorationTolerance(string axis, int pixels, bool rejected)
    {
        using var fixture = new PositionDiagnostic(_directory);
        var original = fixture.Bounds();
        var observed = int.MinValue;
        fixture.OnRecoveryTick(1, () =>
        {
            fixture.MoveNativePixels(axis, pixels);
            var current = fixture.Bounds();
            observed = axis == "x" ? current.Left - original.Left : current.Top - original.Top;
        });
        if (rejected) fixture.Reject("Display recovery did not restore the remembered native position.");
        else
        {
            FinishWindowCheck(fixture.Start());
            var final = fixture.Bounds();
            Assert.That(axis == "x" ? final.Left - original.Left : final.Top - original.Top, Is.EqualTo(pixels));
            fixture.AssertSavedPosition(final);
        }
        Assert.That(observed, Is.EqualTo(pixels), "The test input must reach the requested physical pixel displacement.");
        Assert.That(fixture.Window.Session.HasLiveSample, Is.False);
    }

    [TestCase("x", 1), TestCase("y", -1), Category("Integration")]
    public void PositionDiagnosticPreservesExactUserChoiceFailureForEitherCoordinate(string axis, int pixels)
    {
        using var fixture = new PositionDiagnostic(_directory);
        var changed = false;
        var delta = 0;
        fixture.OnRecoveryTick(2, () =>
        {
            var before = fixture.Bounds();
            fixture.MoveNativePixels(axis, pixels);
            var after = fixture.Bounds();
            delta = axis == "x" ? after.Left - before.Left : after.Top - before.Top;
            changed = true;
        });
        fixture.Reject("Display recovery overrode the user's new position.");
        Assert.That(changed, Is.True);
        Assert.That(delta, Is.EqualTo(pixels));
    }

    [Test, Category("Integration")]
    public void PositionDiagnosticCancellationAtActualRecoveryRetainsTheCallerToken()
    {
        using var fixture = new PositionDiagnostic(_directory);
        var signalled = false;
        fixture.OnRecoveryTick(1, () => { signalled = true; fixture.Cancel(); });
        var error = Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(fixture.Start()));
        Assert.That(signalled, Is.True);
        Assert.That(error!.CancellationToken, Is.EqualTo(fixture.Token));
        Assert.That(File.Exists(Path.Combine(_directory, "position-recovery.png")), Is.False);
        Assert.That(fixture.Timer.IsEnabled, Is.False);
    }

    [TestCase("missing-folder"), TestCase("locked-file"), Category("Integration")]
    public void PositionDiagnosticFinalWriteFailuresPreserveDataAndCompletedNativeRecovery(string failure)
    {
        using var fixture = new PositionDiagnostic(_directory);
        var original = fixture.Bounds();
        var output = failure == "missing-folder" ? Path.Combine(_directory, "missing") : _directory;
        var path = Path.Combine(output, "position-recovery.png");
        if (failure == "locked-file") File.WriteAllText(path, "owned position image sentinel");
        using var locked = failure == "locked-file" ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
        var error = Assert.Catch<IOException>(() => FinishWindowCheck(fixture.Start(output)));
        Assert.That(error, failure == "missing-folder" ? Is.TypeOf<DirectoryNotFoundException>() : Is.TypeOf<IOException>());
        if (failure == "locked-file") Assert.That(File.ReadAllText(path), Is.EqualTo("owned position image sentinel"));
        else Assert.That(Directory.Exists(output), Is.False);
        var final = fixture.Bounds();
        Assert.That(Math.Abs(final.Left - original.Left), Is.LessThanOrEqualTo(2));
        Assert.That(Math.Abs(final.Top - original.Top), Is.LessThanOrEqualTo(2));
        Assert.That(final.Size, Is.EqualTo(original.Size));
        fixture.AssertSavedPosition(final);
        Assert.That(fixture.Timer.IsEnabled, Is.False);
    }

    private static Func<string, CancellationToken, Task> PositionDiagnosticCapture(MainWindow window) => typeof(WindowDiagnostics)
        .GetMethod("CheckWindowPositionRecoveryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
        .CreateDelegate<Func<string, CancellationToken, Task>>(new WindowDiagnostics(window));

    private sealed class PositionDiagnostic : IDisposable
    {
        private readonly MetricCaptureDiagnostic _fixture;
        private readonly string _directory;
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Action _close;
        private readonly List<Action> _cleanup = [];
        private Task? _active;
        internal MainWindow Window => _fixture.Window;
        internal DispatcherTimer Timer { get; }
        internal CancellationToken Token => _shutdown.Token;
        internal PositionDiagnostic(string directory)
        {
            _fixture = new MetricCaptureDiagnostic(directory);
            _directory = directory;
            _close = Bind<Action>("CloseWindowPosition");
            try
            {
                Window.Left = SystemParameters.WorkArea.Left + 20;
                Window.Top = SystemParameters.WorkArea.Top + 20;
                Window.UpdateLayout();
                Bind<Action>("InitializeWindowPosition")();
                Timer = (DispatcherTimer)typeof(MainWindow).GetField("_positionRecoveryTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Window)!;
                Assert.That(Timer.Interval, Is.EqualTo(TimeSpan.FromSeconds(3)));
            }
            catch
            {
                Window.IsClosing = true;
                _close();
                _fixture.Dispose();
                _shutdown.Dispose();
                throw;
            }
        }
        private T Bind<T>(string name) where T : Delegate => typeof(MainWindow)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<T>(Window);
        internal Task Start(string? output = null) => _active = PositionDiagnosticCapture(Window)(output ?? _directory, Token);
        internal void Cancel() => _shutdown.Cancel();
        internal DrawingRectangle Bounds() => NativeCaptureBounds(Window);
        internal void Cleanup(Action action) => _cleanup.Add(action);
        internal void Flush() => FinishWindowCheck(Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
        internal void OnRecoveryTick(int target, Action action)
        {
            var ticks = 0;
            EventHandler handler = (_, _) => { if (++ticks == target) action(); };
            Timer.Tick += handler;
            Cleanup(() => Timer.Tick -= handler);
        }
        internal void MoveNativePixels(string axis, int pixels)
        {
            var dpi = VisualTreeHelper.GetDpi(Window);
            if (axis == "x") Window.Left += pixels / dpi.DpiScaleX;
            else Window.Top += pixels / dpi.DpiScaleY;
        }
        internal void Reject(string message)
        {
            var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(Start()));
            Assert.That(error!.Message, Is.EqualTo(message));
            Assert.That(File.Exists(Path.Combine(_directory, "position-recovery.png")), Is.False);
        }
        internal void AssertSavedPosition(DrawingRectangle bounds)
        {
            var screen = Forms.Screen.FromHandle(new WindowInteropHelper(Window).Handle);
            var saved = (WindowPosition?)typeof(MainWindow).GetField("_windowPosition", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Window);
            Assert.That(saved, Is.EqualTo(WindowPosition.Capture(screen.DeviceName, screen.WorkingArea, new DrawingPoint(bounds.Left, bounds.Top))));
        }
        internal void AssertPng()
        {
            var path = Path.Combine(_directory, "position-recovery.png");
            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
            var visual = (FrameworkElement)Window.Content;
            Assert.That(frame.PixelWidth, Is.EqualTo((int)Math.Ceiling(visual.ActualWidth)));
            Assert.That(frame.PixelHeight, Is.EqualTo((int)Math.Ceiling(visual.ActualHeight)));
            var directory = TestContext.Parameters.Get("PositionDiagnosticArtifactDirectory", "");
            if (directory.Length == 0) return;
            Directory.CreateDirectory(directory);
            using var copy = File.Create(Path.Combine(directory, "position-recovery.png"));
            stream.Position = 0;
            stream.CopyTo(copy);
        }
        public void Dispose()
        {
            foreach (var cleanup in _cleanup.AsEnumerable().Reverse()) cleanup();
            Window.IsClosing = true;
            _shutdown.Cancel();
            _close();
            if (_active is { IsCompleted: false })
            {
                try { FinishWindowCheck(_active); }
                catch (OperationCanceledException) { }
            }
            FinishWindowCheck(Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
            Assert.That(Timer.IsEnabled, Is.False);
            _fixture.Dispose();
            _shutdown.Dispose();
        }
    }
}
