using NUnit.Framework;
using WinMon.Mcp;
using WinMon.Updates;

namespace WinMon.Tests;

[TestFixture]
public sealed class McpCommandLineTests
{
    [TestCase("check")]
    [TestCase("install")]
    public async Task UpdateCommandRunsTheRequestedAction(string argument)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var actions = new List<ToolUpdateAction>();

        var exit = await McpCommandLine.RunAsync(["update", argument], _ => throw new AssertionException("Must not register"),
            output, error, (action, _) =>
            {
                actions.Add(action);
                return Task.FromResult(0);
            });

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero);
            Assert.That(actions.Select(action => action.ToString().ToLowerInvariant()), Is.EqualTo(new[] { argument }));
            Assert.That(output.ToString(), Is.Empty);
            Assert.That(error.ToString(), Is.Empty);
        });
    }

    [TestCase("update")]
    [TestCase("update remove")]
    [TestCase("--unknown")]
    public async Task InvalidArgumentsAreRejectedWithoutStartingTheServerOrUpdater(string arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await McpCommandLine.RunAsync(arguments.Split(' '), _ => throw new AssertionException("Must not register"), output,
            error, (_, _) => throw new AssertionException("Must not update"));

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(64));
            Assert.That(output.ToString(), Is.Empty);
            Assert.That(error.ToString(), Does.Contain("update check|install"));
        });
    }

    [TestCase("--help")]
    [TestCase("update --help")]
    public async Task HelpReturnsSuccessAndWritesUsageToStandardOutput(string arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await McpCommandLine.RunAsync(arguments.Split(' '), _ => throw new AssertionException("Must not register"), output,
            error, (_, _) => throw new AssertionException("Must not update"));

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero);
            Assert.That(output.ToString(), Does.Contain("update check|install"));
            Assert.That(error.ToString(), Is.Empty);
        });
    }
}
