using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private static readonly MethodInfo InfoRefreshMethod = typeof(HardwareInfoWindow)
        .GetMethod("RefreshAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static Task RefreshInfo(HardwareInfoWindow dialog) => InfoRefreshMethod.CreateDelegate<Func<Task>>(dialog)();

    private static WindowsHardwareInfo.Section[] InfoSections(string kind) => kind switch
    {
        "empty" => [],
        "duplicates" => [new("CPU", "Owned details"), new("CPU", "Owned details")],
        _ =>
        [
            new("CPU", "\nOwned processor\nSecond line"),
            new("Memory", "Owned memory", []),
            new("Disks", "Owned drives", [new("First drive", "First details"), new("Second drive", "Second details")], "Owned drive details")
        ]
    };

    [TestCase("empty")]
    [TestCase("mixed")]
    [TestCase("duplicates")]
    [SetCulture("en-US")]
    public void InfoRefreshSuccessPreservesSectionsDetailsAccessibilityAndTheCopiedTimestamp(string kind)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        var sections = InfoSections(kind);
        var copies = new List<string>();
        var dialog = new HardwareInfoWindow(copies.Add);
        var uiThread = Environment.CurrentManagedThreadId;
        var readerThread = uiThread;
        using var scope = HardwareInfoReader.OverrideForTests(dialog, () =>
        {
            readerThread = Environment.CurrentManagedThreadId;
            return sections;
        });
        try
        {
            FinishWindowCheck(RefreshInfo(dialog));
            Assert.Multiple(() =>
            {
                Assert.That(readerThread, Is.Not.EqualTo(uiThread));
                Assert.That(dialog.Ready.IsCompletedSuccessfully, Is.True);
                Assert.That(dialog.RefreshButton.IsEnabled, Is.True);
                Assert.That(dialog.CopyButton.IsEnabled, Is.True);
                Assert.That(dialog.CopyButton.Content, Is.EqualTo(Strings.Copy));
                Assert.That(dialog.Sections.Children.Count, Is.EqualTo(sections.Length));
            });
            for (var index = 0; index < sections.Length; index++)
            {
                var expected = sections[index];
                var expander = (Expander)dialog.Sections.Children[index];
                var content = (StackPanel)expander.Content;
                var details = (TextBox)content.Children[0];
                Assert.Multiple(() =>
                {
                    Assert.That(expander.Header, Is.EqualTo(expected.Name));
                    Assert.That(expander.Tag, Is.EqualTo(expected.Summary));
                    Assert.That(expander.IsExpanded, Is.EqualTo(expected == sections[0]));
                    Assert.That(AutomationProperties.GetHelpText(expander), Is.EqualTo(expected.Summary));
                    Assert.That(details.Text, Is.EqualTo(expected.Details));
                    Assert.That(details.IsReadOnly, Is.True);
                    Assert.That(details.TextWrapping, Is.EqualTo(TextWrapping.Wrap));
                    Assert.That(AutomationProperties.GetName(details), Is.EqualTo(expected.Name));
                });
                if (expected.Items is not { Count: > 0 } items)
                {
                    Assert.That(content.Children.Count, Is.EqualTo(1));
                    Assert.That(details.Margin.Bottom, Is.EqualTo(20));
                    continue;
                }
                Assert.That(details.Margin.Bottom, Is.EqualTo(8));
                var children = (StackPanel)content.Children[1];
                Assert.That(children.Children.Count, Is.EqualTo(items.Count * 2));
                for (var item = 0; item < items.Count; item++)
                {
                    Assert.That(((TextBlock)children.Children[item * 2]).Text, Is.EqualTo(items[item].Name));
                    var text = (TextBox)children.Children[item * 2 + 1];
                    Assert.That(text.Text, Is.EqualTo(items[item].Text));
                    Assert.That(text.IsReadOnly, Is.True);
                    Assert.That(AutomationProperties.GetName(text), Is.EqualTo(items[item].Name));
                }
            }
            dialog.CopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(copies, Has.Count.EqualTo(1));
            var timestamp = copies[0].Split(Environment.NewLine)[4];
            Assert.That(timestamp, Does.StartWith("Capture: "));
            var captured = DateTimeOffset.ParseExact(timestamp["Capture: ".Length..], "yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
            Assert.Multiple(() =>
            {
                Assert.That(copies[0], Is.EqualTo(HardwareInfoText.Format(sections, captured)));
                Assert.That(dialog.SnapshotNote.Text, Is.EqualTo(Strings.Format(Strings.HardwareSnapshotTime, captured)));
                Assert.That(dialog.CopyButton.Content, Is.EqualTo(Strings.Copied));
            });
        }
        finally { dialog.Close(); }
    }

    private static IReadOnlyList<WindowsHardwareInfo.Section> InfoFailedRead(string kind) => kind switch
    {
        "io" => throw new IOException("Owned private reader failure"),
        "invalid" => throw new InvalidOperationException("Owned private reader failure"),
        "canceled" => throw new OperationCanceledException("Owned private reader failure"),
        "null-result" => null!,
        "null-section" => [null!],
        _ => [new("Owned partial section", "Details", [null!])]
    };

    [TestCase("io")]
    [TestCase("invalid")]
    [TestCase("canceled")]
    [TestCase("null-result")]
    [TestCase("null-section")]
    [TestCase("null-item")]
    public void InfoRefreshFailuresKeepTheGenericNoticeClearPartialUiAndCompleteReady(string kind)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        var dialog = new HardwareInfoWindow();
        using var scope = HardwareInfoReader.OverrideForTests(dialog, () => InfoFailedRead(kind));
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        using var trace = new TextWriterTraceListener(text);
        Trace.Listeners.Add(trace);
        try
        {
            FinishWindowCheck(RefreshInfo(dialog));
            Assert.Multiple(() =>
            {
                Assert.That(dialog.Ready.IsCompletedSuccessfully, Is.True);
                Assert.That(dialog.RefreshButton.IsEnabled, Is.True);
                Assert.That(dialog.CopyButton.IsEnabled, Is.False);
                Assert.That(dialog.SnapshotNote.Text, Is.EqualTo(Strings.SnapshotUnavailableTryRefresh));
                Assert.That(dialog.Sections.Children.Count, Is.EqualTo(1));
                Assert.That(((TextBlock)dialog.Sections.Children[0]).Text, Is.EqualTo(Strings.HardwareInformationCouldNotBeReadTryRefresh));
                Assert.That(text.ToString(), Does.Contain("Hardware information failed:"));
                Assert.That(dialog.SnapshotNote.Text, Does.Not.Contain("Owned private"));
            });
        }
        finally { Trace.Listeners.Remove(trace); dialog.Close(); }
    }

    [Test]
    public void InfoRefreshPendingReadRejectsReentryAndDoesNotCompleteReadyEarly()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        var dialog = new HardwareInfoWindow();
        using var release = new ManualResetEventSlim(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var scope = HardwareInfoReader.OverrideForTests(dialog, () =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Owned Info reader was not released");
            return InfoSections("mixed");
        });
        var task = RefreshInfo(dialog);
        try
        {
            FinishWindowCheck(entered.Task);
            Assert.That(RefreshInfo(dialog).IsCompletedSuccessfully, Is.True);
            dialog.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(task.IsCompleted, Is.False);
                Assert.That(dialog.Ready.IsCompleted, Is.False);
                Assert.That(dialog.RefreshButton.IsEnabled, Is.False);
                Assert.That(dialog.CopyButton.IsEnabled, Is.False);
                Assert.That(dialog.SnapshotNote.Text, Is.EqualTo(Strings.ReadingHardwareInformation));
                Assert.That(((TextBlock)dialog.Sections.Children[0]).Text, Is.EqualTo(Strings.ReadingHardwareInformation));
            });
        }
        finally { release.Set(); FinishWindowCheck(task); dialog.Close(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InfoRefreshClosingDuringAReadKeepsTheClosedUiAndStillCompletesReady(bool fail)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        var dialog = new HardwareInfoWindow();
        using var release = new ManualResetEventSlim(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scope = HardwareInfoReader.OverrideForTests(dialog, () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Owned closed Info reader was not released");
            return fail ? throw new IOException("Owned late reader failure") : InfoSections("mixed");
        });
        var task = RefreshInfo(dialog);
        var closed = false;
        try
        {
            FinishWindowCheck(entered.Task);
            var marker = dialog.Sections.Children[0];
            dialog.Close();
            closed = true;
            release.Set();
            FinishWindowCheck(task);
            Assert.Multiple(() =>
            {
                Assert.That(dialog.Ready.IsCompletedSuccessfully, Is.True);
                Assert.That(dialog.RefreshButton.IsEnabled, Is.True);
                Assert.That(dialog.CopyButton.IsEnabled, Is.False);
                Assert.That(dialog.SnapshotNote.Text, Is.EqualTo(Strings.ReadingHardwareInformation));
                Assert.That(dialog.Sections.Children.Cast<UIElement>(), Is.EqualTo(new[] { marker }));
            });
        }
        finally { release.Set(); FinishWindowCheck(task); if (!closed) dialog.Close(); }
    }

    [Test]
    public void InfoRefreshFailureClearsTheCopiedSnapshotAndASubsequentRetryReplacesIt()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        var copies = new List<string>();
        var dialog = new HardwareInfoWindow(copies.Add);
        var step = 0;
        using var scope = HardwareInfoReader.OverrideForTests(dialog, () => step switch
        {
            0 => [new("First snapshot", "Original details")],
            1 => throw new IOException("Owned retry failure"),
            _ => [new("Final snapshot", "Replacement details")]
        });
        try
        {
            FinishWindowCheck(RefreshInfo(dialog));
            var ready = dialog.Ready;
            dialog.CopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(copies.Single(), Does.Contain("First snapshot"));
            step = 1;
            FinishWindowCheck(RefreshInfo(dialog));
            Assert.That(dialog.CopyButton.Content, Is.EqualTo(Strings.Copy));
            dialog.CopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(copies, Has.Count.EqualTo(1));
            step = 2;
            FinishWindowCheck(RefreshInfo(dialog));
            dialog.CopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Multiple(() =>
            {
                Assert.That(copies, Has.Count.EqualTo(2));
                Assert.That(copies[1], Does.Contain("Final snapshot").And.Contain("Replacement details").And.Not.Contain("First snapshot"));
                Assert.That(dialog.Ready, Is.SameAs(ready));
                Assert.That(dialog.Ready.IsCompletedSuccessfully, Is.True);
            });
        }
        finally { dialog.Close(); }
    }
}

