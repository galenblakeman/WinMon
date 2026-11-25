using NUnit.Framework;
using WinMon.Updates;

namespace WinMon.Tests;

[TestFixture]
public sealed class UpdateRestartArgumentGuardTests
{
    [TestCase("null")]
    [TestCase("empty")]
    [TestCase("one")]
    [TestCase("two")]
    [TestCase("four")]
    [TestCase("wrong-flag")]
    [TestCase("case-flag")]
    public void DefaultEntryRejectsUnsupportedArgumentsWithoutChangingRestartIdentity(string shape)
    {
        string[]? args = shape switch
        {
            "null" => null,
            "empty" => [],
            "one" => ["--update-result"],
            "two" => ["--update-result", "unused"],
            "four" => ["--update-result", "unused", "unused", "extra"],
            "wrong-flag" => ["--other", "unused", "unused"],
            "case-flag" => ["--UPDATE-RESULT", "unused", "unused"],
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var expected = UpdateRuntime.IsExpectedRestart;
        var history = UpdateRuntime.RestartHistory;

        Assert.DoesNotThrow(() => UpdateRuntime.AcceptResultArguments(args!));

        Assert.Multiple(() =>
        {
            Assert.That(UpdateRuntime.IsExpectedRestart, Is.EqualTo(expected));
            Assert.That(UpdateRuntime.RestartHistory, Is.EqualTo(history));
        });
    }
}
