using System.Windows;
using System.Windows.Controls;

namespace WinMon.Controls;

/// <summary>Themed confirmation and outcome dialogs for process actions.</summary>
internal sealed class ProcessActionDialog : Window
{
    internal ProcessActionDialog(string message, bool confirmation)
    {
        Title = Strings.EndProcess;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height * 0.8;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowTheme.Attach(this);
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Text");

        var layout = new DockPanel { Margin = new Thickness(24) };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        if (confirmation)
        {
            var end = new Button { Content = Strings.EndProcess, MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
            end.Click += (_, _) => DialogResult = true;
            buttons.Children.Add(end);
        }
        var close = new Button
        {
            Content = confirmation ? Strings.Cancel : Strings.Close,
            MinWidth = 90, IsCancel = true, IsDefault = true
        };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        Loaded += (_, _) => close.Focus();
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        layout.Children.Add(new ScrollViewer
        {
            Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = SystemParameters.WorkArea.Height * 0.8 - 160
        });
        var surface = new Border { Child = layout };
        surface.SetResourceReference(Border.BackgroundProperty, "Surface");
        Content = surface;
    }
}
