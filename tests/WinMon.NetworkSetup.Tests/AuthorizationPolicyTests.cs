using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture]
public sealed class AuthorizationPolicyTests
{
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