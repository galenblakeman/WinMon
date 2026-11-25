using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture, NonParallelizable]
public sealed class ServiceActionOperationsTests
{
    [Test]
    public void ServiceActionDefaultExecutionKeepsValidationBeforeNativeServiceAccess()
    {
        var error = Assert.ThrowsAsync<ArgumentException>(async () =>
            await ServiceActionOperations.ExecuteAsync("Owned unknown service", "unknown", 0, 0, default));
        Assert.That(error!.Message, Is.EqualTo(new ArgumentException("action").Message));
    }

    [TestCase("null")]
    [TestCase("missing-filename")]
    public void ServiceActionDefaultLaunchKeepsTheOriginalPreLaunchValidation(string kind)
    {
        var info = kind == "null" ? null! : new ProcessStartInfo();
        var original = Assert.Catch(() => Process.Start(info));
        var forwarded = Assert.Catch(() => ServiceActionOperations.Start(info));
        Assert.Multiple(() =>
        {
            Assert.That(forwarded!.GetType(), Is.EqualTo(original!.GetType()));
            Assert.That(forwarded.Message, Is.EqualTo(original.Message));
        });
    }

    [Test]
    public async Task ServiceActionOperationScopesFlowAcrossAwaitAndRestoreNestedNormalAndExceptionalScopes()
    {
        var outer = new OwnedServiceOperations(_ => Task.CompletedTask);
        var inner = new OwnedServiceOperations(_ => Task.CompletedTask);
        var scope = ServiceActionOperations.OverrideForTests(outer);
        try
        {
            await Task.Yield();
            await ServiceActionOperations.ExecuteAsync("outer-before", "unknown", 0, 0, default);
            using (ServiceActionOperations.OverrideForTests(inner))
            {
                await Task.Run(() => ServiceActionOperations.ExecuteAsync("inner-normal", "unknown", 0, 0, default));
            }
            await ServiceActionOperations.ExecuteAsync("outer-after-normal", "unknown", 0, 0, default);
            var expected = new InvalidOperationException("Owned scope unwind");
            try
            {
                using var nested = ServiceActionOperations.OverrideForTests(inner);
                await Task.Yield();
                await ServiceActionOperations.ExecuteAsync("inner-exception", "unknown", 0, 0, default);
                throw expected;
            }
            catch (InvalidOperationException error) { Assert.That(error, Is.SameAs(expected)); }
            await ServiceActionOperations.ExecuteAsync("outer-after-exception", "unknown", 0, 0, default);
            Assert.That(outer.Calls.Select(call => call.Name), Is.EqualTo(new[] { "outer-before", "outer-after-normal", "outer-after-exception" }));
            Assert.That(inner.Calls.Select(call => call.Name), Is.EqualTo(new[] { "inner-normal", "inner-exception" }));
        }
        finally { scope.Dispose(); scope.Dispose(); }
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await ServiceActionOperations.ExecuteAsync("Owned restored native validation", "unknown", 0, 0, default));
    }

    [Test]
    public async Task ServiceActionOperationScopesKeepOverlappingFreshProvidersIndependent()
    {
        var parent = new OwnedServiceOperations(_ => Task.CompletedTask);
        using var parentScope = ServiceActionOperations.OverrideForTests(parent);
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        Task Exercise(string name) => Task.Run(async () =>
        {
            var own = new OwnedServiceOperations(_ => Task.CompletedTask);
            using (ServiceActionOperations.OverrideForTests(own))
            {
                if (Interlocked.Increment(ref entered) == 2) bothEntered.TrySetResult();
                await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await ServiceActionOperations.ExecuteAsync(name, "unknown", 0, 0, default);
                await Task.Yield();
                await ServiceActionOperations.ExecuteAsync(name, "unknown", 0, 0, default);
                Assert.That(own.Calls.Select(call => call.Name), Is.EqualTo(new[] { name, name }));
            }
            await ServiceActionOperations.ExecuteAsync("restored-" + name, "unknown", 0, 0, default);
        });
        await Task.WhenAll(Exercise("left"), Exercise("right")).WaitAsync(TimeSpan.FromSeconds(10));
        await ServiceActionOperations.ExecuteAsync("parent", "unknown", 0, 0, default);
        Assert.That(parent.Calls.Select(call => call.Name), Is.EquivalentTo(new[] { "restored-left", "restored-right", "parent" }));
    }

    [Test]
    public async Task ServiceActionOperationScopeRejectsNullWithoutReplacingTheCurrentProvider()
    {
        var provider = new OwnedServiceOperations(_ => Task.CompletedTask);
        using var scope = ServiceActionOperations.OverrideForTests(provider);
        var error = Assert.Throws<ArgumentNullException>(() => ServiceActionOperations.OverrideForTests(null!));
        Assert.That(error!.ParamName, Is.EqualTo("operations"));
        await ServiceActionOperations.ExecuteAsync("still-owned", "unknown", 0, 0, default);
        Assert.That(provider.Calls.Single().Name, Is.EqualTo("still-owned"));
    }
}
