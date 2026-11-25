using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration")]
public sealed class ProcessRowExampleIconCharacterizationTests
{
    private static ProcessUsage Usage(int pid = 0, long creation = 81001) => new(pid, creation, "example.exe", 1);

    private static void Update(ProcessRow row, ProcessUsage usage, bool grouped = false) =>
        row.Update(usage, [new MonitorSample(DateTimeOffset.UnixEpoch, 1, [usage], 0)], 120, false, null, grouped);

    private static Image Icon(ProcessRow row) => row.Children.OfType<Grid>().Single().Children.OfType<Image>().Single();

    private static BitmapSource Sentinel(byte value)
    {
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new[] { value, value, value, (byte)255 }, 4);
        image.Freeze();
        return image;
    }

    private static void Warm(ProcessUsage usage) =>
        Assert.That(ProcessIconProvider.GetAsync(usage).GetAwaiter().GetResult(), Is.SameAs(ProcessIconProvider.Fallback));

    private static void Run(Func<Task> action)
    {
        var previous = SynchronizationContext.Current;
        var dispatcher = Dispatcher.CurrentDispatcher;
        var timer = new DispatcherTimer(DispatcherPriority.Send, dispatcher) { Interval = TimeSpan.FromSeconds(10) };
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var task = action();
            if (!task.IsCompleted)
            {
                var frame = new DispatcherFrame();
                timer.Tick += (_, _) => frame.Continue = false;
                _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false),
                    CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                timer.Start();
                Dispatcher.PushFrame(frame);
            }
            Assert.That(task.IsCompleted, Is.True, "Example icon did not finish within the dispatcher bound");
            task.GetAwaiter().GetResult();
        }
        finally
        {
            timer.Stop();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [TestCase(null)]
    [TestCase("")]
    public void ExampleIconWithoutUsageReturnsBeforeReadingThePath(string? path) => Run(async () =>
    {
        var row = new ProcessRow();
        var sentinel = Sentinel(11);
        Icon(row).Source = sentinel;
        var pending = row.SetExampleIconAsync(path!);
        Assert.That(pending.IsCompletedSuccessfully, Is.True);
        await pending;
        Assert.That(Icon(row).Source, Is.SameAs(sentinel));
        Assert.That(row.Identity, Is.Null);
    });

    [TestCase(false)]
    [TestCase(true)]
    public void ExampleIconWaitsForDispatcherIdleBeforeReplacingTheCurrentImage(bool missingPath) => Run(async () =>
    {
        var usage = Usage();
        Warm(usage);
        var path = missingPath ? Path.Combine(Path.GetTempPath(), "WinMon-missing-icon-" + Guid.NewGuid().ToString("N"), "missing.exe") : "";
        Assert.That(await ProcessIconProvider.GetForPathAsync(path), Is.SameAs(ProcessIconProvider.Fallback));
        var row = new ProcessRow();
        Update(row, usage);
        var sentinel = Sentinel(22);
        Icon(row).Source = sentinel;
        var pending = row.SetExampleIconAsync(path);
        Assert.That(pending.IsCompleted, Is.False);
        Assert.That(Icon(row).Source, Is.SameAs(sentinel));
        await pending;
        Assert.That(Icon(row).Source, Is.SameAs(ProcessIconProvider.Fallback));
        Assert.That(row.Represents((target, grouped) => ReferenceEquals(target, usage) && !grouped), Is.True);
    });

    [TestCase(false)]
    [TestCase(true)]
    public void ExampleIconDoesNotOverwriteARowReassignedToAnotherProcessLifetime(bool changePid) => Run(async () =>
    {
        var first = Usage();
        var replacement = changePid ? Usage(int.MaxValue) : Usage(creation: 81002);
        Warm(first);
        Warm(replacement);
        var row = new ProcessRow();
        Update(row, first);
        Icon(row).Source = Sentinel(33);
        var pending = row.SetExampleIconAsync("");
        Assert.That(pending.IsCompleted, Is.False);
        Update(row, replacement);
        var replacementImage = Sentinel(44);
        Icon(row).Source = replacementImage;
        await pending;
        Assert.That(Icon(row).Source, Is.SameAs(replacementImage));
        Assert.That(row.Represents((target, _) => ReferenceEquals(target, replacement)), Is.True);
        Assert.That(row.Identity!.Value.Item1, Is.EqualTo(replacement.ProcessId));
        Assert.That(row.Identity!.Value.Item2, Is.EqualTo(replacement.CreationTicks));
    });

    [TestCase(false)]
    [TestCase(true)]
    public void ExampleIconStillAppliesWhenOnlyMetadataOrGroupingChangesWithinTheSameLifetime(bool grouped) => Run(async () =>
    {
        var first = Usage();
        Warm(first);
        var row = new ProcessRow();
        Update(row, first);
        var pending = row.SetExampleIconAsync("");
        Assert.That(pending.IsCompleted, Is.False);
        var replacement = first with { Name = "renamed.exe", AppName = "Replacement" };
        Update(row, replacement, grouped);
        Icon(row).Source = Sentinel(55);
        await pending;
        Assert.That(Icon(row).Source, Is.SameAs(ProcessIconProvider.Fallback));
        Assert.That(row.Represents((target, mode) => ReferenceEquals(target, replacement) && mode == grouped), Is.True);
    });

    [Test]
    public void ExampleIconWithUsagePreservesTheNullPathExceptionAndExistingImage() => Run(async () =>
    {
        var usage = Usage();
        Warm(usage);
        var row = new ProcessRow();
        Update(row, usage);
        var sentinel = Sentinel(66);
        Icon(row).Source = sentinel;
        var error = Assert.ThrowsAsync<NullReferenceException>(async () => await row.SetExampleIconAsync(null!));
        Assert.That(error, Is.Not.Null);
        Assert.That(Icon(row).Source, Is.SameAs(sentinel));
        Assert.That(row.Represents((target, _) => ReferenceEquals(target, usage)), Is.True);
        await Task.CompletedTask;
    });

    [TestCase(false)]
    [TestCase(true)]
    public void ExampleIconAfterAFailedRowUpdateKeepsTheNullableLifetimeGuard(bool finishUpdateWhilePending) => Run(async () =>
    {
        var usage = Usage(creation: 81003);
        Warm(usage);
        var row = new ProcessRow();
        Assert.That(Assert.Throws<ArgumentNullException>(() => row.Update(usage, null!, 120, false, null))!.ParamName,
            Is.EqualTo("source"));
        Assert.That(row.Represents((target, _) => ReferenceEquals(target, usage)), Is.True);
        Icon(row).Source = Sentinel(77);
        var pending = row.SetExampleIconAsync("");
        Assert.That(pending.IsCompleted, Is.False);
        var replacementImage = Sentinel(88);
        if (finishUpdateWhilePending)
        {
            Update(row, usage);
            Icon(row).Source = replacementImage;
        }
        await pending;
        Assert.That(Icon(row).Source, Is.SameAs(finishUpdateWhilePending ? replacementImage : ProcessIconProvider.Fallback));
        Assert.That(row.Represents((target, _) => ReferenceEquals(target, usage)), Is.True);
    });
}
