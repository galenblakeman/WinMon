using System.Windows.Media;
using NUnit.Framework;
using WinMon.Preferences;

namespace WinMon.Tests;

public sealed class ThemeChipContrastTests
{
    [TestCaseSource(typeof(ThemeFinishes), nameof(ThemeFinishes.Names))]
    public void ChipLabels_HaveNormalTextContrastInEveryFinish(string name)
    {
        var finish = ThemeFinishes.For(name);

        Assert.Multiple(() =>
        {
            Assert.That(Contrast(finish.Text, finish.Header), Is.GreaterThanOrEqualTo(4.5),
                $"{name} unselected chip");
            Assert.That(Contrast(ThemeFinishes.SelectedChipText(finish), finish.SegmentSelected),
                Is.GreaterThanOrEqualTo(4.5), $"{name} selected chip");
            Assert.That(Contrast(ThemeFinishes.SelectedChipText(finish), finish.Header),
                Is.GreaterThanOrEqualTo(3), $"{name} selected chip outline");
        });
    }

    [TestCaseSource(typeof(ThemeFinishes), nameof(ThemeFinishes.Names))]
    public void InactiveTabLabels_ClearTheirHeaderInEveryFinish(string name)
    {
        var finish = ThemeFinishes.For(name);
        var foreground = ThemeFinishes.InactiveTabText(name, finish);
        Assert.That(Contrast(foreground, finish.Header), Is.GreaterThanOrEqualTo(4.5), name);

        if (!finish.Brushed) return;
        var header = ThemeFinishes.ChromeBrush(finish, true);
        var drawing = (DrawingGroup)header.Drawing!;
        var reflection = (LinearGradientBrush)((GeometryDrawing)drawing.Children[0]).Brush!;
        var sheen = reflection.GradientStops[1].Color;
        Assert.That(Contrast((Color)ColorConverter.ConvertFromString(foreground), sheen),
            Is.GreaterThanOrEqualTo(4.5), $"{name} at header reflection");
    }

    private static double Contrast(string foreground, string background)
    {
        return Contrast((Color)ColorConverter.ConvertFromString(foreground),
            (Color)ColorConverter.ConvertFromString(background));
    }

    private static double Contrast(Color foreground, Color background)
    {
        var a = Luminance(foreground);
        var b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(Color color)
    {
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
