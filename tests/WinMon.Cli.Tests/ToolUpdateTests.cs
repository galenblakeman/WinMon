using WinMon.Updates;
using NUnit.Framework;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class ToolUpdateTests : WinMon.Tests.ToolUpdateTests
{
    private protected override ToolUpdateProduct Product => ToolUpdateProduct.Cli;
}
