using System.Runtime.InteropServices;
using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace MonCore.Windows.Tests;

[TestFixture]
public sealed class NetworkTableAcquisitionCharacterizationTests
{
    private const int Maximum = 32 * 1024 * 1024;
    private delegate uint Reader(IntPtr buffer, ref int size);
    private sealed record Result(byte[]? Buffer, int? ErrorCode, bool Success);

    [TestCase(0u)]
    [TestCase(5u)]
    [TestCase(234u)]
    [TestCase(2147483648u)]
    [TestCase(uint.MaxValue)]
    public void ProbeErrorsRetainTheExactNativeCodeWithoutAllocatingAReadAttempt(uint code)
    {
        var calls = 0;
        var result = Read((IntPtr pointer, ref int size) =>
        {
            calls++;
            Assert.That(pointer, Is.EqualTo(IntPtr.Zero));
            Assert.That(size, Is.Zero);
            size = 16;
            return code;
        });
        AssertFailure(result, unchecked((int)code));
        Assert.That(calls, Is.EqualTo(1));
    }

    [TestCase(int.MinValue)]
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(3)]
    [TestCase(Maximum + 1)]
    [TestCase(int.MaxValue)]
    public void OutOfBoundsProbeSizesReturnInsufficientBufferWithoutAReadAttempt(int proposed)
    {
        var calls = 0;
        var result = Read((IntPtr pointer, ref int size) =>
        {
            calls++; Assert.That(pointer, Is.EqualTo(IntPtr.Zero)); size = proposed; return 122;
        });
        AssertFailure(result, 122);
        Assert.That(calls, Is.EqualTo(1));
    }

    [TestCase(4, 4)]
    [TestCase(16, 4)]
    [TestCase(16, 9)]
    [TestCase(16, 16)]
    [TestCase(Maximum, 4)]
    public void ValidSizesReturnOnlyTheInitializedReportedPrefix(int capacity, int used)
    {
        var calls = 0;
        var payload = Enumerable.Range(0, used).Select(value => (byte)(value + 17)).ToArray();
        var expected = payload.ToArray();
        var result = Read((IntPtr pointer, ref int size) =>
        {
            if (++calls == 1)
            {
                Assert.That(pointer, Is.EqualTo(IntPtr.Zero)); Assert.That(size, Is.Zero); size = capacity; return 122;
            }
            Assert.That(pointer, Is.Not.EqualTo(IntPtr.Zero)); Assert.That(size, Is.EqualTo(capacity));
            Marshal.Copy(payload, 0, pointer, payload.Length); size = used; return 0;
        });
        Array.Fill(payload, (byte)0);
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(2)); Assert.That(result.Success, Is.True);
            Assert.That(result.ErrorCode, Is.Null); Assert.That(result.Buffer, Is.EqualTo(expected));
        });
    }

    [TestCase(int.MinValue)]
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(3)]
    [TestCase(17)]
    [TestCase(Maximum + 1)]
    [TestCase(int.MaxValue)]
    public void AReportedSuccessWithInvalidLengthRetainsInvalidDataError(int supplied)
    {
        var calls = 0;
        var result = Read((IntPtr pointer, ref int size) =>
        {
            if (++calls == 1) { size = 16; return 122; }
            Assert.That(pointer, Is.Not.EqualTo(IntPtr.Zero)); Assert.That(size, Is.EqualTo(16));
            size = supplied; return 0;
        });
        AssertFailure(result, 13);
        Assert.That(calls, Is.EqualTo(2));
    }

    [TestCase(4, 8)]
    [TestCase(8, 4)]
    [TestCase(4, 4)]
    public void OneRetryUsesTheLatestSizeIncludingSmallerOrEqualSizes(int firstSize, int retrySize)
    {
        var calls = 0;
        var payload = Enumerable.Repeat((byte)42, retrySize).ToArray();
        var capacities = new List<int>();
        var result = Read((IntPtr pointer, ref int size) =>
        {
            if (++calls == 1) { Assert.That(pointer, Is.EqualTo(IntPtr.Zero)); size = firstSize; return 122; }
            Assert.That(pointer, Is.Not.EqualTo(IntPtr.Zero)); capacities.Add(size);
            if (calls == 2) { size = retrySize; return 122; }
            Marshal.Copy(payload, 0, pointer, payload.Length); return 0;
        });
        Assert.Multiple(() =>
        {
            Assert.That(capacities, Is.EqualTo(new[] { firstSize, retrySize }));
            Assert.That(calls, Is.EqualTo(3)); Assert.That(result.Success, Is.True);
            Assert.That(result.ErrorCode, Is.Null); Assert.That(result.Buffer, Is.EqualTo(payload));
        });
    }

    [Test]
    public void MaximumReportedPayloadIsAcceptedAndCopiedCompletely()
    {
        var payload = new byte[Maximum];
        payload.AsSpan().Fill(73); payload[0] = 1; payload[^1] = 255;
        var calls = 0;
        var result = Read((IntPtr pointer, ref int size) =>
        {
            if (++calls == 1) { size = Maximum; return 122; }
            Assert.That(size, Is.EqualTo(Maximum));
            Marshal.Copy(payload, 0, pointer, payload.Length); return 0;
        });
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True); Assert.That(result.ErrorCode, Is.Null);
            Assert.That(calls, Is.EqualTo(2)); Assert.That(result.Buffer, Has.Length.EqualTo(Maximum));
            Assert.That(result.Buffer.AsSpan().SequenceEqual(payload), Is.True, "The complete bounded payload must be copied.");
        });
        payload[^1] = 0;
        Assert.That(result.Buffer![^1], Is.EqualTo(255));
    }

    [TestCase(4)]
    [TestCase(16)]
    [TestCase(Maximum)]
    public void TwoUnsuccessfulReadAttemptsExhaustTheBudgetWithoutAThird(int lastSize)
    {
        var calls = 0;
        var result = Read((IntPtr pointer, ref int size) =>
        {
            calls++;
            Assert.That(pointer == IntPtr.Zero, Is.EqualTo(calls == 1));
            size = calls < 3 ? 4 : lastSize; return 122;
        });
        AssertFailure(result, 122);
        Assert.That(calls, Is.EqualTo(3));
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(3)]
    [TestCase(Maximum + 1)]
    public void InvalidResizeStopsImmediatelyWithoutAnotherAllocation(int proposed)
    {
        var calls = 0;
        var result = Read((IntPtr pointer, ref int size) => { size = ++calls == 1 ? 4 : proposed; return 122; });
        AssertFailure(result, 122);
        Assert.That(calls, Is.EqualTo(2));
    }

    [TestCase(5u, -1)]
    [TestCase(234u, Maximum + 1)]
    [TestCase(uint.MaxValue, 4)]
    [TestCase(2147483648u, 0)]
    public void ReadFailuresRetainNativeCodeEvenWhenReportedSizeIsInvalid(uint code, int supplied)
    {
        var calls = 0;
        var result = Read((IntPtr pointer, ref int size) =>
        {
            if (++calls == 1) { size = 4; return 122; }
            size = supplied; return code;
        });
        AssertFailure(result, unchecked((int)code));
        Assert.That(calls, Is.EqualTo(2));
    }

    [TestCase("probe")]
    [TestCase("read")]
    [TestCase("retry")]
    public void CallbackExceptionsPropagateUnwrappedAtEveryStage(string stage)
    {
        var expected = new IOException("owned callback failure");
        var calls = 0;
        var failingCall = stage switch { "probe" => 1, "read" => 2, _ => 3 };
        var actual = Assert.Catch(() => Read((IntPtr pointer, ref int size) =>
        {
            if (++calls == failingCall) throw expected;
            size = 4; return 122;
        }));
        Assert.That(actual, Is.SameAs(expected));
        Assert.That(calls, Is.EqualTo(failingCall));
    }

    [Test]
    public void CallbackCancellationRetainsItsExactExceptionAndToken()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var expected = new OperationCanceledException("owned cancellation", cancellation.Token);
        var actual = Assert.Catch(() => Read((IntPtr pointer, ref int size) =>
        {
            if (pointer == IntPtr.Zero) { size = 4; return 122; }
            throw expected;
        }));
        Assert.That(actual, Is.SameAs(expected));
        Assert.That(((OperationCanceledException)actual!).CancellationToken, Is.EqualTo(cancellation.Token));
    }

    [Test]
    public void NullCallbackRetainsTheOriginalNullReferenceException() =>
        Assert.Throws<NullReferenceException>(() => Read(null));

    [Test]
    public void ConsecutiveCallsReturnIndependentOwnedSnapshots()
    {
        var generation = 0;
        Reader reader = (IntPtr pointer, ref int size) =>
        {
            if (pointer == IntPtr.Zero) { generation++; size = 4; return 122; }
            Marshal.Copy(Enumerable.Repeat((byte)generation, 4).ToArray(), 0, pointer, 4); return 0;
        };
        var first = Read(reader); var second = Read(reader);
        Assert.Multiple(() =>
        {
            Assert.That(first.Buffer, Is.EqualTo(new byte[] { 1, 1, 1, 1 }));
            Assert.That(second.Buffer, Is.EqualTo(new byte[] { 2, 2, 2, 2 }));
            Assert.That(first.Buffer, Is.Not.SameAs(second.Buffer));
            Assert.That(first.Success && second.Success, Is.True);
        });
        first.Buffer![0] = 99;
        Assert.That(second.Buffer, Is.EqualTo(new byte[] { 2, 2, 2, 2 }));
    }

    private static void AssertFailure(Result result, int code)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False); Assert.That(result.Buffer, Is.Null); Assert.That(result.ErrorCode, Is.EqualTo(code));
        });
    }

    private static Result Read(Reader? reader)
    {
        var result = WindowsConnections.ReadTable(reader is null ? null! : new WindowsConnections.TableReader(reader.Invoke));
        return new(result.Buffer, result.ErrorCode, result.Success);
    }
}
