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

    private static double Contrast(string foreground, string background)
    {
        var a = Luminance(foreground);
        var b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
