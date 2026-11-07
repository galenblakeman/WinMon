using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    /// <summary>Render every gauge style in both themes at the default and minimum widths, with illustrative data.</summary>
    internal static async Task CaptureGaugeDiagnosticsAsync(string output, CancellationToken cancellationToken)
    {
        var fixture = new MainWindow(true)
        {
            Left = 40, Top = 40, Width = 1000, Height = 840,
            Preferences = new MonitorPreferences { PrimaryDrive = "C:", SecondaryDrive = "D:" }
        };
        var now = DateTimeOffset.Now;
        for (var i = 0; i < 120; i++)
        {
            var pulse = Math.Exp(-Math.Pow((i - 94) / 3d, 2));
            fixture.Session.History.Add(new MonitorSample(now.AddSeconds(i - 119), 32.8 + 45 * pulse, [], 0,
                MonotonicTimestamp: Stopwatch.Frequency * (i + 1))
            {
                Resources = new ResourceSample
                {
                    TotalMemoryBytes = 64UL * 1_073_741_824, UsedMemoryBytes = 35_433_480_192,
                    DiskName = "C:", DiskActivePercent = 0.1 + 62 * pulse,
                    DiskBytesPerSecond = i == 94 ? 200_000 : 900_000,
                    SecondaryDiskName = "D:", SecondaryDiskActivePercent = 97 + 2 * pulse,
                    SecondaryDiskBytesPerSecond = 300_000,
                    GpuEngine = "3D", GpuPercent = 9 + 60 * pulse,
                    // The utilization peak has no memory reading, so the native setting must fall back to percent.
                    GpuMemoryUsedBytes = i == 94 ? null : 4UL * 1_073_741_824,
                    CpuClockMhz = 4300,
                    NetworkId = "fixture", NetworkName = "Ethernet", LinkBitsPerSecond = 1_000_000_000,
                    ReceiveBytesPerSecond = 1_190 + 8_000_000 * pulse, SendBytesPerSecond = 2_100
                }
            });
        }

        MetricReadout[] readouts =
        [
            fixture.CpuMetric, fixture.MemoryMetric, fixture.DiskMetric, fixture.SecondaryDiskMetric,
            fixture.NetworkMetric, fixture.GpuMetric
        ];
        var visual = (FrameworkElement)fixture.Content;
        var arcs = new[] { GaugeStyle.Bowl, GaugeStyle.Needle, GaugeStyle.SegmentedArc };
        var heights = new SortedSet<double>();
        foreach (var theme in new[] { "Dark", "Light" })
        foreach (var style in Enum.GetValues<GaugeStyle>())
        {
            fixture.Preferences = fixture.Preferences with { Theme = theme, GaugeStyle = style };
            fixture.ApplyTheme();
            foreach (var width in new[] { 1000d, 836d })
            {
                Draw(width);
                foreach (var readout in readouts)
                {
                    heights.Add(readout.ActualHeight);
                    var shown = readout.ValueLabel.ActualWidth;
                    readout.ValueLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    if (readout.ValueLabel.DesiredSize.Width > shown + 0.5)
                        throw new InvalidOperationException($"{style} clipped {readout.Label} at {width} DIPs.");
                }

                visual.UpdateLayout();
                if (readouts.Any(readout => readout.GaugeStyle != style ||
                                            readout.Gauge.Visibility != (style == GaugeStyle.None
                                                ? Visibility.Hidden
                                                : Visibility.Visible)))
                    throw new InvalidOperationException($"{style} did not draw on every available reading.");
                // The needle is a full-size half circle; arcs holding the value are at most slightly wider.
                var widening = style == GaugeStyle.Needle ? 1 : MetricGauge.BowlWidening;
                if (arcs.Contains(style) && readouts.Any(readout =>
                        readout.Gauge.ArcBounds() is var (_, radiusX, radiusY) &&
                        (radiusX < radiusY - 0.001 || radiusX > radiusY * widening + 0.001 || radiusY < 38)))
                    throw new InvalidOperationException(
                        $"{style} is not a full-size, nearly round half circle at {width} DIPs: " + string.Join(", ",
                            readouts.Select(readout => readout.Gauge.ArcBounds()).Select(arc =>
                                $"{arc.RadiusX:0.#}×{arc.RadiusY:0.#}")));
                if (style == GaugeStyle.Bowl)
                {
                    foreach (var readout in readouts)
                    {
                        var strip = (Grid)readout.Parent;
                        var column = Grid.GetColumn(readout);
                        var columnCenter = strip.ColumnDefinitions.Take(column).Sum(part => part.ActualWidth) +
                                           strip.ColumnDefinitions[column].ActualWidth / 2;
                        var gaugeCenter = readout.Gauge.TranslatePoint(readout.Gauge.ArcBounds().Center, strip).X;
                        var valueCenter = readout.ValueLabel.TranslatePoint(
                            new Point(readout.ValueLabel.ActualWidth / 2, 0), strip).X;
                        if (Math.Abs(gaugeCenter - columnCenter) > 0.5 ||
                            Math.Abs(valueCenter - columnCenter) > 0.5)
                            throw new InvalidOperationException(
                                $"{readout.Label} bowl or value is off-center at {width} DIPs: " +
                                $"column {columnCenter:0.#}, bowl {gaugeCenter:0.#}, value {valueCenter:0.#}.");
                    }
                }
                var scaled = fixture.NetworkMetric.ScaleLabel;
                if (arcs.Contains(style) != (scaled.Visibility == Visibility.Visible) ||
                    (arcs.Contains(style) && scaled.Text != RateText.Short(64_009_520)) ||
                    (arcs.Contains(style) && scaled.Text == "100M"))
                    throw new InvalidOperationException($"{style} showed the network peak wrongly.");
                (MetricReadout Readout, string Text)[] corners =
                [
                    (fixture.CpuMetric, Strings.Format(Strings.GaugePeakShort, 78d)),
                    (fixture.MemoryMetric, TableMetricText.Memory(35_433_480_192)),
                    (fixture.DiskMetric, TableMetricText.IoRate(200_000)),
                    (fixture.SecondaryDiskMetric, TableMetricText.IoRate(300_000)),
                    (fixture.GpuMetric, Strings.Format(Strings.GaugePeakShort, 69d))
                ];
                if (corners.Any(corner => corner.Readout.ScaleLabel.Text !=
                        (arcs.Contains(style) ? corner.Text : "") ||
                        (arcs.Contains(style) && !ReferenceEquals(corner.Readout.ScaleLabel.ToolTip,
                            corner.Readout.ToolTip)) ||
                        (corner.Readout.ScaleLabel.Visibility == Visibility.Visible) != arcs.Contains(style) ||
                        corner.Readout.PeakIconVisible != arcs.Contains(style)))
                    throw new InvalidOperationException($"{style} showed a resource corner label wrongly.");
                if (fixture.NetworkMetric.PeakIconVisible != arcs.Contains(style))
                    throw new InvalidOperationException($"{style} showed the network peak icon wrongly.");
                await CaptureVisualAsync(visual,
                    Path.Combine(output, $"gauges-{style.ToString().ToLowerInvariant()}-{theme.ToLowerInvariant()}-{width}.png"),
                    1, cancellationToken);
            }
        }

        // Labels reuse the utilization peak sample; RAM and disk examples have deliberately different
        // native values from their latest samples, so the smoke catches accidental independent native peaks.
        foreach (var theme in new[] { "Dark", "Light" })
        foreach (var width in new[] { 1000d, 836d })
        {
            fixture.Preferences = fixture.Preferences with
                { Theme = theme, GaugeStyle = GaugeStyle.Bowl };
            fixture.ApplyTheme();
            Draw(width);
            fixture.OpenDetails(fixture.MemoryMetric);
            var details = (MetricDetails)fixture.MemoryMetric.ToolTip;
            if (!details.Peak.Contains(Strings.GaugePeakRamUsed, StringComparison.Ordinal) ||
                !details.Peak.Contains(Strings.GaugePeakAtTime[..2], StringComparison.Ordinal) ||
                !ReferenceEquals(fixture.MemoryMetric.ScaleLabel.ToolTip, details))
                throw new InvalidOperationException("Native peak labels or tooltip details were unavailable.");
            fixture.CloseDetails(fixture.MemoryMetric);
            fixture.OpenDetails(fixture.NetworkMetric);
            var networkDetails = (MetricDetails)fixture.NetworkMetric.ToolTip;
            if (!networkDetails.Peak.Contains(Strings.Format(Strings.NetworkScale0, RateText.Format(100_000_000)),
                    StringComparison.Ordinal))
                throw new InvalidOperationException("Network peak tooltip did not retain its gauge scale.");
            fixture.CloseDetails(fixture.NetworkMetric);
            var disk = fixture.DiskMetric.ScaleLabel.Text;
            var expectedDisk = TableMetricText.IoRate(200_000);
            var expectedGpu = Strings.Format(Strings.GaugePeakShort, 69d);
            if (disk != expectedDisk || fixture.GpuMetric.ScaleLabel.Text != expectedGpu)
                throw new InvalidOperationException("Native labels did not use the utilization peak sample or fallback: " +
                    $"disk '{disk}' expected '{expectedDisk}', GPU '{fixture.GpuMetric.ScaleLabel.Text}' expected '{expectedGpu}'.");
            await CaptureVisualAsync(visual,
                Path.Combine(output, $"gauges-native-{theme.ToLowerInvariant()}-{width}.png"), 1, cancellationToken);
        }

        // Appearance finishes change brushes only, so one full-size bowl capture per finish is sufficient.
        foreach (var theme in ThemeFinishes.Names)
        {
            fixture.Preferences = fixture.Preferences with { Theme = theme, GaugeStyle = GaugeStyle.Bowl };
            fixture.ApplyTheme();
            Draw(1000, 840);
            if (readouts.Any(readout => Math.Abs(readout.ActualHeight - MetricReadout.LayoutHeight) > 0.001))
                throw new InvalidOperationException($"{theme} changed the resource strip height.");
            await CaptureVisualAsync(visual, Path.Combine(output, $"theme-{theme.ToLowerInvariant()}.png"),
                1, cancellationToken);
        }

        // RenderTargetBitmap cannot include the DWM backdrop. Capture the composed window as well.
        fixture.Loaded -= fixture.OnLoaded;
        fixture.Left = SystemParameters.WorkArea.Left + 20;
        fixture.Top = SystemParameters.WorkArea.Top + 20;
        fixture.Width = 1000;
        fixture.Height = 840;
        fixture.Show();
        var backdropResults = new List<object>();
        try
        {
            foreach (var theme in new[] { "FrostedPorcelain", "SmokedSapphire", "Crystal", "Slate", "Prism" })
            {
                fixture.Preferences = fixture.Preferences with { Theme = theme, GaugeStyle = GaugeStyle.Bowl };
                fixture.ApplyTheme();
                Draw(1000, 840);
                backdropResults.Add(new
                {
                    Theme = theme, Active = fixture.BackdropActive,
                    PanelOpacity = ((System.Windows.Media.SolidColorBrush)fixture.FindResource("PanelSurface")).Color.A
                });
                var path = Path.Combine(output, $"backdrop-{theme.ToLowerInvariant()}.png");
                try
                {
                    await CaptureExampleWindowAsync(fixture, path, cancellationToken);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("must be foreground", StringComparison.Ordinal))
                {
                    // Keep a content-only capture when Windows denies foreground activation, as Services smoke does.
                    var contentPath = Path.ChangeExtension(path, null) + "-content.png";
                    await CaptureVisualAsync((FrameworkElement)fixture.Content, contentPath, 1, cancellationToken);
                    await File.AppendAllTextAsync(Path.Combine(output, "content-capture-notes.txt"),
                        Path.GetFileName(contentPath) + ": WPF content capture; native caption not verified.\n",
                        cancellationToken);
                }
            }
        }
        finally
        {
            fixture.Hide();
        }
        await File.WriteAllTextAsync(Path.Combine(output, "backdrops.json"),
            JsonSerializer.Serialize(backdropResults), cancellationToken);

        if (heights.Count != 1 || Math.Abs(heights.Single() - MetricReadout.LayoutHeight) > 0.001)
            throw new InvalidOperationException($"Readout heights varied by style: {string.Join(", ", heights)}.");

        fixture.ShownSamplingError = SamplingError.ReadingsUnavailable;
        Draw(836);
        if (readouts.Any(readout => readout.Gauge.Visibility == Visibility.Visible ||
            readout.ScaleLabel.Visibility == Visibility.Visible))
            throw new InvalidOperationException("Unavailable readings kept their gauges or corner labels.");
        fixture.ShownSamplingError = null;

        var store = new PreferenceStore(Path.Combine(output, "gauge-settings.json"));
        await store.SaveAsync(fixture.Preferences with { GaugeStyle = GaugeStyle.SegmentedArc }, cancellationToken);
        if ((await store.LoadAsync(cancellationToken)).GaugeStyle != GaugeStyle.SegmentedArc)
            throw new InvalidOperationException("The gauge style did not survive a save and load.");

        fixture.Preferences = fixture.Preferences with
            { Theme = "Dark", GaugeStyle = GaugeStyle.Bowl };
        fixture.ApplyTheme();
        var settings = new SettingsWindow(fixture.Preferences, () => [],
            captureFolder: Path.Combine(output, "Captures")) { Left = 40, Top = 40 };
        try
        {
            settings.Show();
            settings.ShowSection(SettingsSection.Appearance);
            if (settings.GaugePreview.GaugeStyle != GaugeStyle.Bowl ||
                settings.GaugePreview.Gauge.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Settings did not preview the saved gauge style.");
            await CaptureVisualAsync((FrameworkElement)settings.Content, Path.Combine(output, "settings-gauges.png"),
                1, cancellationToken);
            settings.ThemeSelector.SelectedValue = "Porcelain";
            settings.PaletteSelector.SelectedValue = "Ocean";
            settings.GaugeSelector.SelectedValue = GaugeStyle.Needle;
            await CaptureVisualAsync((FrameworkElement)settings.Content,
                Path.Combine(output, "settings-appearance-porcelain.png"), 1, cancellationToken);
        }
        finally
        {
            settings.Close();
        }

        await File.WriteAllTextAsync(Path.Combine(output, "gauges.json"), JsonSerializer.Serialize(new
        {
            Styles = Enum.GetNames<GaugeStyle>(), Themes = new[] { "Dark", "Light" }.Concat(ThemeFinishes.Names),
            Widths = new[] { 1000, 836 },
            ReadoutHeight = heights.Single(), NetworkPeakOnArcs = true, CornerLabelsOnArcs = true,
            UnavailableHidesGauges = true, PreferenceRoundTrip = true, SettingsPreview = true,
            AlwaysNativePeakLabelsAndTooltips = true
        }), cancellationToken);
        return;

        void Draw(double width, double height = 420)
        {
            fixture.RefreshVisiblePresentation();
            visual.Measure(new Size(width, height));
            visual.Arrange(new Rect(0, 0, width, height));
            visual.UpdateLayout();
            // Gauges draw at their arranged size, so refresh once more as a live tick would.
            fixture.RefreshVisiblePresentation();
            visual.UpdateLayout();
        }
    }
}
