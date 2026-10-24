using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using WinMon.Controls;
using WinMon.Preferences;

namespace WinMon;

public partial class MainWindow
{
    private bool _compactMode;
    private Size _fullViewSize = new(1000, 840);

    private readonly
        List<(MetricReadout Source, Border Box, CpuGraph Graph, TextBlock Label, TextBlock Value, TextBlock Detail)>
        _stripTiles = [];

    private void InitializeCompactView()
    {
        foreach (var (source, metric, accent) in new[]
                 {
                     (CpuMetric, 0, "CpuAccent"), (MemoryMetric, 1, "MemoryAccent"),
                     (DiskMetric, 2, "DiskAccent"), (SecondaryDiskMetric, 5, "SecondaryDiskAccent"),
                     (NetworkMetric, 3, "NetworkAccent"), (GpuMetric, 4, "GpuAccent")
                 })
        {
            var column = CompactStrip.ColumnDefinitions.Count;
            CompactStrip.ColumnDefinitions.Add(new ColumnDefinition());
            var graph = new CpuGraph { TileMetric = metric, IsHitTestVisible = false };
            var label = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            var value = new TextBlock
                { FontSize = 23, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            value.SetResourceReference(TextBlock.ForegroundProperty, accent);
            var detail = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            foreach (var block in new[] { label, value, detail })
            {
                block.HorizontalAlignment = HorizontalAlignment.Left;
                block.SetResourceReference(TextBlock.BackgroundProperty, "Surface");
            }

            var text = new StackPanel
                { Margin = new Thickness(10, 7, 6, 0), VerticalAlignment = VerticalAlignment.Top };
            text.Children.Add(label);
            text.Children.Add(value);
            text.Children.Add(detail);
            var content = new Grid { ClipToBounds = true };
            content.Children.Add(graph);
            content.Children.Add(text);
            var box = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 0), Child = content,
                Focusable = true, ToolTip = source.ToolTip
            };
            KeyboardNavigation.SetTabIndex(box, column);
            box.SetResourceReference(Border.BorderBrushProperty, "Line");
            box.SetResourceReference(FocusVisualStyleProperty, "MetricTileFocusVisual");
            ToolTipService.SetShowsToolTipOnKeyboardFocus(box, true);
            box.ToolTipOpening += (_, _) => OpenDetails(source);
            box.ToolTipClosing += (_, _) => CloseDetails(source);
            Grid.SetColumn(box, column);
            CompactStrip.Children.Add(box);
            _stripTiles.Add((source, box, graph, label, value, detail));
        }

        UpdateCompactButton();
    }

    private void OnToggleCompactView(object sender, RoutedEventArgs e)
    {
        SetCompactView(!_compactMode);
    }

    internal void SetCompactView(bool compact)
    {
        if (compact == _compactMode) return;
        if (compact)
        {
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _fullViewSize = bounds.Size;
            WindowState = WindowState.Normal;
        }

        _compactMode = compact;
        _preferences = _preferences with { CompactMode = compact };
        FullView.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactStrip.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        CompactStatus.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        InfoButton.Visibility = SettingsButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        VersionRun.Text = compact ? "" : $" {AppInfo.Version}";
        MaxHeight = compact ? 156 : double.PositiveInfinity;
        MinHeight = compact ? 156 : 160;
        Height = compact ? 156 : Math.Max(MinHeight, _fullViewSize.Height);
        if (!compact) Width = Math.Max(MinWidth, _fullViewSize.Width);
        UpdateCompactButton();
        RefreshPresentation();
        EnsureVisible();
        CompactViewButton.Focus();
    }

    private void UpdateCompactButton()
    {
        var text = _compactMode ? Strings.FullView : Strings.CompactView;
        CompactViewButton.Content = text;
        CompactViewButton.ToolTip = text;
        AutomationProperties.SetName(CompactViewButton, text);
        ButtonIcon.SetData(CompactViewButton, _compactMode ? ButtonIcon.Expand : ButtonIcon.Compact);
    }

    private void RefreshCompactView()
    {
        if (!_compactMode) return;
        var history = Session.History.Samples;
        CompactStatus.Text = _samplingError is null ? Strings.LiveMode : Strings.Unavailable;
        CompactStatus.ToolTip =
            _samplingError is null ? Strings.LiveMode : Strings.ReadingsUnavailableRetryingEachSecond;
        foreach (var (source, box, graph, label, value, detail) in _stripTiles)
        {
            var visible = source != SecondaryDiskMetric || _preferences.SecondaryDrive is not null;
            box.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            CompactStrip.ColumnDefinitions[Grid.GetColumn(box)].Width = visible
                ? new GridLength(source == NetworkMetric ? 1.3 : 1, GridUnitType.Star)
                : new GridLength(0);
            var nextLabel = source.Label;
            var nextValue = new TextRange(source.ValueLabel.ContentStart, source.ValueLabel.ContentEnd).Text;
            var nameChanged = label.Text != nextLabel || value.Text != nextValue;
            if (label.Text != nextLabel) label.Text = nextLabel;
            if (value.Text != nextValue) value.Text = nextValue;
            // The compact strip prioritizes memory capacity and network direction readings.
            var nextDetail = source == MemoryMetric || source == NetworkMetric ? source.DetailLabel.Text : "";
            if (detail.Text != nextDetail) detail.Text = nextDetail;
            if (nameChanged || string.IsNullOrEmpty(AutomationProperties.GetName(box)))
                AutomationProperties.SetName(box, $"{nextLabel} {nextValue}");
            var definition = _metricDetails[source].Definition;
            if (AutomationProperties.GetHelpText(box) != definition)
                AutomationProperties.SetHelpText(box, definition);
            graph.UseSeriesPatterns = OverallGraph.UseSeriesPatterns;
            graph.SetData(history, MonitorPreferences.ShortRangeSeconds, null);
        }
    }

    internal Size SavedFullViewSize(Rect bounds)
    {
        return _compactMode ? _fullViewSize : bounds.Size;
    }
}
