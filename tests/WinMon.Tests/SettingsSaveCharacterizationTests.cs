using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;
using WinMon.Resources;
using WinMon.Shortcuts;
using WinMon.Updates;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration")]
[SetCulture("en-US"), SetUICulture("en-US")]
public sealed partial class SettingsSaveCharacterizationTests
{
    private readonly List<SettingsWindow> _windows = [];
    private readonly List<object> _addedResources = [];
    private string _folder = null!;
    private string DefaultFolder => Path.Combine(_folder, "default-reports");

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var (key, brush) in new[] { ("Surface", Brushes.Black), ("Text", Brushes.White) })
            if (!app.Resources.Contains(key)) { app.Resources[key] = brush; _addedResources.Add(key); }
        _folder = Path.Combine(Path.GetTempPath(), "WinMon.SettingsSave." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var window in _windows) window.Close();
        _windows.Clear();
        foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
        _addedResources.Clear();
        // Only fixture-created, one-level folders are expected; retain unexpected nested data on failure.
        foreach (var directory in Directory.GetDirectories(_folder)) Directory.Delete(directory, recursive: false);
        foreach (var file in Directory.GetFiles(_folder)) File.Delete(file);
        Directory.Delete(_folder, recursive: false);
    }

    private static MonitorPreferences Preferences() => PreferenceStore.Validate(new MonitorPreferences
    {
        PrimaryDrive = "C:", SecondaryDrive = "D:", NetworkInterfaceId = "old",
        TopProcessCount = 20, GroupProcesses = true, CompactMode = true, SelectedActivityTab = 3,
        PinnedServices = ["owned-service"], ResourceChartType = ResourceChartType.Focus,
        FocusMetric = 2, VisibleMetrics = 5, ChartRangeSeconds = 1800, ChartResourcesCollapsed = false,
        Theme = "Dark", ChartPalette = "Classic", Language = "en", StartupView = StartupViewMode.LastUsed
    });

    private SettingsWindow Window(MonitorPreferences? preferences = null,
        Func<bool, CancellationToken, Task<NetworkMonitoringChangeResult>>? change = null,
        Func<CancellationToken, Task<NetworkMonitoringChangeResult>>? remove = null,
        NetworkMonitoringState networkState = NetworkMonitoringState.Disabled)
    {
        var window = new SettingsWindow(preferences ?? Preferences(),
            () => [new("C:", "Owned C"), new("D:", "Owned D")],
            startupAvailability: StartupAvailability.Available,
            loadNetworkInterfaces: () => [new("old", "Owned old adapter"), new("new", "Owned new adapter")],
            captureFolder: DefaultFolder, checkForUpdates: () => Task.FromResult("owned check"),
            changeNetworkMonitoring: change, removeNetworkService: remove, networkMonitoringState: networkState);
        _windows.Add(window);
        return window;
    }

    private static bool? RunDialog(SettingsWindow window, Func<Task> exercise)
    {
        Exception? failure = null;
        var started = false;
        var existing = Application.Current.Windows.Cast<Window>().ToHashSet();
        DispatcherOperation? noticeWatch = null;
        void WatchUnexpectedNotices()
        {
            foreach (var notice in Application.Current.Windows.Cast<Window>().Where(candidate => !existing.Contains(candidate)).ToArray())
            {
                failure ??= new AssertionException($"Unexpected dialog: {notice.Title}");
                notice.Close();
            }
            noticeWatch = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(WatchUnexpectedNotices));
        }
        window.ContentRendered += async (_, _) =>
        {
            if (started) return;
            started = true;
            try
            {
                await window.LoadDrivesAsync();
                await window.LoadNetworkInterfacesAsync();
                await exercise();
            }
            catch (Exception ex) { failure ??= ex; }
            finally { if (window.IsVisible) window.Close(); }
        };
        // Expected notices are handled at Loaded priority; fail and dismiss any unexpected modal instead of hanging.
        noticeWatch = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(WatchUnexpectedNotices));
        bool? result;
        try { result = window.ShowDialog(); }
        finally { noticeWatch?.Abort(); }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.That(started, Is.True);
        return result;
    }

    [Test]
    public void UnexpectedNoticesAreDismissedAndReportedByTheModalFixture()
    {
        var window = Window();
        var error = Assert.Throws<AssertionException>(() => RunDialog(window, () =>
        {
            var notice = new MessageDialog("owned unexpected notice", "owned message") { Owner = window };
            var fallback = window.Dispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(notice.Close));
            try { notice.ShowDialog(); }
            finally { fallback.Abort(); }
            return Task.CompletedTask;
        }));
        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("Unexpected dialog: owned unexpected notice"));
            Assert.That(window.IsVisible, Is.False);
            Assert.That(window.OwnedWindows.Count, Is.Zero);
        });
    }

    [TestCase(60, 600), TestCase(120, 1800), TestCase(300, 3600), TestCase(600, 600)]
    [TestCase(null, null)]
    public void AcceptedSaveMapsCurrentSelectionsAndKeepsMainWindowPreferences(int? peakSeconds, int? retention)
    {
        var initial = Preferences();
        var window = Window(initial);
        var accepted = RunDialog(window, async () =>
        {
            window.PrimaryDriveSelector.SelectedItem = window.PrimaryDriveSelector.Items.Cast<SettingsWindow.DriveChoice>().Single(choice => choice.Name == "D:");
            window.SecondaryDriveSelector.SelectedItem = window.SecondaryDriveSelector.Items.Cast<SettingsWindow.DriveChoice>().Single(choice => choice.Name == "C:");
            window.NetworkInterfaceSelector.SelectedItem = window.NetworkInterfaceSelector.Items.Cast<SettingsWindow.NetworkChoice>().Single(choice => choice.Id == "new");
            window.StartupViewSelector.SelectedValue = StartupViewMode.Full;
            window.ThemeSelector.SelectedValue = "Light";
            window.PaletteSelector.SelectedValue = "Ocean";
            window.LanguageSelector.SelectedValue = "de";
            window.GaugeSelector.SelectedValue = GaugeStyle.Needle;
            window.GaugePeakWindowSelector.SelectedItem = peakSeconds is { } seconds
                ? Strings.Format(Strings.GaugePeakWindowMinutes, seconds / 60) : null;
            var retentionSelector = Selector(window, SettingsSection.Monitoring, Strings.HistoricalRetention);
            retentionSelector.SelectedItem = retention switch
            {
                1800 => Strings.Value30Minutes, 3600 => Strings.Value60Minutes, 600 => Strings.Value10Minutes, _ => null
            };
            await window.SaveAsync();
        });
        var result = window.Result!;
        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.True);
            Assert.That(result.PrimaryDrive, Is.EqualTo("D:"));
            Assert.That(result.SecondaryDrive, Is.EqualTo("C:"));
            Assert.That(result.NetworkInterfaceId, Is.EqualTo("new"));
            Assert.That(result.StartupView, Is.EqualTo(StartupViewMode.Full));
            Assert.That(result.Theme, Is.EqualTo("Light"));
            Assert.That(result.ChartPalette, Is.EqualTo("Ocean"));
            Assert.That(result.Language, Is.EqualTo("de"));
            Assert.That(result.GaugeStyle, Is.EqualTo(GaugeStyle.Needle));
            Assert.That(result.GaugePeakWindowSeconds, Is.EqualTo(peakSeconds ?? 120));
            Assert.That(result.RetentionSeconds, Is.EqualTo(retention ?? 600));
            Assert.That(result.TopProcessCount, Is.EqualTo(20));
            Assert.That(result.GroupProcesses, Is.True);
            Assert.That(result.CompactMode, Is.True);
            Assert.That(result.SelectedActivityTab, Is.EqualTo(3));
            Assert.That(result.PinnedServices, Is.SameAs(initial.PinnedServices));
            Assert.That(result.ChartConfigurations, Is.SameAs(initial.ChartConfigurations));
            Assert.That(result.ResourceChartType, Is.EqualTo(initial.ResourceChartType));
            Assert.That(result.FocusMetric, Is.EqualTo(initial.FocusMetric));
            Assert.That(result.VisibleMetrics, Is.EqualTo(initial.VisibleMetrics));
            Assert.That(result.ChartRangeSeconds, Is.EqualTo(initial.ChartRangeSeconds));
            Assert.That(result.ChartResourcesCollapsed, Is.EqualTo(initial.ChartResourcesCollapsed));
            Assert.That(initial.PrimaryDrive, Is.EqualTo("C:"));
            Assert.That(Directory.Exists(DefaultFolder), Is.False);
        });
    }

    [Test]
    public void EmptyOptionalSelectionsUseTheirExistingFallbacks()
    {
        var window = Window(Preferences() with { ShowShortcut = HotkeyGesture.NoneText });
        Assert.That(RunDialog(window, async () =>
        {
            window.SecondaryDriveSelector.SelectedIndex = -1;
            window.NetworkInterfaceSelector.SelectedIndex = -1;
            window.StartupViewSelector.SelectedIndex = -1;
            window.ThemeSelector.SelectedIndex = -1;
            window.PaletteSelector.SelectedIndex = -1;
            window.LanguageSelector.SelectedIndex = -1;
            window.GaugeSelector.SelectedIndex = -1;
            window.GaugePeakWindowSelector.SelectedIndex = -1;
            Selector(window, SettingsSection.Monitoring, Strings.HistoricalRetention).SelectedIndex = -1;
            await window.SaveAsync();
        }), Is.True);
        var result = window.Result!;
        Assert.Multiple(() =>
        {
            Assert.That(result.SecondaryDrive, Is.Null);
            Assert.That(result.NetworkInterfaceId, Is.Null);
            Assert.That(result.StartupView, Is.EqualTo(StartupViewMode.LastUsed));
            Assert.That(result.Theme, Is.EqualTo("Dark"));
            Assert.That(result.ChartPalette, Is.EqualTo("Classic"));
            Assert.That(result.Language, Is.EqualTo(AppLanguages.SystemDefault));
            Assert.That(result.GaugeStyle, Is.EqualTo(GaugeStyle.Bowl));
            Assert.That(result.GaugePeakWindowSeconds, Is.EqualTo(120));
            Assert.That(result.RetentionSeconds, Is.EqualTo(600));
            Assert.That(result.ShowShortcut, Is.EqualTo(HotkeyGesture.NoneText));
        });
    }

    [TestCase(false), TestCase(true)]
    public void SavedFlagsAndShortcutReflectTheDialogWithoutMutatingItsInput(bool value)
    {
        var initial = Preferences() with
        {
            KeepHistoryAcrossRestarts = !value, KeepResourceTrends = !value, MinimizeToTray = !value,
            StartWithWindows = !value, AutomaticallyCheckForUpdates = !value,
            ShowShortcut = value ? HotkeyGesture.Default.ToString() : HotkeyGesture.NoneText
        };
        var window = Window(initial);
        Assert.That(RunDialog(window, async () =>
        {
            Check(window, SettingsSection.General, Strings.MinimizeToTheNotificationArea).IsChecked = value;
            Check(window, SettingsSection.General, Strings.StartWinMonWhenISignIn).IsChecked = value;
            Check(window, SettingsSection.Monitoring, Strings.KeepHistoryAcrossRestarts).IsChecked = value;
            window.KeepResourceTrendsCheckBox.IsChecked = value;
            if (UpdatePolicy.GitHubUpdatesEnabled)
                Check(window, SettingsSection.Updates, Strings.AutomaticallyCheckForUpdates).IsChecked = value;
            window.AutoCapture.EnabledSwitch.IsChecked = value;
            window.AutoCapture.Children.OfType<CheckBox>().Single().IsChecked = value;
            await window.SaveAsync();
        }), Is.True);
        var result = window.Result!;
        Assert.Multiple(() =>
        {
            Assert.That(result.KeepHistoryAcrossRestarts, Is.EqualTo(value));
            Assert.That(result.KeepResourceTrends, Is.EqualTo(value));
            Assert.That(result.MinimizeToTray, Is.EqualTo(value));
            Assert.That(result.StartWithWindows, Is.EqualTo(value));
            Assert.That(result.AutomaticallyCheckForUpdates,
                Is.EqualTo(UpdatePolicy.GitHubUpdatesEnabled ? value : initial.AutomaticallyCheckForUpdates));
            Assert.That(result.AutoCapture.Enabled, Is.EqualTo(value));
            Assert.That(result.AutoCapture.Notify, Is.EqualTo(value));
            Assert.That(result.ShowShortcut, Is.EqualTo(initial.ShowShortcut));
            Assert.That(initial.StartWithWindows, Is.EqualTo(!value));
            Assert.That(initial.KeepHistoryAcrossRestarts, Is.EqualTo(!value));
            Assert.That(result.AutoCapture.Rules.Select(rule => rule.Metric), Is.EqualTo(CaptureRuleSetting.Defaults.Select(rule => rule.Metric)));
            Assert.That(Directory.Exists(DefaultFolder), Is.False);
        });
    }

    [TestCase(false), TestCase(true)]
    public void ChartResetIsAppliedOnlyWhenRequestedAndAccepted(bool reset)
    {
        var initial = Preferences();
        var window = Window(initial);
        Assert.That(RunDialog(window, async () =>
        {
            if (reset) window.ResetChartPreferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.That(window.Result, Is.Null);
            await window.SaveAsync();
        }), Is.True);
        var result = window.Result!;
        Assert.Multiple(() =>
        {
            Assert.That(window.ResetChartPreferencesRequested, Is.EqualTo(reset));
            Assert.That(result.ResourceChartType, Is.EqualTo(reset ? ResourceChartType.Overlay : initial.ResourceChartType));
            Assert.That(result.ChartRangeSeconds, Is.EqualTo(reset ? 120 : initial.ChartRangeSeconds));
            Assert.That(result.FocusMetric, Is.EqualTo(reset ? 0 : initial.FocusMetric));
            Assert.That(result.TopProcessCount, Is.EqualTo(initial.TopProcessCount));
            Assert.That(initial.ResourceChartType, Is.EqualTo(ResourceChartType.Focus));
        });
    }

    [TestCase("primary"), TestCase("duplicate"), TestCase("threshold"), TestCase("seconds")]
    [TestCase("folder-file"), TestCase("folder-empty")]
    public void ValidationRejectsBeforeNetworkSetupAndLeavesTheDialogOpenOnTheRightSection(string kind)
    {
        var blocked = Path.Combine(_folder, "owned-file.txt");
        File.WriteAllText(blocked, "owned content");
        var initial = Preferences() with { AutoCapture = new AutoCapturePreferences
        {
            Enabled = kind.StartsWith("folder", StringComparison.Ordinal),
            Folder = kind == "folder-file" ? blocked : kind == "folder-empty" ? "" : null
        } };
        var calls = 0;
        var window = Window(initial, (_, _) => { calls++; return Task.FromResult(new NetworkMonitoringChangeResult(true, NetworkMonitoringState.Active)); });
        Assert.That(RunDialog(window, async () =>
        {
            window.ProcessNetworkMonitoringCheckBox.IsChecked = true;
            var threshold = CaptureField(window, Strings.AutoCaptureThresholdFor);
            if (kind is "primary" or "duplicate" or "threshold") threshold.Text = "invalid";
            if (kind == "primary") window.PrimaryDriveSelector.SelectedIndex = -1;
            if (kind == "duplicate") window.SecondaryDriveSelector.SelectedItem = window.SecondaryDriveSelector.Items.Cast<SettingsWindow.DriveChoice>().Single(choice => choice.Name == "C:");
            if (kind == "seconds") CaptureField(window, Strings.AutoCaptureSecondsFor).Text = "0";
            var driveError = kind is "primary" or "duplicate";
            var expected = driveError ? Strings.ChooseAPrimaryVolumeAndADifferentSecondVolumeOr :
                kind == "threshold" ? Strings.Format(Strings.AutoCaptureInvalidThreshold, 1, 100) :
                kind == "seconds" ? Strings.AutoCaptureInvalidSeconds : Strings.AutoCaptureFolderNotWritable;
            await WithNotice(window, window.SaveAsync, driveError ? Strings.DiskMonitoring : Strings.AutoCaptureHeading, expected);
            Assert.Multiple(() =>
            {
                Assert.That(window.Section, Is.EqualTo(driveError ? SettingsSection.Monitoring : SettingsSection.AutomaticCaptures));
                Assert.That(window.Result, Is.Null);
                Assert.That(window.IsVisible, Is.True);
                Assert.That(calls, Is.Zero);
                Assert.That(File.ReadAllText(blocked), Is.EqualTo("owned content"));
                Assert.That(Directory.Exists(DefaultFolder), Is.False);
            });
        }), Is.False);
        Assert.That(window.Result, Is.Null);
    }

    [TestCase("default"), TestCase("chosen"), TestCase("disabled-file")]
    public void OnlyAnEnabledChosenCaptureFolderIsProbed(string kind)
    {
        var chosen = Path.Combine(_folder, "chosen-reports");
        var file = Path.Combine(_folder, "owned-file.txt");
        File.WriteAllText(file, "owned content");
        var window = Window(Preferences() with { AutoCapture = new AutoCapturePreferences
        {
            Enabled = kind != "disabled-file", Folder = kind == "default" ? null : kind == "chosen" ? chosen : file
        } });
        Assert.That(RunDialog(window, window.SaveAsync), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(window.Result!.AutoCapture.Folder, Is.EqualTo(kind == "default" ? null : kind == "chosen" ? chosen : file));
            Assert.That(Directory.Exists(DefaultFolder), Is.False);
            Assert.That(Directory.Exists(chosen), Is.EqualTo(kind == "chosen"));
            Assert.That(File.ReadAllText(file), Is.EqualTo("owned content"));
            if (kind == "chosen") Assert.That(Directory.GetFileSystemEntries(chosen), Is.Empty, "The writability probe is removed.");
        });
    }

