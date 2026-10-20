using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>Shows a hardware snapshot without interrupting live recording.</summary>
internal sealed class HardwareInfoWindow : Window
{
    private readonly StackPanel _sections = new();
    private readonly Button _refresh = new() { Content = "Refresh", MinWidth = 90 };
    private bool _closed;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Ready => _ready.Task;

    internal HardwareInfoWindow()
    {
        Title = "WinMon hardware information";
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
        DockPanel.SetDock(_refresh, Dock.Right); footer.Children.Add(_refresh);
        var note = new TextBlock { Text = "Snapshot taken when opened or refreshed.", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        footer.Children.Add(note);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = _sections, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var surface = new Border { Child = layout };
        surface.SetResourceReference(Border.BackgroundProperty, "Surface");
        Content = surface;
        Loaded += async (_, _) => await RefreshAsync();
        _refresh.Click += async (_, _) => await RefreshAsync();
        Closed += (_, _) => _closed = true;
    }

    private async Task RefreshAsync()
    {
        if (!_refresh.IsEnabled) return;
        _refresh.IsEnabled = false;
        _sections.Children.Clear();
        _sections.Children.Add(new TextBlock { Text = "Reading hardware information…" });
        try
        {
            var sections = await Task.Run(WindowsHardwareInfo.Read);
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
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("Hardware information failed: {0}", ex);
            if (!_closed)
            {
                _sections.Children.Clear();
                _sections.Children.Add(new TextBlock { Text = "Hardware information could not be read. Try Refresh.", TextWrapping = TextWrapping.Wrap });
            }
        }
        finally { _refresh.IsEnabled = true; _ready.TrySetResult(); }
    }
}
