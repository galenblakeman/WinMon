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
            ProcessesTab.IsChecked == true ? CsvExportKind.ProcessSamples : CsvExportKind.ResourceHistory;
        var dialog = new CsvExportDialog(initial) { Owner = this };
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
            var samples = choice.Kind == CsvExportKind.UsageTotals ? Array.Empty<MonitorSample>() :
                CsvExport.SelectSamples(Session.History.Samples,
                    choice.ChartRange ? _preferences.EffectiveChartRange : null);
            var ledger = Session.Usage;
            var apps = choice.Kind == CsvExportKind.UsageTotals ? ledger.Apps.ToArray() : [];
            var (since, busyTicks, notAttributedTicks) =
                (ledger.Since, ledger.MachineBusyTicks, ledger.NotAttributedCpuTicks);
            if (choice.Kind != CsvExportKind.UsageTotals && samples.Count == 0)
            {
                MessageDialog.ShowNotice(this, Strings.ExportNoData, Strings.ExportCsv);
                return;
            }

            var path = save.FileName;
            var write = Task.Run(() => CsvExport.Write(path, choice.Kind, samples, apps, since, busyTicks,
                notAttributedTicks));
            ExportWrites.Add(write);
            try
            {
                await write;
                if (!IsClosing) MessageDialog.ShowNotice(this, Strings.Format(Strings.ExportSaved, save.FileName),
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
            if (!IsClosing) MessageDialog.ShowNotice(this, Strings.Format(Strings.ExportFailed, ex.Message),
                Strings.ExportCsv);
        }
    }
}
