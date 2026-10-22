using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
[SetUICulture("en")]
public sealed class ProcessActionsTests
{
    private static ProcessUsage Example => new(12345, DateTime.UtcNow.AddHours(-1).ToFileTimeUtc(), "example.exe", 87.6,
        42 * 1_048_576, 250_000) { ImagePath = @"C:\Apps\example.exe", ThreadCount = 12, HandleCount = 400 };

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Menu_CapturesOriginalRowAndDisplaysEveryIcon()
    {
        var process = Example;
        var time = DateTimeOffset.Now.AddMinutes(-5);
        ProcessDetailsSnapshot? captured = null;
        ProcessUsage? location = null;
        ProcessUsage? dumped = null;
        var row = new ProcessRow();
        row.EnableProcessActions(_ => true, _ => { });
        row.EnablePinning((_, _) => false, (_, _) => { });
        row.EnableInspectionActions(s => captured = s, s => captured = s, p => location = p, p => dumped = p,
            _ => true, () => true);
        row.Update(process, [new CpuSample(time, 1, [process], 0)], 120, true, null);
        row.PrepareProcessMenu();
        var items = row.ContextMenu!.Items.OfType<MenuItem>().ToArray();
        Assert.That(items.Select(i => i.Header), Is.EqualTo(new[] { Strings.ProcessProperties + "…", Strings.OpenFileLocation,
            Strings.CopyDetails, Strings.PinProcess, Strings.CreateDumpFile + "…", Strings.Format(Strings.EndPID, process.Name, process.ProcessId) }));
        Assert.That(items.Select(i => i.Icon), Is.All.InstanceOf<System.Windows.Shapes.Path>());
        row.Update(process with { ProcessId = 23456, Name = "replacement.exe" }, [], 120, true, null);
        foreach (var index in new[] { 0, 2 })
        {
            items[index].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(captured!.Process, Is.EqualTo(process));
            Assert.That(captured.Timestamp, Is.EqualTo(time));
            Assert.That(captured.Historical, Is.True);
        }
        items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        items[4].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.That(location, Is.EqualTo(process));
        Assert.That(dumped, Is.EqualTo(process));
    }

    [TestCase(false, 1, false, true, true)]
    [TestCase(true, 1, false, true, true)]
    [TestCase(true, 2, false, true, false)]
    [TestCase(true, 1, true, true, false)]
    [TestCase(true, 1, false, false, false)]
    [TestCase(false, 1, true, true, false)]
    [TestCase(false, 1, false, false, false)]
    [Apartment(ApartmentState.STA)]
    public void Menu_DumpAllowsOnlySingleAvailableLiveProcesses(bool grouped, int count, bool unavailable, bool live, bool expected)
    {
        var row = new ProcessRow();
        var process = Example with { ImagePath = "", MemberCount = count,
            MemberIds = grouped ? count == 1 ? [12345] : [12345, 23456] : [] };
        row.EnableProcessActions(_ => live, _ => { });
        ProcessUsage? dumped = null;
        row.EnableInspectionActions(_ => { }, _ => { }, _ => { }, p => dumped = p,
            p => ProcessDumpWriter.IsEligible(p, live, grouped), () => !live);
        row.Update(process, [], 120, true, null, grouped, unavailable ? PinnedProcessState.Exited : PinnedProcessState.Present);
        row.PrepareProcessMenu();
        var items = row.ContextMenu!.Items.OfType<MenuItem>().ToArray();
        Assert.That(items.Single(i => (string)i.Header == Strings.CreateDumpFile + "…").IsEnabled, Is.EqualTo(expected));
        Assert.That(items.Single(i => (string)i.Header == Strings.OpenFileLocation).IsEnabled, Is.False);
        Assert.That(items.Take(3).Where(i => (string)i.Header != Strings.OpenFileLocation).All(i => i.IsEnabled), Is.True);
        if (expected)
        {
            row.Update(Example with { ProcessId = 23456 }, [], 120, true, null, grouped);
            items.Single(i => (string)i.Header == Strings.CreateDumpFile + "…").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(dumped, Is.EqualTo(process), "Dump retains the original single member and its creation time");
        }
    }

