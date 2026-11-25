using System.Reflection;
using NUnit.Framework;
using WinMon.Updates;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
    public void WindowUpdateNoticeClosingSkipsBothRestartRetryModesAndPreservesExistingNotice(bool expectedRestart, bool canceled)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        var tokenField = typeof(UpdateRuntime).GetField("_resultToken", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing update restart identity");
        var priorToken = tokenField.GetValue(null);
        var lifetime = (CancellationTokenSource)ui.Field("_updateLifetime").GetValue(ui.Window)!;
        try
        {
            tokenField.SetValue(null, expectedRestart ? Guid.NewGuid().ToString("N") : null);
            Assert.That(UpdateRuntime.IsExpectedRestart, Is.EqualTo(expectedRestart));
            ui.Window.IsClosing = true;
            if (canceled) lifetime.Cancel();
            ui.Window.ShowUpdateStatus("owned existing notice", true);
            var before = UpdateUiState(ui.Window);
            var check = (typeof(MainWindow).GetMethod("ShowCompletedUpdateNoticeAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing completed update notice callback"))
                .CreateDelegate<Func<Task>>(ui.Window);

            var task = check();
            Assert.That(task.IsCompletedSuccessfully, Is.True, "Closing must skip the notice consumer and all retry delays.");
            task.GetAwaiter().GetResult();
            Assert.That(UpdateUiState(ui.Window), Is.EqualTo(before));
            ui.Window.ShowCompletedUpdateNotice();
            Assert.Multiple(() =>
            {
                Assert.That(UpdateUiState(ui.Window), Is.EqualTo(before));
                Assert.That(ui.Window.IsClosing, Is.True);
                Assert.That(lifetime.IsCancellationRequested, Is.EqualTo(canceled));
                Assert.That(ui.Window.SamplingTask, Is.Null);
                Assert.That(File.Exists(Path.Combine(_directory, "settings.json")), Is.False);
            });
        }
        finally { tokenField.SetValue(null, priorToken); }
    }
}
