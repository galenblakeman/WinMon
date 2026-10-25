using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using WinMon.Preferences;

namespace WinMon.Controls;

/// <summary>A color-keyed series toggle with a live value, a detail line and a gauge; the tooltip carries the measurement definition.</summary>
public sealed class MetricReadout : Grid
{
    /// <summary>Every gauge style fits this height, so changing the style never moves the chart below.</summary>
    internal const double LayoutHeight = 92;

    public CheckBox Toggle { get; } = new() { IsChecked = true, FontSize = 14, Margin = new Thickness(0, 0, 0, 5) };

    public TextBlock ValueLabel { get; } = new()
        { Text = Strings.MissingReading, FontSize = 27, FontWeight = FontWeights.SemiBold };

    public TextBlock DetailLabel { get; } = new()
    {
        Text = Strings.Collecting, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(0, 3, 4, 3)
    };

    public MetricGauge Gauge { get; } = new() { Visibility = Visibility.Hidden, IsHitTestVisible = false };

    /// <summary>The upper end of an auto scale, shown with arc gauges because a dial's angle reads as absolute.</summary>
    public TextBlock ScaleLabel { get; } = new() { FontSize = 11, Visibility = Visibility.Collapsed };

    private readonly StackPanel _labels = new();
    private readonly StackPanel _gaugeColumn = new()
        { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly DockPanel _toggleRow = new() { LastChildFill = true };
    private readonly Grid _bowl = new();
    private GaugeStyle _style;
    private bool _valid;

    public MetricReadout()
    {
        Height = LayoutHeight;
        Gauge.SetResourceReference(MetricGauge.TrackProperty, "SectionHeaderSurface");
        Gauge.SetResourceReference(MetricGauge.OutlineProperty, "Line");
        Gauge.SetResourceReference(MetricGauge.MarkProperty, "Text");
        ScaleLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        DetailLabel.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        Lay(GaugeStyle.Bowl);
        SizeChanged += (_, _) => Fit();
    }

    /// <summary>The gauge drawing and the layout around it.</summary>
    public GaugeStyle GaugeStyle
    {
        get => _style;
        set
        {
            if (value != _style) Lay(value);
        }
    }

    private bool IsArc => _style is GaugeStyle.Needle or GaugeStyle.Bowl or GaugeStyle.SegmentedArc;

    /// <param name="current">The live reading, or <see langword="null"/> when it is unavailable.</param>
    /// <param name="peak">The highest reading in the last two minutes.</param>
    /// <param name="maximum">The physical value at the top of the scale.</param>
    /// <param name="scale">The formatted top of an auto scale, shown beside arc gauges.</param>
    public void ShowGauge(double? current, double? peak, double maximum, string? scale = null)
    {
        _valid = current is { } value && peak is { } high && maximum > 0 &&
                 double.IsFinite(value) && double.IsFinite(high) && double.IsFinite(maximum);
        ScaleLabel.Text = scale ?? "";
        ShowParts();
        // The gauge always draws fractions of its scale; the caller supplies the physical maximum.
        Gauge.Show(_valid ? Math.Clamp(current!.Value / maximum, 0, 1) : null,
            _valid ? Math.Clamp(peak!.Value / maximum, 0, 1) : 0);
    }

    private void ShowParts()
    {
        Gauge.Visibility = _valid && _style != GaugeStyle.None ? Visibility.Visible : Visibility.Hidden;
        // Arcs keep the label's space on every readout so all arcs share one size and position.
        ScaleLabel.Visibility = !IsArc ? Visibility.Collapsed
            : _valid && ScaleLabel.Text.Length > 0 ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>Place the labels and gauge for <paramref name="style"/>.</summary>
    private void Lay(GaugeStyle style)
    {
        _style = style;
        Children.Clear();
        _labels.Children.Clear();
        _gaugeColumn.Children.Clear();
        _toggleRow.Children.Clear();
        _bowl.Children.Clear();
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();
        Toggle.Margin = new Thickness(0, 0, 0, 5);
        ValueLabel.HorizontalAlignment = HorizontalAlignment.Left;
        ValueLabel.VerticalAlignment = VerticalAlignment.Top;
        ValueLabel.Margin = new Thickness();
        DetailLabel.HorizontalAlignment = HorizontalAlignment.Stretch;
        DetailLabel.Margin = new Thickness(0, 3, 4, 3);
        Gauge.Kind = style;
        Gauge.Margin = new Thickness();
        Gauge.Width = Gauge.Height = double.NaN;
        switch (style)
        {
            case GaugeStyle.Needle:
                // The value sits under the hub, and the detail line moves to the tile's tooltip, which already
                // carries it; that gives the half circle the tile's full height.
                RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                RowDefinitions.Add(new RowDefinition());
                RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddNameRow();
                SetRow(Gauge, 1);
                Children.Add(Gauge);
                ValueLabel.HorizontalAlignment = HorizontalAlignment.Center;
                // Pull the line box up by most of its leading; a needle lying flat at zero still clears the digits.
                ValueLabel.Margin = new Thickness(0, -3, 0, 0);
                SetRow(ValueLabel, 2);
                Children.Add(ValueLabel);
                break;
            case GaugeStyle.Bowl or GaugeStyle.SegmentedArc:
                RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                RowDefinitions.Add(new RowDefinition());
                RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddNameRow();
                ValueLabel.HorizontalAlignment = HorizontalAlignment.Center;
                ValueLabel.VerticalAlignment = VerticalAlignment.Bottom;
                _bowl.Children.Add(Gauge);
                _bowl.Children.Add(ValueLabel);
                SetRow(_bowl, 1);
                Children.Add(_bowl);
                DetailLabel.HorizontalAlignment = HorizontalAlignment.Center;
                DetailLabel.Margin = new Thickness(0, 2, 0, 0);
                SetRow(DetailLabel, 2);
                Children.Add(DetailLabel);
                break;
            case GaugeStyle.BottomBar or GaugeStyle.SegmentedBar:
                RowDefinitions.Add(new RowDefinition());
                RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddLabels();
                Gauge.Margin = new Thickness(0, 0, 4, 2);
                SetRow(Gauge, 1);
                Children.Add(Gauge);
                break;
            case GaugeStyle.None:
                AddLabels();
                break;
            default:
                ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                ColumnDefinitions.Add(new ColumnDefinition());
                _gaugeColumn.Children.Add(Gauge);
                Children.Add(_gaugeColumn);
                SetColumn(AddLabels(), 1);
                break;
        }

        ShowParts();
        Fit();
        return;

        StackPanel AddLabels()
        {
            _labels.Children.Add(Toggle);
            _labels.Children.Add(ValueLabel);
            _labels.Children.Add(DetailLabel);
            Children.Add(_labels);
            return _labels;
        }

        // Arc styles print the network scale at the right of the resource name.
        void AddNameRow()
        {
            Toggle.Margin = new Thickness();
            DockPanel.SetDock(ScaleLabel, Dock.Right);
            ScaleLabel.VerticalAlignment = VerticalAlignment.Center;
            ScaleLabel.Margin = new Thickness(4, 0, 0, 0);
            _toggleRow.Children.Add(ScaleLabel);
            _toggleRow.Children.Add(Toggle);
            Children.Add(_toggleRow);
        }
    }

    /// <summary>Size the gauge and text for the tile's width.</summary>
    private void Fit()
    {
        // Six resources can share the minimum-width window. The value takes the smaller font whenever its column
        // is too narrow for a rate and its unit at the larger one. Arcs always take it: the needle leaves its half
        // circle the most height that way, and a bowl's value fits inside a nearly round arc.
        // Before the first layout there is no width to fit; the larger sizes stand until then.
        var width = ActualWidth > 0 ? ActualWidth : double.PositiveInfinity;
        var narrow = IsArc || width - 27 < 118;
        ValueLabel.FontSize = narrow ? 22 : 27;
        _unit.FontSize = narrow ? 13 : 16;
        DetailLabel.FontSize = narrow ? 13 : 14;
        switch (_style)
        {
            case GaugeStyle.LeftBar:
                Gauge.Width = 22;
                Gauge.Height = 76;
                break;
            case GaugeStyle.BottomBar or GaugeStyle.SegmentedBar:
                Gauge.Height = 8;
                break;
        }
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
            Gauge.SetResourceReference(MetricGauge.FillProperty, value);
        }
    }
}
