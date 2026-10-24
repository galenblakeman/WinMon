using System.Windows;
using System.Windows.Controls;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon;

public partial class MainWindow
{
    private void ArrangePinToolbar()
    {
        Grid.SetColumn(ProcessRankingControls, 0);
        Grid.SetColumnSpan(ProcessRankingControls, 2);
        Grid.SetColumn(ProcessGroupingControls, _inlinePinToolbar ? 5 : 4);
        Grid.SetColumnSpan(ProcessGroupingControls, _inlinePinToolbar ? 1 : 2);
        var pinRow = _inlinePinToolbar ? 0 : _inlineProcessToolbar ? 1 : 2;
        Grid.SetRow(PinnedOnlyCheckBox, pinRow);
        Grid.SetColumn(PinnedOnlyCheckBox, 4);
        Grid.SetColumnSpan(PinnedOnlyCheckBox, _inlinePinToolbar ? 1 : 2);
        PinnedOnlyCheckBox.Margin = _inlinePinToolbar ? new Thickness(0, 0, 12, 0) : new Thickness(0, 6, 0, 0);
    }

    internal void ToggleProcessPin(ProcessUsage process, bool grouped)
    {
        _processPins.Toggle(process, grouped);
        _preferences = _preferences with { PinnedProcesses = _processPins.Snapshot() };
        UpdateProcessRetention();
        RefreshPresentation();
    }

    private void OnPinnedOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        _preferences = _preferences with { PinnedOnly = PinnedOnlyCheckBox.IsChecked == true };
        RefreshPresentation();
    }

    private PinnedProcess[] ResolvePinnedRows(IReadOnlyList<MonitorSample> samples, bool grouped, string query,
        bool appsOnly,
        Func<ProcessRanking> inclusive)
    {
        if (_processPins.Count(grouped) == 0) return [];
        return
        [
            .. _processPins.Resolve(samples.LastOrDefault(), inclusive().Processes, grouped,
                    _selectedSample is not null)
                .Where(p => ProcessSearch.Filter([p.Usage], query, appsOnly).Any())
        ];
    }

    private void UpdatePinnedRows(PinnedProcess[] pinned, ProcessGraphData[] data, IReadOnlyList<MonitorSample> samples,
        bool grouped)
    {
        while (_pinnedRows.Count < pinned.Length)
        {
            var row = new ProcessRow();
            row.EnableProcessActions(CanEndProcess, EndProcess);
            row.EnablePriorityActions(CanSetProcessPriority, process => ProcessPriorityChanger.Read(process), SetProcessPriority);
            EnableProcessInspection(row);
            row.EnablePinning(_processPins.Contains, ToggleProcessPin);
            _pinnedRows.Add(row);
            PinnedProcessRows.Children.Add(row);
        }

        for (var i = 0; i < _pinnedRows.Count; i++)
        {
            _pinnedRows[i].Visibility = i < pinned.Length ? Visibility.Visible : Visibility.Collapsed;
            if (i < pinned.Length)
                _pinnedRows[i].Update(pinned[i].Usage, samples,
                    MonitorPreferences.ShortRangeSeconds, _wide, data[i], grouped, pinned[i].State);
        }

        var pinCount = _processPins.Count(grouped);
        PinnedCountLabel.Text = Strings.Format(Strings.PinnedProcessCount, pinCount);
        PinSummary.Visibility = pinCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        PinnedCountLabel.ToolTip = Strings.PinnedProcessesExplanation;
        RankedDivider.Visibility = pinned.Length > 0 && PinnedOnlyCheckBox.IsChecked != true
            ? Visibility.Visible
            : Visibility.Collapsed;
        RankedDividerText.Text = Strings.Format(Strings.RankedByMetric,
            SortHeaders.First(h => h.Sort == _preferences.ProcessSort).Text);
    }
}