#if !WINMON_STORE
    [TestCase("active"), TestCase("warming"), TestCase("incomplete"), TestCase("disable")]
    public void HealthyNetworkChangesFinishBeforeSettingsAreAccepted(string outcome)
    {
        var disabling = outcome == "disable";
        var state = outcome switch
        {
            "warming" => NetworkMonitoringState.WarmingUp, "incomplete" => NetworkMonitoringState.Incomplete,
            "disable" => NetworkMonitoringState.Disabled, _ => NetworkMonitoringState.Active
        };
        var initial = Preferences() with { ProcessNetworkMonitoringEnabled = disabling };
        var calls = 0;
        var window = Window(initial, (enabled, token) =>
        {
            calls++;
            Assert.That(enabled, Is.EqualTo(!disabling));
            Assert.That(token.CanBeCanceled, Is.True);
            Assert.That(token.IsCancellationRequested, Is.False);
            return Task.FromResult(new NetworkMonitoringChangeResult(true, state));
        });
        Assert.That(RunDialog(window, async () =>
        {
            window.ProcessNetworkMonitoringCheckBox.IsChecked = !disabling;
            await window.SaveAsync();
        }), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(window.Result!.ProcessNetworkMonitoringEnabled, Is.EqualTo(!disabling));
            Assert.That(initial.ProcessNetworkMonitoringEnabled, Is.EqualTo(disabling));
            Assert.That(window.SaveButton.IsEnabled, Is.True);
            Assert.That(window.Navigation.IsEnabled, Is.True);
        });
    }

    [TestCase("failed"), TestCase("unhealthy"), TestCase("throw"), TestCase("cancel"), TestCase("missing")]
    public void RejectedNetworkChangesDoNotPublishSettingsAndRestoreControls(string outcome)
    {
        var calls = 0;
        Func<bool, CancellationToken, Task<NetworkMonitoringChangeResult>> change = (_, _) =>
        {
            calls++;
            if (outcome == "throw") throw new InvalidOperationException("owned setup failure");
            if (outcome == "cancel") throw new OperationCanceledException("owned unrelated cancellation");
            return Task.FromResult(new NetworkMonitoringChangeResult(outcome == "unhealthy",
                outcome == "unhealthy" ? NetworkMonitoringState.Unavailable : NetworkMonitoringState.Failed));
        };
        var window = Window(change: outcome == "missing" ? null : change);
        Assert.That(RunDialog(window, async () =>
        {
            window.ProcessNetworkMonitoringCheckBox.IsChecked = true;
            await window.SaveAsync();
            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(outcome == "missing" ? 0 : 1));
                Assert.That(window.Result, Is.Null);
                Assert.That(window.IsVisible, Is.True);
                Assert.That(window.Section, Is.EqualTo(SettingsSection.Network));
                Assert.That(window.SaveButton.IsEnabled, Is.True);
                Assert.That(window.Navigation.IsEnabled, Is.True);
                Assert.That(window.ProcessNetworkMonitoringCheckBox.IsEnabled, Is.True);
                Assert.That(window.ProcessNetworkMonitoringCheckBox.IsChecked, Is.EqualTo(outcome == "missing"));
                Assert.That(window.ProcessNetworkMonitoringStatus.Text, Is.EqualTo(outcome switch
                {
                    "missing" => Strings.NetworkProcessMonitoringSetupUnavailable,
                    "unhealthy" => Strings.NetworkProcessMonitoringUnavailable, _ => Strings.NetworkProcessMonitoringFailed
                }));
            });
        }), Is.False);
    }

    [TestCase("success"), TestCase("cancelled")]
    public void BusySaveRejectsConcurrentCallsAndCloseCancelsTheChangeBeforeARetry(string completion)
    {
        var pending = new TaskCompletionSource<NetworkMonitoringChangeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = CancellationToken.None;
        var calls = 0;
        var window = Window(change: (_, cancellation) =>
        {
            calls++;
            token = cancellation;
            return calls == 1 ? pending.Task : Task.FromResult(new NetworkMonitoringChangeResult(true, NetworkMonitoringState.Active));
        });
        Assert.That(RunDialog(window, async () =>
        {
            window.ProcessNetworkMonitoringCheckBox.IsChecked = true;
            var firstSave = window.SaveAsync();
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(firstSave.IsCompleted, Is.False);
                    Assert.That(window.SaveButton.IsEnabled, Is.False);
                    Assert.That(window.Navigation.IsEnabled, Is.False);
                    Assert.That(window.ProcessNetworkMonitoringCheckBox.IsEnabled, Is.False);
                });
                await window.SaveAsync();
                Assert.That(calls, Is.EqualTo(1));
                window.Close();
                Assert.That(token.IsCancellationRequested, Is.True);
                Assert.That(window.IsVisible, Is.True, "Closing waits for the pending change.");
                if (completion == "cancelled") pending.SetCanceled(token);
                else pending.SetResult(new NetworkMonitoringChangeResult(true, NetworkMonitoringState.Active));
                await firstSave;
                Assert.Multiple(() =>
                {
                    Assert.That(window.Result, Is.Null);
                    Assert.That(window.ProcessNetworkMonitoringCheckBox.IsChecked, Is.False);
                    Assert.That(window.ProcessNetworkMonitoringStatus.Text, Is.EqualTo(Strings.NetworkProcessMonitoringCancelled));
                    Assert.That(window.SaveButton.IsEnabled, Is.True);
                    Assert.That(window.Navigation.IsEnabled, Is.True);
                });
            }
            finally
            {
                pending.TrySetResult(new NetworkMonitoringChangeResult(false, NetworkMonitoringState.Failed));
                await firstSave;
            }
            window.ProcessNetworkMonitoringCheckBox.IsChecked = true;
            await window.SaveAsync();
        }), Is.True);
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(window.Result!.ProcessNetworkMonitoringEnabled, Is.True);
    }
