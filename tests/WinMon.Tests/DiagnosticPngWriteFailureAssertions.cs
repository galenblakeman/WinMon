using System.Runtime.InteropServices;
using NUnit.Framework;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private static Exception AssertLockedPngWriteFailure(Action operation)
    {
        var error = Assert.Catch(operation);
        if (error is IOException) return error;
        if (error is ExternalException native && native.ErrorCode == unchecked((int)0x80004005) &&
            native.StackTrace?.Contains("System.Drawing.Image.Save", StringComparison.Ordinal) == true)
            return native;
        Assert.Fail($"Expected an IOException or the native GDI+ Image.Save E_FAIL for a locked PNG, but was {error?.GetType().FullName ?? "no exception"}.");
        return null!;
    }

    [Test]
    public void LockedPngWriteFailure_AcceptsAndReturnsTheOriginalIOException()
    {
        var expected = new IOException("owned locked PNG");

        var actual = AssertLockedPngWriteFailure(() => throw expected);

        Assert.That(actual, Is.SameAs(expected));
    }

    [Test]
    public void LockedPngWriteFailure_RejectsUnrelatedExceptionsAndUnprovenNativeFailures()
    {
        Assert.Throws<AssertionException>(() => AssertLockedPngWriteFailure(() => throw new InvalidOperationException()));
        Assert.Throws<AssertionException>(() => AssertLockedPngWriteFailure(() => throw new ExternalException("wrong HRESULT", 1)));
        Assert.Throws<AssertionException>(() => AssertLockedPngWriteFailure(() =>
            throw new ExternalException("not an Image.Save boundary", unchecked((int)0x80004005))));
    }

    [Test, Category("Integration")]
    public void LockedPngWriteFailure_AcceptsNativeImageSaveAndPreservesLockedFile()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "locked-native.png");
        File.WriteAllText(path, "owned locked PNG sentinel");
        using var bitmap = new System.Drawing.Bitmap(1, 1);
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var error = AssertLockedPngWriteFailure(() => bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png));

        Assert.That(error, Is.TypeOf<ExternalException>());
        Assert.That(File.ReadAllText(path), Is.EqualTo("owned locked PNG sentinel"));
    }
}
