using System.Windows.Automation;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    // Keep the language workflow separate from the invariants it verifies after each save.
    private void ValidateLiveLanguageState(MonitorSample? anchor, Task? samplingTask)
    {
        if (window.InfoButton.Content as string != Strings.Info ||
            window.SettingsButton.Content as string != Strings.Settings ||
            window.Processes.ProcessHistoryHeader.Text != Strings.History ||
            window.MemoryMetric.Label != Strings.Memory ||
            AutomationProperties.GetName(window.MemoryMetric.Toggle) !=
            Strings.Format(Strings.ShowOnChart, Strings.Memory) ||
            window.Tray!.ContextMenuStrip!.Items[0].Text != Strings.ShowWinMon ||
            window.Tray!.ContextMenuStrip.Items[1].Text != Strings.Exit ||
            window.Preferences.ProcessSort != ProcessSort.Memory ||
            window.Processes.ProcessSortSelector.SelectedIndex != (int)ProcessSort.Memory ||
            window.Processes.ProcessSortSelector.SelectedItem as string != Strings.Memory ||
            window.Processes.ProcessSearchBox.Text != "__live_language_no_match__" ||
            !ReferenceEquals(window.SelectedSample, anchor) ||
            (anchor is not null && !window.Session.History.Samples.Contains(anchor)) ||
            !ReferenceEquals(window.SamplingTask, samplingTask) ||
            window.Processes.EmptyLabel.Text != Strings.NoRankedProcessesMatchYourSearch)
            throw new InvalidOperationException(
                "Live language switching left stale text or changed recording state.");
    }
}
