using WinMon.Tests;
using WinMon.Updates;
using NUnit.Framework;

namespace WinMon.Mcp.Tests;

[TestFixture]
public sealed class ToolInstallationDetectionTests : ToolInstallationDetectionTestsBase
{
    private protected override ToolUpdateProduct Template => ToolUpdateProduct.Mcp;
}
