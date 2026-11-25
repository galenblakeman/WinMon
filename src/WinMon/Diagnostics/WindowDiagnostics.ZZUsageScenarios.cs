using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    // Keep the Usage workflow separate from ranking, captured navigation and archive scopes.
    private static async Task<Dictionary<string, object>> CaptureUsageRankingAsync(MainWindow fixture,
        string output, CancellationToken cancellationToken)
    {
        fixture.Show();
        var processTarget = fixture.Session.LatestFull!.Processes.First(process => process.AppName == "Google Chrome");
        fixture.Processes.ShowGroup(processTarget);
        fixture.RefreshVisiblePresentation();
        var usageLink = fixture.Processes.ProcessActions.CreateMoreMenu()!.Items.OfType<MenuItem>()
            .Single(item => Equals(item.Header, Strings.ShowInUsage));
        if (!usageLink.IsEnabled)
            throw new InvalidOperationException("Process navigation must work before Usage has ever been opened.");
        usageLink.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (fixture.UsageTab.IsChecked != true || fixture.UsageView.Rows.SelectedItem is not UsageDisplayRow
            { App.ImagePath: var targetPath } || targetPath != processTarget.ImagePath)
            throw new InvalidOperationException("Process navigation must reveal the same executable in Usage.");
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "usage-from-processes.png"), cancellationToken);
        fixture.UsageTab.IsChecked = true;
        var apps = fixture.Session.Usage.Apps;
        if (fixture.UsageTotalValue.Text != MainWindow.CompactCount((uint)apps.Count) ||
            fixture.UsageActiveValue.Text != MainWindow.CompactCount((uint)apps.Count(app => app.Running > 0)) ||
            fixture.UsageCounts.Visibility != Visibility.Visible ||
            fixture.SystemCounts.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Usage tab did not show total and active app counts.");
        var view = fixture.UsageView;
        view.Rows.SelectedItem = view.Rows.Items.Cast<UsageDisplayRow>().First();
        var checks = new Dictionary<string, object>();
        foreach (var theme in new[] { "Dark", "Light" })
        {
            fixture.Preferences = fixture.Preferences with { Theme = theme };
            fixture.ApplyTheme();
            fixture.RefreshVisiblePresentation();
            await CaptureServiceWindowAsync(fixture,
                Path.Combine(output, $"usage-{theme.ToLowerInvariant()}.png"), cancellationToken);
        }

        var rows = view.Rows.Items.Cast<UsageDisplayRow>().ToArray();
        if (rows.Length != 6 || rows[0].App?.AppName != "Google Chrome")
            throw new InvalidOperationException("Usage must list every app, Google Chrome first by CPU time.");
        checks["Rows"] = rows.Select(r => new { r.Name, r.CpuTime, r.Share, r.Processes, r.LastActive }).ToArray();

        view.IoHeader.RaiseEvent(new MouseButtonEventArgs(
            Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent
        });
        if (view.Rows.Items.Cast<UsageDisplayRow>().First().App?.AppName != "Example Setup")
            throw new InvalidOperationException("Ranking by I/O must put the installer first.");
        if (fixture.Preferences.UsageSortColumn != (int)UsageSort.Io)
            throw new InvalidOperationException("The Usage ranking must be stored in preferences.");
        checks["Report"] = UsageText.Report(new UsageFrame(fixture.Session.Usage.Apps,
            fixture.Session.Usage.MachineBusyTicks, fixture.Session.Usage.NotAttributedCpuTicks,
            fixture.Session.Usage.Since, fixture.Session.LatestFull?.Timestamp, false), view.Settings, "");

        return checks;
    }

    private static async Task CaptureUsageNavigationAsync(MainWindow fixture, string output,
        Dictionary<string, object> checks, CancellationToken cancellationToken)
    {
        var view = fixture.UsageView;
        var setup = fixture.Session.Usage.Apps.Single(a => a.Name == "setup");
        fixture.ShowUsageInProcesses(setup);
        if (fixture.ProcessesTab.IsChecked != true || fixture.SelectedSample is not { } moment ||
            setup.LastActive?.Matches(moment) != true || fixture.Processes.GroupedChip.IsChecked != true)
            throw new InvalidOperationException(
                "Show in Processes must open Capture at the exited app's last active sample, grouped.");
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "usage-show-exited.png"), cancellationToken);

        var chrome = fixture.Session.Usage.Apps.Single(a => a.AppName == "Google Chrome");
        fixture.SelectedSample = null;
        fixture.ShowUsageInProcesses(chrome);
        if (fixture.SelectedSample is not null)
            throw new InvalidOperationException("Show in Processes must stay live for a running app.");

        fixture.SelectedSample = fixture.Session.History.Samples[59];
        fixture.UsageTab.IsChecked = true;
        fixture.RefreshVisiblePresentation();
        var period = RecordUsagePeriodChecks(fixture, checks);
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "usage-recorded-period.png"), cancellationToken);
        var selected = fixture.SelectedSample;
        fixture.ShowUsageInProcesses(period.Apps.Single(app => app.AppName == "Google Chrome"));
        if (!ReferenceEquals(fixture.SelectedSample, selected))
            throw new InvalidOperationException("Navigating from period Usage must preserve the captured moment.");
        fixture.SelectedSample = null;
        fixture.UsageTab.IsChecked = true;
        fixture.RefreshVisiblePresentation();
        if (fixture.CurrentUsageFrame().Capturing || !view.ResetUsage.IsEnabled)
            throw new InvalidOperationException("Live Usage must restore session totals and Reset.");
        if (fixture.CaptureCoverage.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Live view must hide Capture coverage.");
    }

    private static UsageFrame RecordUsagePeriodChecks(MainWindow fixture, Dictionary<string, object> checks)
    {
        var view = fixture.UsageView;
        var period = fixture.CurrentUsageFrame();
        if (!period.Capturing || period.Apps.Single(app => app.Name == "setup").Running != 1 ||
            period.Latest != fixture.SelectedSample!.Timestamp || view.ResetUsage.IsEnabled ||
            period.Apps.Sum(app => app.CpuTicks) >= fixture.Session.Usage.Apps.Sum(app => app.CpuTicks))
            throw new InvalidOperationException("Capture Usage must show recorded period totals, not live totals.");
        checks["PeriodReport"] = UsageText.Report(period, view.Settings, "");
        string? copiedCapture = null;
        fixture.CopySnapshot(text => copiedCapture = text);
        if (copiedCapture is null || !copiedCapture.Contains(view.CaptureReport(period), StringComparison.Ordinal))
            throw new InvalidOperationException("Copy capture must append the selected period Usage view.");
        checks["CopiedCapture"] = copiedCapture;
        if (!fixture.CaptureCoverage.Text.Contains(Strings.HistorySelectedAvailable, StringComparison.Ordinal) ||
            !fixture.HistoryCoverageDetails().Contains(Strings.Services, StringComparison.Ordinal))
            throw new InvalidOperationException("Capture coverage must describe the active period and other views.");
        return period;
    }

    private static async Task CaptureUsageArchiveScopesAsync(MainWindow fixture, string output,
        CancellationToken cancellationToken)
    {
        var view = fixture.UsageView;
        var archive = new UsageArchive();
        foreach (var sample in fixture.Session.History.Samples) archive.Add(sample);
        archive.Add(fixture.Session.LatestFull! with { Timestamp = DateTimeOffset.Now.AddDays(-1), MonotonicTimestamp = 0 });
        fixture.DailyUsageArchive = archive;
        fixture.SelectedSample = null;
        fixture.UsageTab.IsChecked = true;
        foreach (var theme in new[] { "Dark", "Light" })
        {
            fixture.Preferences = fixture.Preferences with { Theme = theme };
            fixture.ApplyTheme();
            view.ArchiveScope.SelectedIndex = 1;
            fixture.RefreshVisiblePresentation();
            var day = fixture.CurrentUsageFrame();
            if (day.Apps.Count == 0 || view.ResetUsage.IsEnabled || day.Notice is null)
                throw new InvalidOperationException("Daily Usage must show saved observations without enabling session reset.");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "usage-day-" + theme + ".png"), cancellationToken);
            view.ArchiveScope.SelectedIndex = 2;
            fixture.RefreshVisiblePresentation();
            if (fixture.CurrentUsageFrame().Apps.Sum(a => a.CpuTicks) <= day.Apps.Sum(a => a.CpuTicks))
                throw new InvalidOperationException("Weekly Usage must include the previous day's observations.");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "usage-week-" + theme + ".png"), cancellationToken);
        }
        view.ArchiveScope.SelectedIndex = 0;
    }

}
