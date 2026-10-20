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
    public void Validate_NormalizesPalette(string? input, string expected)
    {
        Assert.That(PreferenceStore.Validate(new MonitorPreferences { ChartPalette = input! }).ChartPalette, Is.EqualTo(expected));
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
