using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class WindowsAssemblyBoundaryTests
{
    [Test]
    public void WindowsAdapter_DependsOnCoreButNotOnTheApplicationOrUiFramework()
    {
        var references = typeof(WindowsFrameSource).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name).ToArray();
        Assert.That(references, Does.Contain("MonCore"));
        Assert.That(references, Has.None.Matches<string>(name =>
            name is not null && (name == "WinMon" || name.StartsWith("Presentation", StringComparison.Ordinal) ||
                                 name.StartsWith("System.Windows.Forms", StringComparison.Ordinal) ||
                                 name.StartsWith("Avalonia", StringComparison.Ordinal))));
    }

    [Test]
    public void WindowsFrameSource_AdvertisesActionsAndPerSampleAvailabilityRemainsSeparate()
    {
        using IFrameSource source = new WindowsFrameSource();
        Assert.Multiple(() =>
        {
            Assert.That(source.Capabilities.SystemMetrics.HasFlag(SystemMetricCapabilities.Cpu), Is.True);
            Assert.That(source.Capabilities.ProcessActions.HasFlag(ProcessActionCapabilities.Terminate), Is.True);
            Assert.That(source.Capabilities.ProcessMetrics.HasFlag(ProcessMetricCapabilities.Gpu), Is.True);
        });
    }
}
