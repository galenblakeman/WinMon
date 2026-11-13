using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture]
public sealed class AuthorizationPolicyTests
{
    [Test]
    public void Save_RejectsEmptyOrExcessiveUsersBeforeCreatingAFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "WinMon-invalid-policy-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidDataException>(() => AuthorizationPolicy.Save(path, []));
        Assert.Throws<InvalidDataException>(() => AuthorizationPolicy.Save(path,
            Enumerable.Range(1, AuthorizationPolicy.MaximumUsers + 1).Select(id => $"S-1-5-21-1-2-3-{id}")));
        Assert.That(File.Exists(path), Is.False);
    }

    [TestCase("[\"S-1-5-21-1-2-3-4\",\"S-1-5-21-1-2-3-3\",\"S-1-5-21-1-2-3-4\"]")]
    [Category("Integration")]
    public void Load_NormalizesAndDeduplicatesOnDiskPolicy(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            Assert.That(AuthorizationPolicy.Load(path), Is.EqualTo(new[] { "S-1-5-21-1-2-3-3", "S-1-5-21-1-2-3-4" }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestCase("")]
    [TestCase("null")]
    [TestCase("[]")]
    [TestCase("[\"S-1-1-0\"]")]
    [Category("Integration")]
    public void Load_RejectsMissingOrUntrustedOnDiskPolicy(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            Assert.Throws<InvalidDataException>(() => AuthorizationPolicy.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void AddAuthorizedUser_NormalizesAndDeduplicates()
    {
        var users = AuthorizationPolicy.AddAuthorizedUser(["S-1-5-21-1-2-3-4"], "S-1-5-21-1-2-3-4");
        Assert.That(users, Is.EqualTo(new[] { "S-1-5-21-1-2-3-4" }));
    }

    [TestCase("S-1-1-0")]
    [TestCase("S-1-5-7")]
    [TestCase("S-1-5-11")]
    [TestCase("not-a-sid")]
    public void AddAuthorizedUser_RejectsNonExplicitAccounts(string sid)
    {
        Assert.That(() => AuthorizationPolicy.AddAuthorizedUser([], sid), Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void AddAuthorizedUser_RejectsMoreThanSixtyFourUsers()
    {
        var existing = Enumerable.Range(0, AuthorizationPolicy.MaximumUsers)
            .Select(static i => $"S-1-5-21-1-2-3-{i + 1}");
        Assert.That(() => AuthorizationPolicy.AddAuthorizedUser(existing, "S-1-5-21-1-2-3-1000"),
            Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void RemoveAuthorizedUser_PreservesOtherProvisionedAccounts()
    {
        var users = AuthorizationPolicy.RemoveAuthorizedUser(["S-1-5-21-1-2-3-4", "S-1-5-21-1-2-3-5"],
            "S-1-5-21-1-2-3-4");
        Assert.That(users, Is.EqualTo(new[] { "S-1-5-21-1-2-3-5" }));
    }

    [Test]
    public void RemoveAuthorizedUser_RejectsAbsentAccount()
    {
        Assert.That(() => AuthorizationPolicy.RemoveAuthorizedUser(["S-1-5-21-1-2-3-4"], "S-1-5-21-1-2-3-5"),
            Throws.TypeOf<InvalidOperationException>());
    }
}
