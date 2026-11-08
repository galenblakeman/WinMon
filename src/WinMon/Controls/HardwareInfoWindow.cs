using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using WinMon.Diagnostics;
using WinMon.Monitoring;
using WinMon.Updates;

namespace WinMon.Controls;

/// <summary>Hardware snapshot, task guide and product/update information.</summary>
internal sealed partial class HardwareInfoWindow : Window
{
    private readonly Action<string> _copyToClipboard;
    private string _snapshotText = "";
    private bool _closed;
    private bool _checking;
    private bool _updateBusy;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Ready => _ready.Task;
    internal event Func<Task>? CheckForUpdatesRequested;
    internal event Action? UpdateRequested;

    internal HardwareInfoWindow(Action<string>? copyToClipboard = null)
    {
        InitializeComponent();
        _copyToClipboard = copyToClipboard ??
                           (text => Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true));
        WindowTheme.Attach(this);
        VersionText.Text = Strings.Format(Strings.ProductVersion, AppInfo.Version);
        DescriptionText.Text = AppInfo.Description;
        LicenseText.Text = Strings.Format(Strings.License, AppInfo.Copyright, AppInfo.License);
        if (Uri.TryCreate(AppInfo.RepositoryUrl, UriKind.Absolute, out var repository))
        {
            RepositoryLink.NavigateUri = repository;
            RepositoryLink.Inlines.Add(new Run(repository.ToString()));
        }

        if (!UpdatePolicy.GitHubUpdatesEnabled) CheckButton.Visibility = Visibility.Collapsed;
        SetUpdateState(Strings.SettingsNotChecked, false);
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) => _closed = true;
    }

    internal void SetUpdateState(string status, bool available, bool busy = false)
    {
        if (_closed) return;
        UpdateStatusText.Text = UpdatePolicy.GitHubUpdatesEnabled ? status : Strings.InfoStoreUpdates;
        UpdateButton.Visibility = available && UpdatePolicy.GitHubUpdatesEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;
        _updateBusy = busy;
        CheckButton.IsEnabled = !_checking && !busy;
        UpdateButton.IsEnabled = !_checking && !busy;
    }

    private async void OnCheckForUpdates(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync();
    }

    internal async Task CheckForUpdatesAsync()
    {
        if (!CheckButton.IsEnabled || !UpdatePolicy.GitHubUpdatesEnabled) return;
        _checking = true;
        CheckButton.IsEnabled = false;
        UpdateButton.IsEnabled = false;
        UpdateStatusText.Text = Strings.UpdateChecking;
        try
        {
            if (CheckForUpdatesRequested is { } check) await check();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("WinMon Info update check: {0}", ex.Message);
            if (!_closed) UpdateStatusText.Text = Strings.UpdateFailed;
        }
        finally
        {
            _checking = false;
            CheckButton.IsEnabled = !_updateBusy;
            UpdateButton.IsEnabled = !_updateBusy;
        }
    }

    private void OnUpdate(object sender, RoutedEventArgs e)
    {
        if (!UpdateButton.IsEnabled || !UpdatePolicy.GitHubUpdatesEnabled) return;
        Close();
        UpdateRequested?.Invoke();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = DiagnosticLog.AppFolder;
            if (folder is null) throw new InvalidOperationException("Logging is unavailable.");
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException
                                       or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Logging.OpenFolderFailed: {0}", ex);
            MessageDialog.ShowNotice(this, Strings.OpenLogsFolderFailed, Strings.OpenLogsFolder);
        }
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        CopyInformation();
    }

    private void OnRepositoryNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Trace.TraceWarning("Could not open {0}: {1}", e.Uri, ex.Message);
        }

        e.Handled = true;
    }

    private async Task RefreshAsync()
    {
        if (!RefreshButton.IsEnabled) return;
        RefreshButton.IsEnabled = false;
        CopyButton.IsEnabled = false;
        CopyButton.Content = Strings.Copy;
        _snapshotText = "";
        SnapshotNote.Text = Strings.ReadingHardwareInformation;
        Sections.Children.Clear();
        Sections.Children.Add(new TextBlock { Text = Strings.ReadingHardwareInformation });
        try
        {
            var sections = await Task.Run(WindowsHardwareInfo.Read);
            var capturedAt = DateTimeOffset.Now;
            if (_closed) return;
            Sections.Children.Clear();
            foreach (var section in sections)
            {
                var details = new TextBox
                {
                    Text = section.Details, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                    BorderThickness = new Thickness(0), Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 20)
                };
                details.SetResourceReference(BackgroundProperty, "Surface");
                details.SetResourceReference(ForegroundProperty, "Text");
                AutomationProperties.SetName(details, section.Name);
                var content = new StackPanel();
                content.Children.Add(details);
                var expander = new Expander
                {
                    Header = section.Name, Tag = section.Summary, Content = content,
                    IsExpanded = section == sections[0]
                };
                AutomationProperties.SetHelpText(expander, section.Summary);
                Sections.Children.Add(expander);
                if (section.Items is not { Count: > 0 }) continue;
                details.Margin = new Thickness(0, 0, 0, 8);
                var items = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
                foreach (var item in section.Items)
                {
                    var title = new TextBlock
                        { Text = item.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
                    var text = new TextBox
                    {
                        Text = item.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                        BorderThickness = new Thickness(0), Padding = new Thickness(0),
                        Margin = new Thickness(0, 4, 0, 8)
                    };
                    text.SetResourceReference(BackgroundProperty, "Surface");
                    text.SetResourceReference(ForegroundProperty, "Text");
                    AutomationProperties.SetName(text, item.Name);
                    items.Children.Add(title);
                    items.Children.Add(text);
                }

                content.Children.Add(items);
            }

            _snapshotText = HardwareInfoText.Format(sections, capturedAt);
            SnapshotNote.Text = Strings.Format(Strings.HardwareSnapshotTime, capturedAt);
            CopyButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Trace.TraceError("Hardware information failed: {0}", ex);
            if (!_closed)
            {
                Sections.Children.Clear();
                SnapshotNote.Text = Strings.SnapshotUnavailableTryRefresh;
                Sections.Children.Add(new TextBlock
                    { Text = Strings.HardwareInformationCouldNotBeReadTryRefresh, TextWrapping = TextWrapping.Wrap });
            }
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            _ready.TrySetResult();
        }
    }

    private void CopyInformation()
    {
        if (!CopyButton.IsEnabled || _snapshotText.Length == 0) return;
        try
        {
            _copyToClipboard(_snapshotText);
            CopyButton.Content = Strings.Copied;
        }
        catch (ExternalException ex)
        {
            Trace.TraceWarning("Could not copy hardware information: {0}", ex.Message);
            CopyButton.Content = Strings.Copy;
            MessageDialog.ShowNotice(this, Strings.TheClipboardIsUnavailableTryCopyAgain, Strings.CopyInformation);
        }
    }
}