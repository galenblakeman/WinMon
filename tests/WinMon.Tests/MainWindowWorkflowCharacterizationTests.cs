using NUnit.Framework;

namespace WinMon.Tests;

// WPF Application and its modal windows must stay on one fixture-owned STA thread.
[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetUICulture("en-US")]
public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Path.Combine(Path.GetTempPath(), "WinMon.WindowWorkflow." + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Assert.That(Path.GetFullPath(_directory), Does.StartWith(temporaryRoot).IgnoreCase);
        Assert.That(Path.GetFileName(_directory), Does.StartWith("WinMon.WindowWorkflow."));
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
