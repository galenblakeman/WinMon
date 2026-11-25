using System.Windows;
using Microsoft.Win32;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class CsvExportOperationsTests
{
    private const string InvalidPath = "owned\0.csv";
    private static void Write() => CsvExportOperations.Write(InvalidPath, CsvExportKind.ResourceHistory,
        [], [], DateTimeOffset.UnixEpoch, 0, 0, [], [], null);
    private static OwnedCsvExportOperations Provider(string name) =>
        new("owned-destination.csv", new CsvExportChoice(CsvExportKind.ResourceHistory, false, name));
    private static Window ClosedOwner()
    {
        var owner = new Window();
        owner.Close();
        return owner;
    }

    [Test]
    public void CsvExportDefaultWriterKeepsInvalidPathValidationBeforeFileCreation()
    {
        var original = Assert.Throws<ArgumentException>(() => CsvExport.Write(InvalidPath,
            CsvExportKind.ResourceHistory, [], [], DateTimeOffset.UnixEpoch, 0, 0, [], [], null));
        var forwarded = Assert.Throws<ArgumentException>(Write);
        Assert.That(forwarded!.ParamName, Is.EqualTo(original!.ParamName));
        Assert.That(forwarded.Message, Is.EqualTo(original.Message));
    }

    [Test]
    public async Task CsvExportScopesFlowThroughAwaitAndRestoreNormalAndExceptionalNestedProviders()
    {
        var owner = ClosedOwner();
        using var outer = Provider("outer.csv");
        using var inner = Provider("inner.csv");
        var scope = CsvExportOperations.OverrideForTests(outer);
        void Exercise(string name)
        {
            Assert.That(CsvExportOperations.Choose(CsvExportKind.Services, "owned note", owner)!.FileName, Is.EqualTo(name));
            Assert.That(CsvExportOperations.ShowSave(new SaveFileDialog(), owner), Is.True);
            Write();
        }
        try
        {
            await Task.Yield();
            Exercise("outer.csv");
            using (CsvExportOperations.OverrideForTests(inner))
            {
                await Task.Yield();
                Exercise("inner.csv");
                await Task.Run(Write);
            }
            Exercise("outer.csv");
            var expected = new InvalidOperationException("Owned CSV scope unwind");
            try
            {
                using var nested = CsvExportOperations.OverrideForTests(inner);
                await Task.Yield();
                Exercise("inner.csv");
                throw expected;
            }
            catch (InvalidOperationException error) { Assert.That(error, Is.SameAs(expected)); }
            Exercise("outer.csv");
            Assert.That(outer.Choices.Count, Is.EqualTo(3));
            Assert.That(inner.Choices.Count, Is.EqualTo(2));
            Assert.That(outer.Writes.Count, Is.EqualTo(3));
            Assert.That(inner.Writes.Count, Is.EqualTo(3));
            Assert.That(outer.Saves.Count, Is.EqualTo(3));
            Assert.That(inner.Saves.Count, Is.EqualTo(2));
        }
        finally { scope.Dispose(); scope.Dispose(); }
        Assert.Throws<ArgumentException>(Write, "Native invalid-path validation proves the previous null provider was restored");
    }

    [Test]
    public async Task CsvExportScopesKeepOverlappingFreshChildProvidersAndTheirParentIndependent()
    {
        var owner = ClosedOwner();
        using var parent = Provider("parent.csv");
        using var scope = CsvExportOperations.OverrideForTests(parent);
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        Task Exercise(string name) => Task.Run(async () =>
        {
            using var own = Provider(name);
            using (CsvExportOperations.OverrideForTests(own))
            {
                if (Interlocked.Increment(ref entered) == 2) both.TrySetResult();
                await both.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.That(CsvExportOperations.Choose(CsvExportKind.Connections, name, owner)!.FileName, Is.EqualTo(name));
                Write();
                await Task.Yield();
                Assert.That(CsvExportOperations.Choose(CsvExportKind.Services, name, owner)!.FileName, Is.EqualTo(name));
                Assert.That(own.Choices.Count, Is.EqualTo(2));
                Assert.That(own.Writes.Count, Is.EqualTo(1));
            }
            Assert.That(CsvExportOperations.Choose(CsvExportKind.Services, name, owner)!.FileName, Is.EqualTo("parent.csv"));
        });
        await Task.WhenAll(Exercise("left.csv"), Exercise("right.csv")).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(CsvExportOperations.Choose(CsvExportKind.UsageTotals, "parent", owner)!.FileName, Is.EqualTo("parent.csv"));
        Assert.That(parent.Choices.Count, Is.EqualTo(3));
        Assert.That(parent.Writes, Is.Empty);
    }

    [Test]
    public void CsvExportScopeRejectsNullWithoutReplacingTheCurrentProvider()
    {
        var owner = ClosedOwner();
        using var provider = Provider("outer.csv");
        using var scope = CsvExportOperations.OverrideForTests(provider);
        var error = Assert.Throws<ArgumentNullException>(() => CsvExportOperations.OverrideForTests(null!));
        Assert.That(error!.ParamName, Is.EqualTo("operations"));
        Assert.That(CsvExportOperations.Choose(CsvExportKind.ResourceHistory, null, owner)!.FileName, Is.EqualTo("outer.csv"));
        Write();
        Assert.That(provider.Writes.Count, Is.EqualTo(1));
    }
}
