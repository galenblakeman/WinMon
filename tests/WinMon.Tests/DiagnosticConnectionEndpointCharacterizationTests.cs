using System.Net;
using System.Reflection;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase("listener", "match", true), TestCase("listener", "wrong-pid", false)]
    [TestCase("listener", "wrong-protocol", false), TestCase("listener", "port-before", false)]
    [TestCase("listener", "port-after", false), TestCase("listener", "remote-present", false)]
    [TestCase("listener", "ipv6", true), TestCase("listener", "different-creation", true)]
    [TestCase("udp", "match", true), TestCase("udp", "wrong-pid", false)]
    [TestCase("udp", "wrong-protocol", false), TestCase("udp", "port-before", false)]
    [TestCase("udp", "port-after", false), TestCase("udp", "remote-present", false)]
    [TestCase("udp", "ipv6", true), TestCase("udp", "different-creation", true)]
    [TestCase("peers", "match", true), TestCase("peers", "wrong-pid", false)]
    [TestCase("peers", "wrong-protocol", false), TestCase("peers", "other-local", false)]
    [TestCase("peers", "empty-remote", false), TestCase("peers", "wrong-remote", false)]
    [TestCase("peers", "client-match", true), TestCase("peers", "client-empty-remote", false)]
    [TestCase("peers", "client-wrong-remote", false), TestCase("peers", "ipv6", true)]
    [TestCase("peers", "different-creation", true)]
    public void ConnectionEndpointDiagnosticPreservesOwnershipProtocolPortAndNullablePeerRules(string kind, string partition, bool expected)
    {
        const int listenerPort = 41001, clientPort = 41002, datagramPort = 42001;
        var ordinal = kind == "listener" ? 2 : kind == "peers" ? 3 : 4;
        // These anonymous methods are explicit report targets. Bind their current compiled identities;
        // do not run the parent workflow that opens sockets or reads the native connection table.
        var method = typeof(WindowDiagnostics).GetNestedTypes(BindingFlags.NonPublic)
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Single(value => value.Name == $"<CaptureConnectionsDiagnosticsAsync>b__{ordinal}");
        var closure = Activator.CreateInstance(method.DeclaringType!, nonPublic: true)!;
        foreach (var (name, value) in new[] { ("tcpPort", listenerPort), ("clientPort", clientPort), ("udpPort", datagramPort) })
        {
            var field = method.DeclaringType!.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new AssertionException("Endpoint predicate capture changed: " + name);
            field.SetValue(closure, value);
        }
        var check = method.CreateDelegate<Func<NetworkConnection, bool>>(closure);
        var localPort = kind == "udp" ? datagramPort : listenerPort;
        int? remotePort = kind == "peers" ? clientPort : null;
        if (partition == "port-before") localPort--;
        if (partition == "port-after") localPort++;
        if (partition == "other-local") localPort = listenerPort + 3;
        if (partition.StartsWith("client-", StringComparison.Ordinal)) { localPort = clientPort; remotePort = listenerPort; }
        if (partition.EndsWith("empty-remote", StringComparison.Ordinal)) remotePort = null;
        if (partition == "wrong-remote") remotePort = listenerPort;
        if (partition == "client-wrong-remote") remotePort = clientPort;
        if (partition == "remote-present") remotePort = clientPort;
        var protocol = kind == "udp" ? TransportProtocol.Udp : TransportProtocol.Tcp;
        if (partition == "wrong-protocol") protocol = protocol == TransportProtocol.Tcp ? TransportProtocol.Udp : TransportProtocol.Tcp;
        var address = partition == "ipv6" ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        // Null locals additionally prove that ownership/protocol rejection precedes port access.
        var local = partition is "wrong-pid" or "wrong-protocol" ? null! : new IPEndPoint(address, localPort);
        var connection = new NetworkConnection(partition == "wrong-pid" ? -1 : Environment.ProcessId,
            partition == "different-creation" ? 999 : 100, protocol, local,
            remotePort is { } port ? new IPEndPoint(address, port) : null);

        Assert.That(check(connection), Is.EqualTo(expected));
        Assert.That(check(connection), Is.EqualTo(expected), "Repeated evaluation must remain side-effect free.");
        Assert.That(connection.LocalEndpoint, Is.SameAs(local));
        Assert.That(connection.RemoteEndpoint?.Port, Is.EqualTo(remotePort));
    }
}
