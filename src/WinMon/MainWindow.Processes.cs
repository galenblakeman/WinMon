using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
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
    }

    private void OnToggleActivityActions(object sender, RoutedEventArgs e)
    {
        _preferences = _preferences with { ProcessActionsHidden = !_preferences.ProcessActionsHidden };
        UpdateActivityActionsToggle();
    }

    private void UpdateActivityActionsToggle()
    {
        var hidden = _preferences.ProcessActionsHidden;
        Processes.ActionsHidden = hidden;
        ServiceView.ActionsHidden = hidden;
        var label = hidden ? Strings.ShowActions : Strings.HideActions;
        ActivityActionsToggle.Content = label;
        ButtonIcon.SetData(ActivityActionsToggle, hidden ? ButtonIcon.ChevronDown : ButtonIcon.ChevronUp);
        ActivityActionsToggle.ToolTip = label;
        AutomationProperties.SetName(ActivityActionsToggle, label);
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
