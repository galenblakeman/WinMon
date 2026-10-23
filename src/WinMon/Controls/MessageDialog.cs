using System.Windows;
using System.Windows.Controls;

namespace WinMon.Controls;

/// <summary>Application-styled notices and confirmations; file pickers and UAC are OS-owned.</summary>
internal class MessageDialog : Window
{
    internal MessageDialog(string title, string message, string? confirmLabel = null)
    {
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        MaxWidth = SystemParameters.WorkArea.Width;
        MaxHeight = SystemParameters.WorkArea.Height * 0.8;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowTheme.Attach(this);
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Text");
        var layout = new DockPanel { Margin = new Thickness(24) };
        var buttons = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        if (confirmLabel is not null)
        {
            var confirm = new Button { Content = confirmLabel, MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
            confirm.Click += (_, _) => DialogResult = true;
            buttons.Children.Add(confirm);
        }
        var close = new Button
        {
            Content = confirmLabel is null ? Strings.Close : Strings.Cancel,
            MinWidth = 90, IsCancel = true, IsDefault = true
        };
        ButtonIcon.SetData(close, ButtonIcon.Cancel);
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
            MaxHeight = Math.Max(40, MaxHeight - 160)
        });
        var surface = new Border { Child = layout };
        surface.SetResourceReference(Border.BackgroundProperty, "Surface");
        Content = surface;
    }

    internal static void ShowNotice(Window? owner, string message, string title) => Show(owner, title, message);

    internal static bool Confirm(Window owner, string message, string title, string confirmLabel) =>
        Show(owner, title, message, confirmLabel);

    private static bool Show(Window? owner, string title, string message, string? confirmLabel = null)
    {
        var dialog = new MessageDialog(title, message, confirmLabel);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }
}
