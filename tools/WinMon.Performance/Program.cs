using MonCore.Monitoring;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinMon;
using WinMon.Monitoring;
using WinMon.Preferences;

// Deterministic UI-thread benchmark: one synthetic sample per frame, then the same refresh, layout and
// render pass the dispatcher runs once a second. Sampling is stopped; history is synthetic. Besides the live
// and historical frames it covers grouping, the Services tab, compact mode, and pinned and selected processes.
// usage: WinMon.Performance <result.json> [screenshot.png]
// It reaches WinMon through InternalsVisibleTo, so renaming a member it uses breaks the build, not the run.
internal static class Program
{
    private const int Processes = 850;
    private const int WarmupFrames = 400;
    private const int Batches = 7;
    private const int FramesPerBatch = 60;
    private const int ServiceHosts = 60;
    private const int Services = 300;

    [STAThread]
    private static void Main(string[] args)
    {
        using var diagnosticLog = args.Contains("--without-logging") ? null : new MonCore.Windows.Diagnostics.DiagnosticLog(
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "Logs"), "App");
        if (diagnosticLog is not null) Trace.Listeners.Add(diagnosticLog);
        System.Windows.Input.Keyboard.DefaultRestoreFocusMode = System.Windows.Input.RestoreFocusMode.None;
        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SkipStartup();
        var window = new MainWindow(true);
        var session = window.Session;
        var history = session.History;
        var serviceTree = new ServiceTree();
        var catalog = Catalog();
        var anchor = DateTimeOffset.Parse("2026-10-19T12:00:00Z");
        var second = 0;
        var fixedConnectionHistory = false;
        void AddSample()
        {
            second++;
            var full = Sample(anchor, second);
            var options = window.CurrentSamplingOptions();
            var compacted = ProcessRetention.Compact(full, 32, options.Retain, options.RetainAll);
            session.Record(compacted, full, serviceTree.Capture(full, catalog));
        }
        window.Width = 1000;
        window.Height = 900;
        window.Show();
        Flush();
        // Stop the real sampler, then replace anything it recorded with synthetic history.
        window.StopForFixtureAsync().Wait();
        Flush();
        history.Resize(1);
        history.Resize(600);
        for (var i = 0; i < 600; i++) AddSample();
        window.RefreshPresentation();
        Flush();

