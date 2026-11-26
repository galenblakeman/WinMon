using System.Diagnostics;
using System.Reflection;
using System.Windows;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Diagnostics;
using WinMon.Preferences;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase(GaugeStyle.None), TestCase(GaugeStyle.LeftBar), TestCase(GaugeStyle.BottomBar)]
    [TestCase(GaugeStyle.SegmentedBar), TestCase(GaugeStyle.Bowl), TestCase(GaugeStyle.Needle)]
    [TestCase(GaugeStyle.SegmentedArc)]
    public void GaugeCornerDiagnosticAcceptsEachOriginalStyleWithoutChangingReadout(GaugeStyle style)
    {
        CheckGaugeCorner(style, "valid", false);
    }

    [TestCase(GaugeStyle.Bowl, "text", true), TestCase(GaugeStyle.Bowl, "tooltip", true)]
    [TestCase(GaugeStyle.Bowl, "hidden", true), TestCase(GaugeStyle.Bowl, "collapsed", true)]
    [TestCase(GaugeStyle.Bowl, "icon", true)]
    [TestCase(GaugeStyle.None, "text", true), TestCase(GaugeStyle.None, "visible", true)]
    [TestCase(GaugeStyle.None, "icon", true), TestCase(GaugeStyle.None, "tooltip", false)]
    [TestCase(GaugeStyle.None, "hidden", false)]
    public void GaugeCornerDiagnosticPreservesTextTooltipVisibilityAndIconRules(GaugeStyle style, string partition, bool rejected)
    {
        CheckGaugeCorner(style, partition, rejected);
    }

    [Test]
    public void GaugeCornerDiagnosticPreservesTheOriginalNullReadoutFailure()
    {
        Func<(MetricReadout, string), bool> check = corner => WindowDiagnostics.InvalidGaugeCorner(GaugeStyle.Bowl, corner);
        Assert.Throws<NullReferenceException>(() => check((null!, "80%")));
    }

    private void CheckGaugeCorner(GaugeStyle style, string partition, bool rejected)
    {
        using var ui = new UpdateUi(_directory, true);
        var arc = style is GaugeStyle.Bowl or GaugeStyle.Needle or GaugeStyle.SegmentedArc;
        var readout = new MetricReadout { GaugeStyle = style };
        readout.ShowGauge(50, 80, 100, arc ? "80%" : "", arc ? "80%" : "", true);
        // Supply the original diagnostic's independent tooltip-identity inputs.
        readout.ToolTip = new object();
        readout.ScaleLabel.ToolTip = readout.ToolTip;
        if (partition == "icon")
        {
            readout.GaugeStyle = arc ? GaugeStyle.None : GaugeStyle.Bowl;
            readout.ShowGauge(50, 80, 100, "80%", "80%", true);
            readout.ScaleLabel.Text = arc ? "80%" : "";
            readout.ScaleLabel.Visibility = arc ? Visibility.Visible : Visibility.Collapsed;
            readout.ScaleLabel.ToolTip = readout.ToolTip;
            Assert.That(readout.PeakIconVisible, Is.EqualTo(!arc));
        }
        if (partition == "text") readout.ScaleLabel.Text = "wrong";
        if (partition == "tooltip") readout.ScaleLabel.ToolTip = new object();
        if (partition == "visible") readout.ScaleLabel.Visibility = Visibility.Visible;
        if (partition == "hidden") readout.ScaleLabel.Visibility = Visibility.Hidden;
        if (partition == "collapsed") readout.ScaleLabel.Visibility = Visibility.Collapsed;
        var before = (readout.ScaleLabel.Text, readout.ScaleLabel.ToolTip, readout.ToolTip,
            readout.ScaleLabel.Visibility, readout.PeakIconVisible, readout.GaugeStyle);
        Func<(MetricReadout, string), bool> check = corner => WindowDiagnostics.InvalidGaugeCorner(style, corner);

        Assert.That(check((readout, "80%")), Is.EqualTo(rejected));
        Assert.That(check((readout, "80%")), Is.EqualTo(rejected));
        Assert.That((readout.ScaleLabel.Text, readout.ScaleLabel.ToolTip, readout.ToolTip,
            readout.ScaleLabel.Visibility, readout.PeakIconVisible, readout.GaugeStyle), Is.EqualTo(before));
    }

    [TestCase(-1, 10, 10, 100_000, 5, 1_000_000)]
    [TestCase(0, 10, 10, 100_000, 5, 1_000_000)]
    [TestCase(9, 10, 10, 100_000, 5, 1_000_000)]
    [TestCase(10, 80, 90, 800_000, 70, 12_000_000)]
    [TestCase(19, 80, 90, 800_000, 70, 12_000_000)]
    [TestCase(20, 20, 15, 120_000, 10, 2_000_000)]
    [TestCase(29, 20, 15, 120_000, 10, 2_000_000)]
    [TestCase(30, 20, 15, 120_000, 10, 2_000_000)]
    public void RecorderComparisonDiagnosticPreservesPhaseBoundariesAndUnchangedInput(int second,
        double cpu, double disk, double receive, double processCpu, double processIo)
    {
        var originalProcess = new ProcessUsage(-1, 100, "original", 3);
        var resources = new ResourceSample { DiskName = "fixture", TotalMemoryBytes = 1234, GpuPercent = 17 };
        var incoming = new MonitorSample(DateTimeOffset.UnixEpoch.AddDays(2), 7, [originalProcess], 2,
            IsGap: true, MonotonicTimestamp: Stopwatch.Frequency * 100L)
        { Resources = resources, IndependentProcessReadings = true, CompactedProcesses = 4, IntervalSeconds = 2 };
        var makeSample = typeof(WindowDiagnostics).GetMethod("CreateFlightRecorderComparisonSample",
            BindingFlags.NonPublic | BindingFlags.Static)!.CreateDelegate<Func<MonitorSample, int, MonitorSample>>();
        Func<int, MonitorSample> make = second => makeSample(incoming, second);
        var result = make(second);
        Assert.Multiple(() =>
        {
            Assert.That(result.Timestamp, Is.EqualTo(incoming.Timestamp.AddSeconds(second)));
            Assert.That(result.MonotonicTimestamp, Is.EqualTo(incoming.MonotonicTimestamp + second * Stopwatch.Frequency));
            Assert.That(result.TotalCpuPercent, Is.EqualTo(cpu));
            Assert.That(result.Resources.DiskActivePercent, Is.EqualTo(disk));
            Assert.That(result.Resources.ReceiveBytesPerSecond, Is.EqualTo(receive));
            Assert.That(result.Resources.SendBytesPerSecond, Is.EqualTo(20_000));
            Assert.That(result.Resources.LinkBitsPerSecond, Is.EqualTo(100_000_000));
            Assert.That(result.Resources, Is.EqualTo(resources with { DiskActivePercent = disk,
                ReceiveBytesPerSecond = receive, SendBytesPerSecond = 20_000, LinkBitsPerSecond = 100_000_000 }));
            Assert.That(result.Processes, Has.Count.EqualTo(1));
            Assert.That(result.Processes[0], Is.EqualTo(new ProcessUsage(9000, 9000, "comparison-worker", processCpu,
                100_000_000, processIo)));
            Assert.That(result.IsGap, Is.True);
            Assert.That(result.UnavailableProcesses, Is.EqualTo(2));
            Assert.That(result.IndependentProcessReadings, Is.True);
            Assert.That(result.CompactedProcesses, Is.EqualTo(4));
            Assert.That(result.IntervalSeconds, Is.EqualTo(2));
            Assert.That(incoming.Resources, Is.SameAs(resources));
            Assert.That(incoming.Processes[0], Is.SameAs(originalProcess));
            Assert.That(incoming.TotalCpuPercent, Is.EqualTo(7));
        });
        var repeated = make(second);
        Assert.That(repeated, Is.EqualTo(result with { Processes = repeated.Processes }));
        Assert.That(repeated.Processes, Is.EqualTo(result.Processes));
    }

    private static T AnonymousDiagnostic<T>(string name, params object[] captures) where T : Delegate
    {
        // Retained for unrelated example diagnostics; gauge-corner checks use the named seam above.
        var method = typeof(WindowDiagnostics).GetNestedTypes(BindingFlags.NonPublic)
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Single(value => value.Name == name);
        return (T)method.CreateDelegate(typeof(T), Closure(method.DeclaringType!));

        object Closure(Type type)
        {
            var value = Activator.CreateInstance(type, nonPublic: true)!;
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var capture = captures.SingleOrDefault(field.FieldType.IsInstanceOfType);
                if (capture is not null) field.SetValue(value, capture);
                else if (field.FieldType.DeclaringType == typeof(WindowDiagnostics) &&
                         field.FieldType.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal))
                    field.SetValue(value, Closure(field.FieldType));
            }
            return value;
        }
    }
}
