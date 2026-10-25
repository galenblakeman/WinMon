using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using WinMon.Controls;

namespace WinMon;

public partial class MainWindow
{
    internal const double CompactHeight = 142;
    private Size _fullViewSize = new(1000, 840);

    private readonly List<(MetricReadout Source, Border Box, MetricReadout Readout)> _compactTiles = [];
    private readonly Dictionary<MetricReadout, MetricReadout> _compactMetrics = [];

    private void InitializeCompactView()
    {
        foreach (var (source, accent) in new[]
                 {
                     (CpuMetric, "CpuAccent"), (MemoryMetric, "MemoryAccent"),
                     (DiskMetric, "DiskAccent"), (SecondaryDiskMetric, "SecondaryDiskAccent"),
                     (NetworkMetric, "NetworkAccent"), (GpuMetric, "GpuAccent")
                 })
        {
            var column = CompactStrip.ColumnDefinitions.Count;
            CompactStrip.ColumnDefinitions.Add(new ColumnDefinition());
            var readout = new MetricReadout { ShowSeriesToggle = false, Margin = new Thickness(10, 4, 8, 4) };
            readout.AccentKey = accent;
            var box = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 0), Child = readout,
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
            _compactTiles.Add((source, box, readout));
            _compactMetrics.Add(source, readout);
        }

        UpdateCompactButton();
    }

    private void OnToggleCompactView(object sender, RoutedEventArgs e)
    {
        SetCompactView(!CompactMode);
    }

    internal void SetCompactView(bool compact)
    {
        if (compact == CompactMode) return;
        if (compact)
        {
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _fullViewSize = bounds.Size;
            WindowState = WindowState.Normal;
        }

        CompactMode = compact;
        _preferences = _preferences with { CompactMode = compact };
        FullView.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactStrip.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        CompactStatus.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        InfoButton.Visibility = SettingsButton.Visibility = ExportButton.Visibility =
            compact ? Visibility.Collapsed : Visibility.Visible;
        VersionRun.Text = compact ? "" : $" {AppInfo.Version}";
        MaxHeight = compact ? CompactHeight : double.PositiveInfinity;
        MinHeight = compact ? CompactHeight : 160;
        Height = compact ? CompactHeight : Math.Max(MinHeight, _fullViewSize.Height);
        if (!compact) Width = Math.Max(MinWidth, _fullViewSize.Width);
        UpdateCompactButton();
        RefreshPresentation();
        EnsureVisible();
        CompactViewButton.Focus();
    }

    private void UpdateCompactButton()
    {
        var text = CompactMode ? Strings.FullView : Strings.CompactView;
        CompactViewButton.Content = text;
        CompactViewButton.ToolTip = text;
        AutomationProperties.SetName(CompactViewButton, text);
        ButtonIcon.SetData(CompactViewButton, CompactMode ? ButtonIcon.Expand : ButtonIcon.Compact);
    }

    private void RefreshCompactView()
    {
        if (!CompactMode) return;
        CompactStatus.Text = _samplingError is null ? Strings.LiveMode : Strings.Unavailable;
        CompactStatus.ToolTip =
            _samplingError is null ? Strings.LiveMode : Strings.ReadingsUnavailableRetryingEachSecond;
        foreach (var (source, box, readout) in _compactTiles)
        {
            var visible = source != SecondaryDiskMetric || _preferences.SecondaryDrive is not null;
            box.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            CompactStrip.ColumnDefinitions[Grid.GetColumn(box)].Width = visible
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
            readout.CopyReadingFrom(source);
            var value = new TextRange(readout.ValueLabel.ContentStart, readout.ValueLabel.ContentEnd).Text;
            var name = $"{readout.Label} {value}";
            if (AutomationProperties.GetName(box) != name) AutomationProperties.SetName(box, name);
            var definition = _metricDetails[source].Definition;
            if (AutomationProperties.GetHelpText(box) != definition)
                AutomationProperties.SetHelpText(box, definition);
        }
    }

    internal Size SavedFullViewSize(Rect bounds)
    {
        return CompactMode ? _fullViewSize : bounds.Size;
    }
}
