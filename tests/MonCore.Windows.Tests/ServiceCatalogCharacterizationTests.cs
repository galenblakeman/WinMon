using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;
using NUnit.Framework;
using Native = MonCore.Windows.Monitoring.WindowsServices;

namespace MonCore.Windows.Tests;

[TestFixture]
public sealed class ServiceCatalogCharacterizationTests
{
    private sealed record Entry(string Name = "svc", string Display = "Display", uint State = 4, uint Pid = 42, uint Controls = 1);
    private sealed record Page(bool Success, int Error, uint Resume, Entry[] Entries,
        Action<byte[], nint>? Mutate = null);
    private static Page Complete(params Entry[] entries) => new(true, 0, 0, entries);
    private static Page More(uint resume, params Entry[] entries) => new(false, 234, resume, entries);

    [TestCase(0)]
    [TestCase(5)]
    [TestCase(234)]
    public void SuccessfulEmptyPageIgnoresLastErrorAndReturnsCapturedUtcTime(int lastError)
    {
        var backend = new Backend(new Page(true, lastError, 99, []));
        var result = backend.Reader.Read();

        Assert.Multiple(() =>
        {
            Assert.That(result.ObservedFileTime, Is.EqualTo(backend.Clock.Now.UtcDateTime.ToFileTimeUtc()));
            Assert.That(result.Services, Is.Empty);
            Assert.That(backend.Log, Is.EqualTo(new[] { "clock", "open", "check", "page:0", "dispose" }));
            Assert.That(backend.Disposals, Is.EqualTo(1));
            Assert.That(backend.ResumeInputs, Is.EqualTo(new uint[] { 0 }));
        });
    }

    [Test]
    public void NativeRowsKeepOrderOrdinalNamesUnicodeAndRawUnsignedFields()
    {
        var backend = new Backend(Complete(new Entry("zeta", "Béta 😀", 7, 0, 0),
            new("Alpha", "A", uint.MaxValue, int.MaxValue, uint.MaxValue), new("alpha", "a", 4, 42, 1)));
        var result = backend.Reader.Read();

        Assert.That(result.Services, Is.EqualTo(new[] {
            new ServiceObservation("zeta", "Béta 😀", 7, 0, 0),
            new ServiceObservation("Alpha", "A", uint.MaxValue, int.MaxValue, uint.MaxValue),
            new ServiceObservation("alpha", "a", 4, 42, 1) }));
        Assert.That(backend.Disposals, Is.EqualTo(1));
    }

    [Test]
    public void PagesCarryResumeTokensAndUseTheTimestampFromBeforeOpeningTheManager()
    {
        var backend = new Backend(More(17, new Entry("z", "Z")), new(true, 5, 999, [new("a", "A")]));
        var initial = backend.Clock.Now;
        backend.OnPage = () => backend.Clock.Now = backend.Clock.Now.AddHours(1);
        var result = backend.Reader.Read();

        Assert.Multiple(() =>
        {
            Assert.That(result.Services.Select(service => service.Name), Is.EqualTo(new[] { "z", "a" }));
            Assert.That(result.ObservedFileTime, Is.EqualTo(initial.UtcDateTime.ToFileTimeUtc()));
            Assert.That(backend.ResumeInputs, Is.EqualTo(new uint[] { 0, 17 }));
            Assert.That(backend.Log, Is.EqualTo(new[] { "clock", "open", "check", "page:0", "page:17", "dispose" }));
            Assert.That(backend.Disposals, Is.EqualTo(1));
        });
    }

