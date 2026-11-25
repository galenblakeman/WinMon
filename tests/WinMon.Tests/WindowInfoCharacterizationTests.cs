using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;
using WinMon.Updates;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private static Action? InfoUpdateHandlers(UpdateUi ui) => (Action?)ui.Field("UpdateCheckChanged").GetValue(ui.Window);

    private void OpenOwnedInfoWithoutReadingHardware(UpdateUi ui, Action<HardwareInfoWindow> verify)
    {
        ui.Window.ApplyTheme(false, false);
        // The existing disabled-refresh guard runs before any reader is queued, including during Loaded.
        var style = new Style(typeof(HardwareInfoWindow));
        style.Setters.Add(new Setter(UIElement.IsEnabledProperty, false));
        style.Setters.Add(new Setter(Window.ShowActivatedProperty, false));
        Application.Current.Resources[typeof(HardwareInfoWindow)] = style;
        ui.Window.ShowActivated = false;
        ui.Window.ShowInTaskbar = false;
        ui.Window.Show();
        var existing = Application.Current.Windows.Cast<Window>().ToHashSet();
        var before = InfoUpdateHandlers(ui);
        Exception? failure = null;
        var observed = false;
        DispatcherOperation? watch = null;
        var timeout = new DispatcherTimer(DispatcherPriority.Send, ui.Window.Dispatcher) { Interval = TimeSpan.FromSeconds(10) };
        void Watch()
        {
            var dialog = Application.Current.Windows.OfType<HardwareInfoWindow>().SingleOrDefault(window => !existing.Contains(window));
            if (dialog is null || !dialog.IsLoaded)
            {
                watch = ui.Window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Watch));
                return;
            }
            observed = true;
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(dialog.Owner, Is.SameAs(ui.Window));
                    Assert.That(dialog.IsEnabled, Is.False);
                    Assert.That(dialog.Ready.IsCompleted, Is.False);
                    Assert.That(dialog.Sections.Children, Is.Empty);
                    Assert.That(InfoUpdateHandlers(ui)!.GetInvocationList().Length, Is.EqualTo((before?.GetInvocationList().Length ?? 0) + 1));
                });
                dialog.IsEnabled = true;
                Assert.That(dialog.RefreshButton.IsEnabled, Is.True, "Loaded must not have entered the hardware reader");
                verify(dialog);
            }
            catch (Exception error) { failure = error; }
            finally { dialog.Close(); }
        }
        timeout.Tick += (_, _) =>
        {
            failure ??= new AssertionException("Owned Info dialog exceeded its modal bound");
            foreach (var dialog in Application.Current.Windows.OfType<HardwareInfoWindow>().Where(window => !existing.Contains(window)).ToArray())
                dialog.Close();
        };
        try
        {
            timeout.Start();
            watch = ui.Window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Watch));
            ui.Window.InfoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        finally
        {
            timeout.Stop();
            watch?.Abort();
        }
        if (failure is not null) throw new AssertionException(failure.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(observed, Is.True);
            Assert.That(InfoUpdateHandlers(ui)?.GetInvocationList(), Is.EqualTo(before?.GetInvocationList()));
            Assert.That(Directory.Exists(_directory), Is.False);
        });
    }

    private static void SetInfoState(UpdateUi ui, bool downloading, int pending, UpdateOffer? offer)
    {
        ui.Field("_updateDownloading").SetValue(ui.Window, downloading);
        ui.Field("_updateChecksPending").SetValue(ui.Window, pending);
        ui.Field("_updateOffer").SetValue(ui.Window, offer);
        ui.Field("_updateCheckStatusKey").SetValue(ui.Window, offer is null ? nameof(Strings.SettingsNotChecked) : nameof(Strings.UpdateAvailable));
        ui.Window.UpdateStatusText.Text = "Owned transfer 37%";
    }

    private static void VerifyInfoState(HardwareInfoWindow dialog, bool downloading, int pending, UpdateOffer? offer)
    {
        var busy = downloading || pending > 0;
        var status = downloading ? "Owned transfer 37%" : pending > 0 ? Strings.UpdateChecking :
            offer is null ? Strings.SettingsNotChecked : Strings.Format(Strings.UpdateAvailable, offer.Version);
        Assert.Multiple(() =>
        {
            Assert.That(dialog.UpdateStatusText.Text, Is.EqualTo(UpdatePolicy.GitHubUpdatesEnabled ? status : Strings.InfoStoreUpdates));
            Assert.That(dialog.UpdateButton.Visibility, Is.EqualTo(UpdatePolicy.GitHubUpdatesEnabled && offer is not null ? Visibility.Visible : Visibility.Collapsed));
            Assert.That(dialog.CheckButton.IsEnabled, Is.EqualTo(!busy));
            Assert.That(dialog.UpdateButton.IsEnabled, Is.EqualTo(!busy));
            Assert.That(dialog.Sections.Children, Is.Empty);
            Assert.That(dialog.CopyButton.IsEnabled, Is.False);
            Assert.That(dialog.Ready.IsCompleted, Is.False);
        });
    }

    private static IEnumerable<TestCaseData> InfoInitialStates()
    {
        yield return new TestCaseData(false, 0, false).SetName("InfoUpdateInitialState_IdleWithoutOffer");
        if (!UpdatePolicy.GitHubUpdatesEnabled) yield break;
        yield return new TestCaseData(false, 0, true).SetName("InfoUpdateInitialState_Available");
        yield return new TestCaseData(false, 1, false).SetName("InfoUpdateInitialState_CheckingWithoutOffer");
        yield return new TestCaseData(false, 1, true).SetName("InfoUpdateInitialState_CheckingWithOffer");
        yield return new TestCaseData(true, 0, true).SetName("InfoUpdateInitialState_TransferWithOffer");
        yield return new TestCaseData(true, 1, false).SetName("InfoUpdateInitialState_TransferWinsOverChecking");
    }

