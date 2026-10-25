using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
[SetUICulture("en")]
public sealed class StringResourceTests
{
    [Test]
    public void AllTextResources_AreEmbeddedAndHaveValidFormatPlaceholders()
    {
        var properties = typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string)).ToArray();
        Assert.That(properties, Is.Not.Empty);
        foreach (var property in properties)
        {
            var text = (string?)property.GetValue(null);
            Assert.That(text, Is.Not.Null.And.Not.Empty, property.Name);
            Assert.DoesNotThrow(() => CompositeFormat.Parse(text!), property.Name);
        }
    }

    [TestCase("it-IT")]
    [TestCase("pt-PT")]
    [TestCase("zh-TW")]
    public void MissingTranslation_FallsBackToEnglishAndFormatsWithCurrentCulture(string unsupportedCulture)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(unsupportedCulture);
            Assert.That(Strings.WinMonSettings, Is.EqualTo("WinMon settings"));
            Assert.That(Strings.Format(Strings.PercentOneDecimal, 12.5), Is.EqualTo("12,5%"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Test]
    public void PaletteLabels_KeepEveryStoredPaletteIdentifier()
    {
        foreach (var name in ChartPalettes.Names)
        {
            var choice = new PaletteChoice(name);
            Assert.That(choice.Name, Is.EqualTo(name));
            Assert.That(choice.DisplayName, Is.EqualTo(ChartPalettes.DisplayName(name)));
        }
    }

    [TestCase("en")]
    [TestCase("de")]
    [TestCase("es")]
    [TestCase("fr")]
    [TestCase("ja")]
    [TestCase("ko")]
    [TestCase("pl")]
    [TestCase("pt-BR")]
    [TestCase("ru")]
    [TestCase("zh-Hans")]
    public void ThemeLabels_AreSingleWordsInEveryLanguage(string culture)
    {
        foreach (var key in new[] { "ThemeDark", "ThemeLight", "ThemeSystem", "ThemeCopper",
                     "ThemeGunmetal", "ThemePorcelain", "ThemeMidnightInk", "ThemeEvergreenInstrument",
                     "ThemeFrostedPorcelain", "ThemeSmokedSapphire", "ThemeCrystal", "ThemeSlate",
                     "ThemePrism" })
        {
            var label = Strings.ResourceManager.GetString(key, CultureInfo.GetCultureInfo(culture));
            Assert.That(label, Is.Not.Null.And.Not.Empty, key);
            Assert.That(label, Does.Not.Match(@"\s"), $"{culture}: {key}");
        }
    }

    [TestCase("zh-CN", "WinMon 设置")]
    [TestCase("zh-SG", "WinMon 设置")]
    [TestCase("ru-RU", "Настройки WinMon")]
    [TestCase("es-ES", "Ajustes de WinMon")]
    [TestCase("es-MX", "Ajustes de WinMon")]
    [TestCase("pt-BR", "Configurações do WinMon")]
    [TestCase("de-DE", "WinMon-Einstellungen")]
    [TestCase("ja-JP", "WinMon の設定")]
    [TestCase("fr-FR", "Paramètres de WinMon")]
    [TestCase("fr-CA", "Paramètres de WinMon")]
    [TestCase("pl-PL", "Ustawienia WinMon")]
    [TestCase("ko-KR", "WinMon 설정")]
    public void DisplayLanguage_ResolvesLocalizedTextIncludingParentCultures(string culture, string expected)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            Assert.That(Strings.WinMonSettings, Is.EqualTo(expected));
        }
        finally { CultureInfo.CurrentUICulture = original; }
    }

    [TestCase("zh-Hans")]
    [TestCase("ru")]
    [TestCase("es")]
    [TestCase("pt-BR")]
    [TestCase("de")]
    [TestCase("ja")]
    [TestCase("fr")]
    [TestCase("pl")]
    [TestCase("ko")]
    public void Translation_HasEveryKeyAndPreservesFormatSpecifiersAndLineBreaks(string culture)
    {
        var neutral = Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var localized = Strings.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo(culture), true, false);
        Assert.That(localized, Is.Not.Null, "The satellite assembly must be deployed.");
        var keys = neutral.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToArray();
        Assert.That(localized!.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key), Is.EquivalentTo(keys));
        foreach (var key in keys)
        {
            var original = neutral.GetString(key)!;
            var translation = localized!.GetString(key);
            Assert.That(translation, Is.Not.Null.And.Not.Empty, key);
            Assert.DoesNotThrow(() => CompositeFormat.Parse(translation!), key);

            Assert.That(Placeholders(translation!), Is.EqualTo(Placeholders(original)), key);
            Assert.That(translation!.Count(c => c == '\n'), Is.EqualTo(original.Count(c => c == '\n')), key);
            continue;

            // Order can change with grammar; argument numbers, multiplicity, and numeric/date formats cannot.
            static string[] Placeholders(string text) =>
            [
                .. Regex.Matches(text, @"\{\d+(?:,[^}:]+)?(?::[^}]+)?\}")
                    .Select(m => m.Value).Order()
            ];
        }
    }

    [Test]
    [SetUICulture("de-DE")]
    public void TranslatedPaletteLabels_DoNotChangeStoredIdentifiers()
    {
        var palette = new PaletteChoice("High-Contrast & Inclusive");
        Assert.That(palette.DisplayName, Is.EqualTo("Hoher Kontrast"));
        Assert.That(palette.Name, Is.EqualTo("High-Contrast & Inclusive"));
    }

    [Test]
    public void FallbackCulture_IsEnglishInTheMainAssembly()
    {
        Assert.That(typeof(Strings).Assembly.GetCustomAttribute<System.Resources.NeutralResourcesLanguageAttribute>()?.CultureName,
            Is.EqualTo("en"));
    }
}
