using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using NUnit.Framework;
using Forms = System.Windows.Forms;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PositionRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadPositionRect(nint hwnd, out PositionRect bounds);

    private delegate nint PositionHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled);

    private sealed class PositionFlow : IDisposable
    {
        private readonly UpdateUi _ui;
        internal MainWindow Window => _ui.Window;
        internal readonly DispatcherTimer Timer;
        internal readonly Action Recover;
        internal readonly PositionHook Hook;
        internal nint Handle;

        internal PositionFlow(string directory, bool show = false)
        {
            _ui = new UpdateUi(directory, diagnostic: true);
            Recover = Bind<Action>("RecoverWindowPosition");
            Hook = Bind<PositionHook>("WindowPositionHook");
            Timer = new DispatcherTimer(DispatcherPriority.Background, Window.Dispatcher)
                { Interval = TimeSpan.FromMinutes(1) };
            _ui.Field("_positionRecoveryTimer").SetValue(Window, Timer);
            if (!show) return;
            Window.ApplyTheme(false, false);
            Window.ShowActivated = false;
            Window.ShowInTaskbar = false;
            Window.WindowStartupLocation = WindowStartupLocation.Manual;
            Window.Left = SystemParameters.WorkArea.Left + 20;
            Window.Top = SystemParameters.WorkArea.Top + 20;
            Window.Title = "Owned window-position fixture";
            Window.Show();
            Handle = new WindowInteropHelper(Window).Handle;
            _ui.Field("_positionSource").SetValue(Window, HwndSource.FromHwnd(Handle));
            Flush();
        }

        private T Bind<T>(string name) where T : Delegate =>
            (typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new AssertionException("Missing position callback: " + name)).CreateDelegate<T>(Window);
        internal WindowPosition? Saved
        {
            get => (WindowPosition?)_ui.Field("_windowPosition").GetValue(Window);
            set => _ui.Field("_windowPosition").SetValue(Window, value);
        }
        internal PositionRect Bounds()
        {
            var success = ReadPositionRect(Handle, out var bounds);
            var error = Marshal.GetLastPInvokeError();
            Assert.That(success, Is.True, "Owned HWND geometry query failed: " + error);
            return bounds;
        }
        internal void Flush() => FinishWindowCheck(Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
        public void Dispose() { Timer.Stop(); _ui.Dispose(); }
    }

    [TestCase("closing"), TestCase("moving"), TestCase("minimized"), TestCase("maximized"), TestCase("missing-source")]
    public void WindowPositionRecoveryGuardsStopTheTimerWithoutChangingTheSavedTarget(string guard)
    {
        using var flow = new PositionFlow(_directory);
        var saved = new WindowPosition("owned-disconnected-display", new DrawingPoint(30, 40));
        flow.Saved = saved;
        if (guard == "closing") flow.Window.IsClosing = true;
        if (guard == "moving") { var handled = false; flow.Hook(0, 0x0231, 0, 0, ref handled); }
        if (guard == "minimized") flow.Window.WindowState = WindowState.Minimized;
        if (guard == "maximized") flow.Window.WindowState = WindowState.Maximized;
        var before = (flow.Window.Left, flow.Window.Top, flow.Window.Width, flow.Window.Height, flow.Window.WindowState);
        flow.Timer.Start();
        flow.Recover();
        Assert.That(flow.Timer.IsEnabled, Is.False);
        Assert.That(flow.Saved, Is.SameAs(saved));
        Assert.That((flow.Window.Left, flow.Window.Top, flow.Window.Width, flow.Window.Height, flow.Window.WindowState), Is.EqualTo(before));
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [TestCase(false), TestCase(true)]
    public void WindowPositionRecoveryFallsBackForMissingTargetsWithoutReplacingTheOriginalChoice(bool missingMonitor)
    {
        using var flow = new PositionFlow(_directory, show: true);
        var saved = missingMonitor ? new WindowPosition("owned-disconnected-" + Guid.NewGuid().ToString("N"), new DrawingPoint(80, 90)) : null;
        flow.Saved = saved;
        flow.Window.Left = -100000;
        flow.Window.Top = -100000;
        flow.Flush();
        flow.Timer.Start();
        flow.Recover();
        flow.Flush();
        Assert.That(flow.Window.Left, Is.EqualTo(SystemParameters.WorkArea.Left + 20));
        Assert.That(flow.Window.Top, Is.EqualTo(SystemParameters.WorkArea.Top + 20));
        Assert.That(flow.Saved, Is.SameAs(saved));
        Assert.That(flow.Timer.IsEnabled, Is.False);
        Assert.That(flow.Window.SamplingTask, Is.Null);
    }

    [TestCase(24, 30), TestCase(-400, -500), TestCase(int.MaxValue, int.MaxValue)]
    public void WindowPositionRecoveryRestoresAndClampsTheOwnedWindowWithoutResizing(int x, int y)
    {
        using var flow = new PositionFlow(_directory, show: true);
        var screen = Forms.Screen.FromHandle(flow.Handle);
        var saved = new WindowPosition(screen.DeviceName, new DrawingPoint(x, y));
        flow.Saved = saved;
        var before = flow.Bounds();
        var width = before.Right - before.Left;
        var height = before.Bottom - before.Top;
        var expected = saved.Resolve([(screen.DeviceName, screen.WorkingArea)], new DrawingSize(width, height));
        flow.Timer.Start();
        flow.Recover();
        flow.Flush();
        var after = flow.Bounds();
        Assert.That(new DrawingPoint(after.Left, after.Top), Is.EqualTo(expected));
        Assert.That((after.Right - after.Left, after.Bottom - after.Top), Is.EqualTo((width, height)));
        Assert.That(flow.Saved, Is.SameAs(saved));
        Assert.That(flow.Timer.IsEnabled, Is.False);
        Assert.That(flow.Window.IsVisible, Is.True);
    }

    [Test]
    public void WindowPositionMoveMessagesRememberTheUsersNewPositionAndStopRecovery()
    {
        using var flow = new PositionFlow(_directory, show: true);
        flow.Saved = new WindowPosition("owned-old-display", new DrawingPoint(1, 2));
        var handled = true;
        Assert.That(flow.Hook(flow.Handle, 0x0231, 0, 0, ref handled), Is.EqualTo(nint.Zero));
        flow.Window.Left += 35;
        flow.Window.Top += 20;
        flow.Flush();
        var moved = flow.Bounds();
        flow.Recover();
        Assert.That((flow.Bounds().Left, flow.Bounds().Top), Is.EqualTo((moved.Left, moved.Top)), "An active user move must not be undone");
        var screen = Forms.Screen.FromHandle(flow.Handle);
        flow.Timer.Start();
        Assert.That(flow.Hook(flow.Handle, 0x0232, 0, 0, ref handled), Is.EqualTo(nint.Zero));
        Assert.That(handled, Is.True, "The hook observes messages without claiming them");
        Assert.That(flow.Timer.IsEnabled, Is.False);
        var saved = WindowPosition.Capture(screen.DeviceName, screen.WorkingArea, new DrawingPoint(moved.Left, moved.Top));
        Assert.That(flow.Saved, Is.EqualTo(saved));
        var size = new DrawingSize(moved.Right - moved.Left, moved.Bottom - moved.Top);
        var expected = saved.Resolve([(screen.DeviceName, screen.WorkingArea)], size);
        flow.Window.Left += 20;
        flow.Flush();
        flow.Recover();
        flow.Flush();
        var restored = flow.Bounds();
        Assert.That(new DrawingPoint(restored.Left, restored.Top), Is.EqualTo(expected));
    }

    [Test]
    public void WindowPositionUnrelatedMessagesPreserveTheTimerTargetAndHandledFlag()
    {
        using var flow = new PositionFlow(_directory);
        var saved = new WindowPosition("owned-target", new DrawingPoint(10, 20));
        flow.Saved = saved;
        flow.Timer.Start();
        var handled = false;
        Assert.That(flow.Hook(0, 0x0000, 0, 0, ref handled), Is.EqualTo(nint.Zero));
        Assert.That(handled, Is.False);
        Assert.That(flow.Timer.IsEnabled, Is.True);
        Assert.That(flow.Saved, Is.SameAs(saved));
    }
}
