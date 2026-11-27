using System.Buffers.Binary;
using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace MonCore.Windows.Tests;

[TestFixture]
public sealed class WindowsNvmeHealthTests
{
    [TestCase(0, 100)]
    [TestCase(112, 0)]
    [TestCase(255, 92)]
    public void Parse_PreservesZeroAndAboveRatedEndurance(byte used, byte spare)
    {
        Assert.That(WindowsNvmeHealth.Parse(Descriptor(used, spare)), Is.EqualTo((used, (byte?)spare)));
    }

    [Test]
    public void Parse_RejectsTruncationBadOffsetsAndAnUnpopulatedLog()
    {
        var invalidOffset = Descriptor(4, 100);
        BinaryPrimitives.WriteUInt32LittleEndian(invalidOffset.AsSpan(24), uint.MaxValue);
        var invalidSpare = Descriptor(4, 101);
        var emptyLog = Descriptor(0, 0);
        Array.Clear(emptyLog, 48, 512);
        Assert.Multiple(() =>
        {
            Assert.That(WindowsNvmeHealth.Parse(invalidOffset), Is.Null);
            Assert.That(WindowsNvmeHealth.Parse(emptyLog), Is.Null);
            Assert.That(WindowsNvmeHealth.Parse(Descriptor(4, 100).AsSpan(0, 559)), Is.Null);
            Assert.That(WindowsNvmeHealth.Parse(invalidSpare), Is.EqualTo(((byte)4, (byte?)null)));
        });
    }

    private static byte[] Descriptor(byte used, byte spare)
    {
        var data = new byte[560];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 48);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 48);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), 40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), 512);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(49), 303);
        data[51] = spare;
        data[53] = used;
        return data;
    }
}
