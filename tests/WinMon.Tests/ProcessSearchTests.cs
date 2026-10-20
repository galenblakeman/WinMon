using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

public class ProcessSearchTests
{
    private static readonly ProcessUsage[] Rows =
    [
        new(120, 1, "Editor", 8),
        new(240, 2, "msedge", 4) { MemberCount = 2, MemberIds = [240, 360] },
        new(480, 3, "WinMon", 1)
    ];

    [Test]
    public void NameSearch_IgnoresCaseAndSurroundingWhitespace()
    {
        Assert.That(ProcessSearch.Filter(Rows, "  EDGE  "), Is.EqualTo([Rows[1]]));
    }

    [Test]
    public void PidSearch_FindsIndividualAndNonRepresentativeGroupMember()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProcessSearch.Filter(Rows, "120"), Is.EqualTo([Rows[0]]));
            Assert.That(ProcessSearch.Filter(Rows, "360"), Is.EqualTo([Rows[1]]));
            Assert.That(ProcessSearch.Filter(Rows, "360").Single().MemberCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void FilteringBeforeTopLimit_FindsLowerRankedRowsAndPreservesOrder()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProcessSearch.Filter(Rows, "WinMon").Take(1), Is.EqualTo([Rows[2]]));
            Assert.That(ProcessSearch.Filter(Rows, "0"), Is.EqualTo(Rows));
            Assert.That(ProcessSearch.Filter(Rows, "  "), Is.EqualTo(Rows));
            Assert.That(ProcessSearch.Filter(Rows, "absent"), Is.Empty);
        });
    }
}
