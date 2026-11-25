using WinMon.Monitoring;
using System.Diagnostics;
using NUnit.Framework;

namespace WinMon.Tests;

[TestFixture, NonParallelizable]
public sealed class NetworkSetupAvailabilityCharacterizationTests
{
    private static readonly string[] Assets =
    [
        "WinMon.NetworkSetup.exe", "WinMon.NetworkSetup.Service.zip",
        "WinMon.NetworkSetup.Manifest.json", "WinMon.NetworkSetup.Manifest.sig"
    ];

    [TestCase("install")]
    [TestCase("remove")]
    [TestCase("invalid-operation")]
    [TestCase("canceled-install")]
    [TestCase("null-operation")]
    [TestCase("null-verifier")]
    public void MissingCompiledBundleFailsBeforeArgumentsCancellationAndVerification(string partition)
    {
        var assembly = typeof(NetworkSetupBroker).Assembly;
        var resources = assembly.GetManifestResourceNames();
        if (Assets.Any(name => resources.Contains(name, StringComparer.Ordinal)))
            Assert.Ignore("This fail-closed fixture requires a build with every signed setup asset omitted.");
        Assert.That(NetworkSetupBroker.IsAvailable, Is.False);
        var operation = partition switch
        {
            "remove" => "remove",
            "invalid-operation" => "unsupported",
            "null-operation" => null!,
            _ => "install"
        };
        using var cancellation = new CancellationTokenSource();
        if (partition == "canceled-install") cancellation.Cancel();
        var calls = 0;
        Func<CancellationToken, Task> verify = partition == "null-verifier" ? null! : _ =>
        {
            calls++;
            throw new AssertionException("Missing setup assets must not reach verification.");
        };
        using var trace = new SetupTrace();
        Trace.Listeners.Add(trace);
        try
        {
            var task = new NetworkSetupBroker().RunAsync(operation, verify, cancellation.Token);
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(task.IsCanceled, Is.False);
            var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
            Assert.That(error!.Message, Is.EqualTo("Signed network setup is unavailable."));
            Assert.That(calls, Is.Zero);
            Assert.That(cancellation.IsCancellationRequested, Is.EqualTo(partition == "canceled-install"));
            Assert.That(trace.Events, Is.Empty);
            Assert.That(assembly.GetManifestResourceNames(), Is.EqualTo(resources));
        }
        finally { Trace.Listeners.Remove(trace); }
    }

    private sealed class SetupTrace : TraceListener
    {
        internal List<string> Events { get; } = [];
        public override void Write(string? message)
        {
            if (message?.StartsWith("Network.Setup", StringComparison.Ordinal) == true) Events.Add(message);
        }
        public override void WriteLine(string? message) => Write(message);
        public override void TraceEvent(TraceEventCache? cache, string source, TraceEventType type, int id, string? message) => Write(message);
        public override void TraceEvent(TraceEventCache? cache, string source, TraceEventType type, int id, string? format, params object?[]? args) => Write(format);
    }
}
