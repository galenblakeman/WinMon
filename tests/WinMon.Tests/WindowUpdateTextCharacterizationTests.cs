using System.Reflection;
using System.Globalization;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Resources;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private static Action RefreshWindowUpdateText(MainWindow window) =>
        (typeof(MainWindow).GetMethod("RefreshUpdateText", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing update-text callback")).CreateDelegate<Action>(window);

    [TestCase("dismissed")]
    [TestCase("downloading")]
    [TestCase("collapsed")]
    [Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateTextRefreshGuardsPreserveTheCurrentNoticeAndActions(string guard)
    {
        using var ui = new UpdateUi(_directory);
        if (guard == "dismissed")
        {
            ui.Window.ShowUpdateStatus("initial offer notice", true);
            ui.Window.DismissUpdateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        ui.Window.ShowUpdateStatus("guarded notice", true);
        if (guard == "downloading") ui.Field("_updateDownloading").SetValue(ui.Window, true);
        if (guard == "collapsed") ui.Window.UpdateFooter.Visibility = Visibility.Collapsed;
        var before = UpdateUiState(ui.Window);
        RefreshWindowUpdateText(ui.Window)();
        Assert.That(UpdateUiState(ui.Window), Is.EqualTo(before));
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateTextRefreshWithoutAnOfferKeepsTheVisibleCaptureNotice()
    {
        using var ui = new UpdateUi(_directory);
        ui.Window.ShowUpdateStatus("capture saved: owned.csv", false);
        var before = UpdateUiState(ui.Window);
        RefreshWindowUpdateText(ui.Window)();
        Assert.That(UpdateUiState(ui.Window), Is.EqualTo(before));
        Assert.That(Directory.Exists(_directory), Is.False);
    }

#if !WINMON_STORE
    [TestCase(false)]
    [TestCase(true)]
    [Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateTextRefreshReformatsTheRetainedOfferWithoutChangingActionVisibility(bool captureNotice)
    {
        using var ui = new UpdateUi(_directory);
        using var key = RSA.Create(2048);
        const string version = "99999.1.1";
        using var client = WindowReleaseClient(key, () => version);
        ui.Attach(client);
        FinishWindowCheck(ui.Check(false, default));
        AssertWindowOffer(ui.Window, version);
        if (captureNotice) ui.Window.ShowUpdateStatus("capture saved: owned.csv", true);
        else ui.Window.UpdateStatusText.Text = "stale localized offer text";
        var before = UpdateUiState(ui.Window);
        var refresh = RefreshWindowUpdateText(ui.Window);
        refresh();
        var expected = (Strings.Format(Strings.UpdateAvailable, version), before.Item2, before.Item3,
            before.Item4, before.Item5, before.Item6);
        Assert.That(UpdateUiState(ui.Window), Is.EqualTo(expected));
        refresh();
        Assert.That(UpdateUiState(ui.Window), Is.EqualTo(expected));
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateTextApplyLanguageReplacesACaptureNoticeWhileLeavingUpdateActionsHidden()
    {
        using var ui = new UpdateUi(_directory);
        using var key = RSA.Create(2048);
        const string version = "99999.1.1";
        using var client = WindowReleaseClient(key, () => version);
        ui.Attach(client);
        FinishWindowCheck(ui.Check(false, default));
        ui.Window.ShowUpdateStatus("capture saved: owned.csv", true);
        var previousCulture = CultureInfo.CurrentUICulture;
        var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        var previousResources = Strings.Culture;
        try
        {
            ui.Window.ApplyLanguage();
            Assert.That(UpdateUiState(ui.Window), Is.EqualTo((Strings.Format(Strings.UpdateAvailable, version),
                Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed)));
            Assert.That(Directory.Exists(_directory), Is.False);
        }
        finally
        {
            Strings.SetCulture(previousResources);
            CultureInfo.DefaultThreadCurrentUICulture = previousDefault;
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
#endif
}
