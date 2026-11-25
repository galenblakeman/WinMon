using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, Category("Integration"), SetCulture("en-US")]
    public void ConnectionsWorkflowDiagnosticPreservesReportsNavigationQualityPinsAndCopyEvent()
    {
        var copies = new List<string>();
        using var scope = new ConnectionsWorkflowScope(_directory, copies.Add);
        try
        {
            scope.Run();
            using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "native-connections.json")));
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "connections.json")));
            Assert.Multiple(() =>
            {
                Assert.That(native.RootElement.GetProperty("ProcessId").GetInt32(), Is.EqualTo(Environment.ProcessId));
                Assert.That(native.RootElement.GetProperty("TcpPort").GetInt32(), Is.GreaterThan(0));
                Assert.That(native.RootElement.GetProperty("UdpPort").GetInt32(), Is.GreaterThan(0));
                Assert.That(native.RootElement.GetProperty("OwnEndpoints").GetArrayLength(), Is.GreaterThanOrEqualTo(4));
                Assert.That(report.RootElement.GetProperty("NativeEndpoints").GetInt32(), Is.GreaterThan(0));
                Assert.That(report.RootElement.GetProperty("NativeAvailable").GetBoolean() ||
                            report.RootElement.GetProperty("NativePartial").GetBoolean(), Is.True);
                Assert.That(report.RootElement.GetProperty("FixtureChecks").GetString(), Is.EqualTo("passed"));
                Assert.That(report.RootElement.GetProperty("IllustrativeData").GetBoolean(), Is.True);
                Assert.That(report.RootElement.GetProperty("CaptureMilliseconds").GetArrayLength(), Is.EqualTo(6));
                Assert.That(report.RootElement.GetProperty("CaptureMilliseconds").EnumerateArray().All(value => value.GetDouble() >= 0), Is.True);
                Assert.That(copies, Has.Count.EqualTo(1));
                Assert.That(copies.Single(), Does.Contain("203.0.113.24:443"));
                Assert.That(File.ReadAllText(Path.Combine(_directory, "capture-report.txt")), Does.Contain("203.0.113.24:443"));
                Assert.That(scope.Window!.Session.History.Samples, Has.Count.EqualTo(121));
                Assert.That(scope.Window.Preferences.PinnedConnections, Has.Length.EqualTo(1));
                Assert.That(scope.Window.ConnectionView.DisplayedRows, Has.Count.EqualTo(2));
                Assert.That(scope.Window.ConnectionView.DisplayedRows[0].Pinned, Is.True);
                Assert.That(scope.Window.ConnectionView.DisplayedRows[0].Connections, Has.Count.EqualTo(5));
                Assert.That(scope.Window.ConnectionView.DisplayedRows.Sum(row => row.Connections.Count), Is.EqualTo(6));
                Assert.That(scope.Window.ConnectionView.PinnedOnly.IsChecked, Is.False);
            });
            AssertConnectionsWorkflowClosed(scope);
            var own = WindowsConnections.Capture().Connections.Where(connection => connection.ProcessId == Environment.ProcessId).ToArray();
            var tcpPort = native.RootElement.GetProperty("TcpPort").GetInt32();
            var udpPort = native.RootElement.GetProperty("UdpPort").GetInt32();
            Assert.That(own.Any(connection => connection.Protocol == TransportProtocol.Tcp && connection.LocalEndpoint.Port == tcpPort), Is.False);
            Assert.That(own.Any(connection => connection.Protocol == TransportProtocol.Udp && connection.LocalEndpoint.Port == udpPort), Is.False);
            var images = Directory.GetFiles(_directory, "*.png");
            Assert.That(images.Length, Is.GreaterThanOrEqualTo(20));
            foreach (var path in images)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert.That(frame.PixelWidth, Is.GreaterThan(0));
                Assert.That(frame.PixelHeight, Is.GreaterThan(0));
            }
        }
        finally { PreserveConnectionsWorkflowArtifacts("complete"); }
    }

    [Test, Category("Integration"), SetCulture("en-US")]
    public void ConnectionsWorkflowDiagnosticLateCancellationPreservesNativeReportAndClosesFixture()
    {
        using var scope = new ConnectionsWorkflowScope(_directory, _ => Assert.Fail("Copy must not run before the first canceled capture"), true);
        try
        {
            Assert.Catch<OperationCanceledException>(scope.Run);
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Path.Combine(_directory, "native-connections.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(_directory, "connections.json")), Is.False);
                Assert.That(Directory.GetFiles(_directory, "*.png"), Is.Empty);
                Assert.That(scope.Window!.Session.History.Samples, Has.Count.EqualTo(121));
            });
            AssertConnectionsWorkflowClosed(scope);
        }
        finally { PreserveConnectionsWorkflowArtifacts("late-cancel"); }
    }

    [Test, Category("Integration"), SetCulture("en-US")]
    public void ConnectionsWorkflowDiagnosticLockedNativeReportPreservesSentinelAndCreatesNoFixture()
    {
        Directory.CreateDirectory(_directory);
        using var ui = new UpdateUi(_directory, true);
        var path = Path.Combine(_directory, "native-connections.json");
        File.WriteAllText(path, "owned connections sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var windows = Application.Current.Windows.Count;
        var task = WindowDiagnostics.CaptureConnectionsDiagnosticsAsync(_directory, CancellationToken.None);
        try
        {
            Assert.Throws<IOException>(() => FinishWindowCheck(task));
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(path), Is.EqualTo("owned connections sentinel"));
                Assert.That(Directory.GetFiles(_directory).Select(Path.GetFileName), Is.EqualTo(new[] { "native-connections.json" }));
                Assert.That(Application.Current.Windows.Count, Is.EqualTo(windows));
                Assert.That(ui.Window.SamplingTask, Is.Null);
            });
        }
        finally { PreserveConnectionsWorkflowArtifacts("locked-native-report"); }
    }

    private static void AssertConnectionsWorkflowClosed(ConnectionsWorkflowScope scope)
    {
        Assert.Multiple(() =>
        {
            Assert.That(scope.Window, Is.Not.Null);
            Assert.That(scope.Window!.IsVisible, Is.False);
            Assert.That(scope.Window.SamplingTask, Is.Null);
            Assert.That(Application.Current.Windows.Cast<Window>(), Does.Not.Contain(scope.Window));
            Assert.That(scope.DispatcherError, Is.Null);
            Assert.That(scope.TimedOut, Is.False);
        });
    }

    private sealed class ConnectionsWorkflowScope : IDisposable
    {
        private readonly AutoCaptureDiagnosticScope _scope;
        private ConnectionsView? _configuredView;
        private Action<string>? _originalClipboardHandler;
        private readonly Action<string> _copy;
        private readonly bool _cancelAtFixture;
        private static readonly FieldInfo CopyEvent = typeof(ConnectionsView).GetField("CopyRequested",
            BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new InvalidOperationException("Missing connection copy event boundary");
        internal MainWindow? Window => _scope.Window;
        internal Exception? DispatcherError => _scope.DispatcherError;
        internal bool TimedOut => _scope.TimedOut;

        internal ConnectionsWorkflowScope(string directory, Action<string> copy, bool cancelAtFixture = false)
        {
            _copy = copy;
            _cancelAtFixture = cancelAtFixture;
            _scope = new AutoCaptureDiagnosticScope(directory, ConfigureWindow);
            InstallDesignActionStyles();
            InstallServicesWorkflowTitleStyles();
        }

        private bool ConfigureWindow(MainWindow window)
        {
            if (window.ConnectionView is not { } view || CopyEvent.GetValue(view) is not Action<string> handlers) return false;
            var original = handlers.GetInvocationList().Cast<Action<string>>().SingleOrDefault(handler => ReferenceEquals(handler.Target, window));
            if (original is null) throw new InvalidOperationException("Cannot isolate the owned connection clipboard subscriber");
            view.CopyRequested -= original;
            view.CopyRequested += _copy;
            _originalClipboardHandler = original;
            _configuredView = view;
            if (_cancelAtFixture) _scope.Cancel();
            return true;
        }

        internal void Run() => _scope.Run(WindowDiagnostics.CaptureConnectionsDiagnosticsAsync);
        public void Dispose()
        {
            if (_configuredView is not null)
            {
                _configuredView.CopyRequested -= _copy;
                _configuredView.CopyRequested += _originalClipboardHandler;
            }
            _scope.Dispose();
        }
    }

    private void PreserveConnectionsWorkflowArtifacts(string scenario)
    {
        if (TestContext.Parameters.Get("ConnectionsWorkflowArtifactDirectory") is not { Length: > 0 } artifacts) return;
        artifacts = Path.Combine(artifacts, scenario);
        Directory.CreateDirectory(artifacts);
        foreach (var path in Directory.GetFiles(_directory))
            File.Copy(path, Path.Combine(artifacts, Path.GetFileName(path)), true);
    }
}
