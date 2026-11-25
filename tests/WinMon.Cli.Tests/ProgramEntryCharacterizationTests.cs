using System.Globalization;
using NUnit.Framework;

namespace WinMon.Cli.Tests;

[TestFixture, NonParallelizable, SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ProgramEntryCharacterizationTests
{
    [TestCase("root-help")]
    [TestCase("watch-help")]
    [TestCase("unknown-option")]
    [TestCase("invalid-tab")]
    [TestCase("invalid-format")]
    public void RedirectedEntryPreservesHelpErrorsEncodingAndConsoleOwnership(string partition)
    {
        Assert.That(Console.IsOutputRedirected, Is.True, "This fixture characterizes the redirected test-host entry path.");
        var args = partition switch
        {
            "root-help" => new[] { "--help" },
            "watch-help" => ["watch", "--help"],
            "unknown-option" => ["--not-a-winmon-option"],
            "invalid-tab" => ["watch", "--tab", "not-a-tab"],
            "invalid-format" => ["snapshot", "--format", "not-a-format"],
            _ => throw new ArgumentOutOfRangeException(nameof(partition))
        };
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var originalInput = Console.In;
        var encoding = Console.OutputEncoding;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        int exit;
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var ownedOut = Console.Out;
            var ownedError = Console.Error;
            var entry = typeof(CliCommand).Assembly.EntryPoint!;
            Assert.That(entry.ReturnType, Is.EqualTo(typeof(int)));
            exit = (int)entry.Invoke(null, [args])!;
            Assert.That(Console.Out, Is.SameAs(ownedOut));
            Assert.That(Console.Error, Is.SameAs(ownedError));
            Assert.That(Console.In, Is.SameAs(originalInput));
            Assert.That(Console.OutputEncoding.CodePage, Is.EqualTo(encoding.CodePage));
            Assert.That(Console.OutputEncoding.GetPreamble(), Is.EqualTo(encoding.GetPreamble()));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
        Assert.That(Console.Out, Is.SameAs(originalOut));
        Assert.That(Console.Error, Is.SameAs(originalError));
        Assert.That(Console.In, Is.SameAs(originalInput));
        if (partition.EndsWith("help", StringComparison.Ordinal))
        {
            Assert.That(exit, Is.Zero);
            Assert.That(error.ToString(), Is.Empty);
            Assert.That(output.ToString(), Does.Contain("Usage:").And.Contain("--help"));
            Assert.That(output.ToString(), partition == "root-help"
                ? Does.Contain("snapshot").And.Contain("watch").And.Contain("hardware")
                : Does.Contain("--tab").And.Contain("--top").And.Contain("--no-save"));
        }
        else
        {
            Assert.That(exit, Is.EqualTo(1));
            var rejected = partition switch
            {
                "unknown-option" => "--not-a-winmon-option",
                "invalid-tab" => "not-a-tab",
                _ => "not-a-format"
            };
            Assert.That(error.ToString(), Does.Contain(rejected));
        }
    }
}
