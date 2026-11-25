using System.Runtime.InteropServices;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class EventMessageFormatCharacterizationTests
{
    [Test]
    public void InvalidMetadataReturnsEmptyWithoutFormattingAndDisposesOnlyMetadata()
    {
        using var item = new Item(); var metadata = new Metadata { Invalid = true };
        Assert.That(Render(item, metadata), Is.Empty);
        Assert.Multiple(() => { Assert.That(metadata.InvalidReads, Is.EqualTo(1)); Assert.That(metadata.Capacities, Is.Empty); Assert.That(metadata.Disposals, Is.EqualTo(1)); Assert.That(item.IsClosed, Is.False); });
    }

    [TestCase("empty")]
    [TestCase("ordinary")]
    [TestCase("trailing-nuls")]
    [TestCase("all-nuls")]
    [TestCase("interior-nul")]
    [TestCase("newlines")]
    public void SuccessfulFormattingTrimsOnlyTrailingNulsAndKeepsFullMessage(string kind)
    {
        var (text, expected) = kind switch
        {
            "empty" => ("", ""), "ordinary" => ("message", "message"), "trailing-nuls" => ("message\0\0", "message"),
            "all-nuls" => ("\0\0", ""), "interior-nul" => ("a\0b\0", "a\0b"), _ => ("first\r\nsecond\nthird", "first\r\nsecond\nthird")
        };
        using var item = new Item(); var metadata = new Metadata(Step.Success(text, (uint)text.Length));
        Assert.That(Render(item, metadata), Is.EqualTo(expected));
        Assert.Multiple(() => { Assert.That(metadata.Capacities, Is.EqualTo(new[] { 1024 })); Assert.That(metadata.Items.Single(), Is.SameAs(item)); Assert.That(metadata.Disposals, Is.EqualTo(1)); Assert.That(item.IsClosed, Is.False); });
    }

    [TestCase(0u, 0)] [TestCase(1u, 1)] [TestCase(1023u, 1023)]
    [TestCase(1024u, 1024)] [TestCase(1025u, 1024)] [TestCase(65536u, 1024)]
    [TestCase(2147483647u, 1024)]
    public void SuccessfulUsedCountIsClampedToCurrentCapacityWithoutAResize(uint used, int expected)
    {
        using var item = new Item(); var metadata = new Metadata(Step.Success(new string('x', 1024), used));
        Assert.That(Render(item, metadata), Is.EqualTo(new string('x', expected)));
        Assert.Multiple(() => { Assert.That(metadata.Capacities, Is.EqualTo(new[] { 1024 })); Assert.That(metadata.Disposals, Is.EqualTo(1)); Assert.That(item.IsClosed, Is.False); });
    }

    [TestCase(2147483648u)] [TestCase(uint.MaxValue)]
    public void OverflowingSuccessfulUsedCountRetainsTheOriginalLengthException(uint used)
    {
        using var item = new Item(); var metadata = new Metadata(Step.Success("", used));
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Render(item, metadata));
        Assert.Multiple(() => { Assert.That(error!.ParamName, Is.EqualTo("length")); Assert.That(metadata.Disposals, Is.EqualTo(1)); Assert.That(metadata.Capacities, Is.EqualTo(new[] { 1024 })); Assert.That(item.IsClosed, Is.False); });
    }

    [TestCase(0)] [TestCase(1)] [TestCase(5)] [TestCase(87)] [TestCase(-1)]
    public void NonBufferNativeErrorsReturnEmptyWithoutRetrying(int error)
    {
        using var item = new Item(); var metadata = new Metadata(Step.Failure(error, uint.MaxValue));
        Assert.That(Render(item, metadata), Is.Empty);
        Assert.Multiple(() => { Assert.That(metadata.Capacities, Is.EqualTo(new[] { 1024 })); Assert.That(metadata.Disposals, Is.EqualTo(1)); Assert.That(item.IsClosed, Is.False); });
    }

    [TestCase(65537u)] [TestCase(2147483647u)] [TestCase(uint.MaxValue)]
    public void InsufficientBufferAboveTheCharacterLimitReturnsEmptyWithoutAllocatingRequestedSize(uint requested)
    {
        using var item = new Item(); var metadata = new Metadata(Step.Failure(122, requested));
        Assert.That(Render(item, metadata), Is.Empty);
        Assert.That(metadata.Capacities, Is.EqualTo(new[] { 1024 }));
        Assert.That(metadata.Disposals, Is.EqualTo(1));
    }

    [TestCase(0)] [TestCase(1)] [TestCase(16)] [TestCase(1024)] [TestCase(2048)] [TestCase(65536)]
    public void RetryAcceptsExactRequestedCapacityIncludingZeroShrinkingAndMaximum(int requested)
    {
        using var item = new Item(); var text = new string('r', requested);
        var metadata = new Metadata(Step.Failure(122, (uint)requested), Step.Success(text, (uint)requested));
        Assert.That(Render(item, metadata), Is.EqualTo(text));
        Assert.Multiple(() => { Assert.That(metadata.Capacities, Is.EqualTo(new[] { 1024, requested })); Assert.That(metadata.Items, Has.All.SameAs(item)); Assert.That(metadata.InvalidReads, Is.EqualTo(1)); Assert.That(metadata.Disposals, Is.EqualTo(1)); Assert.That(item.IsClosed, Is.False); });
    }

    [Test]
    public void EveryRetryReceivesAFreshZeroedBufferAndSuccessIgnoresStaleError()
    {
        using var item = new Item();
        var metadata = new Metadata(new(false, 122, 1024, "failed data", null), new(true, 5, 5, "ready", buffer => Assert.That(buffer, Is.All.EqualTo('\0'))));
        Assert.That(Render(item, metadata), Is.EqualTo("ready"));
        Assert.That(metadata.Capacities, Is.EqualTo(new[] { 1024, 1024 }));
        Assert.That(metadata.Disposals, Is.EqualTo(1));
    }

    [Test]
    public void MultipleGrowthRepliesAndLaterFailureRetainExactOrderAndEmptyResult()
    {
        using var item = new Item();
        var metadata = new Metadata(Step.Failure(122, 2048), Step.Failure(122, 4096), Step.Failure(5, 4096));
        Assert.That(Render(item, metadata), Is.Empty);
        Assert.That(metadata.Capacities, Is.EqualTo(new[] { 1024, 2048, 4096 }));
        Assert.That(metadata.Disposals, Is.EqualTo(1));
    }

    [TestCase(0u)] [TestCase(16u)] [TestCase(1024u)]
    public void RepeatedInsufficientSizeContinuesUntilTheOwnedCallbackStopsIt(uint requested)
    {
        using var item = new Item(); var expected = new IOException("bounded retry sentinel");
        var metadata = new Metadata(Step.Failure(122, requested), Step.Failure(122, requested), Step.Throw(expected));
        Assert.That(Assert.Catch(() => Render(item, metadata)), Is.SameAs(expected));
        Assert.Multiple(() => { Assert.That(metadata.Capacities, Is.EqualTo(new[] { 1024, (int)requested, (int)requested })); Assert.That(metadata.Disposals, Is.EqualTo(1)); Assert.That(item.IsClosed, Is.False); });
    }

    [TestCase("first")]
    [TestCase("retry")]
    public void FormattingExceptionsRemainUnwrappedAndCloseOnlyTheMetadata(string phase)
    {
        using var item = new Item(); var expected = new InvalidOperationException("owned formatter failure");
        var metadata = phase == "first" ? new Metadata(Step.Throw(expected)) : new Metadata(Step.Failure(122, 2048), Step.Throw(expected));
        Assert.That(Assert.Catch(() => Render(item, metadata)), Is.SameAs(expected));
        Assert.Multiple(() => { Assert.That(metadata.Disposals, Is.EqualTo(1)); Assert.That(item.IsClosed, Is.False); });
    }

    [Test]
    public void CallbackCancellationRetainsItsExactExceptionTokenAndDisposal()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); using var item = new Item();
        var expected = new OperationCanceledException("owned cancellation", cancellation.Token); var metadata = new Metadata(Step.Throw(expected));
        var error = Assert.Catch(() => Render(item, metadata));
        Assert.Multiple(() => { Assert.That(error, Is.SameAs(expected)); Assert.That(((OperationCanceledException)error!).CancellationToken, Is.EqualTo(cancellation.Token)); Assert.That(metadata.Disposals, Is.EqualTo(1)); Assert.That(item.IsClosed, Is.False); });
    }

    [Test]
    public void InvalidStateGetterFailureStillDisposesMetadata()
    {
        using var item = new Item(); var expected = new IOException("owned state failure"); var metadata = new Metadata { InvalidError = expected };
        Assert.That(Assert.Catch(() => Render(item, metadata)), Is.SameAs(expected));
        Assert.That(metadata.Capacities, Is.Empty); Assert.That(metadata.Disposals, Is.EqualTo(1)); Assert.That(item.IsClosed, Is.False);
    }

    [TestCase(false)] [TestCase(true)]
    public void MetadataDisposalFailureRetainsItsPrecedenceOverReturnOrFormattingFailure(bool formatFailure)
    {
        using var item = new Item(); var expected = new IOException("owned disposal failure");
        var metadata = new Metadata(formatFailure ? Step.Throw(new InvalidOperationException("earlier")) : Step.Success("ok", 2)) { DisposeError = expected };
        Assert.That(Assert.Catch(() => Render(item, metadata)), Is.SameAs(expected));
        Assert.That(metadata.Disposals, Is.EqualTo(1)); Assert.That(item.IsClosed, Is.False);
    }

    private sealed record Step(bool Result, int Error, uint Used, string Text, Action<char[]>? Inspect)
    {
        public static Step Success(string text, uint used) => new(true, 0, used, text, null);
        public static Step Failure(int error, uint used) => new(false, error, used, "", null);
        public static Step Throw(Exception error) => new(false, 0, 0, "", _ => throw error);
    }
    private sealed class Metadata(params Step[] steps) : IEventMessageMetadata
    {
        public bool Invalid { get; init; }
        public Exception? InvalidError { get; init; }
        public Exception? DisposeError { get; init; }
        public int InvalidReads { get; private set; }
        public int Disposals { get; private set; }
        public List<int> Capacities { get; } = [];
        public List<SafeHandle> Items { get; } = [];
        public bool IsInvalid { get { InvalidReads++; if (InvalidError is not null) throw InvalidError; return Invalid; } }
        public bool TryFormat(SafeHandle item, char[] buffer, out uint used, out int error)
        {
            Assert.That(item.IsClosed, Is.False); Items.Add(item); Capacities.Add(buffer.Length);
            Assert.That(Capacities.Count, Is.LessThanOrEqualTo(steps.Length), "The callback sequence must stay bounded.");
            var step = steps[Capacities.Count - 1]; step.Inspect?.Invoke(buffer);
            step.Text.AsSpan(0, Math.Min(step.Text.Length, buffer.Length)).CopyTo(buffer); used = step.Used; error = step.Error; return step.Result;
        }
        public void Dispose() { Disposals++; if (DisposeError is not null) throw DisposeError; }
    }
    private sealed class Item() : SafeHandle(IntPtr.Zero, true)
    {
        public override bool IsInvalid => true;
        protected override bool ReleaseHandle() => throw new AssertionException("An owned fake item never releases a native handle.");
    }

    private static string Render(SafeHandle item, Metadata metadata) => WindowsHistoryEventReader.FormatMessage(item, metadata);
}
