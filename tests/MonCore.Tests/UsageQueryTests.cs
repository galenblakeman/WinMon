using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.Tests;

[TestFixture]
public sealed class UsageQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 18, 0, 0, TimeSpan.Zero);

    [Test]
    public void Select_UsesUsageColumnOrderAndNameForTies()
    {
        AppUsage[] apps =
        [
            App("b", cpu: 100, io: 5, gpu: null, lifetimes: 1),
            App("a", cpu: 100, io: 9, gpu: 2, lifetimes: 3, running: 1),
            App("c", cpu: 300, io: 1, gpu: 1, lifetimes: 2, running: 2,
                lastActive: new SampleKey(1, Now))
        ];

        Assert.Multiple(() =>
        {
            Assert.That(Names(0), Is.EqualTo(["c", "a", "b"]));
            Assert.That(Names(1), Is.EqualTo(["a", "b", "c"]));
            Assert.That(Names(2), Is.EqualTo(["a", "c", "b"]));
            Assert.That(Names(3), Is.EqualTo(["a", "c", "b"]));
            Assert.That(Names(4), Is.EqualTo(["c", "a", "b"]));
        });

        string[] Names(int sort) => [.. UsageQuery.Select(apps, sort, "", false, 0, app => app.Name)
            .Select(app => app.Name)];
    }

    [Test]
    public void Select_FiltersBeforeTopAndMatchesDisplayNameAndPath()
    {
        AppUsage[] apps =
        [
            App("worker", cpu: 900) with { ImagePath = @"C:\Tools\worker.exe" },
            App("chrome", cpu: 500, isApp: true) with { AppName = "Google Chrome" },
            App("code", cpu: 400, isApp: true) with { ImagePath = @"C:\Apps\code.exe" },
            App("edge", cpu: 300, isApp: true)
        ];

        Assert.Multiple(() =>
        {
            Assert.That(Names("", true, 2), Is.EqualTo(["chrome", "code"]));
            Assert.That(Names("google", false, 5), Is.EqualTo(["chrome"]));
            Assert.That(Names(@"c:\apps", false, 5), Is.EqualTo(["code"]));
            Assert.That(Names("", false, 0), Has.Length.EqualTo(4));
        });

        string[] Names(string filter, bool appsOnly, int top) => [.. UsageQuery.Select(apps, 0, filter, appsOnly,
            top, app => app.AppName.Length > 0 ? app.AppName : app.Name).Select(app => app.Name)];
    }

    private static AppUsage App(string name, long cpu = 0, double io = 0, double? gpu = null, int lifetimes = 1,
        int running = 0, bool isApp = false, SampleKey? lastActive = null)
    {
        return new AppUsage(name, name, "", "", "", cpu, io, gpu, lifetimes, running, isApp, lastActive);
    }
}
