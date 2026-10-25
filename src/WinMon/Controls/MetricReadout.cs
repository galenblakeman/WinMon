using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MicroGauge.Wpf;

namespace WinMon.Controls;

/// <summary>A color-keyed series toggle with a live value and detail line; the tooltip carries the measurement definition.</summary>
public sealed class MetricReadout : Grid
{
    public CheckBox Toggle { get; } = new() { IsChecked = true, FontSize = 14, Margin = new Thickness(0, 0, 0, 5) };

    public TextBlock ValueLabel { get; } = new()
        { Text = Strings.MissingReading, FontSize = 27, FontWeight = FontWeights.SemiBold };

    public TextBlock DetailLabel { get; } = new()
    {
        Text = Strings.Collecting, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(0, 3, 4, 3)
    };

    public WpfGaugeLinear Gauge { get; } = new()
    {
        Width = 22, Height = 76, IsVertical = true, MaxValue = 100,
        ValueWidthExtent = 1, TopExtent = 0.04f, BottomExtent = 0.04f,
        LeftExtent = 0.02f, RightExtent = 0.02f, BackingStrokeWidth = 0,
        TickInterval = float.MaxValue, MinorTickInterval = float.MaxValue,
        LabelInterval = float.MaxValue,
        NeedleBrush = Brushes.Transparent, ValueFontBrush = Brushes.Transparent,
        LabelFontBrush = Brushes.Transparent, SetNeedleBrush = Brushes.Transparent,
        Visibility = Visibility.Hidden, IsHitTestVisible = false
    };

    private readonly Border _peakLine = new()
    {
        Width = 22, Height = 2, IsHitTestVisible = false,
        Visibility = Visibility.Hidden
    };

    public MetricReadout()
    {
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labels = new StackPanel();
        labels.Children.Add(Toggle);
        labels.Children.Add(ValueLabel);
        labels.Children.Add(DetailLabel);
        Children.Add(labels);
        var rail = new Canvas { Width = 22, Height = 76, Margin = new Thickness(2, 0, 3, 0),
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        rail.Children.Add(Gauge);
        rail.Children.Add(_peakLine);
        SetColumn(rail, 1);
        Children.Add(rail);
        Gauge.SetResourceReference(WpfGaugeLinear.BackingBrushProperty, "SectionHeaderSurface");
        Gauge.SetResourceReference(WpfGaugeLinear.BackingOutlineProperty, "Line");
        _peakLine.SetResourceReference(Border.BackgroundProperty, "Text");
        DetailLabel.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        SizeChanged += (_, _) =>
        {
            // Six resources can share the minimum-width window; retain the rate and its unit beside the rail.
            var narrow = ActualWidth < 145;
            ValueLabel.FontSize = narrow ? 22 : 27;
            _unit.FontSize = narrow ? 13 : 16;
            DetailLabel.FontSize = narrow ? 13 : 14;
        };
    }

    public void ShowGauge(double? current, double? peak, double maximum)
    {
        var valid = current is { } value && peak is { } high && maximum > 0 &&
                    double.IsFinite(value) && double.IsFinite(high) && double.IsFinite(maximum);
        Gauge.Visibility = valid ? Visibility.Visible : Visibility.Hidden;
        _peakLine.Visibility = Gauge.Visibility;
        if (!valid) return;
        // The instrument always draws on a 0–100 scale; the caller supplies the physical maximum.
        // Keeping the drawing scale bounded also avoids excessive tick work for bit-per-second values.
        Gauge.Value = Math.Clamp(current!.Value / maximum * 100, 0, 100);
        var peakRatio = Math.Clamp(peak!.Value / maximum, 0, 1);
        Canvas.SetTop(_peakLine, 76 * (0.04 + 0.92 * (1 - peakRatio)) - 1);
    }

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string),
        typeof(MetricReadout), new PropertyMetadata("", (sender, args) =>
        {
            var readout = (MetricReadout)sender;
            readout.Toggle.Content = args.NewValue;
            AutomationProperties.SetName(readout.Toggle, Strings.Format(Strings.ShowOnChart, args.NewValue));
        }));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    internal void RefreshLanguage()
    {
        AutomationProperties.SetName(Toggle, Strings.Format(Strings.ShowOnChart, Label));
    }

    /// <summary>Show a number with a smaller unit so rates fit the same column as percentages.</summary>
    public void ShowValue(string number, string unit)
    {
        // Setting Text replaces the runs, so check they are still the label's before reusing them.
        if (ValueLabel.Inlines.Count != 2 || ValueLabel.Inlines.FirstInline != _number ||
            ValueLabel.Inlines.LastInline != _unit)
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

    public string AccentKey
    {
        set
        {
            Toggle.SetResourceReference(Control.ForegroundProperty, value);
            ValueLabel.SetResourceReference(TextBlock.ForegroundProperty, value);
            Gauge.SetResourceReference(WpfGaugeLinear.ValueBarBrushProperty, value);
        }
    }
}
