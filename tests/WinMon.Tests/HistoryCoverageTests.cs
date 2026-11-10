using NUnit.Framework;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
public sealed class HistoryCoverageTests
{
    [Test]
    public void CoverageNamesRetainedSpanAndSelectedAvailabilitySeparately()
    {
        var now = DateTimeOffset.UtcNow;
        var text = MainWindow.HistoryCoverageLine("Services", now.AddMinutes(-2), now, false, true);
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("Services"));
            Assert.That(text, Does.Contain(Strings.HistorySelectedUnavailable));
            Assert.That(text, Does.Contain(Strings.HistoryShortenedByMemoryLimit));
            Assert.That(MainWindow.HistoryCoverageLine("Usage", null, null, false, false),
                Does.Contain(Strings.MissingReading));
            Assert.That(MainWindow.HistoryCoverageLine("Processes", now, now, true, false),
                Does.Contain(Strings.HistorySelectedAvailable));
        });
    }
}
