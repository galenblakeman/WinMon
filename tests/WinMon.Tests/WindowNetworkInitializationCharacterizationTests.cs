using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration")]
public sealed class WindowNetworkInitializationCharacterizationTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void InitializationPreservesDisabledDiagnosticAndPreCancelledStartupContracts(bool diagnostic, bool enabled)
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var added = new List<object>();
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) { app.Resources[key] = new Style(); added.Add(key); }
        if (!app.Resources.Contains("TitleBarButtonText"))
        { app.Resources["TitleBarButtonText"] = new DataTemplate(); added.Add("TitleBarButtonText"); }
        if (!app.Resources.Contains(typeof(Button)))
        { app.Resources[typeof(Button)] = new Style(typeof(Button)); added.Add(typeof(Button)); }
        var root = Path.Combine(Path.GetTempPath(), "WinMon.NetworkInitialization." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "marker.txt"), "owned startup baseline");
        MainWindow? window = null;
        try
        {
            var preferences = new MonitorPreferences { ProcessNetworkMonitoringEnabled = enabled };
            window = new MainWindow(diagnostic, preferences, new PreferenceStore(Path.Combine(root, "settings.json")));
            window.Loaded -= window.OnLoaded;
            window.Preferences = preferences;
            // RestoreAsync must finish at its canceled gate before any connection/setup work.
            // The normal enabled/noncanceled branch is deliberately outside this fixture.
            if (enabled) window.StopForFixtureAsync().GetAwaiter().GetResult();
            Method("InitializeNetworkMonitoring").CreateDelegate<Action>(window)();

#if WINMON_STORE
            const bool supported = false;
#else
            const bool supported = true;
#endif
            Assert.That(NetworkSetupBroker.Supported, Is.EqualTo(supported));
            var controller = Field<NetworkMonitoringController>(window, "_networkMonitoring");
            var restore = Field<Task>(window, "_networkRestore");
            if (diagnostic || !supported)
            {
                Assert.That(controller, Is.Null);
                Assert.That(restore, Is.Null);
            }
            else
            {
                Assert.That(controller, Is.Not.Null);
                Assert.That(controller!.State, Is.EqualTo(NetworkMonitoringState.Disabled));
                if (enabled)
                {
                    Assert.That(restore, Is.Not.Null);
                    Assert.That(restore!.IsCompletedSuccessfully, Is.True);
                    var result = ((Task<NetworkMonitoringChangeResult>)restore).GetAwaiter().GetResult();
                    Assert.That(result, Is.EqualTo(new NetworkMonitoringChangeResult(false, NetworkMonitoringState.Cancelled)));
                }
                else Assert.That(restore, Is.Null);
            }

            var change = Method("ChangeNetworkMonitoringAsync")
                .CreateDelegate<Func<bool, CancellationToken, Task<NetworkMonitoringChangeResult>>>(window)(false, CancellationToken.None);
            Assert.That(change.IsCompletedSuccessfully, Is.True);
            var expected = diagnostic || !supported
                ? new NetworkMonitoringChangeResult(false, supported ? NetworkMonitoringState.SetupUnavailable : NetworkMonitoringState.StoreUnavailable)
                : enabled
                    ? new NetworkMonitoringChangeResult(false, NetworkMonitoringState.Cancelled)
                    : new NetworkMonitoringChangeResult(true, NetworkMonitoringState.Disabled);
            Assert.That(change.GetAwaiter().GetResult(), Is.EqualTo(expected));
            Assert.That(window.Preferences, Is.SameAs(preferences));
            Assert.That(window.SamplingTask, Is.Null);
            Assert.That(window.IsVisible, Is.False);
            Assert.That(window.Tray, Is.Null);
            Assert.That(Directory.GetFileSystemEntries(root).Select(Path.GetFileName), Is.EqualTo(new[] { "marker.txt" }));
            Assert.That(File.ReadAllText(Path.Combine(root, "marker.txt")), Is.EqualTo("owned startup baseline"));
        }
        finally
        {
            if (window is not null)
            {
                window.StopForFixtureAsync().GetAwaiter().GetResult();
                var shutdown = Method("ShutdownNetworkMonitoringAsync").CreateDelegate<Func<Task>>(window)();
                Assert.That(shutdown.IsCompletedSuccessfully, Is.True);
                shutdown.GetAwaiter().GetResult();
                foreach (var view in new FrameworkElement[] { window.UsageView, window.Processes, window.ServiceView, window.ConnectionView })
                    view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                window.CloseForFixture();
            }
            foreach (var key in added) app.Resources.Remove(key);
            Assert.That(Path.GetFullPath(root), Does.StartWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WinMon.NetworkInitialization."))));
            Directory.Delete(root, true);
        }
    }

    private static MethodInfo Method(string name) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static T? Field<T>(MainWindow window, string name) where T : class =>
        (T?)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
}
