using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
public sealed class CpuLanguageCharacterizationTests
{
    private CultureInfo _formats = null!;
    private CultureInfo _uiCulture = null!;
    private CultureInfo? _resourceCulture;

    [SetUp]
    public void UseEnglish()
    {
        _formats = CultureInfo.CurrentCulture;
        _uiCulture = CultureInfo.CurrentUICulture;
        _resourceCulture = Strings.Culture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        Strings.SetCulture(CultureInfo.CurrentUICulture);
    }

    [TearDown]
    public void RestoreCultures()
    {
        CultureInfo.CurrentCulture = _formats;
        CultureInfo.CurrentUICulture = _uiCulture;
        Strings.SetCulture(_resourceCulture);
    }

    private static readonly MonitorSample[] Samples = Enumerable.Range(0, 3).Select(second =>
        new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(second), 50, [], 0,
            MonotonicTimestamp: (100 + second) * Stopwatch.Frequency)).ToArray();

    private static CpuGraph Graph(bool grid = true, ResourceChartType mode = ResourceChartType.Overlay,
        MonitorSample? selected = null)
    {
        var graph = new CpuGraph { ShowGrid = grid, ChartType = mode, VisibleMetrics = 1 };
        graph.Resources["Line"] = Brushes.Blue;
        graph.Resources["Muted"] = Brushes.Black;
        graph.Resources["PlotSurface"] = Brushes.White;
        graph.Resources["CpuChartAccent"] = Brushes.Red;
        graph.Resources["Accent"] = Brushes.Red;
        graph.Measure(new Size(240, 160));
        graph.Arrange(new Rect(0, 0, 240, 160));
        graph.SetData(Samples, 120, selected);
        return graph;
    }

    private static GlyphRun[] RenderLabels(CpuGraph graph)
    {
        graph.UpdateLayout();
        var bitmap = new RenderTargetBitmap(240, 160, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(graph);
        return Leaves(VisualTreeHelper.GetDrawing(graph)).OfType<GlyphRunDrawing>()
            .Select(drawing => drawing.GlyphRun).ToArray();
    }

    private static IEnumerable<Drawing> Leaves(Drawing drawing)
    {
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var leaf in Leaves(child)) yield return leaf;
        else yield return drawing;
    }

    private static string Text(GlyphRun run) => new(run.Characters.ToArray());

    [Test]
    public void RefreshClearsOnlyGridTooltipsAndPreservesSelection(
        [Values(false, true)] bool grid, [Values(false, true)] bool selected,
        [Values(ResourceChartType.Overlay, ResourceChartType.LogicalProcessors)] ResourceChartType mode)
    {
        var graph = Graph(grid, mode, selected ? Samples[1] : null);
        Assert.That(graph.IsMouseOver, Is.False, "A detached chart must not depend on the native pointer position.");
        var sentinel = new object();
        graph.ToolTip = sentinel;
        var anchor = graph.SelectionAnchor;
        Assert.That(graph.SelectionInPlot, Is.EqualTo(selected));

        graph.RefreshLanguage();

        if (grid) Assert.That(graph.ToolTip, Is.Null);
        else Assert.That(graph.ToolTip, Is.SameAs(sentinel));
        Assert.That(graph.SelectionInPlot, Is.EqualTo(selected));
        Assert.That(graph.SelectionAnchor, Is.EqualTo(anchor));
        graph.ToolTip = sentinel;
        graph.RefreshLanguage();
        if (grid) Assert.That(graph.ToolTip, Is.Null, "Repeated refresh must also clear a stale tooltip.");
        else Assert.That(graph.ToolTip, Is.SameAs(sentinel));
    }

    [Test]
    public void RefreshRedrawsLocalizedLiveLabelWithoutNewSamples()
    {
        var graph = Graph();
        Assert.That(RenderLabels(graph).Select(Text), Does.Contain("Now"));
        Strings.SetCulture(CultureInfo.GetCultureInfo("fr-FR"));

        graph.RefreshLanguage();

        var text = RenderLabels(graph).Select(Text).ToArray();
        Assert.That(text, Does.Contain("Maintenant"));
        Assert.That(text, Does.Not.Contain("Now"));
        Strings.SetCulture(CultureInfo.GetCultureInfo("en-US"));
        graph.RefreshLanguage();
        Assert.That(RenderLabels(graph).Select(Text), Does.Contain("Now"));
    }

    [Test]
    public void RefreshReshapesUnchangedLabelsForNewFormattingCulture()
    {
        var graph = Graph();
        var original = RenderLabels(graph).Single(run => Text(run) == "50%");
        Assert.That(original.Language.IetfLanguageTag, Is.EqualTo("en-us"));
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        var fresh = RenderLabels(Graph()).Single(run => Text(run) == "50%");
        Assert.That(fresh.Language.IetfLanguageTag, Is.EqualTo("fr-fr"));
        graph.InvalidateVisual();
        var cached = RenderLabels(graph).Single(run => Text(run) == "50%");
        Assert.That(cached.Language.IetfLanguageTag, Is.EqualTo("en-us"), "Redraw alone keeps the old shaped label.");

        graph.RefreshLanguage();

        var refreshed = RenderLabels(graph).Single(run => Text(run) == "50%");
        Assert.That(refreshed.Language.IetfLanguageTag, Is.EqualTo(fresh.Language.IetfLanguageTag));
        Assert.That(Text(refreshed), Is.EqualTo(Text(original)), "The text stays equal while its shaping culture changes.");
    }
}
