using NUnit.Framework;
using WinMon.Updates;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class ToolUpdateTests : WinMon.Tests.ToolUpdateTests
{
    private protected override ToolUpdateProduct Product => ToolUpdateProduct.Cli;
}
