using System.Buffers.Binary;
using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace MonCore.Windows.Tests;

[TestFixture]
public sealed class WindowsProcessSuspensionTests
{
    [Test]
    public void Parse_AllWaitingSuspendedThreadsReturnsTrue()
    {
        var bytes = Entry(42, 100, (5, 5), (5, 5));
        Assert.That(WindowsProcessSuspension.Parse(bytes, 42, 100), Is.True);
    }

    [Test]
    public void Parse_RunningThreadReturnsFalseAndInvalidThreadSpanReturnsUnavailable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WindowsProcessSuspension.Parse(Entry(42, 100, (2, 0)), 42, 100), Is.False);
            Assert.That(WindowsProcessSuspension.Parse(InvalidEntry(), 42, 100), Is.Null);
            Assert.That(WindowsProcessSuspension.Parse(Entry(43, 100, (5, 5)), 42, 100), Is.Null);
        });
    }

    private static byte[] Entry(int pid, long created, params (int State, int Reason)[] threads)
    {
        var bytes = new byte[0x100 + 0x50 * threads.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)threads.Length);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(0x20), created);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(0x50), pid);
        for (var index = 0; index < threads.Length; index++)
        {
            var thread = bytes.AsSpan(0x100 + index * 0x50);
            BinaryPrimitives.WriteInt32LittleEndian(thread[0x44..], threads[index].State);
            BinaryPrimitives.WriteInt32LittleEndian(thread[0x48..], threads[index].Reason);
        }
        return bytes;
    }

    private static byte[] InvalidEntry()
    {
        var bytes = Entry(42, 100, (5, 5));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2);
        return bytes;
    }
}
