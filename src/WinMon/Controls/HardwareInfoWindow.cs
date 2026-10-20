using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Documents;
using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>Shows the version and a hardware snapshot without interrupting live recording.</summary>
internal sealed class HardwareInfoWindow : Window
{
    private readonly StackPanel _sections = new();
    private readonly Button _refresh = new() { Content = "Refresh", MinWidth = 90 };
    private readonly Button _copy = new() { Content = "Copy", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsEnabled = false,
        ToolTip = "Copy all information, including collapsed details, as plain text" };
    private readonly Action<string> _copyToClipboard;
    private readonly TextBlock _note = new() { Text = "Snapshot taken when opened or refreshed.", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private string _snapshotText = "";
    private bool _closed;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Ready => _ready.Task;

    internal HardwareInfoWindow(Action<string>? copyToClipboard = null)
    {
        _copyToClipboard = copyToClipboard ?? (text => Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true));
        Title = "About WinMon";
        Width = 600; Height = 650; MinWidth = 420; MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowTheme.Attach(this);
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Text");
        var layout = new DockPanel { Margin = new Thickness(24) };
        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right); footer.Children.Add(close);
        DockPanel.SetDock(_copy, Dock.Right); footer.Children.Add(_copy);
        DockPanel.SetDock(_refresh, Dock.Right); footer.Children.Add(_refresh);
        _note.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        footer.Children.Add(_note);
        AutomationProperties.SetName(_copy, "Copy all information");
        _copy.Click += (_, _) => CopyInformation();
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var about = CreateAbout();
        DockPanel.SetDock(about, Dock.Top); layout.Children.Add(about);
        layout.Children.Add(new ScrollViewer { Content = _sections, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var surface = new Border { Child = layout };
        surface.SetResourceReference(Border.BackgroundProperty, "Surface");
        Content = surface;
        Loaded += async (_, _) => await RefreshAsync();
        _refresh.Click += async (_, _) => await RefreshAsync();
        Closed += (_, _) => _closed = true;
    }

    /// <summary>Version, copyright, license and source link, above the hardware sections.</summary>
    private static StackPanel CreateAbout()
    {
        var about = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        var heading = new TextBlock { Text = $"WinMon {AppInfo.Version}", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        about.Children.Add(heading);
        about.Children.Add(new TextBlock { Text = AppInfo.Description, TextWrapping = TextWrapping.Wrap });
        var notice = new TextBlock { Text = $"{AppInfo.Copyright} · {AppInfo.License} License", TextWrapping = TextWrapping.Wrap };
        notice.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        about.Children.Add(notice);
        if (Uri.TryCreate(AppInfo.RepositoryUrl, UriKind.Absolute, out var repository))
        {
            var link = new Hyperlink(new Run(repository.ToString())) { NavigateUri = repository };
            link.SetResourceReference(TextElement.ForegroundProperty, "Accent");
            AutomationProperties.SetName(link, "WinMon source code on GitHub");
            link.RequestNavigate += (_, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    System.Diagnostics.Trace.TraceWarning("Could not open {0}: {1}", e.Uri, ex.Message);
                }
                e.Handled = true;
            };
            about.Children.Add(new TextBlock(link) { Margin = new Thickness(0, 2, 0, 0) });
        }
        return about;
    }

    private async Task RefreshAsync()
    {
        if (!_refresh.IsEnabled) return;
        _refresh.IsEnabled = false;
        _copy.IsEnabled = false;
        _copy.Content = "Copy";
        _snapshotText = "";
        _note.Text = "Reading hardware information…";
        _sections.Children.Clear();
        _sections.Children.Add(new TextBlock { Text = "Reading hardware information…" });
        try
        {
            var sections = await Task.Run(WindowsHardwareInfo.Read);
            var capturedAt = DateTimeOffset.Now;
            if (_closed) return;
            _sections.Children.Clear();
            foreach (var section in sections)
            {
                var heading = new TextBlock { Text = section.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
                heading.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                var details = new TextBox { Text = section.Details, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                    BorderThickness = new Thickness(0), Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 20) };
                details.SetResourceReference(BackgroundProperty, "Surface");
                details.SetResourceReference(ForegroundProperty, "Text");
                AutomationProperties.SetName(details, section.Name);
                _sections.Children.Add(heading); _sections.Children.Add(details);
                if (section.Items is { Count: > 0 })
                {
                    details.Margin = new Thickness(0, 0, 0, 8);
                    var items = new StackPanel { Margin = new Thickness(12, 8, 0, 0) };
                    foreach (var item in section.Items)
                    {
                        var title = new TextBlock { Text = item.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
                        var text = new TextBox { Text = item.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                            BorderThickness = new Thickness(0), Padding = new Thickness(0), Margin = new Thickness(0, 4, 0, 16) };
                        text.SetResourceReference(BackgroundProperty, "Surface");
                        text.SetResourceReference(ForegroundProperty, "Text");
                        AutomationProperties.SetName(text, item.Name);
                        items.Children.Add(title); items.Children.Add(text);
                    }
                    var expander = new Expander { Header = $"{section.DetailHeading} ({section.Items.Count})", Content = items, Margin = new Thickness(0, 0, 0, 20) };
                    expander.SetResourceReference(ForegroundProperty, "Text");
                    AutomationProperties.SetName(expander, section.DetailHeading);
                    _sections.Children.Add(expander);
                }
            }
            _snapshotText = HardwareInfoText.Format(sections, capturedAt);
            _note.Text = $"Snapshot: {capturedAt:HH:mm:ss zzz}";
            _copy.IsEnabled = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("Hardware information failed: {0}", ex);
            if (!_closed)
            {
                _sections.Children.Clear();
                _note.Text = "Snapshot unavailable. Try Refresh.";
                _sections.Children.Add(new TextBlock { Text = "Hardware information could not be read. Try Refresh.", TextWrapping = TextWrapping.Wrap });
            }
        }
        finally { _refresh.IsEnabled = true; _ready.TrySetResult(); }
    }

    private void CopyInformation()
    {
        if (!_copy.IsEnabled || _snapshotText.Length == 0) return;
        try
        {
            _copyToClipboard(_snapshotText);
            _copy.Content = "Copied";
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            System.Diagnostics.Trace.TraceWarning("Could not copy hardware information: {0}", ex.Message);
            _copy.Content = "Copy";
            MessageBox.Show(this, "The clipboard is unavailable. Try Copy again.", "Copy information", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
