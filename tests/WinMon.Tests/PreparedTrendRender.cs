using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MonCore.Monitoring;
using WinMon.Controls;

namespace WinMon.Tests;

/// <summary>Owned graph data and public WPF output for original/current render comparisons.</summary>
public sealed class PreparedTrendRender
{
    public static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch.AddHours(12);
    public static IReadOnlyList<string> Cases { get; } = new[]
    {
        "empty", "missing", "zero-seconds", "resource-change", "clock-change", "small-width", "small-height", "minimum",
        "primary", "weighted-column", "partial-before", "partial-at", "partial-after", "break", "gap", "collapsed-gap",
        "receive", "secondary-only", "send", "selection", "stale-selection", "gradient", "fallback", "clipped-time", "normalized-range", "zero-values",
        "day-wide", "seven-day"
    }.Concat(Enum.GetValues<ResourceTrendMetric>().Select(metric => "metric-" + metric)).ToArray();

    internal ResourceTrendGraph Graph { get; } = new();
    public string Name { get; }
    public int Width { get; private set; } = 242;
    public int Height { get; private set; } = 130;
    public ResourceTrendMetric Metric { get; private set; } = ResourceTrendMetric.Cpu;
    private ResourceTrendBucket[] _buckets;
    private DateTimeOffset _end = Start.AddMinutes(5);
    private ResourceTrendBucket? _selection;
    public int SelectionEvents { get; private set; }

    private static ResourceTrendValue Value(double average, double seconds = 60,
        ResourceTrendBreak reason = ResourceTrendBreak.None) => new(average * seconds, seconds, 0, 1000, "owned", reason);

    private static ResourceTrendBucket Bucket(double seconds, double average, double observed = 60,
        ResourceTrendBreak reason = ResourceTrendBreak.None) => new(Start.AddSeconds(seconds),
        new Dictionary<ResourceTrendMetric, ResourceTrendValue> { [ResourceTrendMetric.Cpu] = Value(average, observed, reason) });

    public PreparedTrendRender(string name)
    {
        Name = name;
        _buckets = Enumerable.Range(0, 5).Select(index => Bucket(index * 60, 10 + index * 20)).ToArray();
        Graph.Resources["PlotSurface"] = Brushes.Black;
        Graph.Resources["Line"] = Brushes.Gray;
        Graph.Resources["Muted"] = Brushes.White;
        Graph.Resources["Accent"] = Brushes.Red;
        Graph.Resources["NetworkAccent"] = Brushes.Lime;
        Graph.Resources["Text"] = Brushes.White;
        if (name.StartsWith("metric-", StringComparison.Ordinal))
        {
            Metric = Enum.Parse<ResourceTrendMetric>(name[7..]);
            _buckets = [new(Start, new Dictionary<ResourceTrendMetric, ResourceTrendValue> { [Metric] = Value(10) }),
                new(Start.AddMinutes(1), new Dictionary<ResourceTrendMetric, ResourceTrendValue> { [Metric] = Value(90) })];
        }
        else switch (name)
        {
            case "empty": _buckets = []; break;
            case "missing": _buckets = [new(Start, new Dictionary<ResourceTrendMetric, ResourceTrendValue>())]; break;
            case "zero-seconds": _buckets = [Bucket(0, 50, 0)]; break;
            case "resource-change": _buckets = [Bucket(0, 50, 60, ResourceTrendBreak.ResourceChanged)]; break;
            case "clock-change": _buckets = [Bucket(0, 50, 60, ResourceTrendBreak.ClockChanged)]; break;
            case "small-width": Width = 43; break;
            case "small-height": Height = 31; break;
            case "minimum": Width = 44; Height = 32; _buckets = []; break;
            case "primary": break;
            case "weighted-column": _buckets = [Bucket(0, 10, 10), Bucket(1, 90, 50)]; break;
            case "partial-before": _buckets = [Bucket(0, 50, 59.9989)]; break;
            case "partial-at": _buckets = [Bucket(0, 50, 59.999)]; break;
            case "partial-after": _buckets = [Bucket(0, 50, 60)]; break;
            case "break": _buckets = [Bucket(0, 10), Bucket(60, 50, 60, ResourceTrendBreak.CollectionRestart), Bucket(120, 90)]; break;
            case "gap": _buckets = [Bucket(0, 10), Bucket(120, 50), Bucket(180, 90)]; break;
            case "collapsed-gap": Width = 44; _buckets = [Bucket(0, 10), Bucket(120, 90)]; break;
            case "peak-across-collapsed-gap":
                Width = 44;
                _buckets = [new(Start, new Dictionary<ResourceTrendMetric, ResourceTrendValue>
                {
                    [ResourceTrendMetric.Cpu] = Value(4) with { Maximum = 98, PeakUtcTicks = Start.AddSeconds(5).UtcTicks }
                }), Bucket(120, 9)];
                break;
            case "receive": case "secondary-only": case "send":
                Metric = name == "send" ? ResourceTrendMetric.Send : ResourceTrendMetric.Receive;
                _buckets = Enumerable.Range(0, 2).Select(index =>
                {
                    var values = new Dictionary<ResourceTrendMetric, ResourceTrendValue>();
                    if (name != "secondary-only") values[Metric] = Value(10 + index * 20);
                    values[name == "send" ? ResourceTrendMetric.Receive : ResourceTrendMetric.Send] = Value(100 + index * 100);
                    return new ResourceTrendBucket(Start.AddMinutes(index), values);
                }).ToArray();
                break;
            case "selection": _selection = _buckets[2]; break;
            case "stale-selection": _selection = Bucket(-60, 50); break;
            case "gradient": Graph.Resources["Accent"] = new LinearGradientBrush(Colors.Red, Colors.Blue, 0); break;
            case "fallback":
                foreach (var key in new[] { "PlotSurface", "Line", "Muted", "Accent", "NetworkAccent", "Text" })
                    Graph.Resources[key] = new object();
                break;
            case "clipped-time": _buckets = [Bucket(-60, 10), Bucket(600, 90)]; _selection = _buckets[1]; break;
            case "normalized-range": _end = Start; _buckets = [Bucket(0, 10), Bucket(60, 90)]; break;
            case "zero-values": _buckets = [Bucket(0, 0), Bucket(60, 0)]; break;
            case "day-wide": case "seven-day":
                var minutes = name == "day-wide" ? 1440 : 10080;
                Width = name == "day-wide" ? 3000 : 1000;
                Height = 180;
                _end = Start.AddMinutes(minutes);
                _buckets = Enumerable.Range(0, minutes).Select(index => Bucket(index * 60, 10 + index % 91)).ToArray();
                break;
            default: throw new ArgumentException("Unknown prepared rendering case", nameof(name));
        }
        Graph.BucketSelected += (_, _) => SelectionEvents++;
        Refresh();
    }

