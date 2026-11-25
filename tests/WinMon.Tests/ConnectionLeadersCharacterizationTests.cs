using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en")]
public sealed class ConnectionLeadersCharacterizationTests
{
    private static string Lines(params string[] values) => string.Join(Environment.NewLine, values) + Environment.NewLine;
    private static string Count(int top, int shown, int eligible, int rows) => Strings.Format(Strings.CaptureReportConnectionCount,
        top == 0 ? Strings.AllEntries : top.ToString(System.Globalization.CultureInfo.CurrentCulture), shown, eligible, eligible - shown, rows);

    private static ProcessUsage Owner(int id, string name, bool app = true) => new(id, id * 10L, name, 1)
    {
        ImagePath = $@"C:\Apps\{name}.exe", HasAppWindow = app
    };

    private static NetworkConnection Endpoint(ProcessUsage owner, int port, TransportProtocol protocol = TransportProtocol.Tcp,
        bool remote = true) => new(owner.ProcessId, owner.CreationTicks, protocol, new(IPAddress.Loopback, port),
        remote ? new(IPAddress.Loopback, port + 1000) : null, protocol == TransportProtocol.Tcp ? TcpState.Established : null);

    private static ProcessNetworkFrame Network(ProcessNetworkQuality quality = ProcessNetworkQuality.None,
        bool supported = false, ProcessNetworkQuality flowQuality = ProcessNetworkQuality.None) => new(2000, 100, quality, [])
    {
        FlowTrafficSupported = supported, FlowQuality = flowQuality, WindowStartQpc = 1000, WindowEndQpc = 1500
    };

