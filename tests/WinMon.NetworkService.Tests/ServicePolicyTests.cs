using NUnit.Framework;
using WinMon.NetworkService;

namespace WinMon.Tests;

[TestFixture]
public sealed class ServicePolicyTests
{
    [Test]
    public void ValidateUsers_RequiresExplicitAccountSidsAndBoundsPolicy()
    {
        Assert.That(ServicePolicy.ValidateUsers(["S-1-5-21-1-2-3-1001", "S-1-5-21-1-2-3-1001"]), Has.Count.EqualTo(1));
        Assert.Throws<InvalidDataException>(() => ServicePolicy.ValidateUsers([]));
        Assert.Throws<InvalidDataException>(() => ServicePolicy.ValidateUsers(["S-1-1-0"]));
        Assert.Throws<InvalidDataException>(() => ServicePolicy.ValidateUsers(["S-1-5-11"]));
        Assert.That(() => ServicePolicy.ValidateUsers(["not-a-sid"]), Throws.Exception);
        Assert.Throws<InvalidDataException>(() => ServicePolicy.ValidateUsers(Enumerable.Repeat("S-1-5-21-1-2-3-1001", 65).ToArray()));
    }
}
