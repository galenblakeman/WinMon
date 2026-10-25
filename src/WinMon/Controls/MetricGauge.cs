using System.Windows;
using System.Windows.Media;
using WinMon.Preferences;

namespace WinMon.Controls;

/// <summary>Draws a resource's live level and two-minute peak in one <see cref="GaugeStyle"/>.</summary>
/// <remarks>
/// The marks live in a retained drawing that is reopened only when a mark moves by half a pixel or more, so a
/// steady reading costs no drawing and no layout. The peak is always a dark line, as the window's legend says.
/// </remarks>
public sealed class MetricGauge : FrameworkElement
{
    private const int Segments = 10;

    public static readonly DependencyProperty FillProperty = Register(nameof(Fill));
    public static readonly DependencyProperty TrackProperty = Register(nameof(Track));
    public static readonly DependencyProperty OutlineProperty = Register(nameof(Outline));
    public static readonly DependencyProperty MarkProperty = Register(nameof(Mark));

    private readonly DrawingGroup _drawing = new();
    private GaugeStyle _kind;
    private double? _level;
    private double _peak;
    private double _drawnLevel = double.NaN;
    private double _drawnPeak = double.NaN;
    private Pen? _fillPen;
    private Pen? _trackPen;
    private Pen? _markPen;
    private Pen? _borderPen;
    private Pen? _needlePen;
    private Geometry? _track;
    private Geometry[] _segments = [];

