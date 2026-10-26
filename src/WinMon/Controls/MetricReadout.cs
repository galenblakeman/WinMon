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
    internal const double LayoutHeight = 98;

    private const double ArcHeaderGap = 6;
    private const double CornerRightInset = 12;

    public CheckBox Toggle { get; } = new() { IsChecked = true, FontSize = 14, Margin = new Thickness(0, 0, 0, 5) };

    private readonly TextBlock _plainName = new() { FontSize = 14, Margin = new Thickness(0, 0, 0, 5) };

    /// <summary>Show a resource name instead of a chart-series checkbox when there is no chart.</summary>
    public bool ShowSeriesToggle
    {
        get => _showSeriesToggle;
        set
        {
            if (_showSeriesToggle == value) return;
            _showSeriesToggle = value;
            Lay(_style);
        }
    }

    private bool _showSeriesToggle = true;

    public TextBlock ValueLabel { get; } = new()
        { Text = Strings.MissingReading, FontSize = 27, FontWeight = FontWeights.SemiBold };

    public TextBlock DetailLabel { get; } = new()
    {
        Text = Strings.Collecting, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(0, 3, 4, 3)
    };

    public MetricGauge Gauge { get; } = new() { Visibility = Visibility.Hidden, IsHitTestVisible = false };

    /// <summary>A compact context label beside the name on arc gauges: peak or network scale.</summary>
    public TextBlock ScaleLabel { get; } = new() { FontSize = 11, Visibility = Visibility.Collapsed };

    private readonly StackPanel _labels = new();
    private readonly StackPanel _gaugeColumn = new()
        { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly DockPanel _toggleRow = new() { LastChildFill = true };
    private readonly Grid _bowl = new();
    private GaugeStyle _style;
    private bool _valid;
    private string _fullCornerLabel = "";
    private string _shortCornerLabel = "";

    public MetricReadout()
    {
        Height = LayoutHeight;
        Gauge.SetResourceReference(MetricGauge.TrackProperty, "SectionHeaderSurface");
        Gauge.SetResourceReference(MetricGauge.OutlineProperty, "GaugeTrack");
        Gauge.SetResourceReference(MetricGauge.MarkProperty, "Text");
        ScaleLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        DetailLabel.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        Lay(GaugeStyle.Bowl);
        SizeChanged += (_, _) => { Fit(); UpdateCornerLabel(); };
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
    /// <param name="scale">Context beside an arc's resource name; hidden for non-arc styles.</param>
    /// <param name="shortScale">A shorter version for narrow tiles.</param>
    public void ShowGauge(double? current, double? peak, double maximum, string? scale = null,
        string? shortScale = null)
    {
        _valid = current is { } value && peak is { } high && maximum > 0 &&
                 double.IsFinite(value) && double.IsFinite(high) && double.IsFinite(maximum);
        _fullCornerLabel = scale ?? "";
        _shortCornerLabel = shortScale ?? _fullCornerLabel;
        UpdateCornerLabel();
        ShowParts();
        // The gauge always draws fractions of its scale; the caller supplies the physical maximum.
        Gauge.Show(_valid ? Math.Clamp(current!.Value / maximum, 0, 1) : null,
            _valid ? Math.Clamp(peak!.Value / maximum, 0, 1) : 0);
    }

    private void UpdateCornerLabel()
    {
        // Full and Compact tiles use different outer margins. Keep the label's right edge the same
        // distance from the tile edge rather than from the readout's inner edge.
        var rightMargin = Math.Max(0, CornerRightInset - Margin.Right);
        if (ScaleLabel.Margin.Right != rightMargin)
            ScaleLabel.Margin = new Thickness(4, 0, rightMargin, 0);
        var label = ActualWidth is > 0 and < 155 ? _shortCornerLabel : _fullCornerLabel;
        if (ScaleLabel.Text != label) ScaleLabel.Text = label;
        if (ScaleLabel.ToolTip is not string tooltip || tooltip != _fullCornerLabel)
        {
            ScaleLabel.ToolTip = _fullCornerLabel;
            AutomationProperties.SetName(ScaleLabel, _fullCornerLabel);
        }
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
        _plainName.Margin = new Thickness(0, 0, 0, 5);
        ValueLabel.HorizontalAlignment = HorizontalAlignment.Left;
        ValueLabel.VerticalAlignment = VerticalAlignment.Top;
        ValueLabel.Margin = new Thickness();
        DetailLabel.HorizontalAlignment = HorizontalAlignment.Stretch;
        DetailLabel.Margin = new Thickness(0, 3, 4, 3);
        Gauge.Kind = style;
        Gauge.Margin = new Thickness();
        _bowl.Margin = new Thickness();
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
                Gauge.Margin = new Thickness(0, ArcHeaderGap, 0, 0);
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
                _bowl.Margin = new Thickness(0, ArcHeaderGap, 0, 0);
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

        UpdateCornerLabel();
        ShowParts();
        Fit();
        return;

        StackPanel AddLabels()
        {
            _labels.Children.Add(_showSeriesToggle ? Toggle : _plainName);
            _labels.Children.Add(ValueLabel);
            _labels.Children.Add(DetailLabel);
            Children.Add(_labels);
            return _labels;
        }

        // Arc styles print a short metric context at the right of the resource name.
        void AddNameRow()
        {
            Toggle.Margin = new Thickness();
            _plainName.Margin = new Thickness();
            DockPanel.SetDock(ScaleLabel, Dock.Right);
            ScaleLabel.VerticalAlignment = VerticalAlignment.Center;
            ScaleLabel.Margin = new Thickness(4, 0, CornerRightInset, 0);
            _toggleRow.Children.Add(ScaleLabel);
            _toggleRow.Children.Add(_showSeriesToggle ? Toggle : _plainName);
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
            readout._plainName.Text = (string)args.NewValue;
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

    /// <summary>Copy the shared live text into a second layout without changing the source's chart toggle.</summary>
    internal void CopyReadingFrom(MetricReadout source)
    {
        if (Label != source.Label) Label = source.Label;
        if (source.ValueLabel.Inlines.Count == 2 && source.ValueLabel.Inlines.FirstInline == source._number &&
            source.ValueLabel.Inlines.LastInline == source._unit)
            ShowValue(source._number.Text, source._unit.Text.TrimStart());
        else if (ValueLabel.Text != source.ValueLabel.Text)
            ValueLabel.Text = source.ValueLabel.Text;
        if (DetailLabel.Text != source.DetailLabel.Text) DetailLabel.Text = source.DetailLabel.Text;
    }

    private readonly Run _number = new();
    private readonly Run _unit = new() { FontSize = 16 };

    public string AccentKey
    {
        set
        {
            Toggle.SetResourceReference(Control.ForegroundProperty, value);
            _plainName.SetResourceReference(TextBlock.ForegroundProperty, value);
            ValueLabel.SetResourceReference(TextBlock.ForegroundProperty, value);
            Gauge.SetResourceReference(MetricGauge.FillProperty, value);
        }
    }
}
