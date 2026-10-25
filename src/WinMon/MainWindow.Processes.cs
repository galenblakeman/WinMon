using System.Runtime.InteropServices;
using System.Windows;
using WinMon.Controls;

namespace WinMon;

public partial class MainWindow
{
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
