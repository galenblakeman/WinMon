using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ProcessRowUpdateCharacterizationTests
{
    private static ProcessUsage Usage(bool grouped = false) => new(int.MaxValue, 100, "worker.exe", 12.5,
        16UL * 1_048_576, 4_000_000)
    {
        AppName = "Friendly", ProductName = "Product", Publisher = "Publisher", PrivateBytes = 8UL * 1_048_576,
        GpuPercent = 25, GpuEngine = "3D", GpuId = "gpu0", ThreadCount = 47, HandleCount = 1234,
        HasAppWindow = true, MemberCount = grouped ? 3 : 1, MemberIds = grouped ? [41, 42, 43] : []
    };

    private static MonitorSample[] History(ProcessUsage usage) =>
        [new(DateTimeOffset.UnixEpoch.AddDays(1), 90, [usage], 0)];

    private static ProcessRow Row()
    {
        var row = new ProcessRow();
        row.Resources["Surface"] = Brushes.White;
        row.Resources["SectionHeaderSurface"] = Brushes.Gold;
        row.Resources["Text"] = Brushes.Black;
        row.Resources["Muted"] = Brushes.Gray;
        row.Resources["ProcessPinAccent"] = Brushes.Red;
        row.Resources["CaptionButton"] = new Style(typeof(Button));
        return row;
    }

    private static TextBlock[] Labels(ProcessRow row) => row.Children.OfType<Grid>().Single().Children
        .OfType<StackPanel>().Single().Children.OfType<TextBlock>().ToArray();
    private static Button Pin(ProcessRow row) => row.Children.OfType<Grid>().Single().Children.OfType<Button>().Single();
    private static TextBlock Cell(ProcessRow row, int column) => row.Children.OfType<TextBlock>()
        .Single(text => Grid.GetColumn(text) == column);
    private static string[] Readings(ProcessRow row) => Enumerable.Range(2, 6).Select(column => Cell(row, column).Text).ToArray();

    [Test]
    public void ResourceColumnsKeepGroupMemoryAndUnavailableValuesDistinct(
        [Values(false, true)] bool grouped, [Values(false, true)] bool missing)
    {
        var usage = Usage(grouped);
        if (missing) usage = usage with
        {
            WorkingSetBytes = null, PrivateBytes = null, GpuPercent = null, IoBytesPerSecond = null,
            ThreadCount = null, HandleCount = null
        };
        var row = Row();
        row.Update(usage, History(usage), 120, true, null, grouped);
        Assert.That(Readings(row), Is.EqualTo(missing
            ? new[] { "12.5%", Strings.MissingReading, Strings.MissingReading, Strings.MissingReading, Strings.MissingReading, Strings.MissingReading }
            : new[] { "12.5%", "25.0%", grouped ? "8.0 MiB" : "16.0 MiB", "4.0 MB/s", "47", "1,234" }));
        Assert.That(Cell(row, 4).ToolTip, Is.EqualTo(grouped
            ? Strings.PrivateBytesSummedAcrossThisExecutableSCurrentProcesses
            : Strings.ResidentWorkingSetIncludingSharedPagesDoesNotSumTo));
        Assert.That(Cell(row, 5).ToolTip, Is.EqualTo(Strings.ReadWriteIOIncludesFileNetworkAndDeviceOperations));
        Assert.That(Cell(row, 6).ToolTip, Is.EqualTo(Strings.ProcessThreadsExplanation));
        Assert.That(Cell(row, 7).ToolTip, Is.EqualTo(Strings.ProcessHandlesExplanation));
        Assert.That(Cell(row, 3).ToolTip, Is.EqualTo(missing
            ? Strings.GPUUtilizationUnavailableForThisProcessNoValidCounterOr
            : Strings.Format(Strings.BusiestEnginePerIntervalAveragedOverReadableIntervalsInThe, "3D", "gpu0")));
        var accessible = AutomationProperties.GetName(row);
        foreach (var value in Readings(row)) Assert.That(accessible, Does.Contain(value));
    }

    [Test]
    public void ZeroReadingsRemainAvailable([Values(false, true)] bool grouped)
    {
        var usage = Usage(grouped) with
        {
            CpuPercent = 0, GpuPercent = 0, WorkingSetBytes = 0, PrivateBytes = 0,
            IoBytesPerSecond = 0, ThreadCount = 0, HandleCount = 0
        };
        var row = Row();
        row.Update(usage, History(usage), 120, true, null, grouped);
        Assert.That(Readings(row), Is.EqualTo(new[] { "0.0%", "0.0%", "0 B", "0 B/s", "0", "0" }));
    }

    [Test]
    public void DisplayNamesKeepFallbackPrecedenceAndRawNameInTooltips(
        [Values("app", "product", "raw", "same")] string choice, [Values(false, true)] bool grouped)
    {
        var usage = Usage(grouped) with
        {
            AppName = choice switch { "app" => "Friendly", "same" => "worker.exe", _ => " \t" },
            ProductName = choice == "raw" ? "\t" : "Product"
        };
        var name = choice switch { "app" => "Friendly", "product" => "Product", _ => "worker.exe" };
        var display = name + (grouped ? " ×3" : "");
        var row = Row();
        row.Update(usage, History(usage), 120, true, null, grouped);
        Assert.That(Labels(row)[0].Text, Is.EqualTo(display));
        var expectedStart = display + (name == "worker.exe" ? "" : " (worker.exe)") + " · ";
        Assert.That(Labels(row)[0].ToolTip, Does.StartWith(expectedStart));
        Assert.That(row.ToolTip, Does.StartWith(expectedStart));
        Assert.That(Labels(row)[0].ToolTip, Does.EndWith("Publisher\n" + Strings.ExecutablePathUnavailable));
    }

    [Test]
    public void KindStaysVisibleWithOrWithoutPidAndGroupCount(
        [Values(null, false, true)] bool? app, [Values(false, true)] bool showPid,
        [Values(false, true)] bool grouped)
    {
        var usage = Usage(grouped) with { HasAppWindow = app };
        var row = Row();
        row.Update(usage, History(usage), 120, showPid, null, grouped);
        var kind = app is null ? Strings.Unknown : app.Value ? Strings.ProcessKindApp : Strings.ProcessKindBackground;
        var identity = grouped ? Strings.Format(Strings.Processes, 3) : Strings.Format(Strings.PID, int.MaxValue);
        Assert.That(Labels(row)[1].Text, Is.EqualTo(showPid ? identity + " · " + kind : kind));
        Assert.That(Labels(row)[1].Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(AutomationProperties.GetName(row), Does.Contain(" · " + kind + " · "));
    }

    [Test]
    public void PinAppearanceAndEveryLifetimeStateRecoverOnRowReuse(
        [Values(PinnedProcessState.Present, PinnedProcessState.Exited, PinnedProcessState.NotRecorded,
            PinnedProcessState.Unavailable)] PinnedProcessState state,
        [Values(false, true)] bool pinned, [Values(false, true)] bool grouped)
    {
        var usage = Usage(grouped);
        var row = Row();
        var calls = 0;
        row.EnablePinning((target, mode) =>
        {
            calls++;
            Assert.That(target, Is.SameAs(usage));
            Assert.That(mode, Is.EqualTo(grouped));
            Assert.That(row.Represents((current, currentMode) => ReferenceEquals(current, usage) && currentMode == grouped), Is.True);
            return pinned;
        }, (_, _) => { });
        row.Update(usage, History(usage), 120, true, null, grouped, state);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(row.Background, Is.SameAs(pinned ? Brushes.Gold : Brushes.White));
        Assert.That(Pin(row).Foreground, Is.SameAs(pinned ? Brushes.Red : Brushes.Gray));
        Assert.That(Pin(row).ToolTip, Is.EqualTo(pinned ? Strings.UnpinProcess : Strings.PinProcess));
        Assert.That(AutomationProperties.GetName(Pin(row)), Is.EqualTo((pinned ? Strings.UnpinProcess : Strings.PinProcess) + ": Friendly"));
        Assert.That(Labels(row)[0].Foreground, Is.SameAs(state == PinnedProcessState.Present ? Brushes.Black : Brushes.Gray));
        if (state != PinnedProcessState.Present)
        {
            var status = state switch
            {
                PinnedProcessState.Exited => Strings.ProcessExited,
                PinnedProcessState.NotRecorded => Strings.ProcessNotRecorded,
                _ => Strings.Unavailable
            };
            Assert.That(Readings(row), Is.All.EqualTo(Strings.MissingReading));
            Assert.That(Labels(row)[1].Text, Is.EqualTo(Strings.Format(Strings.PID, int.MaxValue) + " · " + status));
            Assert.That(Cell(row, 3).ToolTip, Is.EqualTo(status));
            Assert.That(AutomationProperties.GetName(row), Does.EndWith(" · " + status));
        }
        row.Update(usage, History(usage), 120, true, null, grouped);
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(Readings(row), Is.EqualTo(new[] { "12.5%", "25.0%", grouped ? "8.0 MiB" : "16.0 MiB", "4.0 MB/s", "47", "1,234" }));
        Assert.That(Labels(row)[0].Foreground, Is.SameAs(Brushes.Black));
    }

    [Test]
    public void SuppliedHistoryKeepsSortSpecificScaleAndGroupMeaning(
        [Values(ProcessSort.Cpu, ProcessSort.Gpu, ProcessSort.Memory, ProcessSort.Io,
            ProcessSort.Threads, ProcessSort.Handles)] ProcessSort sort, [Values(false, true)] bool grouped)
    {
        var usage = Usage(grouped);
        var history = History(usage);
        var scale = sort switch { ProcessSort.Memory => 64 * 1_048_576d, ProcessSort.Io => 64_000_000d,
            ProcessSort.Threads or ProcessSort.Handles => 1234, _ => 64 };
        var data = new ProcessGraphData(history, [32], scale, sort);
        var row = Row();
        row.Update(usage, history, 120, true, data, grouped);
        var expected = sort switch
        {
            ProcessSort.Gpu => (grouped ? "Busiest-member GPU" : "GPU") + " history\n0–64.0% GPU utilization",
            ProcessSort.Memory => (grouped ? "Summed private memory" : "Resident memory") + " history\n0–64 MiB",
            ProcessSort.Io => (grouped ? "Summed read/write I/O" : "Read/write I/O") + " history\n0–64 MB/s",
            ProcessSort.Threads => "Threads history: 0–1,234.",
            ProcessSort.Handles => "Handles history: 0–1,234.",
            _ => (grouped ? "Summed CPU" : "CPU") + " history\n0–64.0% of total machine CPU"
        };
        Assert.That(row.Children.OfType<CpuGraph>().Single().ScaleMaximum, Is.EqualTo(scale));
        Assert.That(row.ToolTip, Does.Contain(expected + " · last 2 min\nSame scale for every row"));
    }

    [Test]
    public void MissingPreparedDataBuildsTheCpuHistoryFromSuppliedSamples()
    {
        var usage = Usage();
        var row = Row();
        row.Update(usage, History(usage), 120, true, null);
        Assert.That(row.Children.OfType<CpuGraph>().Single().ScaleMaximum, Is.EqualTo(13.8));
        Assert.That(row.ToolTip, Does.Contain("CPU history\n0–13.8% of total machine CPU"));
    }

    private static Action Observe(DependencyObject target, DependencyProperty property, Action changed)
    {
        var descriptor = DependencyPropertyDescriptor.FromProperty(property, target.GetType());
        EventHandler handler = (_, _) => changed();
        descriptor.AddValueChanged(target, handler);
        return () => descriptor.RemoveValueChanged(target, handler);
    }

    [Test]
    public void PinCallbackAndVisibleNotificationsKeepTheirOriginalOrder()
    {
        var row = Row();
        var usage = Usage();
        var calls = new List<string>();
        row.EnablePinning((_, _) => { calls.Add("pin"); return true; }, (_, _) => { });
        var remove = new List<Action>
        {
            Observe(Labels(row)[0], TextBlock.TextProperty, () => calls.Add("name")),
            Observe(Labels(row)[0], FrameworkElement.ToolTipProperty, () => calls.Add("name-tooltip")),
            Observe(Labels(row)[1], TextBlock.TextProperty, () => calls.Add("pid")),
            Observe(row, FrameworkElement.ToolTipProperty, () => calls.Add("history-tooltip")),
            Observe(row, AutomationProperties.NameProperty, () => calls.Add("automation"))
        };
        var names = new[] { "cpu", "gpu", "memory", "io", "threads", "handles" };
        for (var index = 0; index < names.Length; index++)
        {
            var name = names[index];
            remove.Add(Observe(Cell(row, index + 2), TextBlock.TextProperty, () => calls.Add(name)));
        }
        try
        {
            row.Update(usage, History(usage), 120, true, null);
            Assert.That(calls, Is.EqualTo(new[] { "pin", "name", "name-tooltip", "pid", "cpu", "gpu", "history-tooltip",
                "memory", "io", "threads", "handles", "automation" }));
        }
        finally { foreach (var cleanup in remove) cleanup(); }
    }

    [Test]
    public void ThrowingPinPredicateLeavesCapturedIdentityButDoesNotStartPainting()
    {
        var row = Row();
        var original = Usage();
        row.Update(original, History(original), 120, true, null);
        var previous = Readings(row);
        var next = original with { AppName = "Replacement", CpuPercent = 99 };
        var failure = new InvalidOperationException("pin failed");
        row.EnablePinning((_, _) => throw failure, (_, _) => { });
        Assert.That(Assert.Throws<InvalidOperationException>(() => row.Update(next, History(next), 120, true, null)), Is.SameAs(failure));
        Assert.That(row.Represents((target, _) => ReferenceEquals(target, next)), Is.True);
        Assert.That(Labels(row)[0].Text, Is.EqualTo("Friendly"));
        Assert.That(Readings(row), Is.EqualTo(previous));
    }

    [Test]
    public void NullUsageThrowsBeforeChangingTheRowOrCallingPinning()
    {
        var row = Row();
        var usage = Usage();
        row.Update(usage, History(usage), 120, true, null);
        var readings = Readings(row);
        var tip = row.ToolTip;
        var calls = 0;
        row.EnablePinning((_, _) => { calls++; return false; }, (_, _) => { });
        Assert.That(Assert.Throws<ArgumentNullException>(() => row.Update(null!, History(usage), 120, true, null))!.ParamName, Is.EqualTo("usage"));
        Assert.That(calls, Is.Zero);
        Assert.That(row.Represents((target, _) => ReferenceEquals(target, usage)), Is.True);
        Assert.That(Readings(row), Is.EqualTo(readings));
        Assert.That(row.ToolTip, Is.EqualTo(tip));
    }

    [Test]
    public void NullHistoryThrowsAfterCapturingUsageButBeforePinning()
    {
        var row = Row();
        var usage = Usage();
        row.Update(usage, History(usage), 120, true, null);
        var next = usage with { AppName = "Replacement" };
        var calls = 0;
        row.EnablePinning((_, _) => { calls++; return false; }, (_, _) => { });
        Assert.That(Assert.Throws<ArgumentNullException>(() => row.Update(next, null!, 120, true, null))!.ParamName, Is.EqualTo("source"));
        Assert.That(calls, Is.Zero);
        Assert.That(row.Represents((target, _) => ReferenceEquals(target, next)), Is.True);
        Assert.That(Labels(row)[0].Text, Is.EqualTo("Friendly"));
    }

    [Test]
    public void ReentrantPinningKeepsInnerIdentityAndAvailabilityButOuterDisplayLocals()
    {
        var row = Row();
        var outer = Usage();
        var inner = Usage(true) with { Name = "inner.exe", AppName = "Inner", CreationTicks = 101 };
        var entered = false;
        row.EnablePinning((target, _) =>
        {
            if (!entered)
            {
                entered = true;
                row.Update(inner, History(inner), 120, true, null, true, PinnedProcessState.Unavailable);
            }
            return ReferenceEquals(target, outer);
        }, (_, _) => { });
        row.Update(outer, History(outer), 120, true, null);
        Assert.That(row.Represents((target, grouped) => ReferenceEquals(target, inner) && grouped), Is.True);
        Assert.That(Labels(row)[0].Text, Is.EqualTo("Friendly"));
        Assert.That(Labels(row)[0].Foreground, Is.SameAs(Brushes.Gray));
        Assert.That(row.Background, Is.SameAs(Brushes.Gold));
        Assert.That(Readings(row), Is.All.EqualTo(Strings.MissingReading));
        Assert.That(Labels(row)[1].Text, Is.EqualTo(Strings.Format(Strings.PID, int.MaxValue) + " · "));
        Assert.That(Cell(row, 4).ToolTip, Is.EqualTo(Strings.ResidentWorkingSetIncludingSharedPagesDoesNotSumTo));
        Assert.That(row.ToolTip, Does.Contain("CPU history\n").And.Not.Contain("Summed CPU history"));
    }

    [Test]
    public void ReusingTheSameIdentityRefreshesChangedMetadataInBothTooltips()
    {
        var row = Row();
        var usage = Usage();
        row.Update(usage, History(usage), 120, true, null);
        var next = usage with { AppName = "Replacement", Publisher = "New publisher", ImagePath = @"C:\fixture\worker.exe" };
        row.Update(next, History(next), 180, true, null);
        Assert.That(Labels(row)[0].Text, Is.EqualTo("Replacement"));
        Assert.That(Labels(row)[0].ToolTip, Does.StartWith("Replacement (worker.exe) · ").And.EndWith("New publisher\nC:\\fixture\\worker.exe"));
        Assert.That(row.ToolTip, Does.StartWith("Replacement (worker.exe) · ").And.Contain("New publisher\nC:\\fixture\\worker.exe").And.Contain("last 3 min"));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(12)]
    [TestCase(13)]
    public void GroupMemberTooltipsKeepTheTwelvePidBoundary(int count)
    {
        var usage = Usage(true) with { MemberCount = count, MemberIds = Enumerable.Range(1, count).ToArray() };
        var row = Row();
        row.Update(usage, History(usage), 120, true, null, true);
        var shown = string.Join(", ", Enumerable.Range(1, Math.Min(12, count)));
        var identity = count <= 1 ? Strings.Format(Strings.PID, int.MaxValue)
            : count == 12 ? "PIDs " + shown : "PIDs " + shown + " +1 more";
        Assert.That(Labels(row)[0].Text, Is.EqualTo("Friendly" + (count > 1 ? " ×" + count : "")));
        Assert.That(Labels(row)[0].ToolTip, Does.Contain(" · " + identity + "\n"));
        Assert.That(row.ToolTip, Does.Contain(" · " + identity + "\n"));
    }
}
