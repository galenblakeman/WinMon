using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture, Category("Integration")]
public sealed class ConnectionCsvCharacterizationTests
{
    private const string Header = "timestamp_utc,ipv4_available,ipv6_available,pid,creation_ticks,name,image_path,protocol,local_address,local_port,remote_address,remote_port,tcp_state";
    private const string Timestamp = "2026-11-21T11:14:15.0000000Z";
    private static readonly MonitorSample Sample = new(new DateTimeOffset(2026, 11, 21, 13, 14, 15,
        TimeSpan.FromHours(2)), 1, [], 0);
    private static readonly ProcessUsage Owner = new(42, 100, "worker", 0) { ImagePath = @"C:\app.exe" };
    private static readonly NetworkConnection Endpoint = new(42, 100, TransportProtocol.Tcp,
        new(IPAddress.Loopback, 0), new(IPAddress.IPv6Loopback, 65535), TcpState.Established);
    private string _directory = null!;
    private string Pathname => Path.Combine(_directory, "connections.csv");

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), nameof(ConnectionCsvCharacterizationTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    private static ConnectionCapture Capture(NetworkConnection? endpoint = null, ProcessUsage? owner = null) =>
        new(Sample, new([endpoint ?? Endpoint], new(true), new(false, 5)), [owner ?? Owner], null);

    private void Write(params ConnectionCapture[] captures) => CsvExport.Write(Pathname, CsvExportKind.Connections,
        [], [], DateTimeOffset.UnixEpoch, 0, 0, connections: captures);

    private void AssertText(params string[] rows)
    {
        var text = string.Join(Environment.NewLine, new[] { Header }.Concat(rows)) + Environment.NewLine;
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Pathname), Is.EqualTo(text));
            Assert.That(File.ReadAllBytes(Pathname), Is.EqualTo(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray()));
            Assert.That(Directory.GetFiles(_directory), Is.EqualTo(new[] { Pathname }));
        });
    }

    [TestCase("en-US")]
    [TestCase("fr-FR")]
    [TestCase("ar-SA")]
    public void OutputPreservesUtf8BomUtcRawNumbersAndEndpointColumnsAcrossCultures(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Write(Capture());
            AssertText(Timestamp + @",true,false,42,100,worker,C:\app.exe,Tcp,127.0.0.1,0,::1,65535,Established");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestCase("=formula", "'=formula")]
    [TestCase(" +formula", "' +formula")]
    [TestCase("\t-formula", "'\t-formula")]
    [TestCase("@formula", "'@formula")]
    [TestCase("ordinary", "ordinary")]
    [TestCase("a,b", "\"a,b\"")]
    [TestCase("a\"b", "\"a\"\"b\"")]
    [TestCase("a\r\nb", "\"a\r\nb\"")]
    [TestCase("", "")]
    public void OwnerTextKeepsFormulaProtectionAndCsvEscaping(string value, string expected)
    {
        Write(Capture(owner: Owner with { Name = value, ImagePath = value }));

        AssertText(Timestamp + $",true,false,42,100,{expected},{expected},Tcp,127.0.0.1,0,::1,65535,Established");
    }

    [TestCase(null, "")]
    [TestCase(99L, "99")]
    [TestCase(100L, "100")]
    [TestCase(0L, "0")]
    [TestCase(-1L, "-1")]
    public void OwnerLookupUsesExactNonNullTokenIncludingZeroAndNegative(long? token, string expectedToken)
    {
        var owner = Owner with { CreationTicks = token ?? 100 };
        if (token == 99) owner = Owner;
        Write(Capture(Endpoint with { ProcessCreationTicks = token }, owner));
        var metadata = token is null or 99 ? "," : @"worker,C:\app.exe";

        AssertText(Timestamp + $",true,false,42,{expectedToken},{metadata},Tcp,127.0.0.1,0,::1,65535,Established");
    }

    [Test]
    public void ExistingTupleLookupCanBorrowMetadataAcrossLifetimeDomains()
    {
        Write(Capture(owner: Owner with { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds }));

        AssertText(Timestamp + @",true,false,42,100,worker,C:\app.exe,Tcp,127.0.0.1,0,::1,65535,Established");
    }

    [Test]
    public void NullOwnerDisplayFieldsRemainEmptyAndUdpKeepsOptionalRemoteAndState()
    {
        Write(Capture(Endpoint with { Protocol = TransportProtocol.Udp, RemoteEndpoint = null, State = null },
            Owner with { Name = null!, ImagePath = null! }));

        AssertText(Timestamp + ",true,false,42,100,,,Udp,127.0.0.1,0,,,");
    }

    [Test]
    public void CapturesAndConnectionsKeepTheirOrderAndDoNotUseTrafficOrGapFilters()
    {
        var first = Capture();
        var secondEndpoint = Endpoint with { ProcessId = 43, Protocol = (TransportProtocol)99, State = (TcpState)99 };
        var second = first with
        {
            Sample = Sample with { Timestamp = Sample.Timestamp.AddSeconds(1), IsGap = true },
            Connections = first.Connections! with { Connections = [secondEndpoint, Endpoint] },
            Network = new(1, 1, ProcessNetworkQuality.Unavailable, [])
        };
        Write(first, second);

        AssertText(Timestamp + @",true,false,42,100,worker,C:\app.exe,Tcp,127.0.0.1,0,::1,65535,Established",
            "2026-11-21T11:14:16.0000000Z,true,false,43,100,,,99,127.0.0.1,0,::1,65535,99",
            @"2026-11-21T11:14:16.0000000Z,true,false,42,100,worker,C:\app.exe,Tcp,127.0.0.1,0,::1,65535,Established");
    }

    [TestCase(0, "false,false")]
    [TestCase(1, "false,false")]
    [TestCase(2, "true,false")]
    [TestCase(3, "false,true")]
    public void EmptyOrUnavailableFramesWriteOnePlaceholderWithoutReadingOwners(int kind, string availability)
    {
        NetworkConnectionsFrame? frame = kind switch
        {
            0 => null,
            1 => new(null!, new(false, 5), new(false, 50)),
            2 => new([], new(true), new(false, 5)),
            _ => new([], new(false, 5), new(true))
        };
        Write(new ConnectionCapture(Sample, frame, null!, null));

        AssertText(Timestamp + "," + availability + ",,,,,,,,,,");
    }

    [Test]
    public void EmptyOrNullCaptureCollectionWritesOnlyTheHeader()
    {
        Write();
        AssertText();
        CsvExport.Write(Pathname, CsvExportKind.Connections, [], [], DateTimeOffset.UnixEpoch, 0, 0, connections: null);
        AssertText();
    }

    [TestCase(0, typeof(ArgumentException))]
    [TestCase(1, typeof(ArgumentNullException))]
    [TestCase(2, typeof(NullReferenceException))]
    [TestCase(3, typeof(NullReferenceException))]
    [TestCase(4, typeof(NullReferenceException))]
    [TestCase(5, typeof(NullReferenceException))]
    [TestCase(6, typeof(NullReferenceException))]
    [TestCase(7, typeof(NullReferenceException))]
    public void MalformedCapturePreservesOriginalFileAndRemovesTemporaryOutput(int kind, Type error)
    {
        var original = new byte[] { 9, 8, 7, 0, 255 };
        File.WriteAllBytes(Pathname, original);
        var capture = Capture();
        capture = kind switch
        {
            0 => capture with { Owners = [Owner, Owner], Connections = capture.Connections! with
                { Connections = [Endpoint with { ProcessCreationTicks = null }] } },
            1 => capture with { Owners = null! },
            2 => capture with { Owners = [null!] },
            3 => capture with { Connections = capture.Connections! with { Connections = null! } },
            4 => capture with { Connections = capture.Connections! with { Connections = [null!] } },
            5 => capture with { Connections = capture.Connections! with { Connections = [Endpoint with { LocalEndpoint = null! }] } },
            6 => null!,
            _ => capture with { Sample = null! }
        };

        Assert.That(() => Write(capture), Throws.TypeOf(error));
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(Pathname), Is.EqualTo(original));
            Assert.That(Directory.GetFiles(_directory), Is.EqualTo(new[] { Pathname }));
        });
    }
}
