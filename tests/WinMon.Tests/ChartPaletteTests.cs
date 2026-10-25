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
    [TestCase("Cyberpunk / Neon", "Cyberpunk / Neon")]
    public void Validate_NormalizesPalette(string? input, string expected)
    {
        Assert.That(PreferenceStore.Validate(new MonitorPreferences { ChartPalette = input! }).ChartPalette, Is.EqualTo(expected));
    }

    [TestCase("Neon", "#00E5FF,#FF1493,#B6FF00,#FF6D00,#A100FF,#FFE600")]
    [TestCase("Arcade", "#FF1744,#2979FF,#00E676,#FFEA00,#D500F9,#FF9100")]
    [TestCase("Tropical", "#FF3D71,#00B8D9,#FFAB00,#6554FF,#00C896,#FF5630")]
    public void CustomPalette_PreservesRequestedResourceOrderInBothThemes(string palette, string hexColors)
    {
        var expected = hexColors.Split(',');
        Assert.Multiple(() =>
        {
            Assert.That(ChartPalettes.Colors(palette, false), Is.EqualTo(expected));
            Assert.That(ChartPalettes.Colors(palette, true), Is.EqualTo(expected));
            Assert.That(ChartPalettes.Names, Does.Contain(palette));
        });
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
    public void PaletteSwatches_FollowSelectedFinish()
    {
        var choice = new PaletteChoice("Sophisticated Pastel");
        var candy = ThemeFinishes.For("Candy");
        choice.UpdateTheme(candy.Light, candy);
        Assert.That(choice.Colors, Is.EqualTo(ChartPalettes.ColorsForTheme(choice.Name, candy.Light, candy)));
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
