using System.ComponentModel;
using NUnit.Framework;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class NetworkCommandTests
{
    [TestCase("start")]
    [TestCase("install")]
    public async Task ExplicitOperationRunsWithoutStartingInteractiveCollector(string operation)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var called = new List<string>();
        var command = CliCommand.Create(() => throw new AssertionException("Must not sample"), output, error,
            (action, _) => { called.Add(action); return Task.CompletedTask; });
        var exit = await command.Parse(["network", operation]).InvokeAsync();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero);
            Assert.That(called, Is.EqualTo(new[] { operation }));
            Assert.That(output.ToString(), Does.Contain("watch --network --tab connections"));
            Assert.That(error.ToString(), Is.Empty);
        });
    }

    [Test]
    public async Task SetupFailureDoesNotPrintPrivilegedErrorDetailsOrClaimSuccess()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var command = CliCommand.Create(output: output, error: error,
            runNetwork: (_, _) => throw new IOException("sensitive helper path"));
        var exit = await command.Parse(["network", "install"]).InvokeAsync();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(output.ToString(), Is.Empty);
            Assert.That(error.ToString(), Does.Contain("network install").And.Not.Contain("sensitive"));
        });
    }

    [Test]
    public async Task UacCancellationIsReportedWithoutClaimingSuccess()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var command = CliCommand.Create(output: output, error: error,
            runNetwork: (_, _) => throw new Win32Exception(1223));
        Assert.That(await command.Parse(["network", "install"]).InvokeAsync(), Is.EqualTo(1));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Is.Not.Empty);
    }

    [Test]
    public async Task DevelopmentOrStoreBuildCannotLaunchSetupWithoutSignedBundle()
    {
        if (typeof(CliCommand).Assembly.GetManifestResourceInfo("WinMon.NetworkSetup.exe") is not null)
            Assert.Ignore("Missing-bundle check is only applicable to builds without setup assets.");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var command = CliCommand.Create(output: output, error: error);
        Assert.That(await command.Parse(["network", "install"]).InvokeAsync(), Is.EqualTo(1));
        Assert.That(error.ToString(), Is.Not.Empty);
        Assert.That(output.ToString(), Is.Empty);
    }

    [Test]
    public void UnknownNetworkOperationIsRejected()
    {
        Assert.That(CliCommand.Create().Parse(["network", "remove"]).Errors, Is.Not.Empty);
    }
}
