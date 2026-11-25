using System.Globalization;
using NUnit.Framework;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
[SetCulture("en-US")]
[SetUICulture("en-US")]
public sealed class PaletteDescriptionCharacterizationTests
{
    private const string NeonDescription = "Custom · vivid cyan, pink, lime, and violet";
    private CultureInfo? _resourceCulture;

    [SetUp]
    public void UseCurrentUiCulture()
    {
        _resourceCulture = Strings.Culture;
        Strings.SetCulture(null);
    }

    [TearDown]
    public void RestoreResourceCulture() => Strings.SetCulture(_resourceCulture);

    [TestCase("Classic", "Classic")]
    [TestCase("Ocean", "Ocean")]
    [TestCase("Colorblind", "Colorblind")]
    [TestCase("High-Contrast & Inclusive", "High-Contrast")]
    [TestCase("Sophisticated Pastel", "Sophisticated")]
    [TestCase("Vibrant Monochromatic Shift", "Vibrant")]
    [TestCase("Warm Earth Tones", "Earth Tones")]
    [TestCase("Cool Tech", "Cool Tech")]
    [TestCase("Studio", "Studio")]
    [TestCase("Signal", "Signal")]
    [TestCase("Botanical", "Botanical")]
    [TestCase("Spectrum", "Spectrum")]
    [TestCase("Neon", NeonDescription)]
    [TestCase("Arcade", "Custom · saturated primary and secondary colors")]
    [TestCase("Tropical", "Custom · bright warm colors balanced with blue and teal")]
    [TestCase("Candy", "Custom · sour apple, blue raspberry, bubblegum, and butterscotch")]
    [TestCase("Wildberry", "Custom · gooseberry, blueberry, cloudberry, raspberry, and blackcurrant")]
    [TestCase("Copper", "Custom · verdigris, periwinkle, brass, rose, aqua, and lavender")]
    [TestCase("Gunmetal", "Custom · instrument green, sky blue, amber, magenta, violet, and red-orange")]
    public void CanonicalPalettesKeepTheirEnglishDescriptionOrDisplayLabel(string name, string expected)
    {
        var choice = new PaletteChoice(name);

        Assert.Multiple(() =>
        {
            Assert.That(ChartPalettes.Description(name), Is.EqualTo(expected));
            Assert.That(choice.Description, Is.EqualTo(expected));
            Assert.That(choice.Name, Is.EqualTo(name));
        });
    }

    [TestCase("Cyberpunk / Neon", NeonDescription,
        TestName = nameof(AliasesAndCaseInsensitiveWhitespaceNormalizeOnlyTheDescription) + "_Legacy")]
    [TestCase("  cYbErPuNk / nEoN  ", NeonDescription,
        TestName = nameof(AliasesAndCaseInsensitiveWhitespaceNormalizeOnlyTheDescription) + "_MixedLegacy")]
    [TestCase(" \tNeOn\r\n", NeonDescription,
        TestName = nameof(AliasesAndCaseInsensitiveWhitespaceNormalizeOnlyTheDescription) + "_NeonWhitespace")]
    [TestCase("\tHIGH-CONTRAST & INCLUSIVE\n", "High-Contrast",
        TestName = nameof(AliasesAndCaseInsensitiveWhitespaceNormalizeOnlyTheDescription) + "_LabelWhitespace")]
    public void AliasesAndCaseInsensitiveWhitespaceNormalizeOnlyTheDescription(string input, string expected)
    {
        var choice = new PaletteChoice(input);

        Assert.Multiple(() =>
        {
            Assert.That(ChartPalettes.Description(input), Is.EqualTo(expected));
            Assert.That(choice.Description, Is.EqualTo(expected));
            Assert.That(choice.Name, Is.EqualTo(input));
        });
    }