    public void Refresh() => Graph.SetData(_buckets, Metric, Start, _end, _selection);

    public byte[] Render()
    {
        Refresh();
        Graph.Measure(new Size(Width, Height));
        Graph.Arrange(new Rect(0, 0, Width, Height));
        Graph.UpdateLayout();
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(Graph);
        var pixels = new byte[Width * Height * 4];
        bitmap.CopyPixels(pixels, Width * 4, 0);
        return pixels;
    }

    public IReadOnlyList<GeometryDrawing> Shapes => Flatten(VisualTreeHelper.GetDrawing(Graph)).OfType<GeometryDrawing>().ToArray();
    public IReadOnlyList<GlyphRunDrawing> Glyphs => Flatten(VisualTreeHelper.GetDrawing(Graph)).OfType<GlyphRunDrawing>().ToArray();

    private static IEnumerable<Drawing> Flatten(Drawing? drawing)
    {
        if (drawing is null) yield break;
        if (drawing is DrawingGroup group)
        {
            foreach (var child in group.Children)
                foreach (var leaf in Flatten(child)) yield return leaf;
        }
        else yield return drawing;
    }

    public static Point[] Points(Geometry geometry) => PathGeometry.CreateFromGeometry(geometry).Figures
        .SelectMany(figure => new[] { figure.StartPoint }.Concat(figure.Segments.SelectMany(segment => segment switch
        {
            LineSegment line => new[] { line.Point }, PolyLineSegment line => line.Points.ToArray(),
            _ => throw new InvalidOperationException("Unexpected flattened drawing segment")
        }))).ToArray();

    public string Snapshot(byte[] pixels) => JsonSerializer.Serialize(new
    {
        Name, Width, Height, Metric, SelectionEvents, SystemParameters.HighContrast,
        Pixels = Convert.ToHexString(SHA256.HashData(pixels)),
        Shapes = Shapes.Select(shape => new
        {
            Geometry = shape.Geometry.ToString(CultureInfo.InvariantCulture),
            Path = PathGeometry.CreateFromGeometry(shape.Geometry).ToString(CultureInfo.InvariantCulture),
            Bounds = shape.Geometry.Bounds.ToString(CultureInfo.InvariantCulture),
            Brush = shape.Brush?.ToString(CultureInfo.InvariantCulture),
            Pen = shape.Pen is null ? null : new { shape.Pen.Thickness, Brush = shape.Pen.Brush.ToString(CultureInfo.InvariantCulture),
                Dashes = shape.Pen.DashStyle.Dashes.ToArray(), shape.Pen.DashStyle.Offset },
            Transform = shape.Geometry.Transform.Value.ToString(CultureInfo.InvariantCulture)
        }).ToArray(),
        Glyphs = Glyphs.Select(glyph => new
        {
            Font = glyph.GlyphRun.GlyphTypeface.FontUri.OriginalString, glyph.GlyphRun.FontRenderingEmSize,
            Origin = glyph.GlyphRun.BaselineOrigin.ToString(CultureInfo.InvariantCulture), glyph.GlyphRun.BidiLevel,
            Glyphs = glyph.GlyphRun.GlyphIndices.ToArray(), Advances = glyph.GlyphRun.AdvanceWidths.ToArray(),
            Offsets = glyph.GlyphRun.GlyphOffsets?.Select(point => point.ToString(CultureInfo.InvariantCulture)).ToArray(),
            Brush = glyph.ForegroundBrush.ToString(CultureInfo.InvariantCulture)
        }).ToArray()
    });
}
