using System.Windows;
using NUnit.Framework;
using DrawingPoint = System.Drawing.Point;
using Forms = System.Windows.Forms;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase("no-source"), TestCase("minimized"), TestCase("maximized")]
    public void PositionDiagnosticRememberGuardsPreserveTheExistingTarget(string state)
    {
        using var flow = new PositionFlow(_directory);
        var saved = new WindowPosition("owned existing monitor", new DrawingPoint(30, 40));
        flow.Saved = saved;
        flow.Window.WindowState = state == "minimized" ? WindowState.Minimized
            : state == "maximized" ? WindowState.Maximized : WindowState.Normal;
        flow.Window.RememberWindowPositionForFixture();
        Assert.That(flow.Saved, Is.SameAs(saved));
        Assert.That(flow.Window.WindowState, Is.EqualTo(state == "minimized" ? WindowState.Minimized
            : state == "maximized" ? WindowState.Maximized : WindowState.Normal));
        Assert.That(flow.Window.Session.HasLiveSample, Is.False);
    }

    [Test, Category("Integration")]
    public void PositionDiagnosticRememberRecordsTheCurrentNativePositionWithoutMovingOrResizing()
    {
        using var flow = new PositionFlow(_directory, show: true);
        flow.Window.Left += 15;
        flow.Flush();
        var before = flow.Bounds();
        flow.Window.RememberWindowPositionForFixture();
        var after = flow.Bounds();
        var screen = Forms.Screen.FromHandle(flow.Handle);
        Assert.That(flow.Saved, Is.EqualTo(WindowPosition.Capture(screen.DeviceName, screen.WorkingArea,
            new DrawingPoint(before.Left, before.Top))));
        Assert.That((after.Left, after.Top, after.Right, after.Bottom), Is.EqualTo((before.Left, before.Top, before.Right, before.Bottom)));
        Assert.That(flow.Window.Session.HasLiveSample, Is.False);
    }
}