    [Test]
    public void Details_IncludeCapturedIdentityAndSuppressUnavailableMetrics()
    {
        var p = Example;
        var snapshot = new ProcessDetailsSnapshot(p, false, DateTimeOffset.Now, true, false, Strings.ProcessExited);
        var text = snapshot.Format();
        Assert.That(text, Does.Contain(p.ImagePath).And.Contain($"PID: {p.ProcessId}").And.Contain(Strings.ProcessHistoricalView));
        Assert.That(text, Does.Contain(Strings.ProcessExited));
        Assert.That(text, Does.Contain("CPU: " + Strings.MissingReading));
        Assert.That(text, Does.Contain(Strings.Threads + ": " + Strings.MissingReading));
        var group = snapshot with { Grouped = true, Process = p with { MemberIds = [12, 24], MemberCount = 2 } };
        Assert.That(group.Format(), Does.Contain("12, 24").And.Not.Contain("PID:").And.Not.Contain(Strings.ProcessStartedLabel + ":"));
    }

    [Test]
    public void Dump_RejectsUnidentifiedGroupedHistoricalAndSelfTargets()
    {
        var p = Example;
        Assert.That(ProcessDumpWriter.IsEligible(p, true, false), Is.True);
        Assert.That(ProcessDumpWriter.IsEligible(p, false, false), Is.False);
        Assert.That(ProcessDumpWriter.IsEligible(p, true, true), Is.False);
        var singleton = p with { MemberIds = [p.ProcessId] };
        Assert.That(ProcessDumpWriter.IsEligible(singleton, true, true), Is.True);
        Assert.That(ProcessDumpWriter.IsEligible(singleton with { MemberIds = [23456] }, true, true), Is.False);
        Assert.That(ProcessDumpWriter.IsEligible(singleton with { MemberCount = 2, MemberIds = [p.ProcessId, 23456] }, true, true), Is.False);
        foreach (var invalid in new[] { p with { ProcessId = 4 }, p with { CreationTicks = 0 },
            p with { MemberIds = [p.ProcessId] }, p with { ProcessId = Environment.ProcessId } })
            Assert.That(ProcessDumpWriter.IsEligible(invalid, true, false), Is.False);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void Native_DumpChecksLifetimeAndWritesOwnedHelper(bool fullMemory, bool grouped)
    {
        using var helper = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            Arguments = "-NoProfile -NonInteractive -Command \"[Console]::WriteLine('ready'); Start-Sleep -Seconds 60\"", UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        })!;
        var directory = Path.Combine(Path.GetTempPath(), "WinMon-dump-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "helper.dmp");
        try
        {
            Assert.That(helper.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult(), Is.EqualTo("ready"));
            var usage = new ProcessUsage(helper.Id, helper.StartTime.ToUniversalTime().ToFileTimeUtc(), "Owned helper", 0);
            if (grouped) usage = usage with { MemberIds = [helper.Id] };
            File.WriteAllText(path, "Existing destination");
            Assert.Throws<InvalidOperationException>(() => ProcessDumpWriter.Write(usage with { CreationTicks = usage.CreationTicks + 1 }, path, fullMemory));
            Assert.That(File.ReadAllText(path), Is.EqualTo("Existing destination"));
            ProcessDumpWriter.Write(usage, path, fullMemory);
            using var reader = new BinaryReader(File.OpenRead(path));
            Assert.That(reader.ReadUInt32(), Is.EqualTo(0x504d444d), "MDMP signature");
            reader.BaseStream.Position = 24;
            var flags = reader.ReadUInt64();
            Assert.That((flags & 2) != 0, Is.EqualTo(fullMemory));
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1), "No partial file left behind");
            Assert.That(helper.HasExited, Is.False);
        }
        finally
        {
            if (!helper.HasExited) { helper.Kill(); helper.WaitForExit(5000); }
            Directory.Delete(directory, true);
        }
    }
}