    [TestCase("same", true, true, true)]
    [TestCase("state", false, true, true)]
    [TestCase("pid", false, true, true)]
    [TestCase("controls", false, true, true)]
    [TestCase("display", false, true, false)]
    [TestCase("name-case", false, false, false)]
    public void ReuseDependsOnEveryObservedFieldAndKeepsOnlyMatchingStringReferences(
        string change, bool sameRecord, bool sameName, bool sameDisplay)
    {
        var changed = change switch
        {
            "state" => new Entry(State: 7), "pid" => new Entry(Pid: 43),
            "controls" => new Entry(Controls: 5), "display" => new Entry(Display: "Changed"),
            "name-case" => new Entry(Name: "Svc"), _ => new Entry()
        };
        var backend = new Backend(Complete(new Entry()), Complete(changed));
        var first = backend.Reader.Read();
        var old = first.Services.Single();
        var current = backend.Reader.Read().Services.Single();

        Assert.Multiple(() =>
        {
            Assert.That(current, Is.EqualTo(new ServiceObservation(changed.Name, changed.Display, changed.State,
                (int)changed.Pid, changed.Controls)));
            Assert.That(ReferenceEquals(current, old), Is.EqualTo(sameRecord));
            Assert.That(ReferenceEquals(current.Name, old.Name), Is.EqualTo(sameName));
            Assert.That(ReferenceEquals(current.DisplayName, old.DisplayName), Is.EqualTo(sameDisplay));
            Assert.That(first.Services, Is.EqualTo(new[] { new ServiceObservation("svc", "Display", 4, 42, 1) }));
            Assert.That(backend.Disposals, Is.EqualTo(2));
        });
    }

    [Test]
    public void ADisappearedServiceDoesNotReuseAnOlderObservationWhenItReturns()
    {
        var backend = new Backend(Complete(new Entry()), Complete(), Complete(new Entry()));
        var first = backend.Reader.Read().Services.Single();
        var empty = backend.Reader.Read();
        var returned = backend.Reader.Read().Services.Single();

        Assert.Multiple(() =>
        {
            Assert.That(empty.Services, Is.Empty);
            Assert.That(returned, Is.EqualTo(new ServiceObservation("svc", "Display", 4, 42, 1)));
            Assert.That(returned, Is.Not.SameAs(first));
            Assert.That(backend.Disposals, Is.EqualTo(3));
        });
    }

    [Test]
    public void DuplicateNativeNamesRemainInOrderAndTheLastBecomesTheNextReadCache()
    {
        var backend = new Backend(Complete(new Entry(Display: "First"), new(Display: "Last", State: 7)),
            Complete(new Entry(Display: "Last", State: 7)));
        var first = backend.Reader.Read();
        var next = backend.Reader.Read();

        Assert.Multiple(() =>
        {
            Assert.That(first.Services, Is.EqualTo(new[] { new ServiceObservation("svc", "First", 4, 42, 1),
                new ServiceObservation("svc", "Last", 7, 42, 1) }));
            Assert.That(next.Services.Single(), Is.SameAs(first.Services[1]));
        });
    }

    [Test]
    public void PartialFailureKeepsThePreviousSuccessfulCacheAndEarlierCatalogFrozen()
    {
        var backend = new Backend(Complete(new Entry()), More(1, new Entry(Display: "Partial", State: 7)),
            new(false, 5, 0, []), Complete(new Entry()));
        var first = backend.Reader.Read();
        AssertError(() => backend.Reader.Read(), 5);
        var recovered = backend.Reader.Read();

        Assert.Multiple(() =>
        {
            Assert.That(first.Services, Is.EqualTo(new[] { new ServiceObservation("svc", "Display", 4, 42, 1) }));
            Assert.That(recovered.Services.Single(), Is.SameAs(first.Services.Single()));
            Assert.That(backend.Disposals, Is.EqualTo(3));
            Assert.That(backend.ResumeInputs, Is.EqualTo(new uint[] { 0, 0, 1, 0 }));
        });
    }

    [TestCase(0)]
    [TestCase(5)]
    [TestCase(13)]
    [TestCase(87)]
    public void NativeErrorWinsBeforeMalformedReturnedRowsAreRead(int error)
    {
        var backend = new Backend(new Page(false, error, 0, [new()], (buffer, _) => SetPointer(buffer, true, 0)));
        AssertError(() => backend.Reader.Read(), error);
        Assert.That(backend.Disposals, Is.EqualTo(1));
    }

