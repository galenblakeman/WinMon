using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
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
        Processes.CanNavigate = CanNavigateProcess;
        Processes.NavigationRequested += NavigateProcess;
        // One preference hides the action bar on every tab.
        Processes.ActionsHiddenChanged += OnActionsHiddenChanged;
        ServiceView.ActionsHiddenChanged += OnActionsHiddenChanged;
        UsageView.ActionsHiddenChanged += OnActionsHiddenChanged;
        ConnectionView.ActionsHiddenChanged += OnActionsHiddenChanged;
    }

    private bool CanNavigateProcess(string target, ProcessDetailsSnapshot snapshot)
    {
        if (snapshot.Historical != (SelectedSample is not null) ||
            snapshot.Historical && (snapshot.Sample is not { } key || SelectedSample is null || !key.Matches(SelectedSample)) ||
            SelectedSample is { } past && !Session.History.Samples.Contains(past)) return false;
        var process = snapshot.Process;
        if (target == "usage")
            return SelectedSample is null ? Session.Usage.ContainsExecutable(process) :
                CurrentUsageFrame().Apps.Any(app => ProcessIdentityPolicy.Windows.Comparer.Equals(app.Key,
                    ProcessIdentityPolicy.Windows.ExecutableKey(process)));
        if (target != "connections" || snapshot.Grouped && process.ImagePath.Length == 0) return false;
        var capture = SelectedSample is { } selected ? Session.Connections.Find(selected) : Session.Connections.Latest;
        var frame = _diagnosticMode && SelectedSample is null ? _connections : capture?.Connections;
        if (frame?.Available != true) return false;
        var owners = _diagnosticMode && SelectedSample is null
            ? Session.LatestFull?.Processes ?? [] : capture?.Owners ?? [];
        var identities = owners.Where(owner => process.ImagePath.Length > 0 && owner.ImagePath.Length > 0 &&
                ProcessIdentityPolicy.Windows.Comparer.Equals(owner.ImagePath, process.ImagePath))
            .Select(owner => owner.Identity).ToHashSet();
        identities.Add(process.Identity);
        return frame.Connections.Any(connection => connection.ProcessCreationTicks is { } ticks &&
            identities.Contains(new ProcessLifetimeIdentity(connection.ProcessId, ticks,
                ProcessLifetimeIdentity.WindowsFileTime)));
    }

    private void NavigateProcess(string target, ProcessDetailsSnapshot snapshot)
    {
        if (!CanNavigateProcess(target, snapshot)) return;
        if (target == "connections")
        {
            ConnectionsTab.IsChecked = true;
            RefreshPresentation();
            ConnectionView.ShowApp(snapshot.Process);
        }
        else if (target == "usage")
        {
            UsageTab.IsChecked = true;
            RefreshPresentation();
            UsageView.ShowApp(snapshot.Process);
        }
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

    private static void UpdateActivityPair(TextBlock display,
        Run totalValue, Run secondaryValue,
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
        ConnectionView.ActionsHidden = hidden;
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
