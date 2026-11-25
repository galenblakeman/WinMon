using System.Globalization;
using System.Net;
using System.Text;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Resources;

namespace WinMon.Cli.Tests;

[TestFixture]
[NonParallelizable]
[SetCulture("en-US")]
[SetUICulture("en-US")]
public sealed class ActivityRenderCharacterizationTests
{
    private static IEnumerable<TestCaseData> UntrustedTextCases
    {
        get
        {
            (string Name, string Raw, string Expected)[] cases =
            [
                ("markup", "[red]owned[/]", "[red]owned[/]"),
                ("escape", "\u001b[31mowned\u001b[0m", "[31mowned[0m"),
                ("controls", "ow\r\n\tned\0\b", "owned"),
                ("format", "ow\u202Ened\u200B", "owned"),
                ("unicode", "Café 中文 😀", "Café 中文 😀")
            ];
            foreach (var (name, raw, expected) in cases)
                foreach (var noColor in new[] { false, true })
                    yield return new TestCaseData(raw, expected, noColor)
                        .SetName(nameof(UntrustedColumnsCellsAndNotesRemainLiteralAndSafe) + "(" + name + "," + noColor + ")");
        }
    }

    [TestCaseSource(nameof(UntrustedTextCases))]
    public void UntrustedColumnsCellsAndNotesRemainLiteralAndSafe(string raw, string expected, bool noColor)
    {
        using var output = Writer();
        var row = new TerminalRow("owned", ["Row:" + raw, "value"],
            () => throw new AssertionException("Rendering a table must not open row details."));
        ActivityCommands.Render(new TerminalTable(["Header:" + raw, "Second"], [row], "Note:" + raw), output, noColor);
        var text = output.ToString();
        Assert.That(text.Contains("Header:" + expected, StringComparison.Ordinal), Is.True);
        Assert.That(text.Contains("Row:" + expected, StringComparison.Ordinal), Is.True);
        Assert.That(text.Contains("Note:" + expected, StringComparison.Ordinal), Is.True);
        Assert.That(text.Any(character => character is '\u001b' or '\u202E' or '\u200B'), Is.False);
    }

