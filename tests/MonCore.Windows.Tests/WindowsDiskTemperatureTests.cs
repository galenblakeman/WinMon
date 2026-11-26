using System.Buffers.Binary;
using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace MonCore.Windows.Tests;

[TestFixture]
public sealed class WindowsDiskTemperatureTests
{
    [Test]
    public void CompositeTemperature_UsesIndexZeroRatherThanAnotherSensor()
    {
        var descriptor = Descriptor(2);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(24), 2);
        BinaryPrimitives.WriteInt16LittleEndian(descriptor.AsSpan(26), 61);
        BinaryPrimitives.WriteInt16LittleEndian(descriptor.AsSpan(42), 39);
        Assert.That(WindowsDiskTemperature.CompositeTemperature(descriptor), Is.EqualTo(39));
    }

    [Test]
    public void CompositeTemperature_RejectsTruncationAndTheUnavailableSentinel()
    {
        var descriptor = Descriptor(1);
        BinaryPrimitives.WriteInt16LittleEndian(descriptor.AsSpan(26), short.MinValue);
        Assert.Multiple(() =>
        {
            Assert.That(WindowsDiskTemperature.CompositeTemperature(descriptor.AsSpan(0, 32)), Is.Null);
            Assert.That(WindowsDiskTemperature.CompositeTemperature(descriptor), Is.Null);
        });
    }

    private static byte[] Descriptor(ushort count)
    {
        var bytes = new byte[24 + 16 * count];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), count);
        return bytes;
    }
}
