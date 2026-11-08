using System.Globalization;
using System.Windows;
using WinMon.Monitoring;

namespace WinMon.Controls;

internal sealed record CsvExportChoice(CsvExportKind Kind, bool ChartRange, string FileName);

public partial class CsvExportDialog : Window
{
    internal CsvExportChoice? Choice { get; private set; }

    internal CsvExportDialog(CsvExportKind initialKind)
    {
        InitializeComponent();
        WindowTheme.Attach(this);
        switch (initialKind)
        {
            case CsvExportKind.ResourceHistory: ResourceChoice.IsChecked = true; break;
            case CsvExportKind.ProcessSamples: ProcessChoice.IsChecked = true; break;
            case CsvExportKind.Services: ServiceChoice.IsChecked = true; break;
            case CsvExportKind.Connections: ConnectionChoice.IsChecked = true; break;
            default: UsageChoice.IsChecked = true; break;
        }
    }

    private CsvExportKind Kind => UsageChoice.IsChecked == true ? CsvExportKind.UsageTotals :
        ServiceChoice.IsChecked == true ? CsvExportKind.Services :
        ConnectionChoice.IsChecked == true ? CsvExportKind.Connections :
        ProcessChoice.IsChecked == true ? CsvExportKind.ProcessSamples : CsvExportKind.ResourceHistory;

    private void OnChoiceChanged(object sender, RoutedEventArgs e)
    {
        if (DataNote is null || TimeChoices is null || FileNameBox is null) return;
        TimeChoices.Visibility = Kind == CsvExportKind.UsageTotals ? Visibility.Collapsed : Visibility.Visible;
        MinHeight = Kind == CsvExportKind.UsageTotals ? 500 : 630;
        DataNote.Text = Kind switch
        {
            CsvExportKind.ResourceHistory => Strings.ExportResourceNote,
            CsvExportKind.ProcessSamples => Strings.ExportProcessNote,
            CsvExportKind.Services => Strings.ExportServiceNote,
            CsvExportKind.Connections => Strings.ExportConnectionNote,
            _ => Strings.ExportUsageNote
        };
        var type = Kind switch
        {
            CsvExportKind.ResourceHistory => "resources",
            CsvExportKind.ProcessSamples => "processes",
            CsvExportKind.Services => "services",
            CsvExportKind.Connections => "connections",
            _ => "usage"
        };
        FileNameBox.Text = $"winmon-{type}-{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var fileName = FileNameBox.Text.Trim();
        if (fileName.Length == 0 || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            FileNameBox.Focus();
            FileNameBox.SelectAll();
            return;
        }

        Choice = new CsvExportChoice(Kind, Kind != CsvExportKind.UsageTotals && RangeChoice.IsChecked == true,
            fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".csv");
        DialogResult = true;
    }
}