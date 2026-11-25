using System.Text.Json;
using NUnit.Framework;
using WinMon.NetworkService;

namespace WinMon.Tests;

[TestFixture]
[Category("Integration")]
public sealed class ServicePolicyInstallationTests
{
    private const string Account = ServicePolicyOriginalTests.Account;
    private static readonly string Valid = JsonSerializer.Serialize(new[] { Account });

    [Test]
    public void AncestorsAreValidatedBeforeEveryNestedEntryAndBeforePolicyDeserialization()
    {
        using var tree = new PolicyDirectory();
        File.WriteAllText(tree.Policy, Valid);
        var child = Directory.CreateDirectory(Path.Combine(tree.Root.FullName, "child"));
        var nested = Path.Combine(child.FullName, "payload.bin");
        File.WriteAllText(nested, "test payload");
        var seen = new List<string>();
        var users = ServicePolicy.ReadInstallation(tree.Root, tree.Root.Parent!.FullName.ToUpperInvariant(),
            entry => seen.Add(entry.FullName));
        Assert.That(users, Is.EqualTo(new[] { Account }));
        Assert.That(seen.Take(2), Is.EqualTo(new[] { tree.Root.FullName, tree.Root.Parent.FullName }));
        Assert.That(seen.Skip(2), Is.EquivalentTo(new[] { tree.Policy, child.FullName, nested }));
        Assert.That(seen.IndexOf(child.FullName), Is.LessThan(seen.IndexOf(nested)));
        using var exclusive = new FileStream(tree.Policy, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.That(exclusive.Length, Is.EqualTo(Valid.Length));
    }

    [Test]
    public void AMatchingInstallationBoundaryIsCheckedOnceAndNoParentIsVisited()
    {
        using var tree = new PolicyDirectory();
        File.WriteAllText(tree.Policy, Valid);
        var seen = new List<string>();
        _ = ServicePolicy.ReadInstallation(tree.Root, tree.Root.FullName, entry => seen.Add(entry.FullName));
        Assert.That(seen, Is.EqualTo(new[] { tree.Root.FullName, tree.Policy }));
    }

    [Test]
    public void AnUnmatchedBoundaryRetainsValidationThroughTheVolumeRoot()
    {
        using var tree = new PolicyDirectory();
        File.WriteAllText(tree.Policy, Valid);
        var ancestors = new List<string>();
        _ = ServicePolicy.ReadInstallation(tree.Root, Path.Combine(tree.Root.FullName, "not-an-ancestor"),
            entry => { if (entry is DirectoryInfo) ancestors.Add(entry.FullName); });
        Assert.That(ancestors[0], Is.EqualTo(tree.Root.FullName));
        Assert.That(ancestors[1], Is.EqualTo(tree.Root.Parent!.FullName));
        Assert.That(ancestors.Last(), Is.EqualTo(Path.GetPathRoot(tree.Root.FullName)));
    }

    [TestCase("root")]
    [TestCase("ancestor")]
    [TestCase("child")]
    public void ProtectionFailureKeepsItsExceptionAndPreventsReadingThePolicyOrRejectedSubtree(string point)
    {
        using var tree = new PolicyDirectory();
        File.WriteAllText(tree.Policy, "invalid json must not be read");
        var child = Directory.CreateDirectory(Path.Combine(tree.Root.FullName, "child"));
        var nested = Path.Combine(child.FullName, "payload.bin");
        File.WriteAllText(nested, "test payload");
        var target = point == "root" ? tree.Root.FullName : point == "ancestor" ? tree.Root.Parent!.FullName : child.FullName;
        var expected = new UnauthorizedAccessException("controlled protection failure");
        var seen = new List<string>();
        var error = Assert.Throws<UnauthorizedAccessException>(() => ServicePolicy.ReadInstallation(tree.Root,
            tree.Root.Parent!.FullName, entry =>
            {
                seen.Add(entry.FullName);
                if (entry.FullName == target) throw expected;
            }));
        Assert.That(error, Is.SameAs(expected));
        Assert.That(seen.Last(), Is.EqualTo(target));
        Assert.That(seen, Does.Not.Contain(nested));
        if (point == "root") Assert.That(seen, Has.Count.EqualTo(1));
        if (point == "ancestor") Assert.That(seen, Has.Count.EqualTo(2));
    }

    [TestCase(4095, true)]
    [TestCase(4096, true)]
    [TestCase(4097, false)]
    public void EntryLimitIncludesFilesAndStopsBeforeCheckingThe4097thEntry(int entries, bool accepted)
    {
        using var tree = new PolicyDirectory();
        File.WriteAllText(tree.Policy, Valid);
        for (var i = 1; i < entries; i++) File.WriteAllText(Path.Combine(tree.Root.FullName, "entry-" + i), "");
        var checks = 0;
        if (accepted)
            Assert.That(ServicePolicy.ReadInstallation(tree.Root, tree.Root.FullName, _ => checks++), Is.EqualTo(new[] { Account }));
        else
            Assert.That(Assert.Throws<InvalidDataException>(() =>
                ServicePolicy.ReadInstallation(tree.Root, tree.Root.FullName, _ => checks++))!.Message,
                Is.EqualTo("Service installation contains too many entries."));
        Assert.That(checks, Is.EqualTo(1 + Math.Min(entries, 4096)));
    }

    [TestCase(0, false)]
    [TestCase(8192, true)]
    [TestCase(8193, false)]
    public void PolicySizeBoundaryIsCheckedBeforeParsingAndClosesTheReadHandle(int length, bool accepted)
    {
        using var tree = new PolicyDirectory();
        File.WriteAllText(tree.Policy, length == 0 ? "" : Valid.PadRight(length));
        if (accepted) Assert.That(Read(tree), Is.EqualTo(new[] { Account }));
        else Assert.That(Assert.Throws<InvalidDataException>(() => Read(tree))!.Message,
            Is.EqualTo("Invalid authorization policy size."));
        using var exclusive = new FileStream(tree.Policy, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.That(exclusive.Length, Is.EqualTo(length));
    }

    [TestCase("[", "json")]
    [TestCase("{}", "json")]
    [TestCase("null", "missing")]
    [TestCase("[]", "users")]
    [TestCase("[null]", "sid")]
    [TestCase("[\"S-1-1-0\"]", "broad")]
    public void ParsingAndUserValidationKeepTheirDistinctErrorsAndReleaseTheFile(string json, string kind)
    {
        using var tree = new PolicyDirectory();
        File.WriteAllText(tree.Policy, json);
        if (kind == "json") Assert.Throws<JsonException>(() => Read(tree));
        else
        {
            var message = kind switch
            {
                "missing" => "Missing authorization policy.", "users" => "Invalid number of provisioned users.",
                "sid" => "Invalid provisioned SID.", _ => "Authorization requires explicit account SIDs."
            };
            Assert.That(Assert.Throws<InvalidDataException>(() => Read(tree))!.Message, Is.EqualTo(message));
        }
        using var exclusive = new FileStream(tree.Policy, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.That(exclusive.Length, Is.GreaterThan(0));
    }

    [Test]
    public void MissingPolicyKeepsTheFileNotFoundPathAfterProtectionChecks()
    {
        using var tree = new PolicyDirectory();
        var checks = 0;
        var error = Assert.Throws<FileNotFoundException>(() =>
            ServicePolicy.ReadInstallation(tree.Root, tree.Root.FullName, _ => checks++));
        Assert.That(error!.FileName, Is.EqualTo(tree.Policy));
        Assert.That(checks, Is.EqualTo(1));
    }

    [TestCase(FileShare.Read, true)]
    [TestCase(FileShare.None, false)]
    public void PolicyReadsCanShareWithReadersButRespectAnExclusiveHandle(FileShare sharing, bool accepted)
    {
        using var tree = new PolicyDirectory();
        File.WriteAllText(tree.Policy, Valid);
        using var other = new FileStream(tree.Policy, FileMode.Open, FileAccess.Read, sharing);
        if (accepted) Assert.That(Read(tree), Is.EqualTo(new[] { Account }));
        else Assert.Throws<IOException>(() => Read(tree));
    }

    [Test]
    public void PolicyReadDoesNotShareWithAnExistingWriter()
    {
        using var tree = new PolicyDirectory();
        File.WriteAllText(tree.Policy, Valid);
        using var writer = new FileStream(tree.Policy, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        Assert.Throws<IOException>(() => Read(tree));
    }

    private static IReadOnlyCollection<string> Read(PolicyDirectory tree) =>
        ServicePolicy.ReadInstallation(tree.Root, tree.Root.FullName, _ => { });
}
