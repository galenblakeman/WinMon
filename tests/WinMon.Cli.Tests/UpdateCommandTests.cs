using NUnit.Framework;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class UpdateCommandTests
{
    [TestCase("check")]
    [TestCase("install")]
    public async Task RequestedUpdateActionRunsWithoutStartingTheCollector(string action)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var actions = new List<string>();
        var command = CliCommand.Create(() => throw new AssertionException("Must not sample"), output, error,
            runUpdate: (requested, _) =>
            {
                actions.Add(requested);
                return Task.FromResult(0);
            });

        var exit = await command.Parse(["update", action]).InvokeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero);
            Assert.That(actions, Is.EqualTo(new[] { action }));
            Assert.That(output.ToString(), Is.Empty);
            Assert.That(error.ToString(), Is.Empty);
        });
    }

    [Test]
    public void UnknownUpdateActionIsRejected()
    {
        Assert.That(CliCommand.Create().Parse(["update", "remove"]).Errors, Is.Not.Empty);
    }
}
