using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    private readonly ProcessSelection _processSelection = new();

    private void EnsureProcessRows(int count)
    {
        while (_rows.Count < count)
        {
            var row = new ProcessRow { Visibility = Visibility.Collapsed };
            row.EnableProcessActions(CanEndProcess, EndProcess);
            row.EnablePriorityActions(CanSetProcessPriority, process => ProcessPriorityChanger.Read(process), SetProcessPriority);
            EnableProcessInspection(row);
            row.EnablePinning(_processPins.Contains, ToggleProcessPin);
            _rows.Add(row);
            ProcessRows.Children.Insert(ProcessRows.Children.IndexOf(_otherRow), row);
        }

        // Release the extra row/graph controls when leaving All or narrowing the results.
        while (_rows.Count > Math.Max(50, count))
        {
            ProcessRows.Children.Remove(_rows[^1]);
            _rows.RemoveAt(_rows.Count - 1);
        }
    }

    // Keep each process on the row that last showed it and move rows to the new ranking. Rebinding rows in place
    // renamed almost every row (and its pin button) for screen readers, and moved keyboard focus to another process,
    // whenever the ranking reordered.
    private void ArrangeProcessRows(IReadOnlyList<ProcessUsage> top, bool grouped)
    {
        EnsureProcessRows(top.Count);
        var byIdentity = new Dictionary<(int, long, string?), ProcessRow>();
        foreach (var row in _rows)
            if (row.Identity is { } identity)
                byIdentity.TryAdd(identity, row);
        var arranged = new ProcessRow?[_rows.Count];
        var claimed = new HashSet<ProcessRow>();
        for (var i = 0; i < top.Count; i++)
            if (byIdentity.Remove(ProcessRow.IdentityOf(top[i], grouped), out var row))
            {
                arranged[i] = row;
                claimed.Add(row);
            }

        using (var spare = _rows.Where(row => !claimed.Contains(row)).GetEnumerator())
            for (var i = 0; i < arranged.Length; i++)
                if (arranged[i] is null && spare.MoveNext())
                    arranged[i] = spare.Current;

        var order = arranged.Select(row => row!).ToArray();
        if (!order.SequenceEqual(_rows)) MoveProcessRows(order);
        _rows.Clear();
        _rows.AddRange(order);
    }

    // Moves only the rows outside the longest run already in the new order, so most rows keep their visuals.
    private void MoveProcessRows(ProcessRow[] order)
    {
        var current = new Dictionary<ProcessRow, int>(_rows.Count);
        for (var i = 0; i < _rows.Count; i++) current[_rows[i]] = i;
        var staying = LongestIncreasingRun(order.Select(row => current[row]).ToArray());
        var focused = Keyboard.FocusedElement as DependencyObject;
        var refocus = (IInputElement?)null;
        for (var i = 0; i < order.Length; i++)
        {
            if (staying[i]) continue;
            if (focused is not null && (order[i] == focused || order[i].IsAncestorOf(focused)))
                refocus = focused as IInputElement;
            ProcessRows.Children.Remove(order[i]);
        }

        var start = ProcessRows.Children.IndexOf(_otherRow) - staying.Count(stays => stays);
        for (var i = 0; i < order.Length; i++)
            if (!staying[i])
                ProcessRows.Children.Insert(start + i, order[i]);
        if (refocus is not null && !ReferenceEquals(Keyboard.FocusedElement, refocus)) Keyboard.Focus(refocus);
    }

    internal static bool[] LongestIncreasingRun(int[] values)
    {
        // Patience sorting: tails[k] is the index ending the best run of length k + 1.
        var tails = new List<int>();
        var previous = new int[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            int low = 0, high = tails.Count;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (values[tails[middle]] < values[i]) low = middle + 1;
                else high = middle;
            }

            previous[i] = low > 0 ? tails[low - 1] : -1;
            if (low == tails.Count) tails.Add(i);
            else tails[low] = i;
        }

        var result = new bool[values.Length];
        for (var i = tails.Count > 0 ? tails[^1] : -1; i >= 0; i = previous[i]) result[i] = true;
        return result;
    }

    private void OnToggleActivityActions(object sender, RoutedEventArgs e)
    {
        _preferences = _preferences with { ProcessActionsHidden = !_preferences.ProcessActionsHidden };
        UpdateActivityActionsToggle();
    }

    private void UpdateActivityActionsToggle()
    {
        var hidden = _preferences.ProcessActionsHidden;
        ProcessActions.ActionsHidden = hidden;
        ServiceView.ActionsHidden = hidden;
        var label = hidden ? Strings.ShowActions : Strings.HideActions;
        ActivityActionsToggle.Content = label;
        ButtonIcon.SetData(ActivityActionsToggle, hidden ? ButtonIcon.ChevronDown : ButtonIcon.ChevronUp);
        ActivityActionsToggle.ToolTip = label;
        AutomationProperties.SetName(ActivityActionsToggle, label);
    }

    private void EnableProcessInspection(ProcessRow row)
    {
        row.ProcessSelected += (process, grouped) =>
        {
            _processSelection.Select(process, grouped);
            UpdateProcessRetention();
            RefreshPresentation();
        };
        row.EnableInspectionActions(
            snapshot => new ProcessPropertiesWindow(snapshot, CopyProcessDetails) { Owner = this }.ShowDialog(),
            snapshot => CopyProcessDetails(snapshot.Format()), OpenProcessLocation,
            process => new ProcessDumpDialog(process, () => CanDumpProcess(process)) { Owner = this }.ShowDialog(),
            CanDumpProcess, () => _selectedSample is not null);
    }

    private void UpdateProcessRetention()
    {
        var pins = _processPins.RetentionSnapshot();
        var selected = _processSelection.RetentionSnapshot();
        Volatile.Write(ref _retainPinned,
            pins is null && selected is null
                ? null
                : process => pins?.Invoke(process) == true || selected?.Invoke(process) == true);
    }

    private void RefreshProcessActions(IReadOnlyList<MonitorSample> samples, bool grouped, Func<ProcessRanking> inclusive)
    {
        var hadSelection = _processSelection.HasSelection;
        var target = _processSelection.Resolve(samples, grouped, _selectedSample is not null, inclusive);
        if (hadSelection && !_processSelection.HasSelection) UpdateProcessRetention();
        var snapshot = target is null
            ? null
            : new ProcessDetailsSnapshot(target.Usage, grouped, samples.LastOrDefault()?.Timestamp,
                _selectedSample is not null, target.State == PinnedProcessState.Present && _samplingError is null,
                _samplingError is not null ? Strings.Unavailable : ProcessRow.StateText(target.State));
        ProcessActions.Update(snapshot,
            snapshot?.Available == true && CanEndProcess(snapshot.Process),
            snapshot?.Available == true && CanDumpProcess(snapshot.Process));
        foreach (var row in _rows.Concat(_pinnedRows)) row.SetSelected(row.Represents(_processSelection.Contains));
    }

    private void OnProcessAction(string action, ProcessDetailsSnapshot snapshot)
    {
        if (_closing) return;
        var process = snapshot.Process;
        switch (action)
        {
            case "properties":
                new ProcessPropertiesWindow(snapshot, CopyProcessDetails) { Owner = this }.ShowDialog(); break;
            case "copy": CopyProcessDetails(snapshot.Format()); break;
            case "location": OpenProcessLocation(process); break;
            case "end" when snapshot is { Available: true, Historical: false } &&
                            snapshot.Grouped == _preferences.GroupProcesses:
                EndProcess(process); break;
            case "dump" when snapshot is { Available: true, Historical: false } &&
                             snapshot.Grouped == _preferences.GroupProcesses && CanDumpProcess(process):
                new ProcessDumpDialog(process, () => CanDumpProcess(process)) { Owner = this }.ShowDialog(); break;
        }
    }

    private bool CanSetProcessPriority(ProcessUsage process) =>
        ProcessPriorityChanger.IsEligible(process,
            !_closing && _selectedSample is null && _samplingError is null, _preferences.GroupProcesses);

    private void SetProcessPriority(ProcessUsage process, ProcessPriorityClass priority)
    {
        if (!CanSetProcessPriority(process)) return;
        if (ProcessPriorityChanger.Set(process, priority) is { } error)
            MessageDialog.ShowNotice(this, error, Strings.SetPriority);
    }

    private bool CanDumpProcess(ProcessUsage process)
    {
        return ProcessDumpWriter.IsEligible(process,
            !_closing && _selectedSample is null, _preferences.GroupProcesses);
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

    private void OpenProcessLocation(ProcessUsage process)
    {
        try
        {
            if (!File.Exists(process.ImagePath)) throw new IOException(Strings.ProcessFileMissing);
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "explorer.exe"))
                { UseShellExecute = false, Arguments = $"/select,\"{process.ImagePath}\"" };
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException
                                       or InvalidOperationException)
        {
            MessageDialog.ShowNotice(this, ex.Message, Strings.OpenFileLocation);
        }
    }
}
