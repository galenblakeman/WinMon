using System.Globalization;
using NUnit.Framework;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
public sealed class LanguagePreferenceTests
{
    [TestCase(null, "System")]
    [TestCase("", "System")]
    [TestCase("unknown", "System")]
    [TestCase("fr-FR", "System")]
    [TestCase(" system ", "System")]
    [TestCase(" DE ", "de")]
    [TestCase("ZH-hans", "zh-Hans")]
    [TestCase("pt-br", "pt-BR")]
    public void Validate_NormalizesStoredLanguage(string? input, string expected)
    {
        Assert.That(PreferenceStore.Validate(new MonitorPreferences { Language = input! }).Language, Is.EqualTo(expected));
    }

    [TestCase("de-DE")]
    [TestCase("ja-JP")]
    [TestCase("it-IT")]
    public void FirstLaunch_UsesSystemDisplayCultureIncludingUnsupportedLanguages(string culture)
    {
        var preferences = new MonitorPreferences();
        var systemCulture = CultureInfo.GetCultureInfo(culture);
        Assert.That(preferences.Language, Is.EqualTo(AppLanguages.SystemDefault));
        Assert.That(AppLanguages.ResolveCulture(preferences.Language, systemCulture), Is.SameAs(systemCulture));
    }

    [Test]
    public void SystemChoice_IsResolvedAgainForEachLaunch()
    {
        Assert.That(AppLanguages.ResolveCulture("System", CultureInfo.GetCultureInfo("de-DE")).Name, Is.EqualTo("de-DE"));
        Assert.That(AppLanguages.ResolveCulture("System", CultureInfo.GetCultureInfo("fr-FR")).Name, Is.EqualTo("fr-FR"));
    }

    private static IEnumerable<string> Languages => AppLanguages.Choices.Select(choice => choice.Name);

    [TestCaseSource(nameof(Languages))]
    public async Task LanguageSelection_RoundTripsAndOverridesSystemWhenExplicit(string language)
    {
        var directory = Path.Combine(Path.GetTempPath(), "WinMon.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PreferenceStore(Path.Combine(directory, "settings.json"));
            await store.SaveAsync(new MonitorPreferences { Language = language });
            var saved = await store.LoadAsync();
            Assert.That(saved.Language, Is.EqualTo(language));
            var originalFormats = CultureInfo.CurrentCulture;
            var resolved = AppLanguages.ResolveCulture(saved.Language, CultureInfo.GetCultureInfo("fr-CA"));
            Assert.That(resolved.Name, Is.EqualTo(language == AppLanguages.SystemDefault ? "fr-CA" : language));
            Assert.That(CultureInfo.CurrentCulture, Is.SameAs(originalFormats));
            await store.SaveAsync(saved with { Language = AppLanguages.SystemDefault });
            Assert.That((await store.LoadAsync()).Language, Is.EqualTo(AppLanguages.SystemDefault));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestCase(null)]
    [TestCase("{\"Theme\":\"Light\"}")]
    public async Task MissingOrOlderSettings_DefaultToSystemLanguage(string? legacySettings)
    {
        var directory = Path.Combine(Path.GetTempPath(), "WinMon.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "settings.json");
            if (legacySettings is not null)
            {
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(path, legacySettings);
            }
            Assert.That((await new PreferenceStore(path).LoadAsync()).Language, Is.EqualTo(AppLanguages.SystemDefault));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
