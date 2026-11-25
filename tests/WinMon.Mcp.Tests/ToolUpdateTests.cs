using WinMon.Updates;
using NUnit.Framework;

namespace WinMon.Mcp.Tests;

[TestFixture]
public sealed class ToolUpdateTests : WinMon.Tests.ToolUpdateTests
{
    private protected override ToolUpdateProduct Product => ToolUpdateProduct.Mcp;
}
