using System.ComponentModel;
using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

public class ProcessAppNameProviderTests
{
    [Test]
    public void Get_CachesNamesByPathIgnoringCase()
    {
        var reads = 0;
        var provider = new ProcessAppNameProvider(_ => { reads++; return ("Programmer's Notepad 2", "PN Editor"); });
        Assert.That(provider.Get(@"C:\Apps\pn.exe"), Is.EqualTo(("Programmer's Notepad 2", "PN Editor")));
        Assert.That(provider.Get(@"c:\apps\PN.EXE"), Is.EqualTo(("Programmer's Notepad 2", "PN Editor")));
        Assert.That(reads, Is.EqualTo(1));
    }

    [Test]
    public void Get_UnreadableOrMissingMetadata_DoesNotInterruptSamplingOrRetryEveryTick()
    {
        Exception[] failures = [new FileNotFoundException(), new UnauthorizedAccessException(), new Win32Exception(5)];
        foreach (var failure in failures)
        {
            var reads = 0;
            var provider = new ProcessAppNameProvider(_ => { reads++; throw failure; });
            Assert.That(provider.Get(""), Is.EqualTo(("", "")));
            Assert.That(provider.Get(@"C:\Apps\missing.exe"), Is.EqualTo(("", "")));
            Assert.That(provider.Get(@"C:\Apps\missing.exe"), Is.EqualTo(("", "")));
            Assert.That(reads, Is.EqualTo(1));
        }
    }

    [Test]
    public void Get_ReadsRealFileMetadata()
    {
        var path = typeof(WindowsCpuSampler).Assembly.Location;
        var expected = FileVersionInfo.GetVersionInfo(path);
        var names = new ProcessAppNameProvider().Get(path);
        Assert.Multiple(() =>
        {
            Assert.That(names.Description, Is.Not.Empty);
            Assert.That(names.Description, Is.EqualTo(expected.FileDescription?.Trim()));
            Assert.That(names.Product, Is.EqualTo(expected.ProductName?.Trim() ?? ""));
        });
    }
}
