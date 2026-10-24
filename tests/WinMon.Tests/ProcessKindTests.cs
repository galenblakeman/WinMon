using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
public sealed class ProcessKindTests
{
    private static MonitorSample Sample(int second, params ProcessUsage[] processes) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 10, processes, 0, false, second * Stopwatch.Frequency);

    private static ProcessUsage Usage(int id, bool? app, double cpu = 1) =>
        new(id, id * 10, "example", cpu, 1) { ImagePath = @"C:\example.exe", HasAppWindow = app };

    [TestCase(false)]
    [TestCase(true)]
    public void Ranking_UsesRecordedStateAtSelectedTime_ThroughCompactionAndWindowChanges(bool grouped)
    {
        var history = new MonitorHistory();
        var app = Usage(1, true) with { HasHadAppWindow = true };
        var background = app with { HasAppWindow = false, CpuPercent = 0 };
        var helper = Usage(2, false, 5);
        var large = Usage(3, false) with { ImagePath = @"C:\other.exe", WorkingSetBytes = 1000 };
        history.Add(ProcessRetention.Compact(Sample(1, app, helper, large), largestByMemory: 1));
        history.Add(ProcessRetention.Compact(Sample(2, background, helper, large), largestByMemory: 1));

        Assert.Multiple(() =>
        {
            Assert.That(ProcessRanking.Rank([history.Samples[0]], grouped: grouped).Processes.Single(p => p.ProcessId == (grouped ? 2 : 1)).HasAppWindow, Is.True);
            Assert.That(ProcessRanking.Rank(history.Samples, grouped: grouped).Processes.Single(p => grouped ? p.MemberCount == 2 : p.ProcessId == 1).HasAppWindow, Is.False);
        });
    }

    [TestCase(true, false, true)]
    [TestCase(false, false, false)]
    [TestCase(null, false, null)]
    [TestCase(null, true, true)]
    public void Group_UsesAllCurrentMembers_RegardlessOfBusiestMember(bool? first, bool? second, bool? expected)
    {
        var group = ProcessRanking.Rank([Sample(1, Usage(1, first), Usage(2, second, 10))], grouped: true).Processes.Single();
        Assert.That(group.HasAppWindow, Is.EqualTo(expected));
    }

    [Test]
    public void Group_ExitedAppDoesNotOverrideCurrentBackgroundMember()
    {
        var ranking = ProcessRanking.Rank([Sample(1, Usage(1, true, 10), Usage(2, false)), Sample(2, Usage(2, false))], grouped: true);
        Assert.That(ranking.Processes.Single().HasAppWindow, Is.False);
    }

    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(false, true)]
    [Apartment(ApartmentState.STA)]
    public void Row_ShowsTypeBesidePidOrGroupCount_AndKeepsTypeInNarrowWindows(bool app, bool grouped)
    {
        var usage = Usage(1, app) with { MemberCount = grouped ? 2 : 1, MemberIds = grouped ? [1, 2] : [] };
        var row = new ProcessRow();
        row.Update(usage, [], 120, true, null, grouped);
        var label = Descendants(row).OfType<TextBlock>().Single(t => t.Text.Contains('·'));
        var kind = app ? Strings.ProcessKindApp : Strings.ProcessKindBackground;
        Assert.That(label.Text, Is.EqualTo($"{(grouped ? Strings.Format(Strings.Processes, 2) : Strings.Format(Strings.PID, 1))} · {kind}"));
        row.Update(usage with { HasAppWindow = !app }, [], 120, false, null, grouped);
        Assert.That(label.Text, Is.EqualTo(app ? Strings.ProcessKindBackground : Strings.ProcessKindApp));
        Assert.That(label.Visibility, Is.EqualTo(Visibility.Visible));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void NativeWindowObservation_TracksVisibleMinimizedAndHiddenWindows()
    {
        var provider = new WindowsAppWindowProvider();
        var window = new Window { Width = 100, Height = 100, ShowActivated = false, Left = -2000 };
        try
        {
            window.Show();
            Assert.That(provider.Capture(), Does.Contain(Environment.ProcessId));
            window.WindowState = WindowState.Minimized;
            Assert.That(provider.Capture(), Does.Contain(Environment.ProcessId));
            window.Hide();
            Assert.That(provider.Capture(), Does.Not.Contain(Environment.ProcessId));
        }
        finally { window.Close(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
