using System.Globalization;
using System.Text;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class CsvExportTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 15, 30, 0, TimeSpan.FromHours(-5));
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "WinMon.CsvExportTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    [Test]
    public void ResourceHistory_UsesInvariantRawValuesAndPreservesUnavailableGaps()
    {
        var sample = new MonitorSample(Start, 31.5, [], 0)
        {
            IntervalSeconds = 1,
            Resources = new ResourceSample
            {
                UsedMemoryBytes = 1_048_576, TotalMemoryBytes = 2_097_152,
                DiskName = "C:", DiskReadBytesPerSecond = 1250.5,
                NetworkName = "Wi-Fi", ReceiveBytesPerSecond = 10.25
            }
        };
        var gap = new MonitorSample(Start.AddSeconds(1), null, [], 0, true);
        var path = Path.Combine(_directory, "resources.csv");
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CsvExport.Write(path, CsvExportKind.ResourceHistory, [sample, gap], [], Start, 0, 0);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        var rows = File.ReadAllLines(path);
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(3));
            Assert.That(rows[0], Does.Contain("memory_used_bytes"));
            Assert.That(rows[1], Does.Contain("2026-10-25T20:30:00.0000000Z,false,1,31.5"));
            Assert.That(rows[1], Does.Contain(",1048576,2097152,"));
            Assert.That(rows[1], Does.Contain(",1250.5,"));
            Assert.That(rows[2], Does.StartWith("2026-10-25T20:30:01.0000000Z,true,0,,"));
            Assert.That(File.ReadAllBytes(path).Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
        });
    }

    [Test]
    public void ProcessSamples_QuotesTextAndNeutralizesSpreadsheetFormula()
    {
        var process = new ProcessUsage(42, 123, "=HYPERLINK(\"x,y\")", 12.5, 1024, 2.5)
        {
            ImagePath = "C:\\a,\"b\"\napp.exe", HasAppWindow = true
        };
        var sample = new MonitorSample(Start, 12.5, [process], 0) { CompactedProcesses = 7 };
        var path = Path.Combine(_directory, "processes.csv");
        CsvExport.Write(path, CsvExportKind.ProcessSamples, [sample], [], Start, 0, 0);

        var content = File.ReadAllText(path, Encoding.UTF8);
        Assert.Multiple(() =>
        {
            Assert.That(content, Does.Contain("\"'=HYPERLINK(\"\"x,y\"\")\""));
            Assert.That(content, Does.Contain("C:\\a,\"\"b\"\"\napp.exe"));
            Assert.That(content, Does.Contain(",12.5,,1024,,2.5,,,true,7"));
        });
    }

    [Test]
    public void UsageTotals_IncludeExitedAppsAndNotAttributedTime()
    {
        var app = new AppUsage("app-key", "Example", "C:\\example.exe", "Example app", "Publisher",
            20_000_000, 1234.5, null, 2, 0, true, SampleKey.Of(new MonitorSample(Start, 1, [], 0)));
        var other = new AppUsage(UsageLedger.OtherAppsKey, "", "", "", "", 0, 0, null, 1, 0, false, null);
        var path = Path.Combine(_directory, "usage.csv");
        CsvExport.Write(path, CsvExportKind.UsageTotals, [], [app, other], Start, 40_000_000, 10_000_000);

        var rows = File.ReadAllLines(path);
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(4));
            Assert.That(rows[1], Does.Contain(",2,50,1234.5,,2,0,true,"));
            Assert.That(rows[2], Does.Contain(",other_apps,Other apps,"));
            Assert.That(rows[3], Does.Contain("not_attributed,,,,,1,25"));
            Assert.That(rows[3], Does.EndWith(",true"));
            Assert.That(File.ReadAllText(path), Does.Not.Contain('\0'));
        });
    }

    [Test]
    public void SelectSamples_UsesLatestRetainedTimeAndIncludesBoundary()
    {
        var samples = new[]
        {
            new MonitorSample(Start, 1, [], 0),
            new MonitorSample(Start.AddSeconds(1), 1, [], 0),
            new MonitorSample(Start.AddSeconds(3), 1, [], 0)
        };
        Assert.Multiple(() =>
        {
            Assert.That(CsvExport.SelectSamples(samples, null), Is.SameAs(samples));
            Assert.That(CsvExport.SelectSamples(samples, 2), Is.EqualTo(samples[1..]));
        });
    }

    [Test]
    public void Services_ExportsStoppedAndSharedServicesWithHostMetricsAndAvailability()
    {
        var host = new ProcessUsage(42, 123, "host", 12.5) { PrivateBytes = 1024 };
        var sample = new MonitorSample(Start, 12.5, [], 0);
        var running = new ServiceObservation("=service", "Shared, service", 4, 42, 1);
        var stopped = new ServiceObservation("stopped", "Stopped", 1, 0, 0);
        ServiceFrame[] frames = [new(sample,
            [new("shared", [running, running with { Name = "second" }], host, [host]),
             new("stopped", [stopped], null, [])]), new(sample, [], false), new(sample, [])];
        var path = Path.Combine(_directory, "services.csv");
        CsvExport.Write(path, CsvExportKind.Services, [], [], Start, 0, 0, services: frames);
        var rows = File.ReadAllLines(path);
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(6));
            Assert.That(rows[1], Does.Contain(",true,'=service,\"Shared, service\",4,42,1,true,123,host,,12.5,1024,"));
            Assert.That(rows[3], Does.EndWith(",true,stopped,Stopped,1,0,0,false,,,,,,"));
            Assert.That(rows[4], Does.Contain(",false,,,,,,,,,,,,"));
            Assert.That(rows[5], Does.Contain(",true,,,,,,,,,,,,"));
            Assert.That(rows.All(row => row.Replace("\"Shared, service\"", "shared").Split(',').Length == 14), Is.True);
        });
    }

    [Test]
    public void Connections_ExportsIpv6UdpAndPartialAvailabilityWithoutReusingPidMetadata()
    {
        var sample = new MonitorSample(Start, 1, [], 0);
        var tcp = new NetworkConnection(42, 123, TransportProtocol.Tcp,
            new(System.Net.IPAddress.IPv6Loopback, 80), new(System.Net.IPAddress.IPv6Loopback, 1234),
            System.Net.NetworkInformation.TcpState.Established);
        var udp = new NetworkConnection(42, 999, TransportProtocol.Udp, new(System.Net.IPAddress.Any, 53));
        var frame = new NetworkConnectionsFrame([tcp, udp], new(false, 5), new(true));
        var owner = new ProcessUsage(42, 123, "@owner", 0) { ImagePath = "C:\\app.exe" };
        ConnectionCapture[] captures = [new(sample, frame, [owner], null), new(sample, null, [], null),
            new(sample, new([], new(true), new(true)), [], null)];
        var path = Path.Combine(_directory, "connections.csv");
        CsvExport.Write(path, CsvExportKind.Connections, [], [], Start, 0, 0, connections: captures);
        var rows = File.ReadAllLines(path);
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(5));
            Assert.That(rows[1], Does.EndWith(",false,true,42,123,'@owner,C:\\app.exe,Tcp,::1,80,::1,1234,Established"));
            Assert.That(rows[2], Does.EndWith(",false,true,42,999,,,Udp,0.0.0.0,53,,,"));
            Assert.That(rows[3], Does.EndWith(",false,false,,,,,,,,,,"));
            Assert.That(rows[4], Does.EndWith(",true,true,,,,,,,,,,"));
            Assert.That(rows.All(row => row.Split(',').Length == 13), Is.True);
        });
    }

    [Test]
    public void FailedWrite_PreservesExistingFileAndRemovesTemporaryFile()
    {
        var path = Path.Combine(_directory, "existing.csv");
        File.WriteAllText(path, "original");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CsvExport.Write(path, (CsvExportKind)99, [], [], Start, 0, 0));
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(path), Is.EqualTo("original"));
            Assert.That(Directory.GetFiles(_directory), Is.EqualTo([path]));
        });
    }
}