[TestFixture, NonParallelizable]
public sealed class HardwareInfoReaderTests
{
    private static WindowsHardwareInfo.Section[] Rows(string name) => [new(name, "Owned details")];
    private static IReadOnlyList<WindowsHardwareInfo.Section> Read(object owner) => HardwareInfoReader.For(owner)();
    private static void IsNative(object owner)
    {
        Func<IReadOnlyList<WindowsHardwareInfo.Section>> native = WindowsHardwareInfo.Read;
        var selected = HardwareInfoReader.For(owner);
        Assert.That(selected.Method, Is.EqualTo(native.Method));
        Assert.That(selected.Target, Is.Null);
    }

    [Test]
    public async Task HardwareReaderOwnedScopesKeepSelectedDelegatesStableAndRestoreNestedProvidersOnce()
    {
        var owner = new object();
        var outer = Rows("Outer");
        var inner = Rows("Inner");
        var other = Rows("Other");
        IsNative(owner);
        using var parent = HardwareInfoReader.OverrideForTests(owner, () => outer);
        await Task.Yield();
        Assert.That(await Task.Run(() => Read(owner)), Is.SameAs(outer));
        var child = HardwareInfoReader.OverrideForTests(owner, () => inner);
        var selected = HardwareInfoReader.For(owner);
        child.Dispose();
        Assert.That(Read(owner), Is.SameAs(outer));
        Assert.That(selected(), Is.SameAs(inner), "A scheduled read keeps its selected provider after scope disposal");
        using (HardwareInfoReader.OverrideForTests(owner, () => other))
        {
            child.Dispose();
            Assert.That(Read(owner), Is.SameAs(other));
        }
        Assert.Throws<InvalidOperationException>(() =>
        {
            using var failed = HardwareInfoReader.OverrideForTests(owner, () => inner);
            Assert.That(Read(owner), Is.SameAs(inner));
            throw new InvalidOperationException("Owned scope failure");
        });
        await Task.Yield();
        Assert.That(Read(owner), Is.SameAs(outer));
        parent.Dispose();
        IsNative(owner);
    }

