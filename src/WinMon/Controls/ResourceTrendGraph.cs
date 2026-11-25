using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>Draws a bounded, minute-aggregated resource trend without filling collection gaps.</summary>
internal sealed class ResourceTrendGraph : FrameworkElement
{
    private IReadOnlyList<ResourceTrendBucket> _buckets = [];
    private ResourceTrendMetric _metric;
    private DateTimeOffset _start;
    private DateTimeOffset _end;
    private int _selected = -1;

    internal event EventHandler<ResourceTrendBucket>? BucketSelected;

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }

    internal void SetData(IReadOnlyList<ResourceTrendBucket> buckets, ResourceTrendMetric metric,
        DateTimeOffset start, DateTimeOffset end, ResourceTrendBucket? selection)
    {
        _buckets = buckets;
        _metric = metric;
        _start = start;
        _end = end > start ? end : start.AddMinutes(1);
        _selected = selection is null ? -1 : IndexOf(selection);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var bounds = new Rect(34, 8, Math.Max(0, ActualWidth - 42), Math.Max(0, ActualHeight - 30));
        if (bounds.Width < 2 || bounds.Height < 2) return;
        var line = Resource("Line", Brushes.DimGray);
        var muted = Resource("Muted", Brushes.Gray);
        var accent = Resource("Accent", Brushes.DodgerBlue);
        drawing.DrawRectangle(Resource("PlotSurface", Brushes.Transparent), null, bounds);
        for (var i = 0; i <= 4; i++)
        {
            var y = bounds.Top + bounds.Height * i / 4;
            drawing.DrawLine(new Pen(line, .6), new Point(bounds.Left, y), new Point(bounds.Right, y));
        }
        Label(drawing, _start.ToLocalTime().ToString("g"), new Point(bounds.Left, bounds.Bottom + 5), muted);
        Label(drawing, _end.ToLocalTime().ToString("g"), new Point(bounds.Right, bounds.Bottom + 5), muted, 1);
        if (_buckets.Count == 0)
        {
            if (IsKeyboardFocused) drawing.DrawRectangle(null, new Pen(Resource("Text", Brushes.White), 1), new Rect(0.5, 0.5,
                Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)));
            return;
        }
        var values = _buckets.Select(bucket => (Bucket: bucket, Value: Value(bucket)))
            .Where(pair => pair.Value?.Average is not null).ToArray();
        var secondaryValues = _metric == ResourceTrendMetric.Receive
            ? _buckets.Select(bucket => (Bucket: bucket, Value: Value(bucket, ResourceTrendMetric.Send)))
                .Where(pair => pair.Value?.Average is not null).ToArray() : [];
        if (values.Length == 0 && secondaryValues.Length == 0)
        {
            if (IsKeyboardFocused) drawing.DrawRectangle(null, new Pen(Resource("Text", Brushes.White), 1), new Rect(0.5, 0.5,
                Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)));
            return;
        }
        var maximum = values.Concat(secondaryValues).Max(pair => pair.Value!.Average!.Value);
        maximum = Math.Max(1, maximum);
        for (var i = 0; i <= 4; i++)
        {
            var y = bounds.Top + bounds.Height * i / 4;
            Label(drawing, FormatValue(maximum * (4 - i) / 4), new Point(0, Math.Max(0, y - 7)), muted);
        }
        var width = Math.Max(1, (int)Math.Floor(bounds.Width));
        DrawPrimarySeries(drawing, values, bounds, width, maximum, accent);
        if (secondaryValues.Length > 0)
            DrawBinnedLine(drawing, secondaryValues, bounds, width, maximum, Resource("NetworkAccent", Brushes.MediumPurple));
        if (_selected >= 0 && _selected < _buckets.Count)
        {
            var x = bounds.Left + Column(_buckets[_selected].StartUtc, bounds, width);
            var selectionPen = new Pen(muted, 1) { DashStyle = DashStyles.Dash };
            drawing.DrawLine(selectionPen, new Point(x, bounds.Top), new Point(x, bounds.Bottom));
        }
        if (IsKeyboardFocused) drawing.DrawRectangle(null, new Pen(Resource("Text", Brushes.White), 1), new Rect(0.5, 0.5,
            Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)));
    }

    private void DrawPrimarySeries(DrawingContext drawing, (ResourceTrendBucket Bucket, ResourceTrendValue? Value)[] values,
        Rect bounds, int width, double maximum, Brush accent)
    {
        var columns = new TrendColumn?[width + 1];
        ResourceTrendBucket? previous = null;
        foreach (var (bucket, value) in values)
        {
            var x = Column(bucket.StartUtc, bounds, width);
            var item = columns[x];
            var breaks = value!.IsBreak || previous is not null && bucket.StartUtc - previous.StartUtc > TimeSpan.FromMinutes(1);
            columns[x] = item is null ? new TrendColumn(value.WeightedSum, value.ObservedSeconds, value.Average!.Value,
                value.Average.Value, breaks, Partial: value.ObservedSeconds < 59.999) : item.Value.Add(value, breaks);
            previous = bucket;
        }
        var wash = SystemParameters.HighContrast ? null : new SolidColorBrush(Color.FromArgb(56,
            accent is SolidColorBrush solid ? solid.Color.R : (byte)30,
            accent is SolidColorBrush colored ? colored.Color.G : (byte)144,
            accent is SolidColorBrush blue ? blue.Color.B : (byte)255));
        if (wash is { CanFreeze: true }) wash.Freeze();
        for (var x = 0; x < columns.Length; x++)
        {
            if (columns[x] is not { } column || column.GapWithinColumn) continue;
            var top = Y(column.Max, maximum, bounds);
            var bottom = Y(column.Min, maximum, bounds);
            drawing.DrawRectangle(wash, null, new Rect(bounds.Left + x, top, 1, Math.Max(1, bottom - top)));
            if (wash is null)
                drawing.DrawLine(new Pen(accent, 1), new Point(bounds.Left + x, top), new Point(bounds.Left + x, bottom));
            if (column.Partial)
            {
                var partial = new Pen(accent, 1) { DashStyle = DashStyles.Dot };
                drawing.DrawLine(partial, new Point(bounds.Left + x, top), new Point(bounds.Left + x, bottom));
            }
        }
        var segment = new StreamGeometry();
        using (var context = segment.Open())
        {
            var open = false;
            for (var x = 0; x < columns.Length; x++)
            {
                if (columns[x] is not { } column) continue;
                if (column.GapWithinColumn)
                {
                    open = false;
                    continue;
                }
                var point = new Point(bounds.Left + x, Y(column.Average, maximum, bounds));
                if (!open || column.BreaksBefore) context.BeginFigure(point, false, false);
                else context.LineTo(point, true, false);
                open = true;
            }
        }
        if (segment.CanFreeze) segment.Freeze();
        drawing.DrawGeometry(null, new Pen(accent, 1.5), segment);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        SelectAt(e.GetPosition(this).X);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_buckets.Count == 0 || e.Key is not (Key.Left or Key.Right or Key.Home or Key.End)) return;
        var index = e.Key switch
        {
            Key.Home => 0,
            Key.End => _buckets.Count - 1,
            Key.Left => Math.Max(0, _selected < 0 ? _buckets.Count - 1 : _selected - 1),
            _ => Math.Min(_buckets.Count - 1, _selected < 0 ? 0 : _selected + 1)
        };
        Select(index);
        e.Handled = true;
    }

    internal void Select(int index)
    {
        if (_buckets.Count == 0) return;
        _selected = Math.Clamp(index, 0, _buckets.Count - 1);
        InvalidateVisual();
        BucketSelected?.Invoke(this, _buckets[_selected]);
    }

    private void SelectAt(double x)
    {
        if (_buckets.Count == 0) return;
        var fraction = Math.Clamp((x - 34) / Math.Max(1, ActualWidth - 42), 0, 1);
        var time = _start + TimeSpan.FromTicks((long)((_end - _start).Ticks * fraction));
        Select(_buckets.Select((bucket, index) => (bucket, index)).MinBy(item => Math.Abs((item.bucket.StartUtc - time).Ticks)).index);
    }

    private void DrawBinnedLine(DrawingContext drawing, (ResourceTrendBucket Bucket, ResourceTrendValue? Value)[] values,
        Rect bounds, int width, double maximum, Brush brush)
    {
        var columns = new TrendColumn?[width + 1];
        ResourceTrendBucket? previous = null;
        foreach (var (bucket, value) in values)
        {
            var column = Column(bucket.StartUtc, bounds, width);
            var prior = columns[column];
            var breaks = value!.IsBreak || previous is not null && bucket.StartUtc - previous.StartUtc > TimeSpan.FromMinutes(1);
            columns[column] = prior is null ? new TrendColumn(value.WeightedSum, value.ObservedSeconds, value.Average!.Value,
                value.Average.Value, breaks) : prior.Value.Add(value, breaks);
            previous = bucket;
        }
        var line = new StreamGeometry();
        using (var context = line.Open())
        {
            var open = false;
            for (var x = 0; x < columns.Length; x++)
            {
                if (columns[x] is not { } column) continue;
                if (column.GapWithinColumn) { open = false; continue; }
                var point = new Point(bounds.Left + x, Y(column.Average, maximum, bounds));
                if (!open || column.BreaksBefore) context.BeginFigure(point, false, false);
                else context.LineTo(point, true, false);
                open = true;
            }
        }
        if (line.CanFreeze) line.Freeze();
        var pen = new Pen(brush, 1.2) { DashStyle = DashStyles.Dot };
        drawing.DrawGeometry(null, pen, line);
    }

    private ResourceTrendValue? Value(ResourceTrendBucket bucket) => Value(bucket, _metric);
    private static ResourceTrendValue? Value(ResourceTrendBucket bucket, ResourceTrendMetric metric) =>
        bucket.Values.TryGetValue(metric, out var value) ? value : null;
    private int IndexOf(ResourceTrendBucket bucket)
    {
        for (var index = 0; index < _buckets.Count; index++)
            if (ReferenceEquals(_buckets[index], bucket) || _buckets[index] == bucket) return index;
        return -1;
    }
    private int Column(DateTimeOffset time, Rect bounds, int width) => Math.Clamp((int)((time - _start).TotalSeconds / (_end - _start).TotalSeconds * width), 0, width);
    private static double Y(double value, double maximum, Rect bounds) => bounds.Bottom - Math.Clamp(value / maximum, 0, 1) * bounds.Height;
    private Brush Resource(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
    private string FormatValue(double value) => _metric switch
    {
        ResourceTrendMetric.MemoryBytes => TableMetricText.Memory((ulong)value),
        ResourceTrendMetric.Receive or ResourceTrendMetric.Send or ResourceTrendMetric.PrimaryDiskRead or
            ResourceTrendMetric.PrimaryDiskWrite or ResourceTrendMetric.SecondaryDiskRead or ResourceTrendMetric.SecondaryDiskWrite => TableMetricText.IoRate(value),
        _ => value.ToString("F0", System.Globalization.CultureInfo.CurrentCulture) + "%"
    };
    private void Label(DrawingContext drawing, string text, Point point, Brush brush, double alignment = 0)
    {
        var glyph = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawing.DrawText(glyph, new Point(point.X - glyph.Width * alignment, point.Y));
    }

    private readonly record struct TrendColumn(double WeightedSum, double Seconds, double Min, double Max, bool BreaksBefore,
        bool GapWithinColumn = false, bool Partial = false)
    {
        internal double Average => WeightedSum / Seconds;
        internal TrendColumn Add(ResourceTrendValue value, bool breaksBefore) => new(WeightedSum + value.WeightedSum,
            Seconds + value.ObservedSeconds, Math.Min(Min, value.Average!.Value), Math.Max(Max, value.Average.Value),
            BreaksBefore || breaksBefore, GapWithinColumn || breaksBefore, Partial || value.ObservedSeconds < 59.999);
    }
}