    private static ConnectionCapture Capture(IReadOnlyList<NetworkConnection>? endpoints = null,
        IReadOnlyList<ProcessUsage>? owners = null, ProcessNetworkFrame? network = null, bool available = true, int second = 300)
    {
        var status = available ? NetworkAddressFamilyStatus.Success : new NetworkAddressFamilyStatus(false, 5);
        var sample = new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(second), 20, owners ?? [], 0)
        {
            MonotonicTimestamp = (1000L + second) * Stopwatch.Frequency, IntervalSeconds = 1
        };
        return new(sample, new(endpoints ?? [], status, status), owners ?? [], network);
    }

    private static ConnectionCapture Ranked()
    {
        ProcessUsage[] owners = [Owner(11, "Alpha"), Owner(22, "Beta"), Owner(33, "Gamma", false)];
        var network = Network() with
        {
            Processes = [new(11, 110, 1100, 2200, 20, 80), new(22, 220, 2200, 4400, 50, 0), new(33, 330, 3300, 6600, 10, 200)]
        };
        return Capture(owners.Select(owner => Endpoint(owner, 1000 + owner.ProcessId)).ToArray(), owners, network);
    }

    private static string[] Headings(string report) => report.Split(Environment.NewLine)
        .Where(line => line.Contains(": ↓ ", StringComparison.Ordinal))
        .Select(line => line[..line.IndexOf(": ↓ ", StringComparison.Ordinal)]).ToArray();

    private static string[][] Rows(string report) => report.Split(Environment.NewLine).Select(line => line.Split('\t'))
        .Where(columns => columns.Length == 9 && int.TryParse(columns[0], out _)).ToArray();

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void UnavailableObservationsReturnTheExactShortReportBeforeValidatingOptions(int kind)
    {
        ConnectionCapture? capture = kind switch
        {
            0 => null,
            1 => Capture() with { Connections = null },
            2 => Capture(available: false),
            3 => Capture(network: Network(supported: true), available: false),
            _ => Capture(network: Network() with { Flows = [new(42, 100, TransportProtocol.Udp, "a", "b", 1, 2, 3, 4)] })
                with { Connections = null }
        };

        var report = CaptureReportText.ConnectionLeaders(capture, options: new(TrafficSeconds: -1));

        Assert.That(report, Is.EqualTo(Lines("", Strings.ConnectionsDetails, Strings.ConnectionsHistoryUnavailable)));
    }

    [Test]
    public void AnEmptyPartiallyAvailableEndpointTableProducesAFullEmptyReport()
    {
        var capture = Capture() with { Connections = new([], new(false, 5), NetworkAddressFamilyStatus.Success) };

        var report = CaptureReportText.ConnectionLeaders(capture);

        Assert.Multiple(() =>
        {
            Assert.That(report, Does.StartWith(Lines("", Strings.ConnectionsDetails, Strings.NetworkProcessMonitoringUnavailable)));
            Assert.That(report, Does.Not.Contain(Strings.ConnectionsHistoryUnavailable));
            Assert.That(report, Does.EndWith(Lines(Count(10, 0, 0, 0), Strings.MissingReading)));
            Assert.That(Rows(report), Is.Empty);
        });
    }

    [TestCase(ProcessNetworkQuality.None, 0)]
    [TestCase(ProcessNetworkQuality.Provisional, 0)]
    [TestCase(ProcessNetworkQuality.WarmingUp, 3)]
    [TestCase(ProcessNetworkQuality.Incomplete, 2)]
    [TestCase(ProcessNetworkQuality.Discontinuity, 2)]
    [TestCase(ProcessNetworkQuality.CapacityExceeded, 2)]
    [TestCase(ProcessNetworkQuality.Unavailable, 1)]
    [TestCase(ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Incomplete | ProcessNetworkQuality.WarmingUp, 1)]
    [TestCase(ProcessNetworkQuality.Incomplete | ProcessNetworkQuality.WarmingUp, 2)]
    [TestCase(ProcessNetworkQuality.Discontinuity | ProcessNetworkQuality.WarmingUp, 2)]
    [TestCase(ProcessNetworkQuality.CapacityExceeded | ProcessNetworkQuality.WarmingUp, 2)]
    [TestCase(ProcessNetworkQuality.Provisional | ProcessNetworkQuality.WarmingUp, 3)]
    public void ProcessQualityWarningsKeepTheirExistingPrecedenceAndUnsupportedFlowNotice(ProcessNetworkQuality quality, int warning)
    {
        var expected = new List<string> { "", Strings.ConnectionsDetails };
        if (warning != 0) expected.Add(warning switch
        {
            1 => Strings.NetworkProcessMonitoringUnavailable, 2 => Strings.NetworkProcessMonitoringIncomplete,
            _ => Strings.NetworkProcessMonitoringWarmingUp
        });
        expected.Add(Strings.CaptureConnectionTrafficUnavailable);

        var report = CaptureReportText.ConnectionLeaders(Capture(network: Network(quality)));

        Assert.That(report, Does.StartWith(Lines(expected.ToArray())));
        Assert.That(report, Does.Not.Contain(Strings.CaptureFlowIncomplete));
    }

    [TestCase(ProcessNetworkQuality.None, false)]
    [TestCase(ProcessNetworkQuality.Provisional, false)]
    [TestCase(ProcessNetworkQuality.WarmingUp, false)]
    [TestCase(ProcessNetworkQuality.Incomplete, true)]
    [TestCase(ProcessNetworkQuality.Discontinuity, true)]
    [TestCase(ProcessNetworkQuality.CapacityExceeded, true)]
    [TestCase(ProcessNetworkQuality.Unavailable, true)]
    [TestCase(ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.WarmingUp, true)]
    public void SupportedFlowWarningsUseTheirSeparateQualityMask(ProcessNetworkQuality quality, bool incomplete)
    {
        var prefix = new List<string> { "", Strings.ConnectionsDetails };
        if (incomplete) prefix.Add(Strings.CaptureFlowIncomplete);
        prefix.Add(Strings.CaptureConnectionTrafficScope + " " + Strings.Format(Strings.CaptureConnectionTrafficWindow, 5d, 5d));

        var report = CaptureReportText.ConnectionLeaders(Capture(network: Network(supported: true, flowQuality: quality)));

        Assert.That(report, Does.StartWith(Lines(prefix.ToArray())));
        Assert.That(report, Does.Not.Contain(Strings.CaptureConnectionTrafficUnavailable));
    }

    [TestCase(100, 1000, 1500, 2000, 5d, 5d)]
    [TestCase(100, 1000, 1500, 1400, 5d, 0d)]
    [TestCase(100, 1000, 1000, 1000, 0d, 0d)]
    [TestCase(0, 1000, 1500, 2000, -1d, -1d)]
    [TestCase(-1, 1000, 1500, 2000, -1d, -1d)]
    [TestCase(100, 1500, 1000, 2000, -1d, -1d)]
    public void FlowWindowFormattingPreservesClockGuardsAndClampsOnlyTheAge(long frequency, long start, long end,
        long timestamp, double duration, double age)
    {
        var network = Network(supported: true) with
        {
            Frequency = frequency, WindowStartQpc = start, WindowEndQpc = end, TimestampQpc = timestamp
        };
        var expected = Strings.CaptureConnectionTrafficScope + (duration < 0 ? "" : " " +
            Strings.Format(Strings.CaptureConnectionTrafficWindow, duration, age));

        var report = CaptureReportText.ConnectionLeaders(Capture(network: network));

        Assert.That(report, Does.StartWith(Lines("", Strings.ConnectionsDetails, expected)));
    }

    [Test]
    public void AValidCapturePreservesTheNegativeTrafficWindowException()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CaptureReportText.ConnectionLeaders(Capture(), options: new(TrafficSeconds: -1)));
        Assert.That(error!.ActualValue, Is.EqualTo(-1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AutomaticOrderUsesCombinedRatesWhileExplicitOptionsKeepTheRequestedSort(bool automatic)
    {
        var report = CaptureReportText.ConnectionLeaders(Ranked(), 1,
            automatic ? null : new ConnectionQueryOptions(NetworkEnabled: true));

        Assert.That(Headings(report), Is.EqualTo(automatic ? new[] { "Gamma" } : ["Beta"]));
        Assert.That(report, Does.Contain(Count(1, 1, 3, 1)));
    }

    [TestCase(-1, 0)]
    [TestCase(0, 3)]
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(10, 3)]
    public void TopLimitsPreserveZeroAsAllAndNegativeAsEmptyWithoutRewritingThePrintedChoice(int top, int shown)
    {
        var report = CaptureReportText.ConnectionLeaders(Ranked(), top);

        Assert.That(Headings(report), Is.EqualTo(new[] { "Gamma", "Alpha", "Beta" }.Take(shown)));
        Assert.That(report, Does.Contain(Count(top, shown, 3, shown)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PinMatchingKeepsTheCallersComparerAndPinsDoNotConsumeTheUnpinnedLimit(bool ignoreCase)
    {
        var capture = Ranked();
        var pins = new HashSet<string>(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        {
            ConnectionQuery.OwnerKey(capture.Owners[0]).ToLowerInvariant()
        };

        var report = CaptureReportText.ConnectionLeaders(capture, 1, pinnedKeys: pins);

        Assert.That(Headings(report), Is.EqualTo(ignoreCase ? new[] { "Alpha", "Gamma" } : ["Gamma"]));
        Assert.That(report, Does.Contain(Count(1, ignoreCase ? 2 : 1, 3, ignoreCase ? 2 : 1)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TriggerOutsidePinsIsForcedOnceAndEligibleCountDependsOnPinnedOnly(bool pinnedOnly)
    {
        var capture = Ranked();
        var pins = new HashSet<string> { ConnectionQuery.OwnerKey(capture.Owners[0]) };

        var report = CaptureReportText.ConnectionLeaders(capture, 1, pinnedKeys: pins,
            pinnedOnly: pinnedOnly, trigger: capture.Owners[1]);

        Assert.That(Headings(report), Is.EqualTo(pinnedOnly ? new[] { "Beta", "Alpha" } : ["Beta", "Alpha", "Gamma"]));
        Assert.That(report, Does.Contain(Count(1, pinnedOnly ? 2 : 3, pinnedOnly ? 2 : 3, pinnedOnly ? 2 : 3)));
        Assert.That(report.Split(Environment.NewLine).Count(line => line == Strings.CaptureReportExtraTrigger), Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SearchAndAppFiltersCanBeBypassedOnlyForTheTriggerAndItsEligibleCount(bool appsOnly)
    {
        var capture = Ranked();
        var options = new ConnectionQueryOptions(AppsOnly: appsOnly, Search: appsOnly ? "" : "Beta", NetworkEnabled: true);

        var report = CaptureReportText.ConnectionLeaders(capture, 1, options, trigger: capture.Owners[2]);

        Assert.That(Headings(report), Is.EqualTo(new[] { "Gamma", "Beta" }));
        Assert.That(report, Does.Contain(Count(1, 2, appsOnly ? 3 : 2, 2)));
        Assert.That(report, Does.Contain(Strings.CaptureReportExtraTrigger));
    }

    [Test]
    public void ATriggerAlreadySelectedKeepsItsOrderAndAnUnobservedTriggerDoesNotInventAGroup()
    {
        var capture = Ranked();
        var all = CaptureReportText.ConnectionLeaders(capture, 0, trigger: capture.Owners[1]);
        var missing = CaptureReportText.ConnectionLeaders(capture, 1,
            new ConnectionQueryOptions(Search: "not-present", NetworkEnabled: true), trigger: Owner(99, "Missing"));

        Assert.Multiple(() =>
        {
            Assert.That(Headings(all), Is.EqualTo(new[] { "Gamma", "Alpha", "Beta" }));
            Assert.That(all, Does.Not.Contain(Strings.CaptureReportExtraTrigger));
            Assert.That(Headings(missing), Is.Empty);
            Assert.That(missing, Does.Not.Contain(Strings.CaptureReportExtraTrigger));
            Assert.That(missing, Does.EndWith(Lines(Count(1, 0, 0, 0), Strings.MissingReading)));
        });
    }

    [Test]
    public void MatchedAndOrphanFlowsShareOneBudgetAcrossAllMembersOfAnExecutableGroup()
    {
        var first = Owner(7, "First") with { ImagePath = @"C:\shared.exe", AppName = "Shared" };
        var second = Owner(8, "Second") with { ImagePath = @"C:\shared.exe", AppName = "Shared" };
        NetworkConnection[] endpoints = [Endpoint(second, 1005, TransportProtocol.Udp, false), Endpoint(first, 1003),
            Endpoint(second, 1004), Endpoint(first, 1002), Endpoint(first, 1001), Endpoint(first, 1000, TransportProtocol.Udp, false)];
        var flows = endpoints.Where(endpoint => endpoint.RemoteEndpoint is not null).Select(endpoint =>
            new ProcessNetworkFlowReading(endpoint.ProcessId, endpoint.ProcessCreationTicks!.Value, endpoint.Protocol,
                ConnectionQuery.Endpoint(endpoint.LocalEndpoint), ConnectionQuery.Endpoint(endpoint.RemoteEndpoint!), 123, 456,
                endpoint.LocalEndpoint.Port == 1003 ? 30 : endpoint.LocalEndpoint.Port == 1004 ? 20 : 10, 0)).ToArray();
        var orphan = new ProcessNetworkFlowReading(8, 80, TransportProtocol.Tcp, "127.0.0.1:1099", "127.0.0.1:2099", 100, 200, 40, 0);
        var capture = Capture(endpoints, [first, second], Network(supported: true) with { Flows = [.. flows, orphan] });

        var report = CaptureReportText.ConnectionLeaders(capture, 0);
        var rows = Rows(report);

        Assert.Multiple(() =>
        {
            Assert.That(Headings(report), Is.EqualTo(new[] { "Shared" }));
            Assert.That(rows.Select(row => row[2]), Is.EqualTo(new[] { "127.0.0.1:1099", "127.0.0.1:1003", "127.0.0.1:1004", "127.0.0.1:1001", "127.0.0.1:1002" }));
            Assert.That(rows.Select(row => row[0]), Is.EqualTo(new[] { "8", "7", "8", "7", "7" }));
            Assert.That(rows[0][4], Is.EqualTo(Strings.CaptureObservedFlowMissingEndpoint));
            Assert.That(rows[1][4], Is.EqualTo("Established"));
            Assert.That(report, Does.Contain(Strings.Format(Strings.CaptureConnectionLimit, 5, 5, 2)));
            Assert.That(report, Does.Contain(Count(0, 1, 1, 5)));
        });
    }

    [Test]
    public void FlowOnlyObservationsPreserveUnknownOwnerPathAndAllNineEndpointColumns()
    {
        var flow = new ProcessNetworkFlowReading(42, 100, TransportProtocol.Udp, "[::1]:4000", "[::1]:5000", 123, 456, 10, 20);
        var capture = Capture(network: Network(supported: true) with { Flows = [flow] }, available: false) with { Connections = null };

        var report = CaptureReportText.ConnectionLeaders(capture);

        Assert.Multiple(() =>
        {
            Assert.That(Headings(report), Is.EqualTo(new[] { Strings.Format(Strings.PID, 42) }));
            Assert.That(report, Does.Contain($"{Strings.ProcessPathLabel}: {Strings.ExecutablePathUnavailable}"));
            Assert.That(Rows(report).Single(), Is.EqualTo(new[] { "42", "UDP", "[::1]:4000", "[::1]:5000",
                Strings.CaptureObservedFlowMissingEndpoint, ConnectionsView.NetworkRate(10), ConnectionsView.NetworkRate(20),
                UsageText.Bytes(123), UsageText.Bytes(456) }));
        });
    }

    [Test]
    public void UnownedUdpEndpointsKeepMissingRemoteStateAndTrafficColumns()
    {
        var endpoint = new NetworkConnection(42, null, TransportProtocol.Udp, new(IPAddress.Loopback, 4000));

        var report = CaptureReportText.ConnectionLeaders(Capture([endpoint]));

        Assert.That(Rows(report).Single(), Is.EqualTo(new[] { "42", "UDP", "127.0.0.1:4000", Strings.MissingReading,
            Strings.MissingReading, Strings.MissingReading, Strings.MissingReading, Strings.MissingReading, Strings.MissingReading }));
    }

    [Test]
    public void RepeatedReportsWithDifferentFiltersDoNotMutateCapturedInputOrLeakSelection()
    {
        var capture = Ranked();
        var endpoints = capture.Connections!.Connections.ToArray();
        var readings = capture.Network!.Processes.ToArray();
        var original = CaptureReportText.ConnectionLeaders(capture, 0);
        _ = CaptureReportText.ConnectionLeaders(capture, 1, new ConnectionQueryOptions(Search: "Alpha", NetworkEnabled: true),
            pinnedKeys: new HashSet<string> { ConnectionQuery.OwnerKey(capture.Owners[2]) }, pinnedOnly: true, trigger: capture.Owners[1]);

        Assert.Multiple(() =>
        {
            Assert.That(CaptureReportText.ConnectionLeaders(capture, 0), Is.EqualTo(original));
            Assert.That(capture.Connections.Connections, Is.EqualTo(endpoints));
            Assert.That(capture.Network.Processes, Is.EqualTo(readings));
            Assert.That(capture.Owners.Select(owner => owner.Name), Is.EqualTo(new[] { "Alpha", "Beta", "Gamma" }));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AutomaticTiesUseCaseInsensitiveNamesAndAPartlyMissingRateStaysLast(bool missingRate)
    {
        var original = Ranked();
        var owners = original.Owners.Select(owner => owner with
        {
            Name = owner.ProcessId == 11 ? "alpha" : owner.ProcessId == 22 ? "Beta" : "gamma"
        }).ToArray();
        var readings = owners.Select(owner => new ProcessNetworkReading(owner.ProcessId, owner.CreationTicks, 100, 200,
            missingRate && owner.ProcessId == 11 ? null : 10, missingRate && owner.ProcessId == 11 ? 999 : 10)).ToArray();
        var capture = Capture(original.Connections!.Connections, owners, original.Network! with { Processes = readings });

        var report = CaptureReportText.ConnectionLeaders(capture, 0);

        Assert.That(Headings(report), Is.EqualTo(missingRate ? new[] { "Beta", "gamma", "alpha" } : ["alpha", "Beta", "gamma"]));
    }

    [Test]
    public void MultiplePinsKeepSharedQueryOrderAndAreNeverCappedEvenForANegativeTop()
    {
        var capture = Ranked();
        var two = new HashSet<string> { ConnectionQuery.OwnerKey(capture.Owners[0]), ConnectionQuery.OwnerKey(capture.Owners[2]) };
        var all = capture.Owners.Select(ConnectionQuery.OwnerKey).ToHashSet();

        var withUnpinned = CaptureReportText.ConnectionLeaders(capture, 1, pinnedKeys: two);
        var onlyPins = CaptureReportText.ConnectionLeaders(capture, -1, pinnedKeys: all);

        Assert.Multiple(() =>
        {
            Assert.That(Headings(withUnpinned), Is.EqualTo(new[] { "Alpha", "Gamma", "Beta" }));
            Assert.That(Headings(onlyPins), Is.EqualTo(new[] { "Beta", "Alpha", "Gamma" }));
            Assert.That(withUnpinned, Does.Contain(Count(1, 3, 3, 3)));
            Assert.That(onlyPins, Does.Contain(Count(-1, 3, 3, 3)));
        });
    }

    [Test]
    public void ReusedPidFlowsRemainWithTheirOwnLifetimeAndDoNotAnnotateTheOldEndpoint()
    {
        var old = Owner(42, "Old");
        var current = old with { CreationTicks = 421, Name = "New", ImagePath = @"C:\Apps\New.exe" };
        var endpoint = Endpoint(old, 4000);
        var flow = new ProcessNetworkFlowReading(42, 421, TransportProtocol.Tcp, "127.0.0.1:4000", "127.0.0.1:5000", 123, 456, 10, 20);
        var network = Network(supported: true) with
        {
            Owners = [current], Flows = [flow], Processes = [new(42, 421, 123, 456, 10, 20)]
        };

        var report = CaptureReportText.ConnectionLeaders(Capture([endpoint], [old], network));
        var rows = Rows(report);

        Assert.Multiple(() =>
        {
            Assert.That(Headings(report), Is.EqualTo(new[] { "New", "Old" }));
            Assert.That(report, Does.Contain(current.ImagePath).And.Contain(old.ImagePath));
            Assert.That(rows, Has.Length.EqualTo(2));
            Assert.That(rows[0][4], Is.EqualTo(Strings.CaptureObservedFlowMissingEndpoint));
            Assert.That(rows[0][7], Is.EqualTo(UsageText.Bytes(123)));
            Assert.That(rows[1][4], Is.EqualTo("Established"));
            Assert.That(rows[1].Skip(5), Is.EqualTo(Enumerable.Repeat(Strings.MissingReading, 4)));
        });
    }

    [TestCase(0, 150, 300)]
    [TestCase(300, 50, 100)]
    public void SelectedPeriodChangesOwnerTotalsButKeepsTheExistingApproximationForAGap(int seconds, long received, long sent)
    {
        var owner = Owner(42, "Alpha");
        var endpoint = Endpoint(owner, 4000);
        var first = Capture([endpoint], [owner], Network() with { Processes = [new(42, 420, 100, 200, 1, 2)] }, second: 0);
        var last = Capture([endpoint], [owner], Network() with { Processes = [new(42, 420, 150, 300, 5, 6)] });

        var report = CaptureReportText.ConnectionLeaders(last, options: new(NetworkEnabled: true, TrafficSeconds: seconds),
            history: [first, last]);

        Assert.Multiple(() =>
        {
            Assert.That(report, Does.Contain($"{Strings.ConnectionsTrafficPeriod}: " + (seconds == 0
                ? Strings.ConnectionsSinceStart : Strings.Format(Strings.GaugePeakWindowMinutes, 5d))));
            Assert.That(report, Does.Contain($"{Strings.ConnectionsReceived} / {Strings.ConnectionsSent}: ≈ {UsageText.Bytes(received)} / ≈ {UsageText.Bytes(sent)}"));
            Assert.That(report, Does.Contain(Count(10, 1, 1, 1)));
        });
    }
}
