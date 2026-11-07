using System.Drawing;
using NUnit.Framework;

namespace WinMon.Tests;

[TestFixture]
public sealed class WindowPositionTests
{
    [Test]
    public void Resolve_DisconnectAndReconnect_PreservesOriginalMonitorAndOffset()
    {
        var position = WindowPosition.Capture("secondary", new Rectangle(-1920, 0, 1920, 1040),
            new Point(-1800, 100));
        var primary = ("primary", new Rectangle(0, 0, 1920, 1040));
        Assert.That(position.Resolve([primary], new Size(1000, 840)), Is.Null);
        Assert.That(position.Resolve([primary, ("SECONDARY", new Rectangle(-1920, 0, 1920, 1040))],
            new Size(1000, 840)), Is.EqualTo(new Point(-1800, 100)));
    }

    [Test]
    public void Resolve_RearrangedMonitor_UsesNewWorkAreaOrigin()
    {
        var position = WindowPosition.Capture("secondary", new Rectangle(1920, 0, 1920, 1080),
            new Point(2020, 120));
        Assert.That(position.Resolve([("secondary", new Rectangle(0, -1080, 1920, 1040))], new Size(1000, 840)),
            Is.EqualTo(new Point(100, -960)));
    }

    [TestCase(800, 600, 1000, 840, 0, 0)]
    [TestCase(1200, 900, 1000, 840, 200, 60)]
    public void Resolve_SmallerWorkArea_KeepsTitleBarAccessible(int width, int height, int windowWidth,
        int windowHeight, int x, int y)
    {
        var position = new WindowPosition("display", new Point(900, 700));
        Assert.That(position.Resolve([("display", new Rectangle(0, 0, width, height))],
            new Size(windowWidth, windowHeight)), Is.EqualTo(new Point(x, y)));
    }
}
