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

    [Test]
    public async Task Palette_RoundTripsWithoutChangingTheme()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WinMon.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PreferenceStore(Path.Combine(directory, "settings.json"));
            await store.SaveAsync(new MonitorPreferences { ChartPalette = "Ocean", Theme = "Light" });
            var loaded = await store.LoadAsync();
            Assert.Multiple(() =>
            {
                Assert.That(loaded.ChartPalette, Is.EqualTo("Ocean"));
                Assert.That(loaded.Theme, Is.EqualTo("Light"));
            });
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
