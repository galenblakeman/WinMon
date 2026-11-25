using NUnit.Framework;
using WinMon.Updates;

namespace WinMon.Tests;

[TestFixture]
public sealed class ToolInstallationDetectionTests : ToolInstallationDetectionTestsBase
{
    private protected override ToolUpdateProduct Template => ToolUpdateProduct.Cli;
}