    [TestCase(null, TestName = nameof(UnrecognizedOrMissingNamesFallBackToClassicWithoutRewritingTheChoice) + "_Null")]
    [TestCase("", TestName = nameof(UnrecognizedOrMissingNamesFallBackToClassicWithoutRewritingTheChoice) + "_Empty")]
    [TestCase(" \t\r\n", TestName = nameof(UnrecognizedOrMissingNamesFallBackToClassicWithoutRewritingTheChoice) + "_Whitespace")]
    [TestCase("unknown", TestName = nameof(UnrecognizedOrMissingNamesFallBackToClassicWithoutRewritingTheChoice) + "_Unknown")]
    [TestCase("Cyberpunk/Neon", TestName = nameof(UnrecognizedOrMissingNamesFallBackToClassicWithoutRewritingTheChoice) + "_NearAlias")]
    public void UnrecognizedOrMissingNamesFallBackToClassicWithoutRewritingTheChoice(string? input)
    {
        var choice = new PaletteChoice(input!);

        Assert.Multiple(() =>
        {
            Assert.That(ChartPalettes.Description(input!), Is.EqualTo("Classic"));
            Assert.That(choice.Description, Is.EqualTo("Classic"));
            Assert.That(choice.Name, Is.EqualTo(input));
        });
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
    [TestCase("it-IT")]
    public void ExistingChoicesReadDescriptionsFromTheCurrentUiCulture(string culture)
    {
        var choice = new PaletteChoice("Neon");
        var fallback = new PaletteChoice("Classic");
        var uiCulture = CultureInfo.CurrentUICulture;
        var formats = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Multiple(() =>
            {
                Assert.That(choice.Description, Is.EqualTo(Resource("PaletteNeonVividDescription", culture)));
                Assert.That(fallback.Description, Is.EqualTo(Resource("PaletteClassic", culture)));
                Assert.That(ChartPalettes.Description(" SIGNAL "), Is.EqualTo(Resource("PaletteSignal", culture)));
                Assert.That(choice.Name, Is.EqualTo("Neon"));
            });

            CultureInfo.CurrentUICulture = uiCulture;
            Assert.That(choice.Description, Is.EqualTo(NeonDescription));
            Assert.That(fallback.Description, Is.EqualTo("Classic"));
        }
        finally
        {
            CultureInfo.CurrentCulture = formats;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    [TestCase("en-US")]
    [TestCase("fr-FR")]
    [TestCase("ja-JP")]
    public void SelectedResourceCultureOverridesThreadCultureAndClearingItUsesTheThreadAgain(string culture)
    {
        var choice = new PaletteChoice("Neon");
        var fallback = new PaletteChoice("Classic");
        Strings.SetCulture(CultureInfo.GetCultureInfo(culture));
        Assert.Multiple(() =>
        {
            Assert.That(choice.Description, Is.EqualTo(Resource("PaletteNeonVividDescription", culture)));
            Assert.That(fallback.Description, Is.EqualTo(Resource("PaletteClassic", culture)));
        });

        Strings.SetCulture(null);
        Assert.That(choice.Description, Is.EqualTo(NeonDescription));
        Assert.That(fallback.Description, Is.EqualTo("Classic"));
    }

    [Test]
    public void ReadingDescriptionsDoesNotReplaceColorsIdentifiersOrPublishPropertyChanges()
    {
        const string identifier = "  cYbErPuNk / nEoN  ";
        var choice = new PaletteChoice(identifier);
        var colors = choice.Colors;
        var values = colors.ToArray();
        var notifications = 0;
        choice.PropertyChanged += (_, _) => notifications++;

        Assert.That(choice.Description, Is.EqualTo(NeonDescription));
        Assert.That(choice.Description, Is.EqualTo(NeonDescription));
        Assert.Multiple(() =>
        {
            Assert.That(choice.Name, Is.EqualTo(identifier));
            Assert.That(choice.Colors, Is.SameAs(colors));
            Assert.That(choice.Colors, Is.EqualTo(values));
            Assert.That(notifications, Is.Zero);
        });
    }

    private static string Resource(string key, string culture)
    {
        var value = Strings.ResourceManager.GetString(key, CultureInfo.GetCultureInfo(culture));
        Assert.That(value, Is.Not.Null.And.Not.Empty, key);
        return value!;
    }
}
