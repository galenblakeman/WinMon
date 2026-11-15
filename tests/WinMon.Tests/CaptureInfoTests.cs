using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en")]
public sealed class CaptureInfoTests
{
    private static ConnectionCapture Capture(int second, long bytes)
    {
        var owners = Enumerable.Range(1, 12).Select(id => new ProcessUsage(id, id, $"app{id:00}", id)
            { ImagePath = $@"C:\Apps\app{id:00}.exe" }).ToArray();
        var endpoints = owners.SelectMany(owner => Enumerable.Range(1, 8).Select(port =>
            new NetworkConnection(owner.ProcessId, owner.CreationTicks, TransportProtocol.Tcp,
                new(IPAddress.Loopback, 10000 + port), new(IPAddress.Loopback, 20000 + port)))).ToArray();
        var network = new ProcessNetworkFrame(second, 1, ProcessNetworkQuality.None,
            owners.Select(owner => new ProcessNetworkReading(owner.ProcessId, owner.CreationTicks,
                bytes, bytes * 2, owner.ProcessId, 0)).ToArray())
        {
            FlowTrafficSupported = true,
            Flows = endpoints.Select(endpoint => new ProcessNetworkFlowReading(endpoint.ProcessId,
                endpoint.ProcessCreationTicks!.Value, endpoint.Protocol,
                ConnectionQuery.Endpoint(endpoint.LocalEndpoint), ConnectionQuery.Endpoint(endpoint.RemoteEndpoint!),
                bytes, bytes * 2, endpoint.LocalEndpoint.Port - 10000, 0)).ToArray()
        };
        var sample = new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(second), 90, owners, 0);
        return new(sample, new(endpoints, NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success), owners, network);
    }

    [Test]
    public void ReportBoundsAppsAndMergedEndpointsButKeepsTriggerPathsAndPeriodTotals()
    {
        var start = Capture(0, 100);
        var end = Capture(300, 400);
        var report = CaptureReportText.ConnectionLeaders(end, history: [start, end], trigger: end.Owners[0]);
        var rows = report.Split(Environment.NewLine).Where(line => line.Length > 0 && char.IsDigit(line[0]) && line.Contains('\t')).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(55), "Ten ranked apps plus the out-of-limit trigger, five connections each.");
            Assert.That(rows.GroupBy(line => line.Split('\t')[0]).All(group => group.Count() == 5), Is.True);
            Assert.That(report, Does.Contain(end.Owners[0].ImagePath).And.Not.Contain(end.Owners[1].ImagePath));
            Assert.That(report, Does.Contain("300 B").And.Contain("600 B").And.Contain("≈"));
            Assert.That(report, Does.Not.Contain(Strings.DetailReceivedSent), "Period totals must not be labeled since start.");
            Assert.That(report, Does.Contain(Strings.Format(Strings.CaptureConnectionLimit, 5, 5, 3)));
            Assert.That(rows.All(line => !line.Contains(":10001\t", StringComparison.Ordinal)), Is.True,
                "The busiest connections win, rather than the first endpoints.");
        });
    }

    [Test]
    public void MissingEndpointFlowsShareTheFiveRowBudgetAndAutomaticSnapshotsGroupExecutables()
    {
        var end = Capture(300, 400);
        var unmatched = end.Network!.Flows[0] with { LocalEndpoint = "127.0.0.1:9999", ReceiveBytesPerSecond = 1000 };
        end = end with { Network = end.Network with { Flows = [.. end.Network.Flows, unmatched] } };
        var report = CaptureReportText.ConnectionLeaders(end, trigger: end.Owners[0]);
        Assert.That(report, Does.Contain("127.0.0.1:9999"));
        Assert.That(report, Does.Contain(Strings.Format(Strings.CaptureConnectionLimit, 5, 5, 4)));
        var filtered = CaptureReportText.ConnectionLeaders(end,
            options: new ConnectionQueryOptions(Search: "no-match", NetworkEnabled: true), trigger: end.Owners[0]);
        Assert.That(filtered, Does.Contain(end.Owners[0].ImagePath));

        var sibling = end.Owners[0] with { ProcessId = 100, CreationTicks = 100, CpuPercent = 0.1 };
        var peak = end.Sample with { Processes = [.. end.Owners, sibling] };
        var fired = new CaptureFired(new CaptureRuleSetting(CaptureMetric.Process, true, 80, 1),
            peak, peak, 90, [90], sibling);
        var snapshot = CaptureReportText.Snapshot(fired, [peak], grouped: true);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Grouped, Is.True);
            Assert.That(snapshot.Processes, Has.Count.EqualTo(11));
            Assert.That(snapshot.Processes[0].MemberIds, Does.Contain(100));
            Assert.That(snapshot.Processes.Count(process => process.ImagePath == sibling.ImagePath), Is.EqualTo(1));
        });
        var memoryTrigger = fired with { Rule = new CaptureRuleSetting(CaptureMetric.Memory, true, 80, 1) };
        var memorySnapshot = snapshot with { Processes = [sibling with { PrivateBytes = 600, WorkingSetBytes = 100 }] };
        Assert.That(CaptureReportText.Notification(memoryTrigger, memorySnapshot), Does.Contain("600 B"));
    }

    [Test]
    public void CaptureViewsApplyLimitsAndRestoreLivePreferences()
    {
        var end = Capture(300, 400);
        var view = new ConnectionsView();
        view.TogglePin(end.Owners[0]);
        view.Load(new ConnectionListSettings(0, ConnectionSort.Receive));
        view.Update(end.Connections, end.Owners, true, end.Network, true, [end], end,
            captureTrigger: end.Owners[0]);
        view.SelectConnectionForFixture(1, 1);
        Assert.Multiple(() =>
        {
            Assert.That(view.DisplayedRows, Has.Count.EqualTo(11));
            Assert.That(view.DisplayedRows.All(row => row.Detail.Contains(row.Process!.ImagePath, StringComparison.Ordinal)), Is.True);
            Assert.That(view.SelectedDetailsForFixture, Does.Contain(Strings.Format(Strings.CaptureConnectionLimit, 5, 5, 3)));
            Assert.That(view.TopCount.IsEnabled, Is.False);
            Assert.That(view.DisplayedRows.Single(row => row.Process?.Identity == end.Owners[0].Identity).Pinned, Is.True,
                "Pins remain identifiable while counting within the capture limit.");
        });
        view.Update(end.Connections, end.Owners, false, end.Network, true);
        Assert.That(view.DisplayedRows, Has.Count.EqualTo(12));
        Assert.That(view.TopCount.IsEnabled, Is.True);

        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources["RangeChip"] = new Style(typeof(RadioButton));
        var processes = new ProcessesView();
        processes.SelectProcess(end.Owners[0], false);
        var settings = new ProcessListSettings(0, ProcessSort.Cpu, false, false, false);
        var frame = new ProcessListFrame([end.Sample], end.Sample, end.Sample, end.Sample,
            end.Sample, end.Sample, false, null, true, settings, end.Owners[0]);
        var report = processes.Update(frame)!;
        Assert.Multiple(() =>
        {
            Assert.That(report.Processes, Has.Count.EqualTo(11));
            Assert.That(report.Processes[0].ImagePath, Is.EqualTo(end.Owners[0].ImagePath));
            Assert.That(report.Grouped, Is.True);
            Assert.That(report.TriggerIncludedOutsideTop, Is.True);
            Assert.That(report.TopCount, Is.EqualTo(10));
        });
        processes.Update(frame with { Selected = null });
        Assert.That(processes.ProcessCountSelector.SelectedItem, Is.EqualTo(TopCountChoices.Selection(0)));
        Assert.That(processes.IndividualChip.IsChecked, Is.True);
        Assert.That(processes.Selection.Contains(end.Owners[0], false), Is.True,
            "Temporary Capture grouping must preserve the live selection.");
    }
}
