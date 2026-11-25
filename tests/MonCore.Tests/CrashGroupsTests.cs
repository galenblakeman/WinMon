using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class CrashGroupsTests
{
    [Test]
    public void Build_RecursAcrossVersionsAndCaseDifferencesWhileKeepingVersionCounts()
    {
        var first = Event("Player.exe", "1.0", "kernelbase.dll", "10.0");
        var second = Event("player.EXE", "2.0", "KERNELBASE.DLL", "11.0", DateTimeOffset.UnixEpoch.AddHours(1));
        var group = CrashGroups.Build([first, second]).Single();

        Assert.That(group.Key, Is.EqualTo(new CrashGroupKey(CrashKind.Crash, "Player.exe", "kernelbase.dll", 0xC0000005, 42)));
        Assert.That(group.Count, Is.EqualTo(2));
        Assert.That(group.FirstTime, Is.EqualTo(first.Timestamp));
        Assert.That(group.LastTime, Is.EqualTo(second.Timestamp));
        Assert.That(group.Versions, Is.EquivalentTo([new CrashVersionCount("1.0", "10.0", 1), new CrashVersionCount("2.0", "11.0", 1)]));
        Assert.That(group.Events, Is.EqualTo([first, second]));
    }

    [Test]
    public void Build_LeavesUnsignedEventsOutAndSeparatesDistinctOffsets()
    {
        var signed = Event("player.exe", "1.0", "app.dll", "1.0");
        var shifted = signed with { Timestamp = signed.Timestamp.AddMinutes(1), CrashSignature = signed.CrashSignature! with { Offset = 43 } };
        var unsigned = signed with { CrashSignature = null };

        var groups = CrashGroups.Build([signed, shifted, unsigned]);

        Assert.That(groups, Has.Count.EqualTo(2));
        Assert.That(groups.Select(group => group.Count), Is.EqualTo([1, 1]));
        Assert.That(groups.SelectMany(group => group.Events), Does.Not.Contain(unsigned));
    }

    [Test]
    public void Build_SeparatesEveryKeyComponentAndGroupsHangs()
    {
        var source = Event("player.exe", "1.0", "app.dll", "1.0");
        var hang = source with { CrashSignature = new(CrashKind.Hang, "player.exe", "1.0") };
        var sameHang = hang with { Timestamp = hang.Timestamp.AddMinutes(1), CrashSignature = hang.CrashSignature! with { ApplicationVersion = "2.0" } };
        var variations = new[]
        {
            source with { CrashSignature = source.CrashSignature! with { Application = "other.exe" } },
            source with { CrashSignature = source.CrashSignature! with { Module = "other.dll" } },
            source with { CrashSignature = source.CrashSignature! with { ExceptionCode = 1 } },
            source with { CrashSignature = source.CrashSignature! with { Offset = 1 } }
        };

        var groups = CrashGroups.Build(variations.Append(source).Append(hang).Append(sameHang));

        Assert.That(groups, Has.Count.EqualTo(6));
        Assert.That(groups.Single(group => group.Key.Kind == CrashKind.Hang).Count, Is.EqualTo(2));
        Assert.That(groups.Single(group => group.Key.Kind == CrashKind.Hang).Versions,
            Is.EquivalentTo([new CrashVersionCount("1.0", null, 1), new CrashVersionCount("2.0", null, 1)]));
    }

    [TestCase("bad\\path.exe")]
    [TestCase("bad/path.exe")]
    [TestCase("bad:app.exe")]
    [TestCase(" ")]
    [TestCase("not-a-version")]
    [TestCase("missing-offset")]
    [TestCase("control")]
    [TestCase("unknown-kind")]
    public void Build_LeavesInvalidSignaturesOut(string value)
    {
        var signature = new CrashSignature(CrashKind.Crash, "player.exe", "1.0", "app.dll", "1.0", 1, 1);
        signature = value switch
        {
            "not-a-version" => signature with { ApplicationVersion = value },
            "missing-offset" => signature with { Offset = null },
            "control" => signature with { Module = "bad\u0001.dll" },
            "unknown-kind" => signature with { Kind = (CrashKind)99 },
            _ => signature with { Application = value }
        };

        Assert.That(CrashGroups.Build([new IncidentEvent(DateTimeOffset.UnixEpoch, "Application Error", 1000,
            IncidentEventLevel.Error, "Crash", CrashSignature: signature)]), Is.Empty);
    }

    [Test]
    public void Build_SortsByCountThenLatestTime()
    {
        var older = Event("older.exe", "1.0", "app.dll", "1.0", DateTimeOffset.UnixEpoch);
        var newer = Event("newer.exe", "1.0", "app.dll", "1.0", DateTimeOffset.UnixEpoch.AddMinutes(1));
        var recurring = Event("recurring.exe", "1.0", "app.dll", "1.0", DateTimeOffset.UnixEpoch.AddMinutes(-1));

        var groups = CrashGroups.Build([older, newer, recurring, recurring with { Timestamp = DateTimeOffset.UnixEpoch }]);

        Assert.That(groups.Select(group => group.Key.Application), Is.EqualTo(["recurring.exe", "newer.exe", "older.exe"]));
    }

    [TestCase(0xC0000005u, "Access violation")]
    [TestCase(0xC0000409u, "Stack buffer overrun")]
    [TestCase(0xE0434352u, "Unhandled .NET exception")]
    [TestCase(1u, null)]
    public void ExceptionName_ReturnsKnownLabelsOnly(uint code, string? expected) =>
        Assert.That(CrashGroups.ExceptionName(code), Is.EqualTo(expected));

    private static IncidentEvent Event(string application, string applicationVersion, string module, string moduleVersion,
        DateTimeOffset? timestamp = null) => new(timestamp ?? DateTimeOffset.UnixEpoch, "Application Error", 1000,
        IncidentEventLevel.Error, "Crash", CrashSignature: new(CrashKind.Crash, application, applicationVersion, module,
            moduleVersion, 0xC0000005, 42));
}
