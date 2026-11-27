using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WinMon.Controls;

public partial class GaugeDetailsStrip : UserControl
{
    internal const double DividerHeight = 14;
    internal const double ExpandedHeight = 72;

    internal sealed record Reading(string Label, string Value, string? Help = null)
    {
        public string LabelHelp => Help ?? Label;
    }

    internal sealed record Column(Reading First, Reading Second, Reading Third);

    private double _dragStartHeight;
    private double _dragStartY;

    public GaugeDetailsStrip()
    {
        InitializeComponent();
    }

    internal event Action<double, bool>? DetailsHeightRequested;

    internal double DetailsHeight
    {
        get => RowsViewport.Height;
        set
        {
            RowsViewport.Height = value;
            RowsViewport.Visibility = value > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    internal void Update(Column cpu, Column memory, Column disk, Column? secondary, Column network, Column gpu)
    {
        SetColumn(Cpu, cpu);
        SetColumn(Memory, memory);
        SetColumn(Disk, disk);
        SetColumn(SecondaryDisk, secondary);
        SecondaryColumn.Width = secondary is null ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        SecondaryDisk.Visibility = SecondaryDivider.Visibility = secondary is null ? Visibility.Collapsed : Visibility.Visible;
        SetColumn(Network, network);
        SetColumn(Gpu, gpu);
    }

    private static void SetColumn(ContentControl target, Column? reading)
    {
        if (!Equals(target.Content, reading)) target.Content = reading;
    }

    private void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        _dragStartHeight = DetailsHeight;
        _dragStartY = Mouse.GetPosition(this).Y;
    }

    private void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        var height = Math.Clamp(_dragStartHeight + Mouse.GetPosition(this).Y - _dragStartY, 0, ExpandedHeight);
        DetailsHeightRequested?.Invoke(height, false);
    }

    private void OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        var height = e.Canceled ? _dragStartHeight : DetailsHeight;
        DetailsHeightRequested?.Invoke(height >= ExpandedHeight / 2 ? ExpandedHeight : 0, true);
    }

    private void OnDividerKeyDown(object sender, KeyEventArgs e)
    {
        var height = e.Key switch
        {
            Key.Up or Key.Home => 0,
            Key.Down or Key.End => ExpandedHeight,
            Key.Enter or Key.Space => DetailsHeight > 0 ? 0 : ExpandedHeight,
            _ => -1
        };
        if (height < 0) return;

        DetailsHeightRequested?.Invoke(height, true);
        e.Handled = true;
    }
}
