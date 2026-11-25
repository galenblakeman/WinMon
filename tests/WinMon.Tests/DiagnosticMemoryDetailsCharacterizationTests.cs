using System.Reflection;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Diagnostics;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase(false), TestCase(true), Category("Integration"), SetCulture("en-US")]
    public void MemoryDiagnosticCapturesAvailableOrMissingPressureReadingsAndClosesDetails(bool missing)
    {
        using var fixture = new MemoryDetailsDiagnostic(_directory, missing);
        FinishWindowCheck(fixture.Start("readings", missing));
        var expected = missing ? Enumerable.Repeat(Strings.MissingReading, 5).ToArray()
            : new[] { "18.5/s", "3.0 GiB", "768.0 MiB", "512.0 MiB", "1.0 GiB" };
        Assert.That(fixture.Details.Rows.Skip(4).Take(5).Select(row => row.Value), Is.EqualTo(expected));
        fixture.AssertPng("readings");
        fixture.AssertClosed();
    }

    [TestCase("rows"), TestCase("label"), TestCase("missing"), TestCase("present"), TestCase("mixed")]
    public void MemoryDiagnosticRejectsOriginalModelContractViolationsAndClosesDetails(string partition)
    {
        using var fixture = new MemoryDetailsDiagnostic(_directory, partition == "missing", partition == "mixed");
        if (partition == "rows") fixture.Details.Rows.Add(new DetailRow { Label = "extra", Value = "extra" });
        if (partition == "label") fixture.Details.Rows[4].Label = "owned wrong label";
        var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Start("invalid", partition == "present")));
        Assert.That(error!.Message, Is.EqualTo("Memory pressure details lost their labels, values or unavailable state."));
        Assert.That(File.Exists(Path.Combine(_directory, "memory-pressure-invalid.png")), Is.False);
        fixture.AssertClosed();
    }

    [TestCase(false), TestCase(true)]
    public void MemoryDiagnosticCancellationClosesDetailsWithoutWritingAnImage(bool missing)
    {
        using var fixture = new MemoryDetailsDiagnostic(_directory, missing);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(fixture.Start("cancelled", missing, cancellation.Token)));
        Assert.That(File.Exists(Path.Combine(_directory, "memory-pressure-cancelled.png")), Is.False);
        fixture.AssertClosed();
    }

    [TestCase("missing-folder"), TestCase("locked-file"), Category("Integration")]
    public void MemoryDiagnosticWriteFailuresCloseDetailsAndPreserveExistingData(string failure)
    {
        var output = failure == "missing-folder" ? Path.Combine(_directory, "missing") : _directory;
        using var fixture = new MemoryDetailsDiagnostic(_directory, false, output: output);
        var path = Path.Combine(output, "memory-pressure-failure.png");
        if (failure == "locked-file") File.WriteAllText(path, "owned memory image sentinel");
        using var locked = failure == "locked-file" ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
        var error = Assert.Catch<IOException>(() => FinishWindowCheck(fixture.Start("failure", false)));
        Assert.That(error, failure == "missing-folder" ? Is.TypeOf<DirectoryNotFoundException>() : Is.TypeOf<IOException>());
        if (failure == "locked-file") Assert.That(File.ReadAllText(path), Is.EqualTo("owned memory image sentinel"));
        else Assert.That(Directory.Exists(output), Is.False);
        fixture.AssertClosed();
    }

    [Test, Category("Integration")]
    public void MemoryDiagnosticPreservesTheOriginalEmptyNameFilename()
    {
        using var fixture = new MemoryDetailsDiagnostic(_directory, true);
        FinishWindowCheck(fixture.Start("", true));
        fixture.AssertPng("");
        fixture.AssertClosed();
    }

    private sealed class MemoryDetailsDiagnostic : IDisposable
    {
        private readonly UpdateUi _ui;
        private readonly string _output;
        internal MetricDetails Details { get; }
        internal MemoryDetailsDiagnostic(string directory, bool missing, bool mixed = false, string? output = null)
        {
            _ui = new UpdateUi(directory, true);
            try
            {
                Directory.CreateDirectory(directory);
                _output = output ?? directory;
                _ui.Window.ApplyTheme(false, false);
                var resources = missing ? new ResourceSample() : new ResourceSample
                {
                    TotalMemoryBytes = 32UL * 1_073_741_824, UsedMemoryBytes = 16UL * 1_073_741_824,
                    PagingReadsPerSecond = mixed ? null : 18.5, StandbyBytes = 3UL * 1_073_741_824,
                    ModifiedBytes = 768UL * 1_048_576, FreeBytes = 512UL * 1_048_576,
                    CompressionStoreBytes = 1_073_741_824
                };
                _ui.Window.Session.Record(new MonitorSample(DateTimeOffset.UnixEpoch, 7, [], 0) { Resources = resources });
                Details = (MetricDetails)_ui.Window.MemoryMetric.ToolTip;
            }
            catch { _ui.Dispose(); throw; }
        }

        internal Task Start(string name, bool unavailable, CancellationToken token = default)
        {
            var method = typeof(WindowDiagnostics).GetMethod("CaptureCompactMemoryDetailsAsync",
                BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing compact memory-details diagnostic");
            var capture = method.CreateDelegate<Func<MainWindow, string, string, bool, CancellationToken, Task>>();
            return capture(_ui.Window, _output, name, unavailable, token);
        }

        internal void AssertPng(string name)
        {
            // The minimal fixture does not load App.xaml's MetricDetails template. Verify the
            // original capture/IO contract without claiming styled product rendering coverage.
            var path = Path.Combine(_output, $"memory-pressure-{name}.png");
            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.Single();
            Assert.That(frame.PixelWidth, Is.GreaterThan(0));
            Assert.That(frame.PixelHeight, Is.GreaterThan(0));
            Assert.That(frame.DpiX, Is.EqualTo(96).Within(.1));
            Assert.That(frame.DpiY, Is.EqualTo(96).Within(.1));
        }

        internal void AssertClosed()
        {
            Assert.That(_ui.Window.MemoryMetric.ToolTip, Is.SameAs(Details));
            Details.Rows[4].Value = "owned closed-details marker";
            _ui.Window.RefreshVisiblePresentation();
            Assert.That(Details.Rows[4].Value, Is.EqualTo("owned closed-details marker"),
                "Closed details must not be refreshed by subsequent presentation updates.");
            Assert.That(_ui.Window.Session.HasLiveSample, Is.False);
        }

        public void Dispose() => _ui.Dispose();
    }
}
