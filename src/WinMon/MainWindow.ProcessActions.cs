using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    private void EnableProcessInspection(ProcessRow row) => row.EnableInspectionActions(
        snapshot => new ProcessPropertiesWindow(snapshot, CopyProcessDetails) { Owner = this }.ShowDialog(),
        snapshot => CopyProcessDetails(snapshot.Format()), OpenProcessLocation,
        process => new ProcessDumpDialog(process, () => CanDumpProcess(process)) { Owner = this }.ShowDialog(),
        CanDumpProcess, () => _selectedSample is not null);

    private bool CanDumpProcess(ProcessUsage process) => ProcessDumpWriter.IsEligible(process,
        !_closing && _selectedSample is null, _preferences.GroupProcesses);

    private void CopyProcessDetails(string text)
    {
        try { Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true); }
        catch (System.Runtime.InteropServices.ExternalException)
        { MessageBox.Show(this, Strings.TheClipboardIsUnavailableTryCopyAgain, Strings.CopyDetails, MessageBoxButton.OK, MessageBoxImage.Information); }
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
        { MessageBox.Show(this, ex.Message, Strings.OpenFileLocation, MessageBoxButton.OK, MessageBoxImage.Information); }
    }
}