    [Test]
    public void MoreDataWithNoEntriesFailsRatherThanLoopingWithoutProgress()
    {
        var backend = new Backend(More(17), Complete(new Entry()));
        AssertError(() => backend.Reader.Read(), 234);
        Assert.That(backend.ResumeInputs, Is.EqualTo(new uint[] { 0 }));
        Assert.That(backend.Disposals, Is.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(30)]
    [TestCase(31)]
    [TestCase(32)]
    [TestCase(33)]
    public void ThirtyTwoPagesAreAllowedButFurtherPagesAreNeverRequested(int partialPages)
    {
        var pages = Enumerable.Range(0, partialPages).Select(i => More((uint)i + 1, new Entry("svc" + i)))
            .Append(Complete(new Entry("last"))).ToArray();
        var backend = new Backend(pages);
        if (partialPages < 32)
            Assert.That(backend.Reader.Read().Services.Select(service => service.Name),
                Is.EqualTo(Enumerable.Range(0, partialPages).Select(i => "svc" + i).Append("last")));
        else AssertError(() => backend.Reader.Read(), 234);

        Assert.That(backend.ResumeInputs, Is.EqualTo(Enumerable.Range(0, Math.Min(32, partialPages + 1)).Select(i => (uint)i)));
        Assert.That(backend.Disposals, Is.EqualTo(1));
    }

    [TestCase(true, "null")]
    [TestCase(false, "null")]
    [TestCase(true, "before")]
    [TestCase(false, "before")]
    [TestCase(true, "end")]
    [TestCase(false, "end")]
    [TestCase(true, "odd")]
    [TestCase(false, "odd")]
    [TestCase(true, "unterminated")]
    [TestCase(false, "unterminated")]
    public void MalformedNativeTextKeepsInvalidDataErrorsAndDisposesTheManager(bool name, string fault)
    {
        var backend = new Backend(new Page(true, 0, 0, [new()], (buffer, address) =>
        {
            var entry = MemoryMarshal.Read<WireEntry>(buffer);
            var pointer = name ? entry.Name : entry.DisplayName;
            var bad = fault switch
            {
                "null" => 0, "before" => address - 2, "end" => address + buffer.Length,
                "odd" => address + 1, _ => pointer
            };
            if (fault == "unterminated") Array.Fill(buffer, (byte)0x61, (int)(pointer - address), buffer.Length - (int)(pointer - address));
            SetPointer(buffer, name, bad);
        }));

        AssertError(() => backend.Reader.Read(), 13);
        Assert.That(backend.Disposals, Is.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void LastAlignedUtf16TerminatorIsWithinTheBufferAndMayBeEmpty(bool name)
    {
        var backend = new Backend(new Page(true, 0, 0, [new()], (buffer, address) => SetPointer(buffer, name, address + buffer.Length - 2)));
        var row = backend.Reader.Read().Services.Single();

        Assert.That(row, Is.EqualTo(new ServiceObservation(name ? "" : "svc", name ? "Display" : "", 4, 42, 1)));
    }

    [TestCase(2147483648u)]
    [TestCase(uint.MaxValue)]
    public void PidOutsideSignedRangeThrowsOverflowWithoutPublishingACatalog(uint pid)
    {
        var backend = new Backend(Complete(new Entry(Pid: pid)));
        Assert.Throws<OverflowException>(() => backend.Reader.Read());
        Assert.That(backend.Disposals, Is.EqualTo(1));
    }

    [TestCase("open", 0)]
    [TestCase("check", 1)]
    [TestCase("page:0", 1)]
    public void DependencyFailureKeepsItsExceptionAndDisposesOnlyAnOpenedManager(string failure, int disposed)
    {
        var backend = new Backend(Complete()) { FailureAt = failure };
        Assert.That(Assert.Throws<Win32Exception>(() => backend.Reader.Read()), Is.SameAs(backend.Failure));
        Assert.That(backend.Disposals, Is.EqualTo(disposed));
    }

    [Test]
    public void TimestampFailurePrecedesManagerOpening()
    {
        var backend = new Backend(Complete());
        backend.Clock.Now = DateTimeOffset.MinValue;
        Assert.Throws<ArgumentOutOfRangeException>(() => backend.Reader.Read());
        Assert.That(backend.Log, Is.EqualTo(new[] { "clock" }));
        Assert.That(backend.Disposals, Is.Zero);
    }

    [Test]
    public void ReaderInstancesKeepIndependentBuffersClocksAndCaches()
    {
        var first = new Backend(Complete(new Entry("first")), Complete(new Entry("first")));
        var second = new Backend(Complete(new Entry("second")));
        second.Clock.Now = second.Clock.Now.AddDays(1);
        var a = first.Reader.Read();
        var b = second.Reader.Read();
        var again = first.Reader.Read();

        Assert.Multiple(() =>
        {
            Assert.That(a.Services.Single().Name, Is.EqualTo("first"));
            Assert.That(b.Services.Single().Name, Is.EqualTo("second"));
            Assert.That(again.Services.Single(), Is.SameAs(a.Services.Single()));
            Assert.That(a.ObservedFileTime, Is.EqualTo(first.Clock.Now.UtcDateTime.ToFileTimeUtc()));
            Assert.That(b.ObservedFileTime, Is.EqualTo(second.Clock.Now.UtcDateTime.ToFileTimeUtc()));
            Assert.That(first.Address, Is.Not.EqualTo(second.Address));
        });
    }

    private static void AssertError(Action action, int error) =>
        Assert.That(Assert.Throws<Win32Exception>(action)!.NativeErrorCode, Is.EqualTo(error));

    [StructLayout(LayoutKind.Sequential)]
    private struct WireEntry { public nint Name, DisplayName; public Native.Status Status; }

    private static void SetPointer(byte[] buffer, bool name, nint pointer)
    {
        var value = MemoryMarshal.Read<WireEntry>(buffer);
        if (name) value.Name = pointer; else value.DisplayName = pointer;
        MemoryMarshal.Write(buffer, in value);
    }

    private sealed class Backend : WindowsServices.IServiceCatalogOperations
    {
        private readonly Queue<Page> _pages;
        public Backend(params Page[] pages)
        {
            _pages = new(pages); Clock = new(this); Reader = new(this, Clock);
        }
        public Native.Reader Reader { get; }
        public Clock Clock { get; }
        public List<string> Log { get; } = [];
        public List<uint> ResumeInputs { get; } = [];
        public int Disposals { get; private set; }
        public nint Address { get; private set; }
        public string? FailureAt { get; set; }
        public Win32Exception Failure { get; } = new(5);
        public Action? OnPage { get; set; }
        public void Record(string entry) { Log.Add(entry); if (entry == FailureAt) throw Failure; }
        public Native.IServiceHandle OpenManager() { Record("open"); return new Manager(this); }
        public void CheckHandle(Native.IServiceHandle manager) { Assert.That(manager, Is.TypeOf<Manager>()); Record("check"); }
        public bool Enumerate(Native.IServiceHandle manager, nint address, int length, out int count, ref uint resume, out int error)
        {
            Assert.That(manager, Is.TypeOf<Manager>());
            Assert.That(length, Is.EqualTo(256 * 1024));
            Assert.That(address, Is.Not.EqualTo(0));
            Record("page:" + resume); ResumeInputs.Add(resume); Address = address;
            var page = _pages.Dequeue();
            var buffer = new byte[length];
            var size = Marshal.SizeOf<WireEntry>();
            var offset = size * page.Entries.Length;
            for (var i = 0; i < page.Entries.Length; i++)
            {
                var row = page.Entries[i];
                var name = Encoding.Unicode.GetBytes(row.Name + '\0');
                var display = Encoding.Unicode.GetBytes(row.Display + '\0');
                var wire = new WireEntry { Name = address + offset,
                    Status = new() { State = row.State, ProcessId = row.Pid, Controls = row.Controls } };
                name.CopyTo(buffer, offset); offset += name.Length;
                wire.DisplayName = address + offset;
                display.CopyTo(buffer, offset); offset += display.Length;
                MemoryMarshal.Write(buffer.AsSpan(i * size), in wire);
            }
            page.Mutate?.Invoke(buffer, address);
            Marshal.Copy(buffer, 0, address, length);
            count = page.Entries.Length; error = page.Error; resume = page.Resume;
            OnPage?.Invoke();
            return page.Success;
        }
        private sealed class Manager(Backend backend) : WindowsServices.IServiceHandle
        { public void Dispose() { backend.Disposals++; backend.Record("dispose"); } }
    }

    private sealed class Clock(Backend backend) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 21, 13, 14, 15, TimeSpan.FromHours(2));
        public override DateTimeOffset GetUtcNow() { backend.Record("clock"); return Now; }
    }
}
