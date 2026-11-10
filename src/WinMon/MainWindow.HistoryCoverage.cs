using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    private void RefreshHistoryCoverage()
    {
        CaptureCoverage.Visibility = SelectedSample is null || CompactMode ? Visibility.Collapsed : Visibility.Visible;
        if (CaptureCoverage.Visibility != Visibility.Visible) return;
        var source = ServicesTab.IsChecked == true ? 1 : ConnectionsTab.IsChecked == true ? 2 :
            UsageTab.IsChecked == true ? 3 : 0;
        CaptureCoverage.Text = DescribeHistory(source, false);
        AutomationProperties.SetName(CaptureCoverage, CaptureCoverage.Text);
        AutomationProperties.SetHelpText(CaptureCoverage, CoverageHelp());
    }

    private void OnHistoryCoverageToolTipOpening(object sender, ToolTipEventArgs e)
    {
        CaptureCoverage.ToolTip = HistoryCoverageDetails();
    }

    internal string HistoryCoverageDetails() => string.Join(Environment.NewLine,
        Enumerable.Range(0, 4).Select(source => DescribeHistory(source, true)).Append(CoverageHelp()));

    private string CoverageHelp() => Strings.Format(Strings.HistoryCoverageHelp,
        UsageText.Duration(TimeSpan.FromSeconds(Session.History.Capacity)),
        UsageText.Duration(TimeSpan.FromSeconds(ServiceHistory.Seconds)));

    private string DescribeHistory(int source, bool details)
    {
        var selected = SelectedSample;
        var retained = selected is not null && Session.History.Samples.Contains(selected);
        var services = Session.Services.Frames;
        var usage = Session.UsageHistory;
        var (label, first, last, available, limited) = source switch
        {
            1 => (Strings.Services, services.FirstOrDefault()?.Sample.Timestamp, services.LastOrDefault()?.Sample.Timestamp,
                retained && Session.Services.Find(selected)?.Available == true, false),
            2 => (Strings.ConnectionsTab, Session.Connections.Oldest?.Sample.Timestamp,
                Session.Connections.Latest?.Sample.Timestamp,
                retained && selected is not null && Session.Connections.Find(selected)?.Available == true,
                Session.Connections.WasMemoryLimited),
            3 => (Strings.UsageTab, usage.OldestTimestamp, usage.LatestTimestamp,
                retained && selected is not null && usage.IsAvailableAt(selected), usage.WasMemoryLimited),
            _ => (Strings.ExportResourceHistory + " / " + Strings.ProcessesTab,
                Session.History.Samples.FirstOrDefault()?.Timestamp, Session.History.Latest?.Timestamp,
                retained && selected?.IsGap == false, Session.History.WasMemoryLimited)
        };
        var line = HistoryCoverageLine(label, first, last, available, limited);
        return details && first is { } start && last is { } end
            ? line + Environment.NewLine + Strings.Format(Strings.HistoryCoverageBounds,
                start.ToLocalTime().DateTime, end.ToLocalTime().DateTime) : line;
    }

    internal static string HistoryCoverageLine(string label, DateTimeOffset? first, DateTimeOffset? last,
        bool available, bool memoryLimited)
    {
        var span = first is { } start && last is { } end
            ? UsageText.Duration(end >= start ? end - start : TimeSpan.Zero) : Strings.MissingReading;
        return Strings.Format(Strings.HistoryCoverageLine, label, span,
                   available ? Strings.HistorySelectedAvailable : Strings.HistorySelectedUnavailable) +
               (memoryLimited ? " · " + Strings.HistoryShortenedByMemoryLimit : "");
    }
}
