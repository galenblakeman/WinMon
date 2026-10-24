using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon;

public partial class MainWindow
{
    private void InitializeUsage()
    {
        UsageView.SettingsChanged += settings => _preferences = _preferences with
        {
            TopUsageCount = settings.TopCount, UsageSortColumn = (int)settings.Sort, UsageAppsOnly = settings.AppsOnly
        };
        UsageView.CopyRequested += CopyProcessDetails;
        UsageView.PropertiesRequested += (name, details) =>
            new ServicePropertiesWindow(name, details, CopyProcessDetails) { Owner = this }.ShowDialog();
        UsageView.ResetRequested += ResetUsage;
        UsageView.CanShowInProcesses = app => app.Running > 0 || LastActiveSample(app) is not null;
        UsageView.ShowInProcessesRequested += ShowUsageInProcesses;
    }

    private static UsageListSettings UsageSettings(MonitorPreferences preferences)
    {
        return new UsageListSettings(preferences.TopUsageCount, (UsageSort)preferences.UsageSortColumn,
            preferences.UsageAppsOnly);
    }

    private void RefreshUsage()
    {
        // The compact strip hides the Usage tab as well.
        if (UsageTab.IsChecked != true || CompactMode) return;
        var ledger = Session.Usage;
        UsageView.Update(new UsageFrame(ledger.Apps, ledger.MachineBusyTicks, ledger.NotAttributedCpuTicks,
            ledger.Since, (Session.LatestFull ?? Session.Latest)?.Timestamp, SelectedSample is not null));
    }

    private void ResetUsage()
    {
        if (!MessageDialog.Confirm(this, Strings.UsageResetConfirm, Strings.UsageTab, Strings.UsageReset)) return;
        Session.Usage.Reset(DateTimeOffset.Now);
        RefreshPresentation();
    }

    /// <summary>The exact sample an exited app was last active in, while it is still in history.</summary>
    private MonitorSample? LastActiveSample(AppUsage app)
    {
        if (app.LastActive is not { } key) return null;
        var samples = Session.History.Samples;
        for (var i = samples.Count - 1; i >= 0; i--)
            if (key.Matches(samples[i]))
                return samples[i];
        return null;
    }

    /// <summary>
    /// Select the app's executable group in Processes by identity, not by search text: live while it runs,
    /// otherwise Capture at the sample it was last active in.
    /// </summary>
    internal void ShowUsageInProcesses(AppUsage app)
    {
        var moment = app.Running > 0 ? null : LastActiveSample(app);
        if (app.Running == 0 && moment is null)
        {
            // The sample can expire between opening the menu and choosing the item.
            MessageDialog.ShowNotice(this, Strings.UsageNotInHistory, Strings.UsageShowInProcesses);
            return;
        }

        SelectedSample = moment;
        Processes.ShowGroup(new ProcessUsage(0, 0, app.Name, 0)
            { ImagePath = app.ImagePath, AppName = app.AppName, Publisher = app.Publisher });
        ProcessesTab.IsChecked = true;
        RefreshPresentation();
    }
}