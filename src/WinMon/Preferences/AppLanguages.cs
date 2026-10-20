using System.Globalization;

namespace WinMon.Preferences;

/// <summary>Stable persisted language identifiers, independent of their displayed names.</summary>
internal static class AppLanguages
{
    internal const string SystemDefault = "System";
    internal sealed record Choice(string Name, string Label)
    {
        public override string ToString() => Label;
    }
    private static readonly string[] Names = [SystemDefault, "en", "zh-Hans", "ru", "es", "pt-BR", "de", "ja", "fr", "pl", "ko"];
    internal static CultureInfo SystemDisplayCulture { get; private set; } = CultureInfo.CurrentUICulture;

    internal static void InitializeSystemDisplayCulture(CultureInfo culture) => SystemDisplayCulture = culture;

    internal static void Apply(string? language)
    {
        var culture = ResolveCulture(language, SystemDisplayCulture);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Strings.SetCulture(culture);
    }

    internal static IReadOnlyList<Choice> Choices =>
    [
        new(SystemDefault, Strings.LanguageSystemDefault),
        new("en", Strings.LanguageEnglish),
        new("zh-Hans", Strings.LanguageChinese),
        new("ru", Strings.LanguageRussian),
        new("es", Strings.LanguageSpanish),
        new("pt-BR", Strings.LanguagePortuguese),
        new("de", Strings.LanguageGerman),
        new("ja", Strings.LanguageJapanese),
        new("fr", Strings.LanguageFrench),
        new("pl", Strings.LanguagePolish),
        new("ko", Strings.LanguageKorean)
    ];

    internal static string Normalize(string? language) => Names.FirstOrDefault(name =>
        string.Equals(name, language?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? SystemDefault;

    internal static CultureInfo ResolveCulture(string? language, CultureInfo systemDisplayCulture)
    {
        var name = Normalize(language);
        return name == SystemDefault ? systemDisplayCulture : CultureInfo.GetCultureInfo(name);
    }
}
