using WinMon.Tests;
using WinMon.Updates;
using NUnit.Framework;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class ToolInstallationDetectionTests : ToolInstallationDetectionTestsBase
{
    private protected override ToolUpdateProduct Template => ToolUpdateProduct.Cli;
}
