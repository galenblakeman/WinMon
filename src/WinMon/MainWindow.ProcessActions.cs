using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
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
        System.Windows.Automation.AutomationProperties.SetName(ActivityActionsToggle, label);
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
        Volatile.Write(ref _retainPinned, pins is null && selected is null ? null :
            process => pins?.Invoke(process) == true || selected?.Invoke(process) == true);
    }

    private void RefreshProcessActions(IReadOnlyList<CpuSample> samples, bool grouped, Func<ProcessRanking> inclusive)
    {
        var hadSelection = _processSelection.HasSelection;
        var target = _processSelection.Resolve(samples, grouped, _selectedSample is not null, inclusive);
        if (hadSelection && !_processSelection.HasSelection) UpdateProcessRetention();
        ProcessDetailsSnapshot? snapshot = target is null ? null : new(target.Usage, grouped, samples.LastOrDefault()?.Timestamp,
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
            case "properties": new ProcessPropertiesWindow(snapshot, CopyProcessDetails) { Owner = this }.ShowDialog(); break;
            case "copy": CopyProcessDetails(snapshot.Format()); break;
            case "location": OpenProcessLocation(process); break;
            case "end" when snapshot.Available && !snapshot.Historical && snapshot.Grouped == _preferences.GroupProcesses:
                EndProcess(process); break;
            case "dump" when snapshot.Available && !snapshot.Historical && snapshot.Grouped == _preferences.GroupProcesses && CanDumpProcess(process):
                new ProcessDumpDialog(process, () => CanDumpProcess(process)) { Owner = this }.ShowDialog(); break;
        }
    }

    private bool CanDumpProcess(ProcessUsage process) => ProcessDumpWriter.IsEligible(process,
        !_closing && _selectedSample is null, _preferences.GroupProcesses);

    private void CopyProcessDetails(string text)
    {
        try { Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true); }
        catch (System.Runtime.InteropServices.ExternalException)
        { WinMon.Controls.MessageDialog.ShowNotice(this, Strings.TheClipboardIsUnavailableTryCopyAgain, Strings.CopyDetails); }
    }

    private void OpenProcessLocation(ProcessUsage process)
    {
        try
        {
            if (!File.Exists(process.ImagePath)) throw new IOException(Strings.ProcessFileMissing);
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
                { UseShellExecute = false, Arguments = $"/select,\"{process.ImagePath}\"" };
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        { WinMon.Controls.MessageDialog.ShowNotice(this, ex.Message, Strings.OpenFileLocation); }
    }
}
