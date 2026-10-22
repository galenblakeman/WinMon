using System.Windows;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon;

public partial class MainWindow
{
    private void ArrangePinToolbar()
    {
        System.Windows.Controls.Grid.SetColumn(ProcessRankingControls, 0);
        System.Windows.Controls.Grid.SetColumnSpan(ProcessRankingControls, 2);
        System.Windows.Controls.Grid.SetColumn(ProcessGroupingControls, _inlinePinToolbar ? 5 : 4);
        System.Windows.Controls.Grid.SetColumnSpan(ProcessGroupingControls, _inlinePinToolbar ? 1 : 2);
        var pinRow = _inlinePinToolbar ? 0 : _inlineProcessToolbar ? 1 : 2;
        System.Windows.Controls.Grid.SetRow(PinnedOnlyCheckBox, pinRow);
        System.Windows.Controls.Grid.SetColumn(PinnedOnlyCheckBox, 4);
        System.Windows.Controls.Grid.SetColumnSpan(PinnedOnlyCheckBox, _inlinePinToolbar ? 1 : 2);
        PinnedOnlyCheckBox.Margin = _inlinePinToolbar ? new Thickness(0, 0, 12, 0) : new Thickness(0, 6, 0, 0);
    }

    private void ToggleProcessPin(ProcessUsage process, bool grouped)
    {
        _processPins.Toggle(process, grouped);
        _preferences = _preferences with { PinnedProcesses = _processPins.Snapshot() };
        Volatile.Write(ref _retainPinned, _processPins.RetentionSnapshot());
        RefreshPresentation();
    }

    private void OnPinnedOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        _preferences = _preferences with { PinnedOnly = PinnedOnlyCheckBox.IsChecked == true };
        RefreshPresentation();
    }

    private PinnedProcess[] ResolvePinnedRows(IReadOnlyList<CpuSample> samples, bool grouped, string query, bool appsOnly)
    {
        if (_processPins.Count(grouped) == 0) return [];
        var all = ProcessRanking.Rank(samples, sort: _preferences.ProcessSort, grouped: grouped, includeInactive: true);
        return [.. _processPins.Resolve(samples.LastOrDefault(), all.Processes, grouped, _selectedSample is not null)
            .Where(p => ProcessSearch.Filter([p.Usage], query, appsOnly).Any())];
    }

    private void UpdatePinnedRows(PinnedProcess[] pinned, ProcessGraphData[] data, IReadOnlyList<CpuSample> samples, bool grouped)
    {
        while (_pinnedRows.Count < pinned.Length)
        {
            var row = new ProcessRow();
            row.EnableProcessActions(CanEndProcess, EndProcess);
            EnableProcessInspection(row);
            row.EnablePinning(_processPins.Contains, ToggleProcessPin);
            _pinnedRows.Add(row);
            PinnedProcessRows.Children.Add(row);
        }
        for (var i = 0; i < _pinnedRows.Count; i++)
        {
            _pinnedRows[i].Visibility = i < pinned.Length ? Visibility.Visible : Visibility.Collapsed;
            if (i < pinned.Length) _pinnedRows[i].Update(pinned[i].Usage, samples,
                MonitorPreferences.ShortRangeSeconds, _wide, data[i], grouped, pinned[i].State);
        }
        var pinCount = _processPins.Count(grouped);
        PinnedCountLabel.Text = Strings.Format(Strings.PinnedProcessCount, pinCount);
        PinSummary.Visibility = pinCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        PinnedCountLabel.ToolTip = Strings.PinnedProcessesExplanation;
        RankedDivider.Visibility = pinned.Length > 0 && PinnedOnlyCheckBox.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
        RankedDividerText.Text = Strings.Format(Strings.RankedByMetric, SortHeaders.First(h => h.Sort == _preferences.ProcessSort).Text);
    }
}
