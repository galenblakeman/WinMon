using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace WinMon.Tests;

[TestFixture]
public sealed class AppInfoTests
{
    [Test]
    public void ProductDetails_ComeFromBuildProperties()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AppInfo.Version, Does.Match(@"^\d+\.\d+\.\d+$"), "No +commit suffix or prerelease noise.");
            Assert.That(AppInfo.Description, Is.Not.Empty);
            Assert.That(AppInfo.Copyright, Does.Contain("Galen Blakeman"));
            Assert.That(AppInfo.License, Is.EqualTo("MIT"));
            Assert.That(AppInfo.RepositoryUrl, Does.StartWith("https://github.com/"));
        });
    }

    [Test]
    public void Version_MatchesTheApplicationManifest()
    {
        var manifest = File.ReadAllText(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", "..", "src", "WinMon", "app.manifest"));
        var manifestVersion = Regex.Match(manifest, "assemblyIdentity version=\"([^\"]+)\"").Groups[1].Value;
        Assert.That(manifestVersion, Is.EqualTo($"{AppInfo.Version}.0"), "Bump app.manifest with Directory.Build.props.");
    }

    private static string SourcePath([CallerFilePath] string path = "") => path;
}
