using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinMon;
using WinMon.Controls;
using WinMon.Monitoring;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static void Main(string[] args)
    {
        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), Private, null, [true], null)!;
        Field("paused").SetValue(window, 1);
        var history = (CpuHistory)Field("history").GetValue(window)!;
        var anchor = DateTimeOffset.Parse("2026-10-19T12:00:00Z");
        for (var second = 1; second <= 600; second++)
        {
            // CPU rank rotates, so the current top five occupied many historical positions.
            var processes = Enumerable.Range(0, 500)
                .Select(i => new ProcessUsage(100000 + i, i + 1, $"fixture-{i}",
                    ((i + second * 7) % 500 + 1) / 100d, 50_000_000, 1000))
                .OrderByDescending(p => p.CpuPercent).ToArray();
            history.Add(new CpuSample(anchor.AddSeconds(second), 35, processes, 0,
                second % 97 == 0, Stopwatch.Frequency * second)
            {
                Resources = new ResourceSample { TotalMemoryBytes = 32_000_000_000,
                    UsedMemoryBytes = 16_000_000_000, DiskActivePercent = 20,
                    DiskName = "C:", DiskBytesPerSecond = 1000, NetworkId = "fixture",
                    LinkBitsPerSecond = 1_000_000_000, ReceiveBytesPerSecond = 1000, SendBytesPerSecond = 2000 }
            });
        }
        var refresh = (Action)typeof(MainWindow).GetMethod("RefreshPresentation", Private)!.CreateDelegate(typeof(Action), window);
        window.Show();
        window.UpdateLayout();
        var graphs = new List<CpuGraph> { (CpuGraph)window.FindName("OverallGraph") };
        var rows = (ProcessRow[])Field("rows").GetValue(window)!;
        graphs.AddRange(rows.Select(r => (CpuGraph)typeof(ProcessRow).GetField("graph", Private)!.GetValue(r)!));
        var render = typeof(CpuGraph).GetMethod("OnRender", Private)!;
        var draw = new DrawingVisual();
        void Frame()
        {
            refresh();
            window.UpdateLayout();
            using var dc = draw.RenderOpen();
            foreach (var graph in graphs) render.Invoke(graph, [dc]);
        }

        var results = new List<object>();
        Measure("visible-refresh", refresh);
        Measure("visible-refresh-and-six-graph-renders", Frame);
        var livePixels = Pixels();
        Field("selectedSample").SetValue(window, history.Samples[479]);
        Measure("historical-refresh-and-six-graph-renders", Frame);
        var historicalPixels = Pixels();
        Field("selectedSample").SetValue(window, null);
        window.Hide();
        Measure("hidden-refresh", refresh);
        var report = new
        {
            Runtime = Environment.Version.ToString(), Samples = 600, ProcessesPerSample = 500,
            CompactWindowSeconds = 120, WarmupIterations = 30, Batches = 7, IterationsPerBatch = 100,
            AssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))),
            Results = results,
            LivePixelsSha256 = livePixels, HistoricalPixelsSha256 = historicalPixels,
            Limitation = "Deterministic STA UI microbenchmark; sampling paused, synthetic history, forced WPF drawing commands. Excludes native collection, compositor/GPU and sustained whole-process overhead."
        };
        File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Field("allowClose").SetValue(window, true);
        window.Close();
        app.Shutdown();

        string Pixels()
        {
            Frame();
            var bitmap = new RenderTargetBitmap(600, 400, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(draw);
            var bytes = new byte[600 * 400 * 4];
            bitmap.CopyPixels(bytes, 600 * 4, 0);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }

        void Measure(string name, Action action)
        {
            for (var i = 0; i < 30; i++) action();
            var times = new List<double>();
            var allocations = new List<double>();
            for (var batch = 0; batch < 7; batch++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var timer = Stopwatch.StartNew();
                for (var i = 0; i < 100; i++) action();
                timer.Stop();
                allocations.Add((GC.GetAllocatedBytesForCurrentThread() - allocated) / 100d);
                times.Add(timer.Elapsed.TotalMilliseconds / 100);
            }
            results.Add(new { Name = name, MedianMilliseconds = times.Order().ElementAt(3),
                MedianAllocatedBytes = allocations.Order().ElementAt(3), MillisecondsPerBatchIteration = times,
                AllocatedBytesPerBatchIteration = allocations });
        }
    }

    private static FieldInfo Field(string name) => typeof(MainWindow).GetField(name, Private)!;
}
