using System.Windows;
using System.Windows.Controls;
using WinMon.Monitoring;

namespace WinMon.Controls;

internal sealed class ProcessPropertiesWindow : Window
{
    internal ProcessPropertiesWindow(ProcessDetailsSnapshot snapshot, Action<string> copy)
    {
        Title = $"{Strings.ProcessProperties} — {snapshot.Process.Name}";
        Width = 620; Height = 580; MinWidth = 380; MinHeight = 300;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowTheme.Attach(this);
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Text");
        var layout = new DockPanel { Margin = new Thickness(24) };
        var heading = new TextBlock { Text = string.IsNullOrWhiteSpace(snapshot.Process.AppName) ? snapshot.Process.Name : snapshot.Process.AppName,
            FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0) };
        var text = snapshot.Format();
        var copyButton = new Button { Content = Strings.CopyDetails, MinWidth = 110 };
        ButtonIcon.SetData(copyButton, ButtonIcon.Copy);
        copyButton.Click += (_, _) => copy(text);
        buttons.Children.Add(copyButton);
        var close = new Button { Content = Strings.Close, IsCancel = true, IsDefault = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom); layout.Children.Add(buttons);
        var details = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 14,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
        details.SetResourceReference(BackgroundProperty, "Surface");
        details.SetResourceReference(ForegroundProperty, "Text");
        System.Windows.Automation.AutomationProperties.SetName(details, Strings.ProcessProperties);
        layout.Children.Add(details);
        Content = layout;
    }
}