        var thread = GetCurrentThread();
        var cyclesPerMs = CyclesPerMillisecond(thread);
        var results = new List<object>();
        if (args.Contains("--metric-brush-only"))
        {
            MeasureChartRenders(true);
            File.WriteAllText(args[0], JsonSerializer.Serialize(new
            {
                LoggingEnabled = diagnosticLog is not null, Runtime = Environment.Version.ToString(),
                MetricBrushOnly = true, FixedMetricPalette = true, Samples = 120, Width = 800, Height = 240, SelectedIndex = 61,
                WindowSeconds = 120, RenderFrames = WarmupFrames + Batches * FramesPerBatch,
                WarmupFrames, Batches, FramesPerBatch,
                AssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))),
                Results = results,
                Limitation = "Fixed detached chart renders on the UI thread; excludes native collection and the WPF render thread."
            }, new JsonSerializerOptions { WriteIndented = true }));
            window.CloseForFixture();
            app.Shutdown();
            return;
        }
        Measure("live-frame");
        if (args.Contains("--live-only"))
        {
            File.WriteAllText(args[0], JsonSerializer.Serialize(new
            {
                LoggingEnabled = diagnosticLog is not null, Runtime = Environment.Version.ToString(),
                WarmupFrames, Batches, FramesPerBatch, Results = results
            }, new JsonSerializerOptions { WriteIndented = true }));
            Pixels(args.Length > 1 ? args[1] : null);
            window.CloseForFixture();
            app.Shutdown();
            return;
        }
        foreach (var (type, name) in new[]
                 {
                     (ResourceChartType.SmallMultiples, "small-multiples-live-frame"),
                     (ResourceChartType.Heatmap, "heatmap-live-frame"),
                     (ResourceChartType.Focus, "focus-live-frame")
                 })
        {
            window.Preferences = window.Preferences with { ResourceChartType = type };
            window.RefreshPresentation();
            Flush();
            Measure(name);
        }
        window.Preferences = window.Preferences with { ResourceChartType = ResourceChartType.Overlay };
        window.RefreshPresentation();
        Flush();
        window.SelectedSample = history.Samples[^60];
        Measure("historical-frame");
        window.SelectedSample = null;
        window.RefreshPresentation();
        Check(window.Processes.GroupedChip);
        Measure("grouped-live-frame");
        Check(window.Processes.IndividualChip);
        Check(window.ServicesTab);
        window.ServiceView.ExpandAll();
        Measure("services-live-frame");
        Check(window.ProcessesTab);
        window.SetCompactView(true);
        Flush();
        Measure("compact-live-frame");
        window.SetCompactView(false);
        Flush();
        PinAndSelect(false);
        Measure("pinned-selected-live-frame");
        Check(window.Processes.GroupedChip);
        PinAndSelect(true);
        Measure("pinned-selected-grouped-live-frame");
        var detailsTip = new System.Windows.Controls.ToolTip
        {
            Content = window.CpuMetric.ToolTip, PlacementTarget = window.CpuMetric
        };
        window.OpenDetails(window.CpuMetric);
        detailsTip.IsOpen = true;
        Flush();
        Measure("pinned-selected-grouped-tooltip-open");
        detailsTip.IsOpen = false;
        detailsTip.Content = null;
        detailsTip.PlacementTarget = null;
        window.CloseDetails(window.CpuMetric);
        Check(window.Processes.IndividualChip);
        window.RefreshPresentation();
        Flush();
        MeasureUsageLedger();
        MeasureUsageLedger(true);
        MeasureResourceTrendArchive();
        MeasureRestartHistory();
        MeasureInvestigationHistory();
        var pixels = Pixels(args.Length > 1 ? args[1] : null);
        window.Preferences = window.Preferences with { ResourceChartType = ResourceChartType.LogicalProcessors };
        window.RefreshPresentation();
        Flush();
        Measure("logical-processors-pinned-selected-live-frame");
        var connectionProcesses = session.LatestFull!.Processes.Take(60).ToArray();
        var endpoints = connectionProcesses.SelectMany(process => Enumerable.Range(0, 8).Select(index =>
            new NetworkConnection(process.ProcessId, process.CreationTicks, TransportProtocol.Tcp,
                new System.Net.IPEndPoint(System.Net.IPAddress.Parse("192.168.1.25"), 50000 + index),
                new System.Net.IPEndPoint(System.Net.IPAddress.Parse("203.0.113.24"), 443),
                System.Net.NetworkInformation.TcpState.Established))).ToArray();
        window.ConnectionView.Load(new WinMon.Controls.ConnectionListSettings(0));
        window.SetConnectionsForFixture(new NetworkConnectionsFrame(endpoints,
            NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success));
        window.ConnectionsTab.IsChecked = true;
        var firstConnectionProcess = connectionProcesses[0];
        window.ConnectionView.SelectConnectionForFixture(firstConnectionProcess.ProcessId, firstConnectionProcess.CreationTicks);
        Flush();
        Measure("connections-executable-live-frame");
        window.Width = 1200;
        Flush();
        var networkStamp = Stopwatch.GetTimestamp();
        var connectionTraffic = new ProcessNetworkFrame(networkStamp, Stopwatch.Frequency,
            ProcessNetworkQuality.Provisional, session.LatestFull!.Processes.Select(process =>
                new ProcessNetworkReading(process.ProcessId, process.CreationTicks, 12_000_000, 3_000_000,
                    2_000_000, 500_000)).ToArray());
        window.SetProcessNetworkForFixture(connectionTraffic);
        window.ConnectionView.SelectConnectionForFixture(firstConnectionProcess.ProcessId, firstConnectionProcess.CreationTicks);
        Flush();
        RequireNetworkColumnVisible();
        if (!window.ConnectionView.DisplayedRows.Any(row => row.NetworkSummary.ReceiveRate is > 0))
            throw new InvalidOperationException("Network benchmark requires healthy owner rates.");
        Measure("connections-network-executable-live-frame");
        var historyEnd = session.LatestFull!;
        var connectionFrame = new NetworkConnectionsFrame(endpoints,
            NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success);
        for (var i = 1; i <= 121; i++)
        {
            var past = historyEnd with { Timestamp = historyEnd.Timestamp.AddSeconds(i),
                MonotonicTimestamp = historyEnd.MonotonicTimestamp > 0
                    ? historyEnd.MonotonicTimestamp + i * Stopwatch.Frequency : 0 };
            session.Record(past, past, connections: connectionFrame, network: connectionTraffic);
        }
        fixedConnectionHistory = true;
        Measure("connections-network-history-executable-frame");
        if (!window.ConnectionView.DisplayedRows.Any(row => row.Graph.ScaleMaximum > 1))
            throw new InvalidOperationException("Traffic history benchmark must render recorded owner rates.");
        foreach (var owner in connectionProcesses.Take(5)) window.ConnectionView.TogglePin(owner);
        Measure("connections-network-history-pinned-frame");
        var networkViewportWidth = window.ConnectionView.HeaderScroller.ViewportWidth;
        window.Preferences = window.Preferences with { TopUsageCount = 0, UsageAppsOnly = false, UsageSortColumn = 0 };
        window.UsageView.Load(new WinMon.Controls.UsageListSettings(0, WinMon.Controls.UsageSort.CpuTime, false));
        Check(window.UsageTab);
        var usageRows = window.UsageView.Rows.Items.Count;
        if (!window.UsageView.IsVisible || usageRows == 0 || usageRows != session.Usage.Apps.Count)
            throw new InvalidOperationException("Usage benchmark requires every synthetic app in the active view.");
        Measure("usage-live-frame");
        if (window.UsageView.Rows.Items.Count != usageRows)
            throw new InvalidOperationException("Usage benchmark rows changed during its fixed observation.");
        var usagePixels = Pixels(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!,
            Path.GetFileNameWithoutExtension(args[0]) + "-usage.png"));
        MeasureChartRenders();
        var report = new
        {
            LoggingEnabled = diagnosticLog is not null,
            Runtime = Environment.Version.ToString(), Samples = 600, ProcessesPerSample = Processes,
            WarmupFrames, Batches, FramesPerBatch,
            AssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))),
            Results = results,
            EnabledNetworkViewportWidth = networkViewportWidth,
            EnabledNetworkColumnFullyVisible = true,
            LivePixelsSha256 = pixels,
            UsageRowCount = usageRows, UsagePixelsSha256 = usagePixels,
            GuideModes = 4, GuideRenderWidth = 800, GuideRenderHeight = 240, GuideSamples = 120,
            GuideRenderFrames = WarmupFrames + Batches * FramesPerBatch,
            Limitation = "UI thread only: refresh, layout, OnRender and channel commit, measured as thread cycles. Excludes native collection and the WPF render thread."
        };
        File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(results));
        window.CloseForFixture();
        app.Shutdown();

        // Only the refresh and the dispatcher work it causes are measured, not building the synthetic sample.
        (ulong Cycles, long Bytes) Frame()
        {
            if (!fixedConnectionHistory) AddSample();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            QueryThreadCycleTime(thread, out var before);
            window.RefreshPresentation();
            Flush();
            QueryThreadCycleTime(thread, out var after);
            return (after - before, GC.GetAllocatedBytesForCurrentThread() - allocated);
        }

        void MeasureChartRenders(bool metricBrushes = false)
        {
            const int width = 800, height = 240;
            var samples = Enumerable.Range(0, 120).Select(index => new MonitorSample(
                anchor.AddSeconds(index), index * 7 % 100, [], 0,
                MonotonicTimestamp: (100 + index) * Stopwatch.Frequency)
            {
                Resources = new ResourceSample
                {
                    TotalMemoryBytes = 100, UsedMemoryBytes = 65, DiskActivePercent = 30,
                    SecondaryDiskActivePercent = 20, ReceiveBytesPerSecond = 100_000,
                    SendBytesPerSecond = 50_000, GpuPercent = 40
                }
            }).ToArray();
            string[] chartKeys = ["CpuChartAccent", "MemoryChartAccent", "DiskChartAccent", "NetworkChartAccent",
                "GpuChartAccent", "SecondaryDiskChartAccent"];
            SolidColorBrush[] chartBrushes = [Brushes.Red, Brushes.Lime, Brushes.Blue, Brushes.Yellow, Brushes.Magenta, Brushes.Cyan];
            (ResourceChartType Mode, string Name, int? TileMetric)[] cases = metricBrushes
                ? [(ResourceChartType.Overlay, "selected-overlay", null),
                    (ResourceChartType.Overlay, "tile-0", 0), (ResourceChartType.Overlay, "tile-1", 1),
                    (ResourceChartType.Overlay, "tile-2", 2), (ResourceChartType.Overlay, "tile-3", 3),
                    (ResourceChartType.Overlay, "tile-4", 4), (ResourceChartType.Overlay, "tile-5", 5)]
                : [(ResourceChartType.Overlay, "overlay", null), (ResourceChartType.Focus, "focus-network", null),
                    (ResourceChartType.SmallMultiples, "small-multiples", null), (ResourceChartType.Heatmap, "heatmap", null)];
            foreach (var (mode, name, tileMetric) in cases)
            {
                var selected = metricBrushes && tileMetric is null ? samples[61] : null;
                var graph = new WinMon.Controls.CpuGraph
                {
                    ShowGrid = tileMetric is null, ChartType = mode, VisibleMetrics = 63, FocusMetric = 3,
                    TileMetric = tileMetric
                };
                graph.Resources["PlotSurface"] = Brushes.Black;
                graph.Resources["Line"] = Brushes.CornflowerBlue;
                graph.Resources["Muted"] = Brushes.White;
                if (metricBrushes)
                {
                    for (var metric = 0; metric < chartKeys.Length; metric++)
                        graph.Resources[chartKeys[metric]] = chartBrushes[metric];
                    graph.Resources["Raised"] = Brushes.Black;
                }
                graph.Measure(new Size(width, height));
                graph.Arrange(new Rect(0, 0, width, height));
                RenderTargetBitmap bitmap = null!;
                (ulong Cycles, long Bytes) Render()
                {
                    var allocated = GC.GetAllocatedBytesForCurrentThread();
                    QueryThreadCycleTime(thread, out var before);
                    graph.SetData(samples, 120, selected);
                    graph.UpdateLayout();
                    bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(graph);
                    QueryThreadCycleTime(thread, out var after);
                    return (after - before, GC.GetAllocatedBytesForCurrentThread() - allocated);
                }

                for (var frame = 0; frame < WarmupFrames; frame++) Render();
                var times = new List<double>();
                var allocations = new List<double>();
                for (var batch = 0; batch < Batches; batch++)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    ulong cycles = 0;
                    long bytes = 0;
                    for (var frame = 0; frame < FramesPerBatch; frame++)
                    {
                        var measured = Render();
                        cycles += measured.Cycles;
                        bytes += measured.Bytes;
                    }
                    times.Add(cycles / cyclesPerMs / FramesPerBatch);
                    allocations.Add(bytes / (double)FramesPerBatch);
                }
                var drawings = ChartDrawings(VisualTreeHelper.GetDrawing(graph)).OfType<GeometryDrawing>().ToArray();
                var selectionMarkers = drawings.Count(drawing => drawing.Geometry is EllipseGeometry && drawing.Pen is not null);
                var traces = drawings.Count(drawing => drawing.Geometry is StreamGeometry && drawing.Brush is null && drawing.Pen is not null);
                if (metricBrushes)
                {
                    if (selectionMarkers != (selected is null ? 0 : 6) || traces != (selected is null ? 1 : 6))
                        throw new InvalidOperationException("Metric brush benchmark requires every fixed selection outline or tile trace.");
                    var colors = drawings.Where(drawing => selected is null
                            ? drawing.Geometry is StreamGeometry && drawing.Brush is null && drawing.Pen is not null
                            : drawing.Geometry is EllipseGeometry && drawing.Pen is not null)
                        .Select(drawing => ((SolidColorBrush)drawing.Pen.Brush).Color);
                    var expectedColors = tileMetric is { } metric
                        ? new[] { chartBrushes[metric].Color } : chartBrushes.Select(brush => brush.Color);
                    if (!colors.SequenceEqual(expectedColors))
                        throw new InvalidOperationException("Metric brush benchmark requires the six fixed resource colors.");
                }
                else
                {
                    var guides = drawings.Where(drawing => drawing.Geometry is LineGeometry &&
                        drawing.Pen?.Brush is SolidColorBrush brush && brush.Color == Colors.CornflowerBlue).ToArray();
                    var horizontal = mode is ResourceChartType.Overlay or ResourceChartType.Focus ? 5 : 0;
                    if (guides.Count(drawing => drawing.Pen.Thickness == 0.5) != 5 ||
                        guides.Count(drawing => drawing.Pen.Thickness == 0.7) != horizontal)
                        throw new InvalidOperationException("Direct chart benchmark requires the actual mode-specific guides.");
                }
                var prefix = metricBrushes ? "metrics" : "guides";
                var pixels = new byte[width * height * 4];
                bitmap.CopyPixels(pixels, width * 4, 0);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!,
                           Path.GetFileNameWithoutExtension(args[0]) + $"-{prefix}-{name}.png"))) encoder.Save(stream);
                results.Add(new
                {
                    Name = $"cpu-{(metricBrushes ? "metric-brush" : "guides")}-{name}-render", MedianMilliseconds = Median(times),
                    MedianAllocatedBytes = Median(allocations), MillisecondsPerFrame = times,
                    AllocatedBytesPerFrame = allocations, PixelsSha256 = Convert.ToHexString(SHA256.HashData(pixels)),
                    SelectionMarkers = selectionMarkers, Traces = traces
                });
            }
        }

        static IEnumerable<Drawing> ChartDrawings(Drawing drawing)
        {
            if (drawing is DrawingGroup group)
                foreach (var child in group.Children)
                    foreach (var leaf in ChartDrawings(child)) yield return leaf;
            else yield return drawing;
        }

        // The usage ledger runs inside Record, which the refresh scenarios leave out, so it is timed on its own:
        // one Add per synthetic 850-process sample, excluding building the sample.
        void MeasureUsageLedger(bool includeHistory = false)
        {
            var ledger = new UsageLedger(anchor);
            var usageHistory = includeHistory ? new UsageHistory() : null;
            var ledgerSecond = 0;
            (ulong Cycles, long Bytes) Add()
            {
                var sample = Sample(anchor, ++ledgerSecond);
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                QueryThreadCycleTime(thread, out var before);
                ledger.Add(sample);
                usageHistory?.Add(sample);
                QueryThreadCycleTime(thread, out var after);
                return (after - before, GC.GetAllocatedBytesForCurrentThread() - allocated);
            }

            for (var i = 0; i < WarmupFrames; i++) Add();
            var times = new List<double>();
            var allocations = new List<double>();
            for (var batch = 0; batch < Batches; batch++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                ulong cycles = 0;
                long bytes = 0;
                for (var i = 0; i < FramesPerBatch; i++)
                {
                    var (addCycles, addBytes) = Add();
                    cycles += addCycles;
                    bytes += addBytes;
                }
                allocations.Add(bytes / (double)FramesPerBatch);
                times.Add(cycles / cyclesPerMs / FramesPerBatch);
            }
            results.Add(new { Name = includeHistory ? "usage-ledger-and-history-add" : "usage-ledger-add", MedianMilliseconds = Median(times),
                MedianAllocatedBytes = Median(allocations), MillisecondsPerFrame = times,
                AllocatedBytesPerFrame = allocations, PixelsSha256 = (string?)null });
        }

        // Trends run for every live full sample, so measure the fixed-size aggregation independently of WPF refresh.
        // The archive keeps only resource readings: no process rows are copied into this path.
        void MeasureResourceTrendArchive()
        {
            var archive = new ResourceTrendArchive();
            var trendSecond = 0;
            (ulong Cycles, long Bytes) Add()
            {
                var sample = Sample(anchor, ++trendSecond) with { MonotonicTimestamp = trendSecond * Stopwatch.Frequency };
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                QueryThreadCycleTime(thread, out var before);
                archive.Add(sample);
                QueryThreadCycleTime(thread, out var after);
                return (after - before, GC.GetAllocatedBytesForCurrentThread() - allocated);
            }

            for (var i = 0; i < WarmupFrames; i++) Add();
            var times = new List<double>();
            var allocations = new List<double>();
            for (var batch = 0; batch < Batches; batch++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                ulong cycles = 0;
                long bytes = 0;
                for (var i = 0; i < FramesPerBatch; i++)
                {
                    var (addCycles, addBytes) = Add();
                    cycles += addCycles;
                    bytes += addBytes;
                }
                allocations.Add(bytes / (double)FramesPerBatch);
                times.Add(cycles / cyclesPerMs / FramesPerBatch);
            }

            // Fill seven days separately so persistence and snapshot timing are not dominated by process fixtures.
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var memoryBefore = GC.GetTotalMemory(true);
            var full = new ResourceTrendArchive();
            for (var minute = 1; minute <= ResourceTrendArchive.RetainedMinutes; minute++)
                full.Add(TrendSample(anchor.AddMinutes(minute)));
            var retainedBytes = GC.GetTotalMemory(true) - memoryBefore;
            var end = anchor.AddMinutes(ResourceTrendArchive.RetainedMinutes + 1);
            var snapshotStart = Stopwatch.GetTimestamp();
            var state = full.Snapshot(end);
            var snapshotMilliseconds = Stopwatch.GetElapsedTime(snapshotStart).TotalMilliseconds;
            var rangeStart = Stopwatch.GetTimestamp();
            var buckets = full.GetBuckets(end, TimeSpan.FromDays(7));
            var rangeMilliseconds = Stopwatch.GetElapsedTime(rangeStart).TotalMilliseconds;
            MeasureResourceTrendView(state, end);
            MeasureResourceTrendView(state, end, wide: true);
            var directory = Path.Combine(Path.GetTempPath(), "WinMon.Performance", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "resource-trends.json");
            var saves = new List<double>();
            var reads = new List<double>();
            long fileBytes = 0;
            var restoredBuckets = 0;
            try
            {
                for (var run = 0; run < 5; run++)
                {
                    var start = Stopwatch.GetTimestamp();
                    ResourceTrendArchive.SaveAsync(path, state).GetAwaiter().GetResult();
                    saves.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    fileBytes = new FileInfo(path).Length;
                    start = Stopwatch.GetTimestamp();
                    var restored = ResourceTrendArchive.LoadAsync(path).GetAwaiter().GetResult();
                    reads.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    restoredBuckets = restored.GetBuckets(end, TimeSpan.FromDays(7)).Count;
                }
            }
            finally { Directory.Delete(directory, true); }

            results.Add(new
            {
                Name = "resource-trend-archive",
                MedianMilliseconds = Median(times),
                MedianAllocatedBytes = Median(allocations),
                MillisecondsPerFrame = times,
                AllocatedBytesPerFrame = allocations,
                RetainedBuckets = buckets.Count,
                RetainedBytes = retainedBytes,
                SnapshotMilliseconds = snapshotMilliseconds,
                RangeMilliseconds = rangeMilliseconds,
                FileBytes = fileBytes,
                RestoredBuckets = restoredBuckets,
                MedianSaveMilliseconds = Median(saves),
                MedianReadMilliseconds = Median(reads),
                SaveMilliseconds = saves,
                ReadMilliseconds = reads,
                PixelsSha256 = (string?)null
            });

            static MonitorSample TrendSample(DateTimeOffset timestamp) => new(timestamp, 42, [], 0)
            {
                IntervalSeconds = 1,
                Resources = new ResourceSample
                {
                    TotalMemoryBytes = 32_000_000_000,
                    UsedMemoryBytes = 18_000_000_000,
                    DiskName = "C:",
                    DiskActivePercent = 32,
                    DiskReadBytesPerSecond = 1_000_000,
                    DiskWriteBytesPerSecond = 2_000_000,
                    SecondaryDiskName = "D:",
                    SecondaryDiskActivePercent = 12,
                    SecondaryDiskReadBytesPerSecond = 3_000_000,
                    SecondaryDiskWriteBytesPerSecond = 4_000_000,
                    NetworkId = "fixture-network",
                    NetworkName = "Fixture Ethernet",
                    ReceiveBytesPerSecond = 5_000_000,
                    SendBytesPerSecond = 6_000_000,
                    GpuId = "fixture-gpu",
                    GpuName = "Fixture GPU",
                    GpuPercent = 24
                }
            };

            // The seven-day chart bins its full archive into screen columns. Measure selecting different minutes
            // through the real History window so layout, rendering and the selection summary stay in the budget.
            void MeasureResourceTrendView(ResourceTrendArchiveState trendState, DateTimeOffset trendEnd, bool wide = false)
            {
                var trendWindow = new WinMon.Controls.FlightRecorderWindow([])
                {
                    Width = wide ? 3000 : 1000,
                    Height = 760
                };
                try
                {
                    trendWindow.Show();
                    trendWindow.UpdateTrends(trendState);
                    trendWindow.HistoryMode.SelectedIndex = 1;
                    trendWindow.TrendRange.SelectedIndex = wide ? 0 : 1;
                    trendWindow.TrendMetric.SelectedIndex = 1;
                    Flush();
                    void Render(int offset)
                    {
                        trendWindow.SelectTrendMinuteForFixture(trendEnd.AddMinutes(-offset));
                        trendWindow.UpdateLayout();
                        Flush();
                    }

                    for (var i = 0; i < 100; i++) Render(i % (7 * 24 * 60));
                    var times = new List<double>();
                    var allocations = new List<double>();
                    for (var batch = 0; batch < 5; batch++)
                    {
                        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                        ulong cycles = 0;
                        long bytes = 0;
                        for (var i = 0; i < 20; i++)
                        {
                            var allocated = GC.GetAllocatedBytesForCurrentThread();
                            QueryThreadCycleTime(thread, out var before);
                            Render(batch * 20 + i);
                            QueryThreadCycleTime(thread, out var after);
                            cycles += after - before;
                            bytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
                        }
                        allocations.Add(bytes / 20d);
                        times.Add(cycles / cyclesPerMs / 20d);
                    }
                    results.Add(new
                    {
                        Name = wide ? "resource-trend-24-hour-wide-render-selection" : "resource-trend-seven-day-render-selection",
                        MedianMilliseconds = Median(times),
                        MedianAllocatedBytes = Median(allocations),
                        MillisecondsPerFrame = times,
                        AllocatedBytesPerFrame = allocations,
                        PixelsSha256 = TrendPixels(trendWindow)
                    });
                }
                finally { trendWindow.Close(); }
            }

            static string TrendPixels(Window trendWindow)
            {
                System.Windows.Input.FocusManager.SetFocusedElement(trendWindow, null);
                System.Windows.Input.Keyboard.ClearFocus();
                var content = (FrameworkElement)trendWindow.Content;
                var dpi = VisualTreeHelper.GetDpi(trendWindow);
                var bitmap = new RenderTargetBitmap((int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
                return Convert.ToHexString(SHA256.HashData(bytes));
            }
        }

        // A full 60-minute history at the 500,000-entry budget, saved and read as an update restart would, with
        // the real clock and deadline: RestoredSamples below the history's count means the save ran out of time.
        void MeasureRestartHistory()
        {
            var full = new MonitorHistory(3600);
            var options = window.CurrentSamplingOptions();
            for (var s = 1; s <= 3600; s++)
                full.Add(ProcessRetention.Compact(Sample(anchor, s), 32, options.Retain, options.RetainAll));
            var samples = full.Samples;
            var usage = session.Usage.Export();
            var directory = Path.Combine(Path.GetTempPath(), "WinMon.Performance", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, RestartHistory.FileName);
            var saves = new List<double>();
            var reads = new List<double>();
            long bytes = 0;
            var restored = 0;
            try
            {
                for (var run = 0; run < 5; run++)
                {
                    var start = Stopwatch.GetTimestamp();
                    var saved = RestartHistory.Save(path, "benchmark", AppInfo.Version, samples, usage,
                        RestartClock.System);
                    saves.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    bytes = saved ? new FileInfo(path).Length : 0;
                    start = Stopwatch.GetTimestamp();
                    var contents = RestartHistory.Read(path, "benchmark", RestartClock.System);
                    reads.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    restored = contents?.Samples.Count ?? 0;
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }

            results.Add(new { Name = "restart-history-60-minutes", Samples = samples.Count,
                ProcessEntries = samples.Sum(sample => sample.Processes.Count), full.WasMemoryLimited,
                FileBytes = bytes, RestoredSamples = restored, MedianSaveMilliseconds = Median(saves),
                MedianReadMilliseconds = Median(reads), SaveMilliseconds = saves, ReadMilliseconds = reads });

            path = Path.Combine(directory, RestartHistory.NormalFileName);
            Directory.CreateDirectory(directory);
            var normalSaves = new List<double>();
            var normalReads = new List<double>();
            try
            {
                for (var run = 0; run < 5; run++)
                {
                    var start = Stopwatch.GetTimestamp();
                    var saved = RestartHistory.SaveNormal(path, AppInfo.Version, samples, RestartClock.System);
                    normalSaves.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    bytes = saved ? new FileInfo(path).Length : 0;
                    start = Stopwatch.GetTimestamp();
                    var contents = RestartHistory.ReadNormal(path, RestartClock.System,
                        samples[^1].Timestamp.AddSeconds(1), 3600);
                    normalReads.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    restored = contents?.Samples.Count ?? 0;
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
            results.Add(new { Name = "normal-history-60-minutes", Samples = samples.Count,
                FileBytes = bytes, RestoredSamples = restored, MedianSaveMilliseconds = Median(normalSaves),
                MedianReadMilliseconds = Median(normalReads), SaveMilliseconds = normalSaves,
                ReadMilliseconds = normalReads });
        }

        void MeasureInvestigationHistory()
        {
            var recording = new MonitorSession(() => throw new InvalidOperationException(),
                () => new SamplingOptions(), action => action(), retainUsageHistory: true);
            recording.History.Resize(600);
            var tree = new ServiceTree();
            for (var s = 1; s <= 600; s++)
            {
                var full = Sample(anchor, s);
                recording.Record(ProcessRetention.Compact(full), full, tree.Capture(full, catalog));
            }
            var state = recording.CaptureInvestigation();
            var path = Path.Combine(Path.GetTempPath(), "WinMon-investigation-" + Guid.NewGuid() + ".bin");
            var saves = new List<double>();
            var reads = new List<double>();
            var restored = 0;
            long bytes = 0;
            try
            {
                for (var run = 0; run < 5; run++)
                {
                    var start = Stopwatch.GetTimestamp();
                    var saved = RestartHistory.SaveNormal(path, AppInfo.Version, recording.History.Samples,
                        RestartClock.System, state);
                    saves.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    bytes = saved ? new FileInfo(path).Length : 0;
                    start = Stopwatch.GetTimestamp();
                    var contents = RestartHistory.ReadNormal(path, RestartClock.System, anchor.AddSeconds(601), 600);
                    reads.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    var copy = new MonitorSession(() => throw new InvalidOperationException(),
                        () => new SamplingOptions(), action => action(), retainUsageHistory: true);
                    if (contents is { } history)
                    {
                        foreach (var sample in history.Samples) copy.Record(sample);
                        copy.RestoreInvestigation(history.Investigation);
                    }
                    restored = copy.History.Samples.Count;
                    if (!saved || restored == 0 || copy.UsageHistory.LatestTimestamp is null)
                        throw new InvalidOperationException("Investigation history did not survive its save/read budget.");
                }
            }
            finally { File.Delete(path); }
            results.Add(new { Name = "investigation-history-10-minutes", Samples = recording.History.Samples.Count,
                RestoredSamples = restored, FileBytes = bytes, MedianSaveMilliseconds = Median(saves),
                MedianReadMilliseconds = Median(reads), SaveMilliseconds = saves, ReadMilliseconds = reads });
        }

        void Measure(string name)
        {
            for (var i = 0; i < WarmupFrames; i++) Frame();
            var times = new List<double>();
            var allocations = new List<double>();
            for (var batch = 0; batch < Batches; batch++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                ulong cycles = 0;
                long bytes = 0;
                for (var i = 0; i < FramesPerBatch; i++)
                {
                    var (frameCycles, frameBytes) = Frame();
                    cycles += frameCycles;
                    bytes += frameBytes;
                }
                allocations.Add(bytes / (double)FramesPerBatch);
                times.Add(cycles / cyclesPerMs / FramesPerBatch);
            }
            results.Add(new { Name = name, MedianMilliseconds = Median(times), MedianAllocatedBytes = Median(allocations),
                MillisecondsPerFrame = times, AllocatedBytesPerFrame = allocations, PixelsSha256 = Pixels(null) });
        }

        void RequireNetworkColumnVisible()
        {
            var view = window.ConnectionView;
            var header = view.NetworkHeader;
            var bounds = header.TransformToAncestor(view.HeaderScroller)
                .TransformBounds(new Rect(new Point(), header.RenderSize));
            if (!header.IsVisible || bounds.Left < -0.1 || bounds.Right > view.HeaderScroller.ViewportWidth + 0.1)
                throw new InvalidOperationException("Network benchmark requires the full column inside the viewport.");
        }
        void Check(System.Windows.Controls.Primitives.ToggleButton toggle)
        {
            toggle.IsChecked = true;
            Flush();
        }

        // Two pins and one selection, each a fixture lifetime that ranks in some frames and not others.
        void PinAndSelect(bool grouped)
        {
            window.Processes.ToggleProcessPin(Fixture(3), grouped);
            window.Processes.ToggleProcessPin(Fixture(100), grouped);
            window.Processes.SelectProcess(Fixture(50), grouped);
            Flush();
        }

        string Pixels(string? path)
        {
            System.Windows.Input.FocusManager.SetFocusedElement(window, null);
            System.Windows.Input.Keyboard.ClearFocus();
            Flush();
            if (System.Windows.Input.Keyboard.FocusedElement is not null)
                throw new InvalidOperationException("Benchmark pixel capture requires cleared keyboard focus.");
            var dpi = VisualTreeHelper.GetDpi(window);
            var content = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(content);
            if (path is not null)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(path);
                encoder.Save(stream);
            }
            var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
    }

    private static ProcessUsage Fixture(int i) => new(100000 + i, i + 1, $"fixture-{i % 120}", 0)
    {
        ImagePath = $@"C:\Program Files\Fixture\fixture-{i % 120}.exe"
    };

    // Shared hosts are the first fixture processes, each with four running services; later processes are
    // their worker children. The rest of the services are stopped.
    private static ServiceCatalog Catalog()
    {
        var services = new ServiceObservation[Services];
        for (var i = 0; i < Services; i++)
        {
            var running = i < ServiceHosts * 4;
            services[i] = new ServiceObservation($"Fixture{i:000}", $"Fixture service {i:000}", running ? 4u : 1u,
                running ? 100000 + i % ServiceHosts : 0, running ? 1u : 0u);
        }
        return new ServiceCatalog(long.MaxValue, services);
    }

    // CPU rank rotates, so rows change identity and every numeric cell changes text each frame.
    private static MonitorSample Sample(DateTimeOffset anchor, int second)
    {
        var processes = new ProcessUsage[Processes];
        for (var i = 0; i < Processes; i++)
        {
            var active = (i + second * 7) % 14 == 0;
            processes[i] = new ProcessUsage(100000 + i, i + 1, $"fixture-{i % 120}",
                active ? ((i * 13 + second * 7) % 500 + 1) / 100d : 0, (ulong)(20_000_000 + (i * 7919 + second * 104729) % 400_000_000),
                active ? (i * 31 + second) % 5_000_000 : 0)
            {
                PrivateBytes = (ulong)(10_000_000 + (i * 104729 + second) % 300_000_000),
                ThreadCount = 4 + (i * 7 + second) % 90,
                HandleCount = 100 + (i * 13 + second) % 4000,
                ParentProcessId = i >= 600 ? 100000 + i % ServiceHosts : 0,
                GpuPercent = i % 40 == 0 ? (i + second) % 30 : null,
                GpuEngine = i % 40 == 0 ? "3D" : "",
                GpuId = i % 40 == 0 ? "luid_0x0_0x1/0" : "",
                ImagePath = $@"C:\Program Files\Fixture\fixture-{i % 120}.exe",
                AppName = i % 3 == 0 ? $"Fixture App {i % 120}" : "",
                HasAppWindow = i % 9 == 0
            };
        }
        Array.Sort(processes, (a, b) => b.CpuPercent.CompareTo(a.CpuPercent));
        return new MonitorSample(anchor.AddSeconds(second), 20 + second % 50, processes, 0, false, Stopwatch.Frequency * second)
        {
            IntervalSeconds = 1,
            IntervalCpuTicks = 12 * TimeSpan.TicksPerSecond,
            LogicalProcessorPercents = Enumerable.Range(0, 12)
                .Select(processor => (double?)((second * (processor + 3) + processor * 11) % 100)).ToArray(),
            Resources = new ResourceSample
            {
                TotalMemoryBytes = 32_000_000_000, UsedMemoryBytes = (ulong)(16_000_000_000 + second * 1_000_000L % 4_000_000_000),
                DiskActivePercent = second % 100, DiskName = "C:", DiskBytesPerSecond = second * 10_000 % 50_000_000,
                NetworkId = "fixture", NetworkName = "Ethernet", LinkBitsPerSecond = 1_000_000_000,
                ReceiveBytesPerSecond = second * 1000 % 3_000_000, SendBytesPerSecond = second * 300 % 1_000_000,
                GpuPercent = second % 60, GpuId = "luid_0x0_0x1/0", GpuEngine = "3D"
            }
        };
    }

    // Run layout and rendering, and everything queued above idle priority, as the dispatcher would.
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);

    private static double Median(List<double> values) => values.Order().ElementAt(values.Count / 2);

    private static double CyclesPerMillisecond(nint thread)
    {
        QueryThreadCycleTime(thread, out var start);
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 200) { }
        QueryThreadCycleTime(thread, out var end);
        return (end - start) / timer.Elapsed.TotalMilliseconds;
    }

    // The Application constructor queues App.OnStartup, which would start a normal session or activate a running
    // instance. WPF skips that callback while shutting down, so report shutdown until it has been dequeued.
    // IsShuttingDown is internal to WPF rather than WinMon, so this is the benchmark's one use of reflection.
    private static void SkipStartup()
    {
        var shuttingDown = typeof(Application).GetProperty("IsShuttingDown",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        shuttingDown.SetValue(null, true);
        Flush();
        shuttingDown.SetValue(null, false);
    }

    [DllImport("kernel32.dll")] private static extern nint GetCurrentThread();
    [DllImport("kernel32.dll")] private static extern bool QueryThreadCycleTime(nint thread, out ulong cycles);
}
