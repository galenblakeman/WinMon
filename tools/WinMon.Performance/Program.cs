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
        Measure("live-frame");
        window.SelectedSample = history.Samples[^60];
        Measure("historical-frame");
        window.SelectedSample = null;
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
        Check(window.Processes.IndividualChip);
        window.RefreshPresentation();
        Flush();
        var pixels = Pixels(args.Length > 1 ? args[1] : null);
        var report = new
        {
            Runtime = Environment.Version.ToString(), Samples = 600, ProcessesPerSample = Processes,
            WarmupFrames, Batches, FramesPerBatch,
            AssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))),
            Results = results,
            LivePixelsSha256 = pixels,
            Limitation = "UI thread only: refresh, layout, OnRender and channel commit, measured as thread cycles. Excludes native collection and the WPF render thread."
        };
        File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(results));
        window.CloseForFixture();
        app.Shutdown();

        // Only the refresh and the dispatcher work it causes are measured, not building the synthetic sample.
        (ulong Cycles, long Bytes) Frame()
        {
            AddSample();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            QueryThreadCycleTime(thread, out var before);
            window.RefreshPresentation();
            Flush();
            QueryThreadCycleTime(thread, out var after);
            return (after - before, GC.GetAllocatedBytesForCurrentThread() - allocated);
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
