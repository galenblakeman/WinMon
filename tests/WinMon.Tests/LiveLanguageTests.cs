using System.ComponentModel;
using System.Globalization;
using NUnit.Framework;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
public sealed class LiveLanguageTests
{
    private CultureInfo? _resourceCulture;
    private CultureInfo? _defaultUiCulture;
    private CultureInfo _uiCulture = null!;
    private CultureInfo _formats = null!;
    private CultureInfo _systemCulture = null!;

    [SetUp]
    public void SaveCultures()
    {
        _resourceCulture = Strings.Culture;
        _defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        _uiCulture = CultureInfo.CurrentUICulture;
        _formats = CultureInfo.CurrentCulture;
        _systemCulture = AppLanguages.SystemDisplayCulture;
    }

    [TearDown]
    public void RestoreCultures()
    {
        AppLanguages.InitializeSystemDisplayCulture(_systemCulture);
        CultureInfo.CurrentUICulture = _uiCulture;
        CultureInfo.CurrentCulture = _formats;
        CultureInfo.DefaultThreadCurrentUICulture = _defaultUiCulture;
        Strings.SetCulture(_resourceCulture);
    }

    [Test]
    public void ExistingBindingSource_RefreshesAndOnlyNotifiesWhenCultureChanges()
    {
        var source = Strings.Text;
        var observed = new List<string>();
        source.PropertyChanged += Changed;
        try
        {
            AppLanguages.Apply("fr");
            AppLanguages.Apply("fr");
            AppLanguages.Apply("ja");
            Assert.That(observed, Is.EqualTo(["Paramètres", "設定"]));
            Assert.That(Strings.Text, Is.SameAs(source));
        }
        finally { source.PropertyChanged -= Changed; }

        return;

        void Changed(object? sender, PropertyChangedEventArgs args)
        {
            Assert.That(args.PropertyName, Is.EqualTo("Item[]"));
            observed.Add(source[nameof(Strings.Settings)]);
        }
    }

    [Test]
    public void SwitchingBackToSystem_RestoresCapturedWindowsLanguage()
    {
        AppLanguages.InitializeSystemDisplayCulture(CultureInfo.GetCultureInfo("de-DE"));
        AppLanguages.Apply("ja");
        Assert.That(Strings.Settings, Is.EqualTo("設定"));
        AppLanguages.Apply(AppLanguages.SystemDefault);
        Assert.That(Strings.WinMonSettings, Is.EqualTo("WinMon-Einstellungen"));
        Assert.That(Strings.Culture!.Name, Is.EqualTo("de-DE"));
    }

    [Test]
    public async Task ExistingAsyncOperation_UsesNewLanguageDespiteCapturedOldUiCulture()
    {
        AppLanguages.Apply("en");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = Task.Run(async () =>
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            ready.SetResult();
            await resume.Task;
            return (CultureInfo.CurrentUICulture.Name, Strings.Settings);
        });
        await ready.Task;
        AppLanguages.Apply("fr");
        resume.SetResult();
        var result = await worker;
        Assert.That(result.Name, Is.EqualTo("en-US"));
        Assert.That(result.Settings, Is.EqualTo("Paramètres"));
    }

    [Test]
    public void UnsupportedSystemLanguage_FallsBackToEnglishWithoutChangingRegionalFormats()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        AppLanguages.InitializeSystemDisplayCulture(CultureInfo.GetCultureInfo("it-IT"));
        AppLanguages.Apply("ko");
        AppLanguages.Apply(AppLanguages.SystemDefault);
        Assert.That(Strings.Settings, Is.EqualTo("Settings"));
        Assert.That(Strings.Format(Strings.PercentOneDecimal, 12.5), Is.EqualTo("12,5%"));
    }
}