#else
    [TestCase(false), TestCase(true)]
    public void StoreRejectsChangedNetworkPreferencesWithoutInvokingSetup(bool initiallyEnabled)
    {
        var calls = 0;
        var window = Window(Preferences() with { ProcessNetworkMonitoringEnabled = initiallyEnabled }, (_, _) =>
        {
            calls++;
            return Task.FromResult(new NetworkMonitoringChangeResult(true, NetworkMonitoringState.Active));
        });
        Assert.That(RunDialog(window, async () =>
        {
            Assert.That(window.ProcessNetworkMonitoringCheckBox.IsEnabled, Is.False);
            window.ProcessNetworkMonitoringCheckBox.IsChecked = !initiallyEnabled;
            await window.SaveAsync();
            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.Zero);
                Assert.That(window.Result, Is.Null);
                Assert.That(window.Section, Is.EqualTo(SettingsSection.Network));
                Assert.That(window.ProcessNetworkMonitoringStatus.Text, Is.EqualTo(Strings.NetworkProcessMonitoringStoreUnavailable));
                Assert.That(window.SaveButton.IsEnabled, Is.True);
            });
        }), Is.False);
    }
#endif

    private static ComboBox Selector(SettingsWindow window, SettingsSection section, string name)
    {
        window.ShowSection(section);
        return Descendants(window).OfType<ComboBox>().Single(control => AutomationProperties.GetName(control) == name);
    }

    private static CheckBox Check(SettingsWindow window, SettingsSection section, string caption)
    {
        window.ShowSection(section);
        return Descendants(window).OfType<CheckBox>().Single(control => control.Content as string == caption);
    }

    private static TextBox CaptureField(SettingsWindow window, string format) => Descendants(window.AutoCapture)
        .OfType<TextBox>().Single(control => AutomationProperties.GetName(control) == Strings.Format(format, Strings.CPU));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Descendants(child)) yield return nested;
    }

    private static async Task WithNotice(SettingsWindow owner, Func<Task> command, string title, string message,
        Action<MessageDialog>? respond = null)
    {
        var existing = Application.Current.Windows.Cast<Window>().ToHashSet();
        Exception? failure = null;
        var seen = 0;
        var operation = owner.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var windows = Application.Current.Windows.Cast<Window>().Where(window => !existing.Contains(window)).ToArray();
            try
            {
                Assert.That(windows, Has.Length.EqualTo(1));
                Assert.That(windows[0], Is.TypeOf<MessageDialog>());
                Assert.That(windows[0].Owner, Is.SameAs(owner));
                Assert.That(windows[0].Title, Is.EqualTo(title));
                Assert.That(Descendants(windows[0]).OfType<TextBlock>().Select(block => block.Text), Does.Contain(message));
                seen++;
                respond?.Invoke((MessageDialog)windows[0]);
            }
            catch (Exception ex) { failure = ex; }
            finally { foreach (var window in windows) window.Close(); }
        }));
        try { await command(); }
        finally
        {
            operation.Abort();
            foreach (var window in Application.Current.Windows.Cast<Window>().Where(window => !existing.Contains(window)).ToArray()) window.Close();
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.That(seen, Is.EqualTo(1));
    }
}
