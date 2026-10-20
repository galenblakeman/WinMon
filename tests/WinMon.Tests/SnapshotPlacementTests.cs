using System.Windows;
using NUnit.Framework;
using WinMon.Controls;

namespace WinMon.Tests;

[TestFixture]
public sealed class SnapshotPlacementTests
{
    [TestCase(39, 8)]
    [TestCase(558, 8)]
    [TestCase(39, 255)]
    [TestCase(558, 255)]
    [TestCase(-100, -100)]
    [TestCase(2000, 2000)]
    public void Calculate_EdgesAndOffscreenAnchors_ContainsEntirePanel(double x, double y)
    {
        var plot = new Rect(39, 8, 519, 247);
        var panel = SnapshotPlacement.Calculate(plot, new Point(x, y), new Size(320, 216));
        Assert.That(plot.Contains(panel), Is.True);
        Assert.That(panel.Size, Is.EqualTo(new Size(320, 216)));
    }

    [Test]
    public void Calculate_RightEdge_FlipsPanelToLeftOfSelection()
    {
        var panel = SnapshotPlacement.Calculate(new Rect(39, 8, 900, 400), new Point(925, 200), new Size(320, 216));
        Assert.That(panel.Right, Is.LessThan(925));
    }

    [Test]
    public void Calculate_LeftEdge_PlacesPanelToRightOfSelection()
    {
        var panel = SnapshotPlacement.Calculate(new Rect(39, 8, 900, 400), new Point(39, 200), new Size(320, 216));
        Assert.That(panel.Left, Is.GreaterThan(39));
    }

    [TestCase(240, 127)]
    [TestCase(60, 40)]
    [TestCase(0, 0)]
    public void Calculate_ChartSmallerThanPanel_ConstrainsBothDimensions(double width, double height)
    {
        var plot = new Rect(39, 8, width, height);
        var panel = SnapshotPlacement.Calculate(plot, new Point(550, 300), new Size(320, 216));
        Assert.That(plot.Contains(panel), Is.True);
    }
}
