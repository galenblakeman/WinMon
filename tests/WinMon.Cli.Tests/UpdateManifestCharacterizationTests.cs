using NUnit.Framework;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class UpdateManifestCharacterizationTests : WinMon.Tests.UpdateManifestCharacterizationTests
{
    private protected override string AssetStem => "WinMon-Cli";
}
