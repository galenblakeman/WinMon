using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

internal sealed class OwnedServiceOperations(
    Func<CancellationToken, Task> execute,
    Func<ProcessStartInfo, Process?>? start = null) : IServiceActionOperations
{
    internal ConcurrentQueue<(string Name, string Action, int Pid, long Creation, CancellationToken Token, int Thread)> Calls { get; } = new();
    internal ConcurrentQueue<ProcessStartInfo> Starts { get; } = new();
    public Task ExecuteAsync(string name, string action, int processId, long creationTicks, CancellationToken cancellationToken)
    {
        Calls.Enqueue((name, action, processId, creationTicks, cancellationToken, Environment.CurrentManagedThreadId));
        return execute(cancellationToken);
    }
    public Process? Start(ProcessStartInfo info)
    {
        Starts.Enqueue(info);
        return start is null ? throw new AssertionException("Unexpected helper launch") : start(info);
    }
}

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase("start", false)]
    [TestCase("start", true)]
    [TestCase("stop", false)]
    [TestCase("stop", true)]
    [TestCase("restart", false)]
    [TestCase("restart", true)]
    public void ServiceActionSuccessfulExecutionPreservesWorkerInputsAndRestoresTheNotice(string action, bool hasHost)
    {
        using var ui = new UpdateUi(_directory);
        var thread = Environment.CurrentManagedThreadId;
        var operations = new OwnedServiceOperations(_ => Task.CompletedTask);
        using var scope = ServiceActionOperations.OverrideForTests(operations);
        var host = hasHost ? new ProcessUsage(1234, 987654321, "Owned host", 0) : null;
        InvokeServiceAction(ui, action, action == "start" ? null : AcceptServiceConfirmation, host);
        Assert.That(operations.Calls.ToArray(), Is.EqualTo(new[]
        {
            (OwnedService.Name, action, host?.ProcessId ?? 0, host?.CreationTicks ?? 0, ui.Window.Lifetime.Token,
                operations.Calls.Single().Thread)
        }));
        Assert.That(operations.Calls.Single().Thread, Is.Not.EqualTo(thread), "Keep SCM work off the UI thread");
        Assert.That(operations.Starts, Is.Empty);
        Assert.That(ui.Window.ServiceView.Notice.Text, Is.EqualTo(Strings.ServicesCollecting));
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [TestCase("missing-service")]
    [TestCase("canceled-native-request")]
    [TestCase("invalid-operation")]
    [TestCase("argument")]
    public void ServiceActionExpectedExecutionErrorsPreserveTheExistingMessageWithoutElevation(string kind)
    {
        using var ui = new UpdateUi(_directory);
        Exception expected = kind switch
        {
            "missing-service" => new Win32Exception(1060),
            "canceled-native-request" => new Win32Exception(1223),
            "invalid-operation" => new InvalidOperationException("Owned invalid operation"),
            _ => new ArgumentException("Owned argument")
        };
        var operations = new OwnedServiceOperations(_ => Task.FromException(expected));
        using var scope = ServiceActionOperations.OverrideForTests(operations);
        InvokeServiceAction(ui, "start");
        Assert.That(ui.Window.ServiceView.Notice.Text, Is.EqualTo(Strings.Format(Strings.ServiceActionFailed, expected.Message)));
        Assert.That(operations.Calls.Count, Is.EqualTo(1));
        Assert.That(operations.Starts, Is.Empty, "Only access denied may request elevation");
    }

    [TestCase("start", false)]
    [TestCase("start", true)]
    [TestCase("stop", false)]
    [TestCase("stop", true)]
    [TestCase("restart", false)]
    [TestCase("restart", true)]
    [SetCulture("ar-SA")]
    public void ServiceActionAccessDeniedPreservesElevationArgumentsAndNullLaunchError(string action, bool hasHost)
    {
        using var ui = new UpdateUi(_directory);
        var operations = new OwnedServiceOperations(_ => Task.FromException(new Win32Exception(5)), _ => null);
        using var scope = ServiceActionOperations.OverrideForTests(operations);
        var host = hasHost ? new ProcessUsage(1234, 987654321, "Owned host", 0) : null;
        InvokeServiceAction(ui, action, action == "start" ? null : AcceptServiceConfirmation, host);
        var start = operations.Starts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(start.FileName, Is.EqualTo(Environment.ProcessPath));
            Assert.That(start.UseShellExecute, Is.True);
            Assert.That(start.Verb, Is.EqualTo("runas"));
            Assert.That(start.WindowStyle, Is.EqualTo(ProcessWindowStyle.Hidden));
            Assert.That(start.Arguments, Is.Empty);
            Assert.That(start.ArgumentList, Is.EqualTo(new[]
            {
                "--service-action", action, OwnedService.Name, hasHost ? "1234" : "0", hasHost ? "987654321" : "0"
            }));
            Assert.That(ui.Window.ServiceView.Notice.Text,
                Is.EqualTo(Strings.Format(Strings.ServiceActionFailed, new Win32Exception(5).Message)));
        });
        Assert.That(operations.Calls.Count, Is.EqualTo(1));
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [TestCase("uac-canceled")]
    [TestCase("invalid-operation")]
    [TestCase("argument")]
    public void ServiceActionHelperLaunchErrorsRetainTheExistingErrorFilter(string kind)
    {
        using var ui = new UpdateUi(_directory);
        Exception expected = kind switch
        {
            "uac-canceled" => new Win32Exception(1223),
            "invalid-operation" => new InvalidOperationException("Owned launch failure"),
            _ => new ArgumentException("Owned launch argument")
        };
        var operations = new OwnedServiceOperations(_ => Task.FromException(new Win32Exception(5)), _ => throw expected);
        using var scope = ServiceActionOperations.OverrideForTests(operations);
        InvokeServiceAction(ui, "start");
        Assert.That(operations.Starts.Count, Is.EqualTo(1));
        Assert.That(ui.Window.ServiceView.Notice.Text, Is.EqualTo(Strings.Format(Strings.ServiceActionFailed, expected.Message)));
    }

    [Test]
    public void ServiceActionPendingWorkRejectsReentryAndAllowsANewActionAfterCompletion()
    {
        using var ui = new UpdateUi(_directory);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var operations = new OwnedServiceOperations(_ =>
        {
            entered.TrySetResult();
            return Interlocked.Increment(ref calls) == 1 ? release.Task : Task.CompletedTask;
        });
        using var scope = ServiceActionOperations.OverrideForTests(operations);
        InvokeServiceAction(ui, "start", whilePending: () =>
        {
            FinishWindowCheck(entered.Task);
            Assert.That(ui.Window.ServiceView.Notice.Text, Is.EqualTo(Strings.ServiceActionInProgress));
            InvokeServiceAction(ui, "stop");
            Assert.That(operations.Calls.Count, Is.EqualTo(1));
            release.TrySetResult();
        });
        InvokeServiceAction(ui, "start", host: new ProcessUsage(0, 0, "Owned zero identity", 0));
        Assert.That(operations.Calls.Count, Is.EqualTo(2));
        Assert.That(operations.Calls.Last().Pid, Is.Zero);
        Assert.That(operations.Calls.Last().Creation, Is.Zero);
        Assert.That(ui.Window.ServiceView.Notice.Text, Is.EqualTo(Strings.ServicesCollecting));
    }

    [Test]
    public void ServiceActionCancellationDuringExecutionIsSilentAndClearsTheBusyNotice()
    {
        using var ui = new UpdateUi(_directory);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new OwnedServiceOperations(_ => { entered.TrySetResult(); return release.Task; });
        using var scope = ServiceActionOperations.OverrideForTests(operations);
        InvokeServiceAction(ui, "start", whilePending: () =>
        {
            FinishWindowCheck(entered.Task);
            Assert.That(operations.Calls.Single().Token, Is.EqualTo(ui.Window.Lifetime.Token));
            Assert.That(ui.Window.ServiceView.Notice.Text, Is.EqualTo(Strings.ServiceActionInProgress));
            ui.Window.Lifetime.Cancel();
            release.TrySetCanceled(ui.Window.Lifetime.Token);
        });
        Assert.That(operations.Starts, Is.Empty);
        Assert.That(ui.Window.ServiceView.Notice.Text, Is.EqualTo(Strings.ServicesCollecting));
    }

    [TestCase("foreign-cancellation")]
    [TestCase("unexpected-io")]
    public void ServiceActionUnexpectedExceptionsKeepAsyncVoidPropagationAndStillReleaseBusyState(string kind)
    {
        using var ui = new UpdateUi(_directory);
        using var foreign = new CancellationTokenSource();
        foreign.Cancel();
        Exception expected = kind == "foreign-cancellation"
            ? new OperationCanceledException("Owned foreign cancellation", foreign.Token)
            : new IOException("Owned unexpected I/O");
        var operations = new OwnedServiceOperations(_ => Task.FromException(expected));
        using var scope = ServiceActionOperations.OverrideForTests(operations);
        InvokeServiceAction(ui, "start", verifyErrors: errors => Assert.That(errors, Is.EqualTo(new[] { expected })));
        Assert.That(ui.Window.Lifetime.IsCancellationRequested, Is.False);
        Assert.That(ui.Window.ServiceView.Notice.Text, Is.EqualTo(Strings.ServicesCollecting));
        Assert.That(operations.Starts, Is.Empty);
        var retry = new OwnedServiceOperations(_ => Task.CompletedTask);
        using var retryScope = ServiceActionOperations.OverrideForTests(retry);
        InvokeServiceAction(ui, "start");
        Assert.That(retry.Calls.Count, Is.EqualTo(1), "Unexpected failures must release the action guard");
        Assert.That(ui.Window.ServiceView.Notice.Text, Is.EqualTo(Strings.ServicesCollecting));
    }

    [Test]
    public void ServiceActionClosingSkipsTheViewUpdateButReleasesTheActionGuard()
    {
        using var ui = new UpdateUi(_directory);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new OwnedServiceOperations(_ => release.Task);
        using var scope = ServiceActionOperations.OverrideForTests(operations);
        InvokeServiceAction(ui, "start", whilePending: () =>
        {
            Assert.That(ui.Window.ServiceView.Notice.Text, Is.EqualTo(Strings.ServiceActionInProgress));
            ui.Window.IsClosing = true;
            release.TrySetResult();
        });
        Assert.That(ui.Window.ServiceView.Notice.Text, Is.EqualTo(Strings.ServiceActionInProgress));
        ui.Window.IsClosing = false;
        InvokeServiceAction(ui, "start");
        Assert.That(operations.Calls.Count, Is.EqualTo(2));
        Assert.That(ui.Window.ServiceView.Notice.Text, Is.EqualTo(Strings.ServicesCollecting));
    }

    [Test]
    public void ServiceActionCloseAfterCompletionClosesOnlyTheFixtureWindow()
    {
        using var ui = new UpdateUi(_directory);
        var closed = 0;
        ui.Window.Closed += (_, _) => closed++;
        ui.Window.CloseAfterServiceAction = true;
        ui.Window.AllowClose = true;
        var operations = new OwnedServiceOperations(_ => Task.CompletedTask);
        using var scope = ServiceActionOperations.OverrideForTests(operations);
        InvokeServiceAction(ui, "start");
        Assert.That(closed, Is.EqualTo(1));
        Assert.That(operations.Calls.Count, Is.EqualTo(1));
    }
}