#if !WINMON_STORE
    private UpdateOffer InfoSignedOffer()
    {
        using var key = RSA.Create(2048);
        using var client = WindowReleaseClient(key, () => "99999.1.1");
        var task = client.CheckAsync("1.0.0", default);
        FinishWindowCheck(task);
        return task.GetAwaiter().GetResult() ?? throw new AssertionException("The owned signed response must yield an offer");
    }
#endif

    [TestCaseSource(nameof(InfoInitialStates))]
    public void InfoUpdateInitialStatePreservesStatusAvailabilityAndBusyPrecedence(bool downloading, int pending, bool available)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        UpdateOffer? offer = null;
#if !WINMON_STORE
        if (available) offer = InfoSignedOffer();
#endif
        SetInfoState(ui, downloading, pending, offer);
        OpenOwnedInfoWithoutReadingHardware(ui, dialog => VerifyInfoState(dialog, downloading, pending, offer));
    }

#if !WINMON_STORE
    [Test]
    public void InfoUpdateLiveTransitionsAndCheckUseTheExistingCallbackAndReleaseItsSubscription()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        var offer = InfoSignedOffer();
        SetInfoState(ui, false, 0, null);
        OpenOwnedInfoWithoutReadingHardware(ui, dialog =>
        {
            VerifyInfoState(dialog, false, 0, null);
            dialog.CheckButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            VerifyInfoState(dialog, false, 0, null);
            foreach (var state in new[] { (false, 1, true), (true, 1, true), (true, 0, false), (false, 0, true), (false, 0, false) })
            {
                var current = state.Item3 ? offer : null;
                SetInfoState(ui, state.Item1, state.Item2, current);
                InfoUpdateHandlers(ui)!.Invoke();
                VerifyInfoState(dialog, state.Item1, state.Item2, current);
            }
        });
        Assert.That(InfoUpdateHandlers(ui)?.GetInvocationList(), Is.Null);
    }
#endif

    [Test]
    public void InfoEntryClosingGuardDoesNotCreateADialogOrSubscribeForUpdates()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        ui.Window.IsClosing = true;
        var before = Application.Current.Windows.Cast<Window>().ToArray();
        ui.Window.InfoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(Application.Current.Windows.Cast<Window>(), Is.EqualTo(before));
        Assert.That(InfoUpdateHandlers(ui)?.GetInvocationList(), Is.Null);
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InfoRefreshDisabledGuardPreservesExistingControlsAndDoesNotCompleteReady(bool closed)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        var dialog = new HardwareInfoWindow();
        try
        {
            dialog.RefreshButton.IsEnabled = false;
            dialog.CopyButton.IsEnabled = true;
            dialog.CopyButton.Content = "Owned copied caption";
            dialog.SnapshotNote.Text = "Owned snapshot note";
            var marker = new TextBlock { Text = "Owned section" };
            dialog.Sections.Children.Add(marker);
            if (closed) dialog.Close();
            var copyWasEnabled = dialog.CopyButton.IsEnabled;
            var refresh = typeof(HardwareInfoWindow).GetMethod("RefreshAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Func<Task>>(dialog);
            var task = refresh();
            Assert.Multiple(() =>
            {
                Assert.That(task.IsCompletedSuccessfully, Is.True);
                Assert.That(dialog.RefreshButton.IsEnabled, Is.False);
                Assert.That(dialog.CopyButton.IsEnabled, Is.EqualTo(copyWasEnabled));
                Assert.That(dialog.CopyButton.Content, Is.EqualTo("Owned copied caption"));
                Assert.That(dialog.SnapshotNote.Text, Is.EqualTo("Owned snapshot note"));
                Assert.That(dialog.Sections.Children.Cast<UIElement>(), Is.EqualTo(new[] { marker }));
                Assert.That(dialog.Ready.IsCompleted, Is.False);
            });
        }
        finally { if (!closed) dialog.Close(); }
    }
}
