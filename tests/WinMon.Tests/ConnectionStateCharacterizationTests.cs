using System.Net.NetworkInformation;
using NUnit.Framework;
using WinMon.Controls;

namespace WinMon.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class ConnectionStateCharacterizationTests : ConnectionStateCharacterizationTestsBase
{
    protected override void AssertState(TcpState? state, string expected) =>
        Assert.That(ConnectionsView.StateText(state), Is.EqualTo(expected));
}
