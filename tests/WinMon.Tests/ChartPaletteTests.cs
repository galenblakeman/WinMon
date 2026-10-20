using NUnit.Framework;
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
        var choice = new PaletteChoice(palette);
        Assert.That(choice.Colors, Is.EqualTo(expected));
        choice.UpdateTheme(true);
        Assert.Multiple(() =>
        {
            Assert.That(choice.Colors, Is.EqualTo(expected));
            Assert.That(ChartPalettes.Colors(palette, false), Is.EqualTo(expected));
            Assert.That(ChartPalettes.Names, Does.Contain(palette));
        });
    }

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