    /// <summary>The resource color.</summary>
    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>The empty part of the left bar.</summary>
    public Brush Track
    {
        get => (Brush)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    /// <summary>The left bar's border, and the empty part of every other style.</summary>
    public Brush Outline
    {
        get => (Brush)GetValue(OutlineProperty);
        set => SetValue(OutlineProperty, value);
    }

    /// <summary>The two-minute peak line.</summary>
    public Brush Mark
    {
        get => (Brush)GetValue(MarkProperty);
        set => SetValue(MarkProperty, value);
    }

    public GaugeStyle Kind
    {
        get => _kind;
        set
        {
            if (_kind == value) return;
            _kind = value;
            Rebuild();
        }
    }

    /// <summary>The level drawn, as a fraction of the scale, or <see langword="null"/> when nothing is drawn.</summary>
    internal double? Level => _level;

    internal double Peak => _peak;

    /// <summary>Show a level and peak as fractions of the scale, or no marks when the reading is unavailable.</summary>
    public void Show(double? level, double peak)
    {
        if (_kind == GaugeStyle.None)
        {
            _level = level;
            _peak = peak;
            return;
        }

        if (level is { } value)
        {
            // Changes smaller than half a pixel along the gauge would not change a drawn pixel.
            var step = 0.5 / Math.Max(1, Length());
            // Segmented styles light whole segments, so a small change can still cross a segment's boundary.
            if (Math.Abs(value - _drawnLevel) < step && Math.Abs(peak - _drawnPeak) < step &&
                (_kind is not (GaugeStyle.SegmentedArc or GaugeStyle.SegmentedBar) || Lit(value) == Lit(_drawnLevel)))
                return;
        }
        else if (_level is null)
        {
            return;
        }

        _level = level;
        _peak = peak;
        Redraw();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawDrawing(_drawing);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Rebuild();
    }

    private static DependencyProperty Register(string name)
    {
        return DependencyProperty.Register(name, typeof(Brush), typeof(MetricGauge),
            new PropertyMetadata(Brushes.Transparent, (sender, _) => ((MetricGauge)sender).Rebuild()));
    }

    private bool IsArc => _kind is GaugeStyle.Needle or GaugeStyle.Bowl or GaugeStyle.SegmentedArc;

    private const double Hub = 4;

    private double Thickness => _kind switch
    {
        GaugeStyle.Needle => 6,
        _ => 8
    };

    /// <summary>How much wider than tall the bowl and segmented arc may be, so a rate and its unit fit inside.</summary>
    internal const double BowlWidening = 1.2;

    /// <summary>The arc's center and radii: the needle's largest half circle, or a nearly round one holding the value.</summary>
    internal (Point Center, double RadiusX, double RadiusY) ArcBounds()
    {
        var size = RenderSize;
        var half = Thickness / 2;
        // The needle's hub hangs below the center.
        var center = new Point(size.Width / 2, size.Height - (_kind == GaugeStyle.Needle ? Hub : 0));
        var across = Math.Max(1, size.Width / 2 - half - 1);
        var up = Math.Max(1, center.Y - half - 1);
        // A perfect circle that tall is too narrow inside for "↓9.52 Mbps", so arcs holding the value widen a little.
        return _kind != GaugeStyle.Needle
            ? (center, Math.Min(across, up * BowlWidening), up)
            : (center, Math.Min(across, up), Math.Min(across, up));
    }

    private double Length()
    {
        if (IsArc)
        {
            var (_, radiusX, radiusY) = ArcBounds();
            return Math.PI * (radiusX + radiusY) / 2;
        }

        return _kind == GaugeStyle.LeftBar ? RenderSize.Height : RenderSize.Width;
    }

    /// <summary>Recreate the pens and the fixed geometry after a style, size or brush change, then redraw.</summary>
    private void Rebuild()
    {
        _fillPen = Frozen(new Pen(Fill, Thickness));
        _trackPen = Frozen(new Pen(Outline, Thickness));
        _markPen = Frozen(new Pen(Mark, 2));
        _borderPen = Frozen(new Pen(Outline, 1));
        _needlePen = Frozen(new Pen(Fill, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        _track = null;
        _segments = [];
        if (IsArc && RenderSize is { Width: > 0, Height: > 0 })
        {
            var (center, radiusX, radiusY) = ArcBounds();
            if (_kind == GaugeStyle.SegmentedArc)
            {
                // Gaps of about two pixels at any radius.
                var gap = 1 / (Math.PI * radiusX);
                _segments =
                [
                    .. Enumerable.Range(0, Segments).Select(i =>
                        Arc(center, radiusX, radiusY, (double)i / Segments + gap, (i + 1d) / Segments - gap))
                ];
            }
            else
            {
                _track = Arc(center, radiusX, radiusY, 0, 1);
            }
        }

        Redraw();
    }

    private void Redraw()
    {
        using var dc = _drawing.Open();
        var size = RenderSize;
        if (_level is not { } level || _kind == GaugeStyle.None || size.Width <= 0 || size.Height <= 0)
        {
            _drawnLevel = double.NaN;
            _drawnPeak = double.NaN;
            return;
        }

        _drawnLevel = level;
        _drawnPeak = _peak;
        switch (_kind)
        {
            case GaugeStyle.LeftBar:
                DrawRail(dc, size, level);
                break;
            case GaugeStyle.BottomBar:
                DrawBar(dc, size, level);
                break;
            case GaugeStyle.SegmentedBar:
                DrawSegmentedBar(dc, size, level);
                break;
            default:
                DrawArc(dc, level);
                break;
        }
    }

    private void DrawRail(DrawingContext dc, Size size, double level)
    {
        var frame = new Rect(size);
        dc.DrawRectangle(Track, _borderPen, frame);
        var inner = new Rect(frame.X + 1, frame.Y + 2, Math.Max(0, frame.Width - 2), Math.Max(0, frame.Height - 4));
        var height = inner.Height * level;
        dc.DrawRectangle(Fill, null, new Rect(inner.X, inner.Bottom - height, inner.Width, height));
        var y = inner.Bottom - inner.Height * _peak;
        dc.DrawLine(_markPen, new Point(frame.Left, y), new Point(frame.Right, y));
    }

    private void DrawBar(DrawingContext dc, Size size, double level)
    {
        var bar = new Rect(0, (size.Height - 5) / 2, size.Width, 5);
        dc.DrawRoundedRectangle(Outline, null, bar, 2.5, 2.5);
        if (level > 0)
            dc.DrawRoundedRectangle(Fill, null, bar with { Width = Math.Max(bar.Height, bar.Width * level) }, 2.5,
                2.5);
        DrawTick(dc, size);
    }

    private void DrawSegmentedBar(DrawingContext dc, Size size, double level)
    {
        const double gap = 2;
        var width = (size.Width - gap * (Segments - 1)) / Segments;
        var lit = Lit(level);
        var top = (size.Height - 6) / 2;
        for (var i = 0; i < Segments; i++)
            dc.DrawRectangle(i < lit ? Fill : Outline, null, new Rect(i * (width + gap), top, width, 6));
        DrawTick(dc, size);
    }

    private static int Lit(double level)
    {
        return (int)Math.Round(level * Segments);
    }

    private void DrawTick(DrawingContext dc, Size size)
    {
        var x = Math.Clamp(size.Width * _peak, 1, size.Width - 1);
        dc.DrawLine(_markPen, new Point(x, 0), new Point(x, size.Height));
    }

    private void DrawArc(DrawingContext dc, double level)
    {
        var (center, radiusX, radiusY) = ArcBounds();
        var half = Thickness / 2;
        if (_kind == GaugeStyle.SegmentedArc)
        {
            var lit = Lit(level);
            for (var i = 0; i < _segments.Length; i++) dc.DrawGeometry(null, i < lit ? _fillPen : _trackPen, _segments[i]);
        }
        else
        {
            dc.DrawGeometry(null, _trackPen, _track);
            if (level > 0) dc.DrawGeometry(null, _fillPen, Arc(center, radiusX, radiusY, 0, level));
        }

        dc.DrawLine(_markPen, ArcPoint(center, radiusX - half - 2, radiusY - half - 2, _peak),
            ArcPoint(center, radiusX + half + 1, radiusY + half + 1, _peak));
        if (_kind != GaugeStyle.Needle) return;
        dc.DrawLine(_needlePen, center, ArcPoint(center, radiusX - half - 1, radiusY - half - 1, level));
        dc.DrawEllipse(Fill, null, center, Hub, Hub);
    }

    /// <summary>The point at <paramref name="fraction"/> of the way from the left end of a half arc to its right end.</summary>
    private static Point ArcPoint(Point center, double radiusX, double radiusY, double fraction)
    {
        var angle = Math.PI * (1 - fraction);
        return new Point(center.X + radiusX * Math.Cos(angle), center.Y - radiusY * Math.Sin(angle));
    }

    private static StreamGeometry Arc(Point center, double radiusX, double radiusY, double from, double to)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ArcPoint(center, radiusX, radiusY, from), false, false);
            context.ArcTo(ArcPoint(center, radiusX, radiusY, to), new Size(radiusX, radiusY), 0, false,
                SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Pen Frozen(Pen pen)
    {
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }
}
