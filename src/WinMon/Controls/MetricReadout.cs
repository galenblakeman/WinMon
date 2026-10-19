using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace WinMon.Controls;

/// <summary>A color-keyed series toggle with a live value and measurement definition.</summary>
public sealed class MetricReadout : StackPanel
{
    public CheckBox Toggle { get; } = new() { IsChecked = true, FontSize = 14, Margin = new Thickness(0, 0, 0, 5) };
    public TextBlock ValueLabel { get; } = new() { Text = "—", FontSize = 34, FontWeight = FontWeights.SemiBold };
    public TextBlock DetailLabel { get; } = new() { Text = "Collecting…", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 4, 3) };
    private readonly TextBlock caption = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 4, 0) };
    public MetricReadout()
    {
        Children.Add(Toggle); Children.Add(ValueLabel); Children.Add(DetailLabel); Children.Add(caption);
        DetailLabel.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        caption.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
    }
    public string Label { get => Toggle.Content?.ToString() ?? ""; set { Toggle.Content = value; System.Windows.Automation.AutomationProperties.SetName(Toggle, $"Show {value} on chart"); } }
    public string Caption { get => caption.Text; set => caption.Text = value; }
    /// <summary>Show a number with a smaller unit so rates fit the same column as percentages.</summary>
    public void ShowValue(string number, string unit)
    {
        ValueLabel.Inlines.Clear();
        ValueLabel.Inlines.Add(new Run(number));
        ValueLabel.Inlines.Add(new Run(" " + unit) { FontSize = 16 });
    }
    public string AccentKey { set { Toggle.SetResourceReference(CheckBox.ForegroundProperty, value); ValueLabel.SetResourceReference(TextBlock.ForegroundProperty, value); } }
}
