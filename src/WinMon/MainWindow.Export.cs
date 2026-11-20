using System.Security;
using System.Windows;
using Microsoft.Win32;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    private static readonly HashSet<Task> ExportWrites = [];

    private static async Task WhenExportsFinishAsync()
    {
        while (ExportWrites.Where(write => !write.IsCompleted).ToArray() is { Length: > 0 } running)
            await Task.WhenAll(running).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing |
                                                       ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    // WPF requires a void event handler; file errors are shown in the dialog below.
    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (IsClosing) return;
        var initial = CompactMode ? CsvExportKind.ResourceHistory :
            UsageTab.IsChecked == true ? CsvExportKind.UsageTotals :
            ServicesTab.IsChecked == true ? CsvExportKind.Services :
            ConnectionsTab.IsChecked == true ? CsvExportKind.Connections :
            ProcessesTab.IsChecked == true ? CsvExportKind.ProcessSamples : CsvExportKind.ResourceHistory;
        var dialog = new CsvExportDialog(initial,
            SelectedSample is null && UsageView.ArchiveDays == 0 ? null : UsageText.PeriodNote(CurrentUsageFrame())) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Choice is not { } choice || IsClosing) return;

        try
        {
            var save = new SaveFileDialog
            {
                Title = Strings.SaveCsv, Filter = Strings.ExportCsvFilter, DefaultExt = ".csv",
                AddExtension = true, OverwritePrompt = true, FileName = choice.FileName
            };
            if (save.ShowDialog(this) != true || IsClosing) return;

            // Capture immutable snapshots on the UI thread, then write without blocking sampling or painting.
            var samples = choice.Kind is not (CsvExportKind.ResourceHistory or CsvExportKind.ProcessSamples)
                ? Array.Empty<MonitorSample>()
                : CsvExport.SelectSamples(Session.History.Samples,
                    choice.ChartRange ? _preferences.EffectiveChartRange : null);
            var ledger = Session.Usage;
            var usageFrame = CurrentUsageFrame();
            var scopedUsage = SelectedSample is not null || UsageView.ArchiveDays > 0;
            var period = choice.Kind == CsvExportKind.UsageTotals && scopedUsage && usageFrame.Available && usageFrame.Latest is { } latest
                ? new UsagePeriod(usageFrame.Apps, usageFrame.MachineBusyTicks, usageFrame.NotAttributedTicks,
                    usageFrame.Since, latest, usageFrame.Complete) { Archived = usageFrame.Archived } : null;
            var apps = choice.Kind == CsvExportKind.UsageTotals ? ledger.Apps.ToArray() : [];
            var (since, busyTicks, notAttributedTicks) =
                (ledger.Since, ledger.MachineBusyTicks, ledger.NotAttributedCpuTicks);
            var start = choice.ChartRange && Session.History.Samples.Count > 0
                ? ProcessRanking.Moment(Session.History.Samples[^1]) - _preferences.EffectiveChartRange
                : double.NegativeInfinity;
            var services = choice.Kind == CsvExportKind.Services
                ? Session.Services.Frames.Where(frame => ProcessRanking.Moment(frame.Sample) >= start).ToArray()
                : [];
            var connections = choice.Kind is CsvExportKind.Connections or CsvExportKind.NetworkTraffic
                ? Session.Connections.Captures.Where(capture => ProcessRanking.Moment(capture.Sample) >= start).ToArray()
                : [];
            var hasData = choice.Kind switch
            {
                CsvExportKind.Services => services.Length > 0,
                CsvExportKind.Connections => connections.Length > 0,
                CsvExportKind.NetworkTraffic => connections.Length > 0,
                CsvExportKind.UsageTotals => !scopedUsage || period is not null,
                _ => samples.Count > 0
            };
            if (!hasData)
            {
                MessageDialog.ShowNotice(this, Strings.ExportNoData, Strings.ExportCsv);
                return;
            }

            var path = save.FileName;
            var write = Task.Run(() => CsvExport.Write(path, choice.Kind, samples, apps, since, busyTicks,
                notAttributedTicks, services, connections, period));
            ExportWrites.Add(write);
            try
            {
                await write;
                if (!IsClosing)
                    MessageDialog.ShowNotice(this, Strings.Format(Strings.ExportSaved, save.FileName),
                        Strings.ExportCsv);
            }
            finally
            {
                ExportWrites.Remove(write);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                                       NotSupportedException or SecurityException)
        {
            if (!IsClosing)
                MessageDialog.ShowNotice(this, Strings.Format(Strings.ExportFailed, ex.Message),
                    Strings.ExportCsv);
        }
    }
}
