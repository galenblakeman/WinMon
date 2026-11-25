using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

public sealed partial class SettingsSaveCharacterizationTests
{
    private SettingsWindow RemovalWindow(Func<CancellationToken, Task<NetworkMonitoringChangeResult>>? remove = null,
        Func<bool, CancellationToken, Task<NetworkMonitoringChangeResult>>? change = null) =>
        Window(Preferences() with { ProcessNetworkMonitoringEnabled = true }, change, remove, NetworkMonitoringState.Active);

    private static void RequestRemoval(SettingsWindow window) =>
        window.RemoveNetworkMonitoringServiceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Task ConfirmRemoval(SettingsWindow window, int answer = 1) => WithNotice(window, () =>
    {
        RequestRemoval(window);
        return Task.CompletedTask;
    }, Strings.NetworkProcessMonitoringRemoveTitle, Strings.NetworkProcessMonitoringRemoveConfirm, dialog =>
    {
        var buttons = Descendants(dialog).OfType<Button>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(buttons.Select(button => button.Content), Is.EqualTo(new[] { Strings.NetworkProcessMonitoringRemove, Strings.Cancel }));
            Assert.That(buttons[0].IsDefault, Is.False);
            Assert.That(buttons[1].IsDefault, Is.True);
            Assert.That(buttons[1].IsCancel, Is.True);
        });
        if (answer < 0) dialog.Close();
        else buttons[answer == 1 ? 0 : 1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    });

    private static async Task RemovalReady(SettingsWindow window)
    {
        var timer = Stopwatch.StartNew();
        while (!window.SaveButton.IsEnabled)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Synthetic removal did not complete");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
    }

    private static void AssertRemovalControls(SettingsWindow window, bool ready)
    {
        Assert.Multiple(() =>
        {
            Assert.That(window.SaveButton.IsEnabled, Is.EqualTo(ready));
            Assert.That(window.Navigation.IsEnabled, Is.EqualTo(ready));
            Assert.That(window.ProcessNetworkMonitoringCheckBox.IsEnabled, Is.EqualTo(ready));
            Assert.That(window.RemoveNetworkMonitoringServiceButton.IsEnabled, Is.EqualTo(ready));
        });
    }

    [Test]
    public void ServiceRemovalWithoutACallbackDoesNotPromptOrChangePreferences()
    {
        var window = RemovalWindow();
        Assert.That(RunDialog(window, () =>
        {
            var status = window.ProcessNetworkMonitoringStatus.Text;
            Assert.That(window.RemoveNetworkMonitoringServiceButton.IsEnabled, Is.False);
            RequestRemoval(window);
            Assert.Multiple(() =>
            {
                Assert.That(window.ProcessNetworkMonitoringCheckBox.IsChecked, Is.True);
                Assert.That(window.ProcessNetworkMonitoringStatus.Text, Is.EqualTo(status));
                Assert.That(window.Result, Is.Null);
                Assert.That(window.SaveButton.IsEnabled, Is.True);
            });
            return Task.CompletedTask;
        }), Is.False);
    }

#if WINMON_STORE
    [Test]
    public void ServiceRemovalInStoreDoesNotPromptOrInvokeAnAvailableCallback()
    {
        var calls = 0;
        var window = RemovalWindow(_ => { calls++; return Task.FromResult(new NetworkMonitoringChangeResult(true, NetworkMonitoringState.Disabled)); });
        Assert.That(RunDialog(window, () =>
        {
            RequestRemoval(window);
            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.Zero);
                Assert.That(window.RemoveNetworkMonitoringServiceButton.IsEnabled, Is.False);
                Assert.That(window.ProcessNetworkMonitoringCheckBox.IsEnabled, Is.False);
                Assert.That(window.ProcessNetworkMonitoringCheckBox.IsChecked, Is.True);
                Assert.That(window.ProcessNetworkMonitoringStatus.Text, Is.EqualTo(Strings.NetworkProcessMonitoringStoreUnavailable));
                Assert.That(window.SaveButton.IsEnabled, Is.True);
            });
            return Task.CompletedTask;
        }), Is.False);
    }
