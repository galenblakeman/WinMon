using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class SettingsDriveLoadingTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void SlowDiscovery_RunsOnceOffUiThread_AndPreservesCurrentSelections(bool chooseNone)
    {
        using var release = new ManualResetEventSlim();
        var uiThread = Environment.CurrentManagedThreadId;
        var workerThread = uiThread;
        var calls = 0;
        var window = new SettingsWindow(new MonitorPreferences { PrimaryDrive = "C:", SecondaryDrive = "Z:" }, () =>
        {
            Interlocked.Increment(ref calls);
            workerThread = Environment.CurrentManagedThreadId;
            release.Wait(TimeSpan.FromSeconds(10));
            return [new("C:", "C: — System"), new("D:", "D: — Data")];
        });
        var (primary, secondary) = Selectors(window);
        try
        {
            // Construction has not touched drive discovery or lost the saved selections.
            Assert.That(calls, Is.Zero);
            Assert.That(Selected(primary), Is.EqualTo("C:"));
            Assert.That(Selected(secondary), Is.EqualTo("Z:"));
            if (chooseNone) secondary.SelectedIndex = 0;

            var task = StartLoading(window);
            Assert.That(task.IsCompleted, Is.False);
            Assert.That(window.LoadDrivesAsync(), Is.SameAs(task));
            release.Set();
            Complete(task);

            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(workerThread, Is.Not.EqualTo(uiThread));
                Assert.That(Selected(primary), Is.EqualTo("C:"));
                Assert.That(((SettingsWindow.DriveChoice)primary.SelectedItem).Label, Is.EqualTo("C: — System"));
                Assert.That(primary.Items.Cast<SettingsWindow.DriveChoice>().Select(d => d.Name), Is.EqualTo(new[] { "C:", "D:" }));
                Assert.That(Selected(secondary), Is.EqualTo(chooseNone ? null : "Z:"));
            });
        }
        finally
        {
            release.Set();
            window.Close();
        }
    }

    [Test]
    public void ClosedDialog_IgnoresLateDiscovery()
    {
        using var release = new ManualResetEventSlim();
        var window = new SettingsWindow(new MonitorPreferences { PrimaryDrive = "C:" }, () =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return [new("D:", "D: — Data")];
        });
        var (primary, _) = Selectors(window);
        var originalItems = primary.ItemsSource;
        var task = StartLoading(window);
        window.Close();
        release.Set();
        Complete(task);
        Assert.That(primary.ItemsSource, Is.SameAs(originalItems));
    }

    [Test]
    public void DiscoveryFailure_PreservesSavedSelections()
    {
        var window = new SettingsWindow(new MonitorPreferences { PrimaryDrive = "C:", SecondaryDrive = "Z:" },
            () => throw new IOException("Drive unavailable"));
        try
        {
            Complete(StartLoading(window));
            var (primary, secondary) = Selectors(window);
            Assert.That(Selected(primary), Is.EqualTo("C:"));
            Assert.That(Selected(secondary), Is.EqualTo("Z:"));
        }
        finally { window.Close(); }
    }

    private static Task StartLoading(SettingsWindow window)
    {
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            return window.LoadDrivesAsync();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void Complete(Task task)
    {
        var frame = new DispatcherFrame();
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (task.IsCompleted || elapsed.Elapsed > TimeSpan.FromSeconds(10)) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.That(task.IsCompleted, Is.True, "Drive loading did not complete");
        task.GetAwaiter().GetResult();
    }

    private static string? Selected(ComboBox selector) => (selector.SelectedItem as SettingsWindow.DriveChoice)?.Name;

    private static (ComboBox Primary, ComboBox Secondary) Selectors(SettingsWindow window)
    {
        return (window.PrimaryDriveSelector, window.SecondaryDriveSelector);
    }
}