    [Test]
    public async Task HardwareReaderFreshOverlappingOwnersKeepTheirProvidersIndependent()
    {
        var parentOwner = new object();
        var parentRows = Rows("Parent");
        using var parent = HardwareInfoReader.OverrideForTests(parentOwner, () => parentRows);
        using var entered = new CountdownEvent(2);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Child(object owner, string name) => Task.Run(async () =>
        {
            var rows = Rows(name);
            using (HardwareInfoReader.OverrideForTests(owner, () => rows))
            {
                entered.Signal();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.That(Read(owner), Is.SameAs(rows));
                Assert.That(Read(parentOwner), Is.SameAs(parentRows));
            }
            IsNative(owner);
        });
        var first = Child(new object(), "First");
        var second = Child(new object(), "Second");
        try
        {
            Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
            Assert.That(Read(parentOwner), Is.SameAs(parentRows));
        }
        finally { release.TrySetResult(); await Task.WhenAll(first, second); }
    }

    [Test]
    public void HardwareReaderNullArgumentsDoNotReplaceTheCurrentOwnerProvider()
    {
        var owner = new object();
        var rows = Rows("Retained");
        using var scope = HardwareInfoReader.OverrideForTests(owner, () => rows);
        Assert.That(Assert.Throws<ArgumentNullException>(() => HardwareInfoReader.OverrideForTests(owner, null!))!.ParamName, Is.EqualTo("reader"));
        Assert.That(Assert.Throws<ArgumentNullException>(() => HardwareInfoReader.OverrideForTests(null!, () => rows))!.ParamName, Is.EqualTo("owner"));
        Assert.That(Assert.Throws<ArgumentNullException>(() => HardwareInfoReader.For(null!))!.ParamName, Is.EqualTo("owner"));
        Assert.That(Read(owner), Is.SameAs(rows));
    }
}