#else
    [TestCase(0)]
    [TestCase(-1)]
    public void ServiceRemovalDismissalKeepsTheEnabledPreferenceAndNeverCallsTheProvider(int answer)
    {
        var calls = 0;
        var window = RemovalWindow(_ => { calls++; return Task.FromResult(new NetworkMonitoringChangeResult(true, NetworkMonitoringState.Disabled)); });
        Assert.That(RunDialog(window, async () =>
        {
            await ConfirmRemoval(window, answer);
            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.Zero);
                Assert.That(window.ProcessNetworkMonitoringCheckBox.IsChecked, Is.True);
                Assert.That(window.ProcessNetworkMonitoringStatus.Text, Is.EqualTo(Strings.NetworkProcessMonitoringActive));
                AssertRemovalControls(window, true);
            });
        }), Is.False);
    }

    [TestCase(NetworkMonitoringState.Failed, "NetworkProcessMonitoringFailed")]
    [TestCase(NetworkMonitoringState.Cancelled, "NetworkProcessMonitoringCancelled")]
    [TestCase(NetworkMonitoringState.SetupUnavailable, "NetworkProcessMonitoringSetupUnavailable")]
    [TestCase(NetworkMonitoringState.Unavailable, "NetworkProcessMonitoringUnavailable")]
    [TestCase(NetworkMonitoringState.UpdateRequired, "NetworkProcessMonitoringUpdateRequired")]
    [TestCase(NetworkMonitoringState.StoreUnavailable, "NetworkProcessMonitoringStoreUnavailable")]
    public void ServiceRemovalFailureStatesKeepThePreferenceAndUseTheirLocalizedStatus(NetworkMonitoringState state, string key)
    {
        var calls = 0;
        var window = RemovalWindow(_ => { calls++; return Task.FromResult(new NetworkMonitoringChangeResult(false, state)); });
        Assert.That(RunDialog(window, async () =>
        {
            await ConfirmRemoval(window);
            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(window.ProcessNetworkMonitoringCheckBox.IsChecked, Is.True);
                Assert.That(window.ProcessNetworkMonitoringStatus.Text, Is.EqualTo(Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture)));
                Assert.That(window.Result, Is.Null);
                AssertRemovalControls(window, true);
            });
        }), Is.False);
    }

    [TestCase("synchronous")]
    [TestCase("faulted")]
    [TestCase("foreign-cancellation")]
    public void ServiceRemovalProviderExceptionsRemainGenericAndNeverExposeTheirDetails(string kind)
    {
        var calls = 0;
        var window = RemovalWindow(_ =>
        {
            calls++;
            return kind switch
            {
                "synchronous" => throw new IOException("private service path"),
                "foreign-cancellation" => throw new OperationCanceledException("private service path"),
                _ => Task.FromException<NetworkMonitoringChangeResult>(new InvalidOperationException("private service path"))
            };
        });
        Assert.That(RunDialog(window, async () =>
        {
            await ConfirmRemoval(window);
            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(window.ProcessNetworkMonitoringStatus.Text, Is.EqualTo(Strings.NetworkProcessMonitoringFailed));
                Assert.That(window.ProcessNetworkMonitoringStatus.Text, Does.Not.Contain("private service path"));
                Assert.That(window.ProcessNetworkMonitoringCheckBox.IsChecked, Is.True);
                AssertRemovalControls(window, true);
            });
        }), Is.False);
    }

    [Test]
    public void ServiceRemovalPendingWorkBlocksReentrySaveAndStatusRefreshUntilCompletion()
    {
        var pending = new TaskCompletionSource<NetworkMonitoringChangeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var token = CancellationToken.None;
        var window = RemovalWindow(received => { calls++; token = received; return pending.Task; });
        Assert.That(RunDialog(window, async () =>
        {
            try
            {
                await ConfirmRemoval(window);
                AssertRemovalControls(window, false);
                Assert.That(token.CanBeCanceled, Is.True);
                Assert.That(token.IsCancellationRequested, Is.False);
                RequestRemoval(window);
                window.RefreshNetworkMonitoringState(NetworkMonitoringState.Active);
                await window.SaveAsync();
                Assert.Multiple(() =>
                {
                    Assert.That(calls, Is.EqualTo(1));
                    Assert.That(window.Result, Is.Null);
                    Assert.That(window.ProcessNetworkMonitoringStatus.Text, Is.EqualTo(Strings.NetworkProcessMonitoringEnabling));
                    Assert.That(window.ProcessNetworkMonitoringCheckBox.IsChecked, Is.True);
                });
                pending.SetResult(new(true, NetworkMonitoringState.Disabled));
                await RemovalReady(window);
                AssertRemovalControls(window, true);
                Assert.That(window.ProcessNetworkMonitoringCheckBox.IsChecked, Is.False);
                Assert.That(window.ProcessNetworkMonitoringStatus.Text, Is.EqualTo(Strings.NetworkProcessMonitoringDisabled));
            }
            finally
            {
                pending.TrySetResult(new(false, NetworkMonitoringState.Failed));
                await RemovalReady(window);
            }
        }), Is.False);
    }

    [Test]
    public void ServiceRemovalSuccessResetsTheSaveBaselineWithoutInvokingSetupAgain()
    {
        var removeCalls = 0;
        var setupCalls = 0;
        var initial = Preferences() with { ProcessNetworkMonitoringEnabled = true };
        var window = Window(initial, (_, _) =>
        {
            setupCalls++;
            return Task.FromResult(new NetworkMonitoringChangeResult(false, NetworkMonitoringState.Failed));
        }, _ => { removeCalls++; return Task.FromResult(new NetworkMonitoringChangeResult(true, NetworkMonitoringState.Disabled)); },
            NetworkMonitoringState.Active);
        Assert.That(RunDialog(window, async () =>
        {
            await ConfirmRemoval(window);
            Assert.That(window.ProcessNetworkMonitoringCheckBox.IsChecked, Is.False);
            await window.SaveAsync();
        }), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(removeCalls, Is.EqualTo(1));
            Assert.That(setupCalls, Is.Zero);
            Assert.That(window.Result!.ProcessNetworkMonitoringEnabled, Is.False);
            Assert.That(initial.ProcessNetworkMonitoringEnabled, Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ServiceRemovalCancellationWinsOverLateCompletionAndRetryUsesANewToken(bool linkedCancellation)
    {
        var pending = new TaskCompletionSource<NetworkMonitoringChangeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new List<CancellationToken>();
        var window = RemovalWindow(token =>
        {
            tokens.Add(token);
            return tokens.Count == 1 ? pending.Task : Task.FromResult(new NetworkMonitoringChangeResult(true, NetworkMonitoringState.Disabled));
        });
        Assert.That(RunDialog(window, async () =>
        {
            try
            {
                await ConfirmRemoval(window);
                window.Close();
                Assert.That(window.IsVisible, Is.True);
                Assert.That(tokens[0].IsCancellationRequested, Is.True);
                AssertRemovalControls(window, false);
                if (linkedCancellation) pending.SetCanceled(tokens[0]);
                else pending.SetResult(new(true, NetworkMonitoringState.Disabled));
                await RemovalReady(window);
                Assert.Multiple(() =>
                {
                    Assert.That(window.ProcessNetworkMonitoringCheckBox.IsChecked, Is.True);
                    Assert.That(window.ProcessNetworkMonitoringStatus.Text, Is.EqualTo(Strings.NetworkProcessMonitoringCancelled));
                    AssertRemovalControls(window, true);
                });
                await ConfirmRemoval(window);
                Assert.Multiple(() =>
                {
                    Assert.That(tokens, Has.Count.EqualTo(2));
                    Assert.That(tokens[1], Is.Not.EqualTo(tokens[0]));
                    Assert.That(tokens[1].CanBeCanceled, Is.True);
                    Assert.That(tokens[1].IsCancellationRequested, Is.False);
                    Assert.That(window.ProcessNetworkMonitoringCheckBox.IsChecked, Is.False);
                    Assert.That(window.ProcessNetworkMonitoringStatus.Text, Is.EqualTo(Strings.NetworkProcessMonitoringDisabled));
                });
            }
            finally
            {
                pending.TrySetResult(new(false, NetworkMonitoringState.Failed));
                await RemovalReady(window);
            }
        }), Is.False);
    }
#endif
}
