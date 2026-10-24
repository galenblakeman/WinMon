using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    private SnapshotReport? _snapshotReport;
    private readonly DispatcherTimer _snapshotCopiedTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    private void OnCopySnapshot(object sender, RoutedEventArgs e)
    {
        CopySnapshot();
    }

    internal void CopySnapshot(Action<string>? copyToClipboard = null)
    {
        // Never substitute the live sample when the selected sample has expired.
        if (_snapshotReport is not { } report || !ReferenceEquals(report.Sample, _selectedSample) ||
            !Session.History.Samples.Contains(report.Sample)) return;
        try
        {
            var text = SnapshotText.Format(report);
            if (copyToClipboard is null) Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true);
            else copyToClipboard(text);
            SnapshotCopyButton.Content = Strings.Copied;
            _snapshotCopiedTimer.Stop();
            _snapshotCopiedTimer.Tick -= OnSnapshotCopiedTimeout;
            _snapshotCopiedTimer.Tick += OnSnapshotCopiedTimeout;
            _snapshotCopiedTimer.Start();
        }
        catch (ExternalException ex)
        {
            Trace.TraceWarning("Could not copy snapshot: {0}", ex.Message);
            ResetSnapshotCopyCaption();
            MessageDialog.ShowNotice(this, Strings.TheClipboardIsUnavailableTryCopyAgain, Strings.CopySnapshot);
        }
    }

    private void OnSnapshotCopiedTimeout(object? sender, EventArgs e)
    {
        ResetSnapshotCopyCaption();
    }

    internal void ResetSnapshotCopyCaption()
    {
        _snapshotCopiedTimer.Stop();
        SnapshotCopyButton.SetBinding(ContentProperty,
            new Binding("[CopySnapshot]") { Source = Strings.Text, Mode = BindingMode.OneWay });
    }

    private void OnLiveModeClick(object sender, RoutedEventArgs e)
    {
        ReturnToLive();
    }

    private void OnSnapshotModeClick(object sender, RoutedEventArgs e)
    {
        OverallGraph.SelectLatest();
    }

    private void UpdateSnapshot(MonitorSample? retained)
    {
        LiveModeButton.IsChecked = _selectedSample is null;
        SnapshotModeButton.IsChecked = _selectedSample is not null;
        SnapshotModeButton.IsEnabled = Session.History.Latest is not null;
        SnapshotLayer.Visibility = _selectedSample is null ? Visibility.Collapsed : Visibility.Visible;
        SnapshotCopyButton.IsEnabled = retained is not null;
        if (!ReferenceEquals(_snapshotReport?.Sample, retained)) ResetSnapshotCopyCaption();
        if (retained is null) _snapshotReport = null;
        if (_selectedSample is null) return;
        SnapshotTitle.Text = Strings.Format(Strings.Snapshot, _selectedSample.Timestamp);
        SnapshotNote.Text = retained is null
            ? Strings.HistoryExpiredThisSampleHasLeftTheRecordingWindow
            : Strings.IntervalReadingsTopReadoutsStayLive;
        SnapshotReadings.Visibility = retained is null ? Visibility.Collapsed : Visibility.Visible;
        if (retained is not null)
        {
            var r = retained.Resources;
            SnapshotCpu.Text = Percent(retained.TotalCpuPercent);
            SnapshotMemory.Text = Percent(r.MemoryPercent);
            SnapshotGpu.Text = Percent(r.GpuPercent);
            SnapshotDiskName.Text =
                r.DiskName.Length == 0 ? Strings.Disk : Strings.Format(Strings.DiskLabel, r.DiskName);
            SnapshotDisk.Text = Percent(r.DiskActivePercent);
            SnapshotSecondaryDiskName.Text = Strings.Format(Strings.DiskLabel, r.SecondaryDiskName);
            SnapshotDisk2.Text = Percent(r.SecondaryDiskActivePercent);
            SnapshotSecondaryDisk.Visibility =
                r.SecondaryDiskName.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            SnapshotNetwork.Text = Rate(r);
        }

        SnapshotPanel.ToolTip = retained is null ? SnapshotNote.Text : CpuGraph.DescribeSample(retained);
        PositionSnapshot();
    }

    private void OnSnapshotChartSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PositionSnapshot();
    }

    private void PositionSnapshot()
    {
        if (_selectedSample is null || SnapshotLayer is null) return;
        var plot = OverallGraph.PlotBounds;
        if (SnapshotReadings.Visibility == Visibility.Visible)
            SnapshotNote.Text = OverallGraph.SelectionInPlot
                ? Strings.IntervalReadingsTopReadoutsStayLive
                : Strings.OutsideChartRangeTopReadoutsStayLive;
        var bounds = SnapshotPlacement.Calculate(plot, OverallGraph.SelectionAnchor,
            new Size(320, CaptureBanner.Visibility == Visibility.Visible ? 250 : 216));
        SnapshotPanel.Width = bounds.Width;
        SnapshotPanel.Height = bounds.Height;
        Canvas.SetLeft(SnapshotPanel, bounds.Left);
        Canvas.SetTop(SnapshotPanel, bounds.Top);
        var anchor = OverallGraph.SelectionAnchor;
        var left = anchor.X < bounds.Left;
        SnapshotPointer.Visibility = left || anchor.X > bounds.Right ? Visibility.Visible : Visibility.Collapsed;
        var side = left ? bounds.Left : bounds.Right;
        var tip = Math.Clamp(side + (left ? -10 : 10), plot.Left, plot.Right);
        var y = Math.Clamp(anchor.Y, bounds.Top + Math.Min(12, bounds.Height / 2),
            bounds.Bottom - Math.Min(12, bounds.Height / 2));
        var half = Math.Min(7, bounds.Height / 2);
        SnapshotPointer.Points =
        [
            new Point(side, y - half), new Point(tip, y), new Point(side, y + half)
        ];
    }
}