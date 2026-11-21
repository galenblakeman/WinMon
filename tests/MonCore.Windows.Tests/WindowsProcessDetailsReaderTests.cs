using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class WindowsProcessDetailsReaderTests
{
    [Test]
    public void SignatureCache_UsesFileIdentityAndEvictsLeastRecentEntry()
    {
        var cache = new ProcessSignatureCache<(int Id, long Size, long Write)>(2);
        var calls = 0;
        ProcessSignature Read() => new(ProcessSignatureStatus.Verified, (++calls).ToString());

        var first = cache.GetOrAdd((1, 10, 1), Read);
        Assert.That(cache.GetOrAdd((1, 10, 1), Read), Is.EqualTo(first));
        _ = cache.GetOrAdd((1, 11, 1), Read); // Changed size is a new identity.
        _ = cache.GetOrAdd((1, 11, 2), Read); // Changed write time is a new identity.
        Assert.That(cache.GetOrAdd((1, 10, 1), Read).Signer, Is.EqualTo("4"));
        Assert.That(cache.Count, Is.EqualTo(2));
    }

    [Test]
    public void SignatureCache_DifferentFileIdAndRecentHitHaveDistinctExpectedEntries()
    {
        var cache = new ProcessSignatureCache<(int Id, long Size, long Write)>(2);
        var calls = 0;
        ProcessSignature Read() => new(ProcessSignatureStatus.Verified, (++calls).ToString());

        _ = cache.GetOrAdd((1, 10, 1), Read);
        _ = cache.GetOrAdd((2, 10, 1), Read); // Replacement can preserve size and write time but not file ID.
        _ = cache.GetOrAdd((1, 10, 1), Read); // Promote ID 1.
        _ = cache.GetOrAdd((3, 10, 1), Read); // Evict ID 2, not the recently read ID 1.

        Assert.Multiple(() =>
        {
            Assert.That(cache.GetOrAdd((1, 10, 1), Read).Signer, Is.EqualTo("1"));
            Assert.That(cache.GetOrAdd((2, 10, 1), Read).Signer, Is.EqualTo("4"));
        });
    }

    [Test]
    public void SignatureCache_ConcurrentMissReadsOnlyOnce()
    {
        var cache = new ProcessSignatureCache<int>(2);
        var calls = 0;
        var results = Parallel.For(0, 16, index =>
        {
            _ = cache.GetOrAdd(1, () =>
            {
                Interlocked.Increment(ref calls); Thread.Sleep(10); return new(ProcessSignatureStatus.Verified, "one");
            });
        });

        Assert.Multiple(() =>
        {
            Assert.That(results.IsCompleted, Is.True);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(cache.Count, Is.EqualTo(1));
        });
    }

    [Test]
    [Category("Integration")]
    public void Read_CurrentProcessReadsItsLifetimeBoundDetails()
    {
        using var process = Process.GetCurrentProcess();

        var details = WindowsProcessDetailsReader.Read(process.Id, process.StartTime.ToUniversalTime().ToFileTimeUtc());

        Assert.Multiple(() =>
        {
            Assert.That(details.Exited, Is.False);
            Assert.That(details.CommandLine, Is.Not.Null);
            Assert.That(details.CommandLine, Does.Contain(Path.GetFileName(Environment.ProcessPath!)));
            Assert.That(details.Owner, Is.Not.Null);
            Assert.That(details.Owner!.AccountName, Does.Contain(Environment.UserName));
            Assert.That(details.Owner!.SessionId, Is.Not.Null);
            Assert.That(details.Signature.Status, Is.Not.EqualTo(ProcessSignatureStatus.Unavailable));
        });
    }

    [Test]
    public void Read_DifferentLifetimeReportsExitedWithoutReadingDetails()
    {
        using var process = Process.GetCurrentProcess();

        var details = WindowsProcessDetailsReader.Read(process.Id, process.StartTime.ToUniversalTime().ToFileTimeUtc() - 1);

        Assert.Multiple(() =>
        {
            Assert.That(details.Exited, Is.True);
            Assert.That(details.CommandLine, Is.Null);
            Assert.That(details.Owner, Is.Null);
        });
    }

    [Test]
    public void Read_InvalidPidReturnsUnavailableDetails()
    {
        var details = WindowsProcessDetailsReader.Read(-1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(details.Exited, Is.False);
            Assert.That(details.CommandLine, Is.Null);
            Assert.That(details.Signature.Status, Is.EqualTo(ProcessSignatureStatus.Unavailable));
        });
    }

    [Test]
    [Category("Integration")]
    public void ReadSignature_SystemFileVerifiesTrustWithoutStartingAProcess()
    {
        var path = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var signature = WindowsProcessDetailsReader.ReadSignatureForTest(path);

        Assert.That(signature.Status, Is.EqualTo(ProcessSignatureStatus.Verified));
        Assert.That(signature.Signer, Does.Contain("Microsoft"));
    }

    [Test]
    [Category("Integration")]
    public void ReadCatalogSignature_SystemLibraryVerifiesCatalogLookup()
    {
        var path = Path.Combine(Environment.SystemDirectory, "kernel32.dll");

        var signature = WindowsProcessDetailsReader.ReadCatalogSignatureForTest(path);

        Assert.That(signature.Status, Is.EqualTo(ProcessSignatureStatus.Verified));
        Assert.That(signature.Signer, Does.Contain("Microsoft"));
    }
}