    [TestCase(false, null)]
    [TestCase(true, null)]
    [TestCase(false, "1")]
    [TestCase(true, "1")]
    [TestCase(false, "0")]
    [TestCase(true, "0")]
    public void RedirectedOutputStaysPlainAcrossExplicitAndEnvironmentColorOptions(bool noColor, string? environment)
    {
        var previous = Environment.GetEnvironmentVariable("NO_COLOR");
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", environment);
            using var output = Writer();
            ActivityCommands.Render(new TerminalTable(["Column"], [new TerminalRow("row", ["[green]literal[/]"], "unused")], "note"), output, noColor);
            Assert.That(output.ToString(), Does.Contain("[green]literal[/]").And.Contain("note"));
            Assert.That(output.ToString().Contains('\u001b'), Is.False);
        }
        finally { Environment.SetEnvironmentVariable("NO_COLOR", previous); }
    }

    [Test]
    public void HeaderRowsAndNoteKeepOrderWhileCellsStayCachedAndDetailsUnread()
    {
        using var output = Writer();
        var firstReads = 0; var secondReads = 0;
        var first = new TerminalRow("first", () => { firstReads++; return ["ROW-FIRST", "one"]; },
            () => throw new AssertionException("First details must remain lazy."));
        var second = new TerminalRow("second", () => { secondReads++; return ["ROW-SECOND", "two"]; },
            () => throw new AssertionException("Second details must remain lazy."));
        var report = new TerminalTable(["HEADER", "Value"], [first, second], "FINAL-NOTE");
        ActivityCommands.Render(report, output, false);
        var text = output.ToString();
        var header = text.IndexOf("HEADER", StringComparison.Ordinal);
        var firstRow = text.IndexOf("ROW-FIRST", StringComparison.Ordinal);
        var secondRow = text.IndexOf("ROW-SECOND", StringComparison.Ordinal);
        var note = text.IndexOf("FINAL-NOTE", StringComparison.Ordinal);
        Assert.That(header, Is.GreaterThanOrEqualTo(0));
        Assert.That(firstRow, Is.GreaterThan(header)); Assert.That(secondRow, Is.GreaterThan(firstRow)); Assert.That(note, Is.GreaterThan(secondRow));
        Assert.That(text.Split("FINAL-NOTE", StringSplitOptions.None), Has.Length.EqualTo(2));
        Assert.That(text.EndsWith("FINAL-NOTE" + Environment.NewLine, StringComparison.Ordinal), Is.True);
        ActivityCommands.Render(report, output, true);
        Assert.That(firstReads, Is.EqualTo(1)); Assert.That(secondReads, Is.EqualTo(1));
        Assert.That(report.Rows.Select(row => row.Key), Is.EqualTo(new[] { "first", "second" }));
        output.Write("CALLER-AFTER");
        Assert.That(output.ToString(), Does.EndWith("CALLER-AFTER"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EmptyRowsKeepHeadersAndTheFinalNoteLine(bool emptyNote)
    {
        using var output = Writer();
        var note = emptyNote ? "" : "NO-ROWS-NOTE";
        ActivityCommands.Render(new TerminalTable(["HEADER", "Value"], [], note), output, true);
        var text = output.ToString();
        Assert.That(text, Does.Contain("HEADER").And.Contain("Value"));
        Assert.That(text.EndsWith(note + Environment.NewLine, StringComparison.Ordinal), Is.True);
        Assert.That(text.Contains('\u001b'), Is.False);
    }

    [Test]
    public void EmptyColumnsAndRowsStillWriteTheNote()
    {
        using var output = Writer();
        ActivityCommands.Render(new TerminalTable([], [], "EMPTY-TABLE-NOTE"), output, false);
        Assert.That(output.ToString().EndsWith("EMPTY-TABLE-NOTE" + Environment.NewLine, StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void CellFactoryFailureRemainsUnwrappedBeforeAnyTableOrNoteIsWritten()
    {
        using var output = Writer();
        var expected = new InvalidDataException("owned cell failure");
        var report = new TerminalTable(["Header"],
            [new TerminalRow("row", () => throw expected, () => throw new AssertionException("No details"))], "NOTE-MUST-NOT-BE-WRITTEN");
        Assert.That(Assert.Catch(() => ActivityCommands.Render(report, output, false)), Is.SameAs(expected));
        Assert.That(output.ToString(), Is.Empty);
        output.Write("still-owned");
        Assert.That(output.ToString(), Is.EqualTo("still-owned"));
    }

    [Test]
    public void BorrowedWriterFailureRemainsUnwrappedAndTheWriterIsNotDisposed()
    {
        var expected = new IOException("owned writer failure");
        using var output = new FailingWriter(expected);
        Assert.That(Assert.Catch(() => ActivityCommands.Render(new TerminalTable(["Header"], [], "note"), output, false)), Is.SameAs(expected));
        Assert.That(output.Disposed, Is.False);
    }

    [TestCase("services")]
    [TestCase("connections")]
    [TestCase("usage")]
    public async Task TableCommandsReachTheRendererAndDisposeTheirOwnedSource(string name)
    {
        var source = new Source();
        using var output = Writer(); using var error = Writer();
        var exit = await CliCommand.Create(() => source, output, error).Parse([name, "--top", "1", "--no-color"]).InvokeAsync();
        Assert.That(exit, Is.Zero, error.ToString());
        Assert.That(source.Count, Is.EqualTo(2)); Assert.That(source.Disposed, Is.True);
        Assert.That(output.ToString(), Does.Contain("Owned"));
        Assert.That(output.ToString().Contains('\u001b'), Is.False);
        Assert.That(error.ToString(), Is.Empty);
    }

    [Test]
    public async Task TableCommandReportsSanitizedWriterFailureAfterDisposingItsSource()
    {
        var source = new Source(); var expected = new IOException("owned\u001b[31m\r\nwrite failure");
        using var output = new FailingWriter(expected); using var error = Writer();
        var exit = await CliCommand.Create(() => source, output, error).Parse(["services", "--no-color"]).InvokeAsync();
        Assert.That(exit, Is.EqualTo(1)); Assert.That(source.Count, Is.EqualTo(2)); Assert.That(source.Disposed, Is.True);
        Assert.That(error.ToString(), Is.EqualTo(string.Format(CultureInfo.CurrentCulture, Strings.CliFailed, "owned[31mwrite failure") + "\n"));
        Assert.That(output.Disposed, Is.False);
    }

    private static StringWriter Writer() => new(CultureInfo.InvariantCulture) { NewLine = "\n" };

    private sealed class FailingWriter(Exception error) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public bool Disposed { get; private set; }
        public override void Write(char value) => throw error;
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class Source : IFrameSource
    {
        public int Count { get; private set; }
        public bool Disposed { get; private set; }
        public CapturedFrame Capture(SamplingOptions options)
        {
            var process = new ProcessUsage(42, 1, "Owned worker", 25, 1024) { ImagePath = @"C:\owned\worker.exe", PrivateBytes = 2048 };
            var sample = new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(Count++), 25, [process], 0)
                { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 };
            return new CapturedFrame(sample, new ServiceFrame(sample,
                [new ServiceGroup("owned", [new ServiceObservation("Owned.Service", "Owned service", 4, 42, 1)], process, [process])]))
                { Connections = new NetworkConnectionsFrame([new NetworkConnection(42, 1, TransportProtocol.Tcp,
                    new IPEndPoint(IPAddress.Loopback, 5000))], NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success) };
        }
        public void Reset() => throw new AssertionException("Command collection must not reset a healthy source.");
        public void Dispose() => Disposed = true;
    }
}
