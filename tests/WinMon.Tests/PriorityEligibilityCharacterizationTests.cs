using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, Category("Integration"), NonParallelizable, Apartment(ApartmentState.STA),
 SetCulture("en-US"), SetUICulture("en-US")]
public sealed class PriorityEligibilityCharacterizationTests
{
    private static readonly DateTimeOffset Time = DateTimeOffset.UnixEpoch.AddDays(1);
    private readonly List<object> _addedResources = [];
    private Process? _helper;
    private ProcessUsage _process = null!;
    private ProcessPriorityClass _originalPriority;
    private ProcessesView _view = null!;

    [OneTimeSetUp]
    public void StartOwnedHelper()
    {
        _helper = Process.Start(new ProcessStartInfo
        {
            FileName = System.IO.Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            Arguments = "-NoProfile -NonInteractive -Command \"[Console]::WriteLine('ready'); Start-Sleep -Seconds 60\"",
            UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        })!;
        try
        {
            Assert.That(_helper.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult(),
                Is.EqualTo("ready"));
            using var target = WindowsProcessPriorityTarget.Open(_helper.Id);
            Assert.That(target.HasExited, Is.False);
            Assert.That(target.IsCritical, Is.False);
            _process = new ProcessUsage(_helper.Id, target.CreationTicks, "owned-priority-fixture", 0);
            _originalPriority = target.PriorityClass;
        }
        catch { StopOwnedHelper(); throw; }
    }

    [OneTimeTearDown]
    public void StopOwnedHelper()
    {
        if (_helper is null) return;
        try
        {
            if (!_helper.HasExited) _helper.Kill();
            Assert.That(_helper.WaitForExit(5000), Is.True, "The fixture must reap its own helper.");
        }
        finally { _helper.Dispose(); _helper = null; }
    }

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var key in new[] { "RangeChip", "ActionBarCollapseButton", "ActionBarButton", "ActionBarLastButton" })
            if (!app.Resources.Contains(key)) { app.Resources[key] = new Style(); _addedResources.Add(key); }
        foreach (var (key, brush) in new[] { ("Surface", Brushes.White), ("Text", Brushes.Black) })
            if (!app.Resources.Contains(key)) { app.Resources[key] = brush; _addedResources.Add(key); }
        _view = new ProcessesView();
        _view.Load(Settings(), []);
        _view.ProcessActions.EnableControlActions(_ => false,
            _ => throw new AssertionException("Priority inspection must not query suspend capability."), _ => false,
            _ => throw new AssertionException("Priority inspection must not observe suspension."),
            _ => throw new AssertionException("Priority inspection must not read efficiency mode."));
        _view.ProcessActions.ActionRequested += (_, _) => Assert.Fail("Menu inspection must not execute a process action.");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (_helper is not null)
            {
                using var target = WindowsProcessPriorityTarget.Open(_helper.Id);
                Assert.That(target.CreationTicks, Is.EqualTo(_process.CreationTicks));
                Assert.That(target.HasExited, Is.False);
                Assert.That(target.PriorityClass, Is.EqualTo(_originalPriority), "Inspection must not change priority.");
            }
        }
        finally
        {
            _view?.Close();
            _view?.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
            _addedResources.Clear();
        }
    }

    private static ProcessListSettings Settings(bool grouped = false) => new(10, ProcessSort.Cpu, grouped, false, false);

    private void SetFrame(string state)
    {
        if (state == "uninitialized") return;
        var sample = new MonitorSample(Time, 10, [], 0);
        var settings = Settings(state == "grouped");
        _view.Update(new ProcessListFrame([sample], sample, sample, state == "historical" ? sample : null,
            sample, sample, false, state == "error" ? SamplingError.ReadingsUnavailable : null, false, settings));
        // Isolate the historical guard from Update's forced grouped capture settings through the existing API.
        if (state == "historical") _view.Load(Settings(), []);
        if (state == "closed") _view.Close();
    }

    private MenuItem PriorityMenu(ProcessUsage? process = null, bool available = true, bool historical = false)
    {
        _view.ProcessActions.Update(new ProcessDetailsSnapshot(process ?? _process, false, Time, historical,
            available, "captured priority fixture"), false, false);
        var menu = _view.ProcessActions.CreateMoreMenu();
        Assert.That(menu, Is.Not.Null);
        Assert.That(menu!.IsOpen, Is.False);
        return menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.SetPriority));
    }

    [TestCase("uninitialized", true)]
    [TestCase("live", true)]
    [TestCase("historical", false)]
    [TestCase("error", false)]
    [TestCase("grouped", false)]
    [TestCase("closed", false)]
    public void RealPriorityBindingUsesCurrentViewSafetyStateForACapturedLiveProcess(string state, bool enabled)
    {
        SetFrame(state);

        var priority = PriorityMenu();

        Assert.That(priority.IsEnabled, Is.EqualTo(enabled));
        Assert.That(priority.Items, Has.Count.EqualTo(4));
        Assert.That(priority.ToolTip, Is.EqualTo(enabled ? null : Strings.ProcessPriorityUnavailable));
    }

    [TestCase("historical")]
    [TestCase("error")]
    [TestCase("grouped")]
    public void ReturningToLiveReevaluatesPriorityEligibilityWithoutChangingTheCapturedLifetime(string state)
    {
        SetFrame(state);
        var blocked = PriorityMenu();
        Assert.That(blocked.IsEnabled, Is.False);

        SetFrame("live");
        var enabled = PriorityMenu();

        Assert.That(enabled, Is.Not.SameAs(blocked));
        Assert.That(enabled.IsEnabled, Is.True);
        Assert.That(blocked.IsEnabled, Is.False, "An existing menu retains its original enablement snapshot.");
    }

    [TestCase("idle")]
    [TestCase("system")]
    [TestCase("self")]
    [TestCase("unknown-start")]
    [TestCase("aggregate-count")]
    [TestCase("member-list")]
    [TestCase("reused-start")]
    public void LivePriorityMenuRejectsProtectedAggregatedAndMismatchedLifetimeRecords(string partition)
    {
        SetFrame("live");
        var process = partition switch
        {
            "idle" => _process with { ProcessId = 0 },
            "system" => _process with { ProcessId = 4 },
            "self" => _process with { ProcessId = Environment.ProcessId },
            "unknown-start" => _process with { CreationTicks = 0 },
            "aggregate-count" => _process with { MemberCount = 2 },
            "member-list" => _process with { MemberIds = [_process.ProcessId] },
            _ => _process with { CreationTicks = _process.CreationTicks + 1 }
        };

        var priority = PriorityMenu(process);

        Assert.That(priority.IsEnabled, Is.False);
        Assert.That(priority.ToolTip, Is.EqualTo(Strings.ProcessPriorityUnavailable));
    }

    [Test]
    public void LoadingAFreshLiveFrameCannotReenablePriorityAfterTheViewCloses()
    {
        SetFrame("live");
        Assert.That(PriorityMenu().IsEnabled, Is.True);
        _view.Close();

        SetFrame("live");

        Assert.That(PriorityMenu().IsEnabled, Is.False);
    }

    [TestCase(false, false)]
    [TestCase(true, true)]
    public void AnUnavailableOrHistoricalCapturedSnapshotCannotEnablePriorityInALiveView(bool available, bool historical)
    {
        SetFrame("live");
        Assert.That(PriorityMenu().IsEnabled, Is.True);

        var blocked = PriorityMenu(available: available, historical: historical);

        Assert.That(blocked.IsEnabled, Is.False);
        Assert.That(blocked.ToolTip, Is.EqualTo(Strings.ProcessPriorityUnavailable));
    }
}
