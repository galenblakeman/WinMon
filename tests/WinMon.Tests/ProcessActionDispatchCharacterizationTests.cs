using Win32Exception = System.ComponentModel.Win32Exception;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ProcessActionDispatchCharacterizationTests
{
    private static readonly ProcessUsage Process = new(int.MaxValue, 1, "captured fixture", 0);
    private static readonly DateTimeOffset Time = DateTimeOffset.UnixEpoch.AddDays(1);
    private readonly List<object> _addedResources = [];
    private ProcessesView _view = null!;

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var key in new[] { "RangeChip", "ActionBarCollapseButton", "ActionBarButton", "ActionBarLastButton" })
            if (!app.Resources.Contains(key)) { app.Resources[key] = new Style(); _addedResources.Add(key); }
        foreach (var (key, brush) in new[] { ("Surface", Brushes.White), ("Text", Brushes.Black) })
            if (!app.Resources.Contains(key)) { app.Resources[key] = brush; _addedResources.Add(key); }
        _view = new ProcessesView();
        Load(false);
        Capabilities();
    }

    [TearDown]
    public void TearDown()
    {
        _view?.Close();
        _view?.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
        _addedResources.Clear();
    }

    private static ProcessDetailsSnapshot Snapshot(bool available = true, bool historical = false, bool grouped = false) =>
        new(Process, grouped, Time, historical, available, "captured state", new SampleKey(0, Time));

    private void Load(bool grouped) => _view.Load(new ProcessListSettings(10, ProcessSort.Cpu, grouped, false, false), []);

    private void Capabilities(bool owned = false)
    {
        _view.ProcessActions.EnablePriorityActions(_ => false, _ => null, (_, _) => Assert.Fail("No priority operation."));
        _view.ProcessActions.EnableControlActions(_ => true, _ => true, _ => owned, _ => false, _ => EfficiencyModeState.Off);
    }

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is FrameworkElement element) yield return element;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private Action Command(string action, ProcessDetailsSnapshot snapshot)
    {
        Capabilities(action == "resume");
        _view.ProcessActions.Update(snapshot, true, true, true, true);
        if (action is "copy" or "end" or "properties")
        {
            var label = action switch
            {
                "copy" => Strings.CopyDetails, "properties" => Strings.Properties,
                _ => snapshot.Grouped ? Strings.EndProcessGroup : Strings.EndProcess
            };
            var button = Descendants(_view.ProcessActions).OfType<Button>().Single(b => Equals(b.Content, label));
            return () => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        var header = action switch
        {
            "dump" => Strings.CreateDumpFile + "…",
            "location" => Strings.OpenFileLocation,
            "connections" => Strings.ShowInConnections,
            "usage" => Strings.ShowInUsage,
            "suspend" => Strings.SuspendProcess,
            "resume" => Strings.ResumeProcess,
            "affinity" => Strings.SetAffinity,
            "efficiency" => Strings.EfficiencyMode,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        var menu = _view.ProcessActions.CreateMoreMenu()!;
        var item = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, header));
        return () => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private static void WithoutDialogs(Action command) => WithDialogs(command);

    private static void WithDialogs(Action command, params Action<Window>[] steps)
    {
        var existing = Application.Current.Windows.Cast<Window>().ToHashSet();
        var seen = new List<string>();
        Exception? failure = null;
        var operations = new List<DispatcherOperation>();
        var completed = 0;
        void Schedule() => operations.Add(Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var windows = Application.Current.Windows.Cast<Window>().Where(w => !existing.Contains(w)).ToArray();
            foreach (var window in windows)
            {
                try
                {
                    if (windows.Length != 1 || completed >= steps.Length)
                        seen.Add(window.GetType().Name + ": " + window.Title);
                    else steps[completed++](window);
                }
                catch (Exception ex) { failure ??= ex; }
                finally
                {
                    if (window.IsVisible)
                    {
                        try { window.DialogResult = false; }
                        catch (InvalidOperationException) { window.Close(); }
                    }
                }
            }
            Schedule();
        })));
        Schedule();
        try { command(); }
        catch (Exception ex) { failure = ex; }
        finally
        {
            foreach (var operation in operations) operation.Abort();
            foreach (var window in Application.Current.Windows.Cast<Window>().Where(w => !existing.Contains(w)).ToArray())
                window.Close();
        }
        Assert.That(seen, Is.Empty, "Only the expected owned dialogs may be opened.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.That(completed, Is.EqualTo(steps.Length));
    }

    private static IEnumerable<TestCaseData> RejectedSnapshots()
    {
        foreach (var action in new[] { "end", "dump", "suspend", "resume", "affinity", "efficiency" })
            foreach (var state in new[] { "unavailable", "historical", "grouped" })
                yield return new TestCaseData(action, state);
    }

    [TestCaseSource(nameof(RejectedSnapshots))]
    public void RejectedActionSnapshotsDoNotOpenDialogsOrTouchControlState(string action, string state)
    {
        var snapshot = Snapshot(state != "unavailable", state == "historical", state == "grouped");
        var command = Command(action, snapshot);
        var requests = 0;
        _view.ProcessActions.ActionRequested += (_, captured) =>
        {
            requests++;
            Assert.That(captured, Is.SameAs(snapshot));
        };
        WithoutDialogs(command);
        Assert.That(requests, Is.EqualTo(1), "Exercise the dispatcher guard even if the visual command is disabled.");
        Assert.That(_view.SuspendedProcesses.Pending, Is.Empty);
    }

    private static IEnumerable<TestCaseData> RejectedContexts()
    {
        foreach (var action in new[] { "end", "dump", "suspend", "resume", "affinity", "efficiency" })
            foreach (var state in new[] { "historical", "grouped", "error" })
                if (state != "error" || action is not ("end" or "dump"))
                    yield return new TestCaseData(action, state);
    }

    [TestCaseSource(nameof(RejectedContexts))]
    public void CapturedCommandsRecheckTheCurrentViewContext(string action, string state)
    {
        var snapshot = Snapshot();
        var command = Command(action, snapshot);
        var sample = new MonitorSample(Time, 50, [Process], 0);
        var settings = new ProcessListSettings(10, ProcessSort.Cpu, state == "grouped", false, false);
        _view.Update(new ProcessListFrame([sample], sample, sample, state == "historical" ? sample : null,
            sample, sample, false, state == "error" ? SamplingError.ReadingsUnavailable : null, false, settings));
        // The action bar can still hold an older command while the parent frame has advanced.
        if (action == "end") command = Command(action, snapshot);
        var requests = 0;
        _view.ProcessActions.ActionRequested += (_, _) => requests++;
        WithoutDialogs(command);
        Assert.That(requests, Is.EqualTo(1));
        Assert.That(_view.SuspendedProcesses.Pending, Is.Empty);
    }

    [TestCase("connections", false)]
    [TestCase("usage", false)]
    [TestCase("connections", true)]
    [TestCase("usage", true)]
    public void NavigationForwardsTheCapturedSnapshotUntilTheViewCloses(string action, bool closing)
    {
        var snapshot = Snapshot(false, true, true);
        var command = Command(action, snapshot);
        _view.ProcessActions.Update(snapshot with { Process = Process with { Name = "replacement" } }, false, false);
        var requests = new List<(string Action, ProcessDetailsSnapshot Snapshot)>();
        _view.NavigationRequested += (target, captured) => requests.Add((target, captured));
        if (closing) _view.Close();
        WithoutDialogs(command);
        Assert.That(requests, Has.Count.EqualTo(closing ? 0 : 1));
        if (!closing)
        {
            Assert.That(requests[0].Action, Is.EqualTo(action));
            Assert.That(requests[0].Snapshot, Is.SameAs(snapshot));
        }
    }

    [TestCase("historical")]
    [TestCase("unavailable")]
    [TestCase("grouped")]
    [TestCase("unidentified")]
    public void CopyUsesTheSuppliedSnapshotWithoutLiveInspectionForRestrictedModes(string mode)
    {
        var snapshot = Snapshot(mode != "unavailable", mode == "historical", mode == "grouped");
        if (mode == "unidentified") snapshot = snapshot with { Process = Process with { CreationTicks = 0 } };
        var copied = new List<string>();
        _view.CopyRequested += copied.Add;
        WithoutDialogs(Command("copy", snapshot));
        Assert.That(copied, Has.Count.EqualTo(1));
        Assert.That(copied[0], Is.EqualTo(snapshot.Format()));
        Assert.That(copied[0], Does.Contain(Process.Name));
    }

    [TestCase("connections")]
    [TestCase("usage")]
    public void NavigationSubscriberExceptionsKeepTheirIdentityAndDoNotPoisonTheNextRequest(string action)
    {
        var snapshot = Snapshot();
        var command = Command(action, snapshot);
        var expected = new InvalidOperationException("navigation subscriber failed");
        Action<string, ProcessDetailsSnapshot> fail = (_, _) => throw expected;
        var following = 0;
        _view.NavigationRequested += fail;
        _view.NavigationRequested += (_, _) => following++;
        Assert.That(Assert.Throws<InvalidOperationException>(() => command()), Is.SameAs(expected));
        Assert.That(following, Is.Zero);
        _view.NavigationRequested -= fail;
        command();
        Assert.That(following, Is.EqualTo(1));
    }

    [TestCase("end")]
    [TestCase("suspend")]
    [Category("Integration")]
    public void CancelledConfirmationsKeepTheirExistingCaptionAndLeaveControlStateUntouched(string action)
    {
        WithDialogs(Command(action, Snapshot()), window =>
        {
            Assert.That(window, Is.TypeOf<ProcessActionDialog>());
            Assert.That(window.Title, Is.EqualTo(Strings.EndProcess));
            window.DialogResult = false;
        });
        Assert.That(_view.SuspendedProcesses.Pending, Is.Empty);
    }

    [TestCase("affinity")]
    [TestCase("efficiency")]
    [TestCase("resume")]
    [Category("Integration")]
    public void DefaultControlReadFailuresAndUntrackedResumeShowANoticeWithoutTrackingATarget(string action)
    {
        WithDialogs(Command(action, Snapshot()), window =>
        {
            Assert.That(window, Is.TypeOf<MessageDialog>());
            Assert.That(window.Title, Is.EqualTo(action switch
            {
                "affinity" => Strings.SetAffinity, "efficiency" => Strings.EfficiencyMode, _ => Strings.ResumeProcess
            }));
            window.DialogResult = false;
        });
        Assert.That(_view.SuspendedProcesses.Pending, Is.Empty);
    }

    [Test, Category("Integration")]
    public void TheDumpChooserCanBeCancelledWithoutStartingAWrite()
    {
        WithDialogs(Command("dump", Snapshot()), window =>
        {
            Assert.That(window, Is.TypeOf<ProcessDumpDialog>());
            Assert.That(window.Title, Is.EqualTo(Strings.CreateDumpFile));
            window.DialogResult = false;
        });
    }

    [Test, Category("Integration")]
    public void HistoricalPropertiesCanBeClosedWithoutReadingLiveInspection()
    {
        WithDialogs(Command("properties", Snapshot(false, true)), window =>
        {
            Assert.That(window, Is.TypeOf<ProcessPropertiesWindow>());
            window.DialogResult = false;
        });
    }

    [Test, Category("Integration")]
    public void MissingExecutableLocationsShowTheExistingNoticeInsteadOfLaunchingExplorer()
    {
        var path = Path.Combine(Path.GetTempPath(), "WinMon-never-created-" + Guid.NewGuid().ToString("N"), "missing.exe");
        Assert.That(File.Exists(path), Is.False);
        var snapshot = Snapshot() with { Process = Process with { ImagePath = path } };
        WithDialogs(Command("location", snapshot), window =>
        {
            Assert.That(window, Is.TypeOf<MessageDialog>());
            Assert.That(window.Title, Is.EqualTo(Strings.OpenFileLocation));
            Assert.That(Descendants(window).OfType<TextBlock>().Select(t => t.Text), Does.Contain(Strings.ProcessFileMissing));
            window.DialogResult = false;
        });
    }

    private ControlState UseControls()
    {
        _view.Close();
        _view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        var state = new ControlState();
        _view = new ProcessesView(new ProcessActionDependencies(state.Open, () => state.Service,
            _ => state.Suspends + state.ExternalSuspends > 0));
        Load(false);
        Capabilities();
        return state;
    }

    private static void AcceptConfirmation(Window window)
    {
        Assert.That(window, Is.TypeOf<ProcessActionDialog>());
        window.DialogResult = true;
    }

    private static void CloseNotice(Window window)
    {
        Assert.That(window, Is.TypeOf<MessageDialog>());
        window.DialogResult = false;
    }

    private static void SetAffinity(Window window)
    {
        Assert.That(window, Is.TypeOf<ProcessAffinityDialog>());
        var dialog = (ProcessAffinityDialog)window;
        foreach (var choice in dialog.Processors.Items.OfType<CheckBox>())
            choice.IsChecked = (ulong)choice.Tag == 2;
        Assert.That(dialog.OkButton.IsEnabled, Is.True);
        dialog.OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [Test, Category("Integration")]
    public void ConfirmedSuspensionTracksExactlyOneBalancedRequestAndObservesItsStatus()
    {
        var state = UseControls();
        WithDialogs(Command("suspend", Snapshot()), AcceptConfirmation);
        Assert.That(state.Suspends, Is.EqualTo(1));
        Assert.That(_view.SuspendedProcesses.Contains(Process), Is.True);
        Assert.That(state.Handles, Has.Count.EqualTo(1));
        Assert.That(state.Handles.All(h => h.Disposed), Is.True);
    }

    [TestCase("reused")]
    [TestCase("critical")]
    [TestCase("exited")]
    [Category("Integration")]
    public void SuspensionRevalidatesTheOpenedLifetimeAndProtectedStateBeforeWriting(string failure)
    {
        var state = UseControls();
        state.CreationTicks = failure == "reused" ? 2 : 1;
        state.Critical = failure == "critical";
        state.Exited = failure == "exited";
        WithDialogs(Command("suspend", Snapshot()), AcceptConfirmation, CloseNotice);
        Assert.That(state.Suspends, Is.Zero);
        Assert.That(_view.SuspendedProcesses.Pending, Is.Empty);
        Assert.That(state.Handles.All(h => h.Disposed), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    [Category("Integration")]
    public void SuspensionHonorsProtectedServiceAndUnreadableServiceVetoes(bool unreadable)
    {
        var state = UseControls();
        state.Service = new(unreadable ? null : Process.ProcessId, unreadable);
        WithDialogs(Command("suspend", Snapshot()), AcceptConfirmation, CloseNotice);
        Assert.That(state.Handles, Is.Empty);
        Assert.That(state.Suspends, Is.Zero);
        Assert.That(_view.SuspendedProcesses.Pending, Is.Empty);
    }

    [TestCase("closing")]
    [TestCase("grouped")]
    [Category("Integration")]
    public void SuspensionRechecksEligibilityAfterConfirmation(string change)
    {
        var state = UseControls();
        WithDialogs(Command("suspend", Snapshot()), window =>
        {
            if (change == "closing") _view.Close(); else Load(true);
            AcceptConfirmation(window);
        });
        Assert.That(state.Handles, Is.Empty);
        Assert.That(_view.SuspendedProcesses.Pending, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    [Category("Integration")]
    public void ResumeBalancesOnlyWinMonsRequestAndRetainsFailuresForRetry(bool fails)
    {
        var state = UseControls();
        Assert.That(_view.SuspendedProcesses.Suspend(Process, state.Open, () => state.Service), Is.Null);
        state.FailResume = fails;
        var command = Command("resume", Snapshot());
        if (fails) WithDialogs(command, CloseNotice); else WithoutDialogs(command);
        Assert.That(state.Suspends, Is.EqualTo(fails ? 1 : 0));
        Assert.That(_view.SuspendedProcesses.Contains(Process), Is.EqualTo(fails));
        Assert.That(state.ResumeCalls, Is.EqualTo(1));
        Assert.That(state.Handles.All(h => h.Disposed), Is.True);
    }

    [Test, Category("Integration")]
    public void ResumeReportsAnOutsideSuspensionWithoutIssuingAnotherResume()
    {
        var state = UseControls();
        Assert.That(_view.SuspendedProcesses.Suspend(Process, state.Open, () => state.Service), Is.Null);
        state.ExternalSuspends = 1;
        WithDialogs(Command("resume", Snapshot()), window =>
        {
            Assert.That(Descendants(window).OfType<TextBlock>().Select(t => t.Text), Does.Contain(Strings.ProcessStillSuspended));
            CloseNotice(window);
        });
        Assert.That(state.Suspends, Is.Zero);
        Assert.That(state.ExternalSuspends, Is.EqualTo(1));
        Assert.That(state.ResumeCalls, Is.EqualTo(1));
        Assert.That(_view.SuspendedProcesses.Pending, Is.Empty);
    }

    [TestCase("cancel")]
    [TestCase("apply")]
    [TestCase("write-failure")]
    [TestCase("eligibility-changed")]
    [TestCase("multiple-groups")]
    [Category("Integration")]
    public void AffinityKeepsCancellationRevalidationReadOnlyAndWriteFailurePolicies(string outcome)
    {
        var state = UseControls();
        state.FailAffinity = outcome == "write-failure";
        state.Groups = outcome == "multiple-groups" ? (ushort)2 : (ushort)1;
        Action<Window> choose = window =>
        {
            Assert.That(window, Is.TypeOf<ProcessAffinityDialog>());
            var dialog = (ProcessAffinityDialog)window;
            if (outcome is "cancel" or "multiple-groups")
            {
                Assert.That(dialog.OkButton.IsEnabled, Is.EqualTo(outcome != "multiple-groups"));
                dialog.DialogResult = false;
            }
            else
            {
                if (outcome == "eligibility-changed") Load(true);
                SetAffinity(window);
            }
        };
        if (state.FailAffinity) WithDialogs(Command("affinity", Snapshot()), choose, CloseNotice);
        else WithDialogs(Command("affinity", Snapshot()), choose);
        Assert.That(state.Affinity, Is.EqualTo(outcome == "apply" ? 2UL : 1UL));
        Assert.That(state.AffinityWrites, Is.EqualTo(outcome is "apply" or "write-failure" ? 1 : 0));
        Assert.That(state.Handles.All(h => h.Disposed), Is.True);
    }

    [TestCase("On")]
    [TestCase("Off")]
    [TestCase("Mixed")]
    public void EfficiencyTogglesFromTheFreshBackendStateInsteadOfTheMenuCheckmark(string initial)
    {
        var state = UseControls();
        var mode = Enum.Parse<EfficiencyModeState>(initial);
        var speed = ProcessPowerThrottlingState.ExecutionSpeed;
        state.Power = new(speed, mode == EfficiencyModeState.On ? speed : 0);
        state.Priority = mode == EfficiencyModeState.Off ? ProcessPriorityClass.Normal : ProcessPriorityClass.Idle;
        WithoutDialogs(Command("efficiency", Snapshot()));
        Assert.That(state.Priority, Is.EqualTo(mode == EfficiencyModeState.On ? ProcessPriorityClass.Normal : ProcessPriorityClass.Idle));
        Assert.That((state.Power.StateMask & speed) != 0, Is.EqualTo(mode != EfficiencyModeState.On));
        Assert.That(state.Handles, Has.Count.EqualTo(2));
        Assert.That(state.Handles.All(h => h.Disposed), Is.True);
    }

    [Test, Category("Integration")]
    public void EfficiencyWriteErrorsAreShownAndAllOpenedHandlesAreDisposed()
    {
        var state = UseControls();
        state.Power = new(ProcessPowerThrottlingState.ExecutionSpeed, 0);
        state.FailPower = true;
        WithDialogs(Command("efficiency", Snapshot()), CloseNotice);
        Assert.That(state.Power.StateMask, Is.Zero);
        Assert.That(state.Priority, Is.EqualTo(ProcessPriorityClass.Normal));
        Assert.That(state.Handles.All(h => h.Disposed), Is.True);
    }

    private sealed class ControlState
    {
        public long CreationTicks = 1;
        public bool Critical, Exited, FailResume, FailAffinity, FailPower;
        public int Suspends, ExternalSuspends, ResumeCalls, AffinityWrites;
        public ushort Groups = 1;
        public ulong Affinity = 1;
        public ProcessPriorityClass Priority = ProcessPriorityClass.Normal;
        public ProcessPowerThrottlingState Power = new(0, 0);
        public NetworkServiceProcessStatus Service = new(null, false);
        public List<ControlHandle> Handles { get; } = [];
        public IProcessControlTarget Open(int pid)
        {
            Assert.That(pid, Is.EqualTo(Process.ProcessId));
            var handle = new ControlHandle(this);
            Handles.Add(handle);
            return handle;
        }
    }

    private sealed class ControlHandle(ControlState state) : IProcessControlTarget
    {
        public bool Disposed { get; private set; }
        public long CreationTicks => state.CreationTicks;
        public bool HasExited => state.Exited;
        public bool IsCritical => state.Critical;
        public ulong AffinityMask => state.Affinity;
        public ulong SystemAffinityMask => 3;
        public ushort ProcessGroupCount => state.Groups;
        public ProcessPriorityClass PriorityClass => state.Priority;
        public ProcessPowerThrottlingState PowerThrottling => state.Power;
        public void Suspend() => state.Suspends++;
        public void Resume()
        {
            state.ResumeCalls++;
            if (state.FailResume) throw new Win32Exception(5);
            state.Suspends--;
        }
        public void SetAffinityMask(ulong mask)
        {
            state.AffinityWrites++;
            if (state.FailAffinity) throw new Win32Exception(5);
            state.Affinity = mask;
        }
        public void SetPriorityClass(ProcessPriorityClass value) => state.Priority = value;
        public void SetPowerThrottling(ProcessPowerThrottlingState value)
        {
            if (state.FailPower) throw new Win32Exception(5);
            state.Power = value;
        }
        public void Dispose() => Disposed = true;
    }
}
