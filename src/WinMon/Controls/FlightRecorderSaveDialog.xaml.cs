using System.Windows;
using Microsoft.Win32;

namespace WinMon.Controls;

internal sealed partial class FlightRecorderSaveDialog : Window
{
    internal (string Path, int SecondsBeforeAndAfter, bool Overwrite)? Choice { get; private set; }

    internal FlightRecorderSaveDialog(string selectedMoment)
    {
        InitializeComponent();
        WindowTheme.Attach(this);
        SelectedMoment.Text = selectedMoment;
        Range.SelectedIndex = 0;
        FileNameBox.Text = $"WinMon-{DateTime.Now:yyyyMMdd-HHmmss}.wmincident";
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        LocationPath.Text = Directory.Exists(documents) ? documents : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private int SecondsBeforeAndAfter => Range.SelectedIndex switch { 0 => 30, 1 => 60, 2 => 300, _ => 0 };

    private void OnChangeLocation(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = Strings.FlightRecorderSaveLocation, InitialDirectory = LocationPath.Text };
        if (picker.ShowDialog(this) == true)
        {
            LocationPath.Text = picker.FolderName;
            ErrorText.Visibility = Visibility.Collapsed;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var fileName = FileNameBox.Text.Trim();
        if (fileName.Length == 0 || fileName.EndsWith('.') || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ShowError(Strings.FlightRecorderInvalidFileName);
            FileNameBox.Focus();
            FileNameBox.SelectAll();
            return;
        }
        if (!Directory.Exists(LocationPath.Text))
        {
            ShowError(Strings.FlightRecorderSaveFolderMissing);
            return;
        }

        var path = Path.Combine(LocationPath.Text, fileName.EndsWith(".wmincident", StringComparison.OrdinalIgnoreCase)
            ? fileName : fileName + ".wmincident");
        var overwrite = File.Exists(path);
        if (overwrite && !MessageDialog.Confirm(this, Strings.Format(Strings.FlightRecorderReplaceFile, Path.GetFileName(path)),
                Title, Strings.FlightRecorderReplace)) return;

        Choice = (path, SecondsBeforeAndAfter, overwrite);
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
