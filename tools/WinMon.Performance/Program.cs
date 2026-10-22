using System.Diagnostics;
using System.IO;
using System.Reflection;
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
// render pass the dispatcher runs once a second. Sampling is stopped; history is synthetic.
// usage: WinMon.Performance <result.json> [screenshot.png]
internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private const int Processes = 850;
    private const int WarmupFrames = 400;
    private const int Batches = 7;
    private const int FramesPerBatch = 60;

    [STAThread]
    private static void Main(string[] args)
    {
        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // The Application constructor queues App.OnStartup, which would start a normal session or activate a running
        // instance. WPF skips that callback while shutting down, so report shutdown until it has been dequeued.
        var shuttingDown = typeof(Application).GetProperty("IsShuttingDown", Static)!;
        shuttingDown.SetValue(null, true);
        Flush();
        shuttingDown.SetValue(null, false);
        var window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), Private, null, [true, null, null], null)!;
        var history = (CpuHistory)Field("_history").GetValue(window)!;
        var compact = typeof(CpuHistory).Assembly.GetType("WinMon.Monitoring.ProcessRetention", true)!.GetMethod("Compact", Static)!;
        var refresh = (Action)typeof(MainWindow).GetMethod("RefreshPresentation", Private)!.CreateDelegate(typeof(Action), window);
        var anchor = DateTimeOffset.Parse("2026-10-19T12:00:00Z");
        var second = 0;
        void AddSample()
        {
            second++;
            var full = Sample(anchor, second);
            Field("_latestFull").SetValue(window, full);
            var compacted = (CpuSample)compact.Invoke(null, [full, 32, null])!;
            Field("_latestCompacted").SetValue(window, compacted);
            history.Add(compacted);
        }
        window.Width = 1000;
        window.Height = 900;
        window.Show();
        Flush();
        // Stop the real sampler, then replace anything it recorded with synthetic history.
        ((CancellationTokenSource)Field("_lifetime").GetValue(window)!).Cancel();
        ((Task?)Field("_samplingTask").GetValue(window))?.ContinueWith(_ => { }).Wait();
        Flush();
        history.Resize(1);
        history.Resize(600);
        for (var i = 0; i < 600; i++) AddSample();
        refresh();
        Flush();

        var thread = GetCurrentThread();
        var cyclesPerMs = CyclesPerMillisecond(thread);
        var results = new List<object>();
        Measure("live-frame");
        Field("_selectedSample").SetValue(window, history.Samples[^60]);
        Measure("historical-frame");
        Field("_selectedSample").SetValue(window, null);
        refresh();
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
        Field("_allowClose").SetValue(window, true);
        window.Close();
        app.Shutdown();

        // Only the refresh and the dispatcher work it causes are measured, not building the synthetic sample.
        (ulong Cycles, long Bytes) Frame()
        {
            AddSample();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            QueryThreadCycleTime(thread, out var before);
            refresh();
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
                MillisecondsPerFrame = times, AllocatedBytesPerFrame = allocations });
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

    // CPU rank rotates, so rows change identity and every numeric cell changes text each frame.
    private static CpuSample Sample(DateTimeOffset anchor, int second)
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
                GpuPercent = i % 40 == 0 ? (i + second) % 30 : null,
                GpuEngine = i % 40 == 0 ? "3D" : "",
                GpuId = i % 40 == 0 ? "luid_0x0_0x1/0" : "",
                ImagePath = $@"C:\Program Files\Fixture\fixture-{i % 120}.exe",
                AppName = i % 3 == 0 ? $"Fixture App {i % 120}" : "",
                HasAppWindow = i % 9 == 0
            };
        }
        Array.Sort(processes, (a, b) => b.CpuPercent.CompareTo(a.CpuPercent));
        return new CpuSample(anchor.AddSeconds(second), 20 + second % 50, processes, 0, false, Stopwatch.Frequency * second)
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

    private static FieldInfo Field(string name) => typeof(MainWindow).GetField(name, Private)!;

    [DllImport("kernel32.dll")] private static extern nint GetCurrentThread();
    [DllImport("kernel32.dll")] private static extern bool QueryThreadCycleTime(nint thread, out ulong cycles);
}
