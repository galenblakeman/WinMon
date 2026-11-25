using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ServiceControlGuardTests
{
    private static IEnumerable<TestCaseData> UnsupportedActions()
    {
        string?[] actions = [null, "", "Start", "STOP", "Restart", " start", "stop ", "pause"];
        for (var i = 0; i < actions.Length; i++)
        foreach (var cancelled in new[] { false, true })
            yield return new TestCaseData(actions[i], cancelled)
                .SetName($"UnsupportedActionReturnsFaultedTaskBeforeCancellation_{i}_{cancelled}");
    }

    [TestCaseSource(nameof(UnsupportedActions))]
    public void UnsupportedActionReturnsFaultedTaskBeforeCancellation(string? action, bool cancelled)
    {
        using var cancellation = new CancellationTokenSource();
        if (cancelled) cancellation.Cancel();
        Task? task = null;

        Assert.DoesNotThrow(() => task = WindowsServices.ExecuteAsync("never-open-this-service", action!,
            -1, long.MinValue, cancellation.Token));
        Assert.That(task, Is.Not.Null);
        Assert.That(task!.IsFaulted, Is.True);
        var error = Assert.ThrowsAsync<ArgumentException>(async () => await task);
        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Is.EqualTo("action"));
            Assert.That(error.ParamName, Is.Null);
            Assert.That(task.IsCanceled, Is.False);
        });
    }
}
