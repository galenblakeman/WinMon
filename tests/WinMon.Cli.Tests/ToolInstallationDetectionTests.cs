using NUnit.Framework;
using WinMon.Updates;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class ToolInstallationDetectionTests : WinMon.Tests.ToolInstallationDetectionTestsBase
{
    private protected override ToolUpdateProduct Template => ToolUpdateProduct.Cli;
}
