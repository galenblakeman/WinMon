using NUnit.Framework;
using WinMon.Updates;

namespace WinMon.Mcp.Tests;

[TestFixture]
public sealed class ToolUpdateTests : WinMon.Tests.ToolUpdateTests
{
    private protected override ToolUpdateProduct Product => ToolUpdateProduct.Mcp;
}
