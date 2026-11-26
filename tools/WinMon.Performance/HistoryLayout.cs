using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MonCore.Monitoring;
using WinMon.Controls;

internal static partial class Program
{
    private static void MeasureHistoryLayout(string output, IReadOnlyList<MonitorSample> samples)
    {
        var recorder = new FlightRecorderWindow(samples);
        recorder.Show();
        Flush();
        try
        {
            var bitmap = new RenderTargetBitmap((int)recorder.ActualWidth, (int)recorder.ActualHeight,
                96, 96, PixelFormats.Pbgra32);
            void Frame(int index)
            {
                recorder.Select(index % samples.Count);
                recorder.UpdateLayout();
                bitmap.Clear();
                bitmap.Render(recorder);
            }
            for (var index = 0; index < WarmupFrames; index++) Frame(index);
            var times = new double[Batches];
            var allocations = new double[Batches];
            for (var batch = 0; batch < Batches; batch++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var timer = Stopwatch.StartNew();
                for (var frame = 0; frame < FramesPerBatch; frame++) Frame(frame);
                timer.Stop();
                times[batch] = timer.Elapsed.TotalMilliseconds / FramesPerBatch;
                allocations[batch] = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)FramesPerBatch;
            }
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                Name = "history-selection-layout-render", WarmupFrames, Batches, FramesPerBatch,
                MedianMilliseconds = Median([.. times]), MedianAllocatedBytes = Median([.. allocations]),
                Limitation = "Synthetic History selection, layout and WPF content render; excludes native collection and compositor."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { recorder.Close(); }
    }
}
