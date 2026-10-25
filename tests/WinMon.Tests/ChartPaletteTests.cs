using NUnit.Framework;
using System.Windows.Media;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
public sealed class ChartPaletteTests
{
    [TestCase(null, "Classic")]
    [TestCase("unknown", "Classic")]
    [TestCase(" ocean ", "Ocean")]
    [TestCase("COLORBLIND", "Colorblind")]
    [TestCase(" neon ", "Neon")]
    [TestCase("ARCADE", "Arcade")]
    [TestCase("tropical", "Tropical")]
    [TestCase(" candy ", "Candy")]
    [TestCase("Cyberpunk / Neon", "Cyberpunk / Neon")]
    public void Validate_NormalizesPalette(string? input, string expected)
    {
        Assert.That(PreferenceStore.Validate(new MonitorPreferences { ChartPalette = input! }).ChartPalette, Is.EqualTo(expected));
    }

    [TestCase("Neon", "#00E5FF,#FF1493,#B6FF00,#FF6D00,#A100FF,#FFE600", "#0090C1,#F0127F,#4E9700,#E55900,#9A00F0,#B07D00")]
    [TestCase("Arcade", "#FF1744,#2979FF,#00E676,#FFEA00,#D500F9,#FF9100", "#F0153F,#2266F0,#009C4E,#B07D00,#B800D9,#C24E00")]
    [TestCase("Tropical", "#FF3D71,#00B8D9,#FFAB00,#6554FF,#00C896,#FF5630", "#FF3D71,#00B8D9,#FFAB00,#6554FF,#00C896,#FF5630")]
    [TestCase("Candy", "#7EE36E,#5FA8FF,#FF9C52,#FF7DB8,#45E0C6,#FFD25E", "#2E7D32,#1565C0,#C2410A,#C2185B,#00796B,#7A5200")]
    public void CustomPalette_PreservesRequestedResourceOrderInEachTheme(string palette, string darkColors, string lightColors)
    {
        Assert.Multiple(() =>
        {
            Assert.That(ChartPalettes.Colors(palette, false), Is.EqualTo(darkColors.Split(',')));
            Assert.That(ChartPalettes.Colors(palette, true), Is.EqualTo(lightColors.Split(',')));
            Assert.That(ChartPalettes.Names, Does.Contain(palette));
        });
    }

    [TestCase("High-Contrast & Inclusive", false)]
    [TestCase("High-Contrast & Inclusive", true)]
    [TestCase("Sophisticated Pastel", true)]
    [TestCase("Vibrant Monochromatic Shift", false)]
    [TestCase("Vibrant Monochromatic Shift", true)]
    [TestCase("Cyberpunk / Neon", true)]
    [TestCase("Warm Earth Tones", false)]
    [TestCase("Signal", true)]
    [TestCase("Spectrum", false)]
    [TestCase("Spectrum", true)]
    [TestCase("Neon", true)]
    [TestCase("Arcade", true)]
    [TestCase("Candy", false)]
    [TestCase("Candy", true)]
    public void HandTunedVariant_KeepsDesignedChartColorsOnEveryThemeInItsMode(string palette, bool light)
    {
        foreach (var theme in new[] { light ? "Light" : "Dark" }.Concat(ThemeFinishes.Names))
        {
            var finish = ThemeFinishes.IsFinish(theme) ? ThemeFinishes.For(theme) : null;
            if (finish is not null && finish.Light != light) continue;
            Assert.That(ChartPalettes.ChartColorsForTheme(palette, light, finish),
                Is.EqualTo(ChartPalettes.Colors(palette, light)), $"{theme} / {palette}");
        }
    }

    [Test]
    public void EveryPaletteColor_ClearsPlotAndReadoutContrastOnEveryTheme()
    {
        foreach (var theme in new[] { "Dark", "Light" }.Concat(ThemeFinishes.Names))
        {
            var finish = ThemeFinishes.IsFinish(theme) ? ThemeFinishes.For(theme) : null;
            var light = theme == "Light" || finish?.Light == true;
            var surface = ColorOf(finish?.Surface ?? (light ? "#F0F5FA" : "#15191E"));
            var plot = ColorOf(finish?.Plot ?? (light ? "#FFFFFF" : "#15191E"));
            foreach (var palette in ChartPalettes.Names)
            {
                var colors = ChartPalettes.ColorsForTheme(palette, light, finish);
                Assert.That(colors, Has.Length.EqualTo(6), $"{theme} / {palette}");
                foreach (var color in colors)
                {
                    Assert.That(ChartPalettes.ContrastRatio(ColorOf(color), surface), Is.GreaterThanOrEqualTo(4.5),
                        $"Gauge text: {theme} / {palette} / {color}");
                    Assert.That(ChartPalettes.ContrastRatio(ColorOf(color), plot), Is.GreaterThanOrEqualTo(3.0),
                        $"Chart trace: {theme} / {palette} / {color}");
                }
            }
        }
    }

    [Test]
    public void ChartColors_ClearPlotContrastWithoutApplyingGaugeTextThreshold()
    {
        foreach (var theme in new[] { "Dark", "Light" }.Concat(ThemeFinishes.Names))
        {
            var finish = ThemeFinishes.IsFinish(theme) ? ThemeFinishes.For(theme) : null;
            var light = theme == "Light" || finish?.Light == true;
            var plot = ColorOf(finish?.Plot ?? (light ? "#FFFFFF" : "#15191E"));
            foreach (var palette in ChartPalettes.Names)
                foreach (var color in ChartPalettes.ChartColorsForTheme(palette, light, finish))
                    Assert.That(ChartPalettes.ContrastRatio(ColorOf(color), plot), Is.GreaterThanOrEqualTo(3.0),
                        $"Chart trace: {theme} / {palette} / {color}");
        }

        var candy = ThemeFinishes.For("Candy");
        Assert.That(ChartPalettes.ChartColorsForTheme("Neon", candy.Light, candy)[2],
            Is.Not.EqualTo(ChartPalettes.ColorsForTheme("Neon", candy.Light, candy)[2]),
            "A bright chart trace should not inherit the gauge text threshold.");
    }

    [Test]
    public void PaletteSwatches_FollowSelectedFinish()
    {
        var choice = new PaletteChoice("Tropical");
        var candy = ThemeFinishes.For("Candy");
        choice.UpdateTheme(candy.Light, candy);
        Assert.That(choice.Colors, Is.EqualTo(ChartPalettes.ChartColorsForTheme(choice.Name, candy.Light, candy)));
        Assert.That(choice.Colors, Is.Not.EqualTo(ChartPalettes.Colors(choice.Name, candy.Light)));
    }

    private static Color ColorOf(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    [TestCaseSource(typeof(ChartPalettes), nameof(ChartPalettes.Names))]
    public async Task Palette_RoundTripsWithoutChangingThemeOrDiskSelection(string palette)
    {
        var directory = Path.Combine(Path.GetTempPath(), "WinMon.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PreferenceStore(Path.Combine(directory, "settings.json"));
            await store.SaveAsync(new MonitorPreferences { ChartPalette = palette, Theme = "Light", PrimaryDrive = "D:", SecondaryDrive = "E:" });
            var loaded = await store.LoadAsync();
            Assert.Multiple(() =>
            {
                Assert.That(loaded.ChartPalette, Is.EqualTo(palette));
                Assert.That(loaded.Theme, Is.EqualTo("Light"));
                Assert.That(loaded.PrimaryDrive, Is.EqualTo("D:"));
                Assert.That(loaded.SecondaryDrive, Is.EqualTo("E:"));
            });
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
