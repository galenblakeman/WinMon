using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

public class ProcessAppNameProviderTests
{
    [TestCase(false, "Example Publisher")]
    [TestCase(true, "Example Publisher")]
    [TestCase(false, "")]
    [TestCase(true, "")]
    [Apartment(ApartmentState.STA)]
    public void Row_ShowsPublisherAbovePathInBothTooltips_WhenAvailable(bool grouped, string publisher)
    {
        const string path = @"C:\Apps\example.exe";
        var usage = new ProcessUsage(1, 10, "example", 1)
        {
            ImagePath = path, Publisher = publisher,
            MemberCount = grouped ? 2 : 1, MemberIds = grouped ? [1, 2] : []
        };
        var row = new ProcessRow();
        row.Update(usage, [], 120, true, null, grouped);
        var labels = row.Children.OfType<Grid>().Single().Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().ToArray();
        var expected = publisher.Length > 0 ? $"\n{publisher}\n{path}" : $"\n{path}";
        Assert.Multiple(() =>
        {
            Assert.That(labels[0].ToolTip as string, Does.Contain(expected));
            Assert.That(row.ToolTip as string, Does.Contain(expected));
            Assert.That(labels[1].ToolTip, Is.Null, "App/background has no separate tooltip.");
            if (publisher.Length == 0)
            {
                Assert.That(labels[0].ToolTip as string, Does.Not.Contain($"\n\n{path}"));
                Assert.That(row.ToolTip as string, Does.Not.Contain($"\n\n{path}"));
            }
        });
    }

    [Test]
    public void Get_CachesNamesByPathIgnoringCase()
    {
        var reads = 0;
        var provider = new ProcessAppNameProvider(_ => { reads++; return ("Programmer's Notepad 2", "PN Editor", "Example Publisher"); });
        Assert.That(provider.Get(@"C:\Apps\pn.exe"), Is.EqualTo(("Programmer's Notepad 2", "PN Editor", "Example Publisher")));
        Assert.That(provider.Get(@"c:\apps\PN.EXE"), Is.EqualTo(("Programmer's Notepad 2", "PN Editor", "Example Publisher")));
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
            Assert.That(provider.Get(""), Is.EqualTo(("", "", "")));
            Assert.That(provider.Get(@"C:\Apps\missing.exe"), Is.EqualTo(("", "", "")));
            Assert.That(provider.Get(@"C:\Apps\missing.exe"), Is.EqualTo(("", "", "")));
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
            Assert.That(names.Publisher, Is.EqualTo(expected.CompanyName?.Trim() ?? ""));
        });
    }
}
