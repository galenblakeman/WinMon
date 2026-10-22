using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace WinMon.Controls;

/// <summary>A color-keyed series toggle with a live value and detail line; the tooltip carries the measurement definition.</summary>
public sealed class MetricReadout : StackPanel
{
    public CheckBox Toggle { get; } = new() { IsChecked = true, FontSize = 14, Margin = new Thickness(0, 0, 0, 5) };
    public TextBlock ValueLabel { get; } = new() { Text = Strings.MissingReading, FontSize = 27, FontWeight = FontWeights.SemiBold };
    public TextBlock DetailLabel { get; } = new() { Text = Strings.Collecting, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 4, 3) };
    public MetricReadout()
    {
        Children.Add(Toggle); Children.Add(ValueLabel); Children.Add(DetailLabel);
        DetailLabel.SetResourceReference(TextBlock.ForegroundProperty, "Text");
    }
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string),
        typeof(MetricReadout), new PropertyMetadata("", (sender, args) =>
        {
            var readout = (MetricReadout)sender;
            readout.Toggle.Content = args.NewValue;
            System.Windows.Automation.AutomationProperties.SetName(readout.Toggle, Strings.Format(Strings.ShowOnChart, args.NewValue));
        }));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    internal void RefreshLanguage() =>
        System.Windows.Automation.AutomationProperties.SetName(Toggle, Strings.Format(Strings.ShowOnChart, Label));
    /// <summary>Show a number with a smaller unit so rates fit the same column as percentages.</summary>
    public void ShowValue(string number, string unit)
    {
        // Setting Text replaces the runs, so check they are still the label's before reusing them.
        if (ValueLabel.Inlines.Count != 2 || ValueLabel.Inlines.FirstInline != _number || ValueLabel.Inlines.LastInline != _unit)
        {
            ValueLabel.Inlines.Clear();
            ValueLabel.Inlines.Add(_number);
            ValueLabel.Inlines.Add(_unit);
        }
        // Unchanged text leaves the runs, and so the label's layout, as they are.
        _number.Text = number;
        _unit.Text = " " + unit;
    }

    private readonly Run _number = new();
    private readonly Run _unit = new() { FontSize = 16 };
    public string AccentKey { set { Toggle.SetResourceReference(Control.ForegroundProperty, value); ValueLabel.SetResourceReference(TextBlock.ForegroundProperty, value); } }
}
