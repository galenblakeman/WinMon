using NUnit.Framework;
using WinMon.Updates;

namespace WinMon.Mcp.Tests;

[TestFixture]
public sealed class ToolInstallationDetectionTests : WinMon.Tests.ToolInstallationDetectionTestsBase
{
    private protected override ToolUpdateProduct Template => ToolUpdateProduct.Mcp;
}
