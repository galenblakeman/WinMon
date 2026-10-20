using System.Windows;
using System.Windows.Controls;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    private void UpdateSnapshot(CpuSample? retained)
    {
        SnapshotLayer.Visibility = _selectedSample is null ? Visibility.Collapsed : Visibility.Visible;
        if (_selectedSample is null) return;
        SnapshotTitle.Text = Strings.Format(Strings.Snapshot, _selectedSample.Timestamp);
        SnapshotNote.Text = retained is null ? Strings.HistoryExpiredThisSampleHasLeftTheRecordingWindow
            : Strings.IntervalReadingsTopReadoutsStayLive;
        SnapshotReadings.Visibility = retained is null ? Visibility.Collapsed : Visibility.Visible;
        if (retained is not null)
        {
            var r = retained.Resources;
            SnapshotCpu.Text = Percent(retained.TotalCpuPercent);
            SnapshotMemory.Text = Percent(r.MemoryPercent);
            SnapshotGpu.Text = Percent(r.GpuPercent);
            SnapshotDiskName.Text = r.DiskName.Length == 0 ? Strings.Disk : Strings.Format(Strings.DiskLabel, r.DiskName);
            SnapshotDisk.Text = Percent(r.DiskActivePercent);
            SnapshotSecondaryDiskName.Text = Strings.Format(Strings.DiskLabel, r.SecondaryDiskName);
            SnapshotDisk2.Text = Percent(r.SecondaryDiskActivePercent);
            SnapshotSecondaryDisk.Visibility = r.SecondaryDiskName.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            SnapshotNetwork.Text = Rate(r);
        }
        SnapshotPanel.ToolTip = retained is null ? SnapshotNote.Text : CpuGraph.DescribeSample(retained);
        PositionSnapshot();
    }

    private void OnSnapshotChartSizeChanged(object sender, SizeChangedEventArgs e) => PositionSnapshot();

    private void PositionSnapshot()
    {
        if (_selectedSample is null || SnapshotLayer is null) return;
        var plot = OverallGraph.PlotBounds;
        if (SnapshotReadings.Visibility == Visibility.Visible)
            SnapshotNote.Text = OverallGraph.SelectionInPlot ? Strings.IntervalReadingsTopReadoutsStayLive
                : Strings.OutsideChartRangeTopReadoutsStayLive;
        var bounds = SnapshotPlacement.Calculate(plot, OverallGraph.SelectionAnchor, new Size(320, 216));
        SnapshotPanel.Width = bounds.Width;
        SnapshotPanel.Height = bounds.Height;
        Canvas.SetLeft(SnapshotPanel, bounds.Left);
        Canvas.SetTop(SnapshotPanel, bounds.Top);
        var anchor = OverallGraph.SelectionAnchor;
        var left = anchor.X < bounds.Left;
        SnapshotPointer.Visibility = left || anchor.X > bounds.Right ? Visibility.Visible : Visibility.Collapsed;
        var side = left ? bounds.Left : bounds.Right;
        var tip = Math.Clamp(side + (left ? -10 : 10), plot.Left, plot.Right);
        var y = Math.Clamp(anchor.Y, bounds.Top + Math.Min(12, bounds.Height / 2), bounds.Bottom - Math.Min(12, bounds.Height / 2));
        var half = Math.Min(7, bounds.Height / 2);
        SnapshotPointer.Points =
        [
            new Point(side, y - half), new Point(tip, y), new Point(side, y + half)
        ];
    }
}
