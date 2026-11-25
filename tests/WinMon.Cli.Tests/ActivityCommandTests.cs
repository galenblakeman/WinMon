using System.Net;
using System.Text.Json;
using MonCore.Monitoring;
using NUnit.Framework;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class ActivityCommandTests
{
    private sealed class Source : IFrameSource
    {
        internal int Count;
        internal bool Disposed;
        public CapturedFrame Capture(SamplingOptions options)
        {
            Count++;
            var process = new ProcessUsage(42, 123, "worker", 25, 100) { ImagePath = "C:\\private\\worker.exe", PrivateBytes = 200 };
            var sample = new MonitorSample(DateTimeOffset.UtcNow, 25, [process], 0) { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 };
            return new CapturedFrame(sample, new ServiceFrame(sample,
                [new ServiceGroup("group", [new ServiceObservation("worker-service", "Worker service", 4, 42, 0)], process, [process])]))
            {
                Connections = new NetworkConnectionsFrame([new NetworkConnection(42, 123, TransportProtocol.Tcp,
                    new IPEndPoint(IPAddress.Loopback, 42))], NetworkAddressFamilyStatus.Success, new(false, 5))
            };
        }
        public void Reset() { }
        public void Dispose() => Disposed = true;
    }

    [TestCase("services")]
    [TestCase("connections")]
    [TestCase("usage")]
    public async Task StructuredCommandsCollectDisposeAndPreserveRawMetrics(string name)
    {
        var source = new Source();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await CliCommand.Create(() => source, output, error).Parse([name, "--format", "json", "--top", "1"]).InvokeAsync();
        using var doc = JsonDocument.Parse(output.ToString());
        var json = doc.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero);
            Assert.That(error.ToString(), Is.Empty);
            Assert.That(source.Disposed, Is.True);
            Assert.That(source.Count, Is.EqualTo(2));
            Assert.That(json.GetProperty("kind").GetString(), Is.EqualTo(name));
            Assert.That(json.GetProperty("available").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(1));
        });
        var entry = json.GetProperty("entries")[0];
        if (name == "usage") Assert.That(entry.GetProperty("cpuTicks").GetInt64(), Is.EqualTo(2_500_000));
        if (name == "services") Assert.That(entry.GetProperty("cpuPercent").GetDouble(), Is.EqualTo(25));
        if (name == "connections")
        {
            Assert.That(entry.GetProperty("localPort").GetInt32(), Is.EqualTo(42));
            Assert.That(json.GetProperty("ipv6").GetProperty("errorCode").GetInt32(), Is.EqualTo(5));
        }
    }

    [TestCase("--duration", "0")]
    [TestCase("--duration", "61")]
    [TestCase("--duration", "invalid")]
    [TestCase("--top", "-1")]
    [TestCase("--top", "1001")]
    [TestCase("--sort", "bad")]
    public void InvalidOptionsNeverCollect(string option, string value)
    {
        var root = CliCommand.Create(() => throw new AssertionException("Must not collect"));
        foreach (var name in new[] { "services", "connections", "usage" })
            Assert.That(root.Parse([name, option, value]).Errors, Is.Not.Empty);
    }

    [Test]
    public async Task CancellationDisposesWithoutPartialOutput()
    {
        var source = new Source();
        using var output = new StringWriter();
        using var cancel = new CancellationTokenSource(50);
        var exit = await CliCommand.Create(() => source, output).Parse(["usage", "--duration", "60", "--format", "json"])
            .InvokeAsync(cancellationToken: cancel.Token);
        Assert.That(exit, Is.EqualTo(130));
        Assert.That(source.Disposed, Is.True);
        Assert.That(output.ToString(), Is.Empty);
    }

    [Test]
    public void MissingLatestServicesDoNotReturnOlderRows()
    {
        using var source = new Source();
        var old = source.Capture(new());
        var missing = old with { Services = null };
        var report = ActivityReport.Create("services", [old, missing], new UsageLedger(old.Full.Timestamp), 0, "", 20);
        var json = JsonSerializer.SerializeToElement(report.Data, SnapshotReport.JsonOptions);
        Assert.That(json.GetProperty("available").GetBoolean(), Is.False);
        Assert.That(json.GetProperty("entries").GetArrayLength(), Is.Zero);
    }

    [Test]
    public void EndpointOwnersRequireMatchingLifetimeAndDomain()
    {
        using var source = new Source();
        var frame = source.Capture(new());
        var reused = frame with { Full = frame.Full with { Processes = [frame.Full.Processes[0] with { CreationTicks = 456 }] } };
        var mismatched = JsonSerializer.SerializeToElement(ActivityReport.Create("connections", [reused],
            new UsageLedger(frame.Full.Timestamp), 0, "worker", 20).Data, SnapshotReport.JsonOptions);
        Assert.That(mismatched.GetProperty("entries").GetArrayLength(), Is.Zero);
        var report = ActivityReport.Create("connections", [frame], new UsageLedger(frame.Full.Timestamp), 0, "worker", 20);
        var json = JsonSerializer.SerializeToElement(report.Data, SnapshotReport.JsonOptions);
        Assert.That(json.GetProperty("entries")[0].GetProperty("name").GetString(), Is.EqualTo("worker"));
        Assert.That(json.GetProperty("entries")[0].GetProperty("domain").GetString(), Is.EqualTo("WindowsFileTime"));
        Assert.That(report.Table.Columns, Does.Not.Contain("PID {0}"));
    }
}
