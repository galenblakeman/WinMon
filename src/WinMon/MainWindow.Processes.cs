using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    // Null until first shown, and after a language change, so the next refresh writes every label.
    private (uint? ProcessCount, uint? ThreadCount, uint? HandleCount)? _systemCounts;
    private (uint? Total, uint? Secondary)? _serviceCounts, _usageCounts;

    private void InitializeProcesses()
    {
        Processes.SettingsChanged += settings =>
        {
            _preferences = settings.ApplyTo(_preferences);
            RefreshPresentation();
        };
        Processes.PinsChanged += () =>
        {
            _preferences = _preferences with { PinnedProcesses = Processes.SavedPins };
            RefreshPresentation();
        };
        Processes.RetentionChanged += () => Volatile.Write(ref _retainPinned, Processes.Retention);
        Processes.RefreshRequested += RefreshPresentation;
        Processes.CopyRequested += CopyProcessDetails;
        // One preference hides the action bar on every tab.
        Processes.ActionsHiddenChanged += OnActionsHiddenChanged;
        ServiceView.ActionsHiddenChanged += OnActionsHiddenChanged;
        UsageView.ActionsHiddenChanged += OnActionsHiddenChanged;
    }

    private void OnActionsHiddenChanged(bool hidden)
    {
        _preferences = _preferences with { ProcessActionsHidden = hidden };
        ApplyActionsHidden();
    }

    // Machine totals beside the activity tabs follow the moment the table shows. Unchanged counts cost no layout.
    private void UpdateSystemCounts(ResourceSample? resource)
    {
        var counts = (resource?.ProcessCount, resource?.ThreadCount, resource?.HandleCount);
        if (_systemCounts == counts) return;
        _systemCounts = counts;
        ProcessCountValue.Text = CompactCount(counts.ProcessCount);
        ThreadCountValue.Text = CompactCount(counts.ThreadCount);
        HandleCountValue.Text = CompactCount(counts.HandleCount);
        var exact = $"{Strings.ProcessesHeading}: {ExactCount(counts.ProcessCount)}\n" +
                    $"{Strings.Threads}: {ExactCount(counts.ThreadCount)}\n" +
                    $"{Strings.Handles}: {ExactCount(counts.HandleCount)}";
        SystemCounts.ToolTip = exact;
        AutomationProperties.SetName(SystemCounts, exact);
    }

    private static void UpdateActivityPair(System.Windows.Controls.TextBlock display,
        System.Windows.Documents.Run totalValue, System.Windows.Documents.Run secondaryValue,
        uint? total, uint? secondary, string totalLabel, string secondaryLabel,
        ref (uint? Total, uint? Secondary)? previous)
    {
        var counts = (total, secondary);
        if (previous == counts) return;
        previous = counts;
        var compactTotal = CompactCount(total);
        var compactSecondary = CompactCount(secondary);
        var exact = $"{totalLabel}: {ExactCount(total)}\n{secondaryLabel}: {ExactCount(secondary)}";
        totalValue.Text = compactTotal;
        secondaryValue.Text = compactSecondary;
        display.ToolTip = exact;
        AutomationProperties.SetName(display, exact);
    }

    private void ApplyActionsHidden()
    {
        var hidden = _preferences.ProcessActionsHidden;
        Processes.ActionsHidden = hidden;
        ServiceView.ActionsHidden = hidden;
        UsageView.ActionsHidden = hidden;
    }

    private void CopyProcessDetails(string text)
    {
        try
        {
            Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true);
        }
        catch (ExternalException)
        {
            MessageDialog.ShowNotice(this, Strings.TheClipboardIsUnavailableTryCopyAgain, Strings.CopyDetails);
        }
    }
}
