using System.Diagnostics;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class UsageArchiveTests
{
    [Test, Explicit("Measures daily archive and scoped capture overhead")]
    public void MeasureArchiveOverhead()
    {
        var processes = Enumerable.Range(1, 850).Select(i => new ProcessUsage(i, i, "worker" + i, 0.01)
            { ImagePath = "C:\\apps\\worker" + i + ".exe", PrivateBytes = 1000 }).ToArray();
        var sample = new MonitorSample(DateTimeOffset.UtcNow, 10, processes, 0) { IntervalSeconds = 1, IntervalCpuTicks = 100_000_000 };
        foreach (var enabled in new[] { false, true })
        {
            var ledger = new UsageLedger(sample.Timestamp);
            var archive = new UsageArchive();
            var rules = new CaptureRules([new CaptureRuleSetting(CaptureMetric.ApplicationCpu, true, 90, 10) { Target = "worker400.exe" },
                new CaptureRuleSetting(CaptureMetric.ApplicationMemory, true, 1024, 10) { Target = "worker400.exe" },
                new CaptureRuleSetting(CaptureMetric.ApplicationNetwork, true, 50, 10) { Target = "worker400.exe" }], TimeSpan.Zero, 12);
            for (var i = 0; i < 400; i++) { ledger.Add(sample); if (enabled) { archive.Add(sample); rules.Observe(sample); } }
            var times = new List<double>();
            var allocations = new List<long>();
            for (var batch = 0; batch < 7; batch++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                for (var i = 0; i < 100; i++) { ledger.Add(sample); if (enabled) { archive.Add(sample); rules.Observe(sample); } }
                watch.Stop();
                times.Add(watch.Elapsed.TotalMilliseconds / 100);
                allocations.Add((GC.GetAllocatedBytesForCurrentThread() - before) / 100);
            }
            TestContext.Out.WriteLine($"{(enabled ? "After: ledger, daily archive, three app rules" : "Before: ledger")}: {times.Order().ElementAt(3):F4} ms/tick, {allocations.Order().ElementAt(3)} bytes/tick (850 apps, median 7 batches)");
        }
    }
    private static MonitorSample Sample(DateTimeOffset end, double seconds = 2) => new(end, 10,
        [new ProcessUsage(42, 123, "worker", 10) { ImagePath = "C:\\worker.exe", IoBytesPerSecond = 4, GpuPercent = 50 }], 0)
        { IntervalSeconds = seconds, IntervalCpuTicks = (long)(seconds * 10_000_000) };

    [Test]
    public void SplitsMidnightAndWeeklyQueriesDoNotInventUnobservedTime()
    {
        var archive = new UsageArchive(TimeZoneInfo.Utc);
        archive.Add(Sample(new DateTimeOffset(2026, 11, 12, 0, 0, 1, TimeSpan.Zero)));
        var first = archive.Query(new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 11));
        var second = archive.Query(new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 12));
        Assert.That(first.Period.Apps.Single().CpuTicks, Is.EqualTo(1_000_000));
        Assert.That(second.Period.Apps.Single().CpuTicks, Is.EqualTo(1_000_000));
        Assert.That(first.Period.Apps.Single().IoBytes, Is.EqualTo(4));
        Assert.That(first.Period.Apps.Single().GpuSeconds, Is.EqualTo(0.5));
        var week = archive.Query(new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 12));
        Assert.That(week.ObservedSeconds, Is.EqualTo(2));
        Assert.That(week.Period.Complete, Is.False);
        Assert.That(week.Period.Apps.Single().Running, Is.Zero);
    }

    [Test]
    public void RetentionGapsAndRestorePreserveOnlyObservedTotals()
    {
        var archive = new UsageArchive(TimeZoneInfo.Utc);
        var end = DateTimeOffset.Parse("2026-11-12T12:00:00Z");
        for (var i = 40; i >= 0; i--) archive.Add(Sample(end.AddDays(-i)));
        Assert.That(archive.Export().Days.Length, Is.EqualTo(35));
        var restored = UsageArchive.Restore(archive.Export());
        restored.Add(Sample(end.AddSeconds(2)));
        restored.Add(Sample(end.AddSeconds(4)) with { IsGap = true });
        var day = restored.Query(new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 12));
        Assert.That(day.ObservedSeconds, Is.EqualTo(4));
        Assert.That(day.Period.Apps.Single().CpuTicks, Is.EqualTo(4_000_000));
        Assert.That(day.Period.Apps.Single().Lifetimes, Is.EqualTo(1));
    }

    [Test]
    public void BackwardClockChangesCannotExceedTheDayBudget()
    {
        var archive = new UsageArchive(TimeZoneInfo.Utc);
        var end = DateTimeOffset.Parse("2026-11-12T12:00:00Z");
        for (var i = 0; i < 50; i++) archive.Add(Sample(end.AddDays(-i)));
        Assert.That(archive.Export().Days.Length, Is.EqualTo(UsageArchive.RetainedDays));
        Assert.DoesNotThrow(() => UsageArchive.Restore(archive.Export()));
    }

    [Test]
    public void CalendarDaysRespectDaylightSavingTime()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var archive = new UsageArchive(zone);
        archive.Add(Sample(DateTimeOffset.Parse("2026-03-08T07:00:01Z")));
        var spring = archive.Query(new DateOnly(2026, 3, 8), new DateOnly(2026, 3, 8));
        Assert.That((spring.Period.Latest - spring.Period.Since).TotalHours, Is.EqualTo(23));
        Assert.That(spring.ObservedSeconds, Is.EqualTo(2));
        var fall = archive.Query(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1));
        Assert.That((fall.Period.Latest - fall.Period.Since).TotalHours, Is.EqualTo(25));
        Assert.That(fall.ObservedSeconds, Is.Zero);
    }

    [Test]
    public void RepeatedMidnightBelongsToTheNewCalendarDay()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 1);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 11, 1);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1), start, end);
        var zone = TimeZoneInfo.CreateCustomTimeZone("midnight-test", TimeSpan.Zero, "Test", "Standard", "Daylight", [rule]);
        var archive = new UsageArchive(zone);
        var date = new DateOnly(2026, 11, 1);
        var period = archive.Query(date, date).Period;
        Assert.That((period.Latest - period.Since).TotalHours, Is.EqualTo(25));
    }

    [Test]
    public async Task AtomicRoundTripAndCorruptInputDoNotDestroyExistingData()
    {
        var folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "usage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "usage.json");
            var archive = new UsageArchive(TimeZoneInfo.Utc);
            archive.Add(Sample(DateTimeOffset.Parse("2026-11-12T12:00:00Z")));
            await UsageArchive.SaveAsync(file, archive.Export());
            Assert.That((await UsageArchive.LoadAsync(file)).Export().Days.Length, Is.EqualTo(1));
            await File.WriteAllTextAsync(file, "broken");
            Assert.ThrowsAsync<System.Text.Json.JsonException>(async () => await UsageArchive.LoadAsync(file));
            Assert.That(await File.ReadAllTextAsync(file), Is.EqualTo("broken"));
            Assert.That(Directory.GetFiles(folder, "*.tmp"), Is.Empty);
            Assert.Throws<InvalidDataException>(() => UsageArchive.Restore(archive.Export() with { SchemaVersion = 2 }));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Test]
    public async Task OutOfRangeTotalsAreRejectedBeforeReplacingTheArchive()
    {
        var archive = new UsageArchive(TimeZoneInfo.Utc);
        archive.Add(Sample(DateTimeOffset.Parse("2026-11-12T12:00:00Z")));
        var state = archive.Export();
        var day = state.Days.Single();
        var invalid = state with { Days = [day with { Totals = day.Totals with
            { Apps = [day.Totals.Apps.Single() with { GpuSeconds = 1e100 }] } }] };
        Assert.Throws<InvalidDataException>(() => UsageArchive.Restore(invalid));
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "invalid-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await UsageArchive.SaveAsync(path, state);
            var original = await File.ReadAllTextAsync(path);
            Assert.ThrowsAsync<InvalidDataException>(async () => await UsageArchive.SaveAsync(path, invalid));
            Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(original));
        }
        finally { File.Delete(path); }
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(60.001)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidIntervalsDoNotChangeRecordedUsage(double seconds)
    {
        var archive = new UsageArchive(TimeZoneInfo.Utc);
        var end = DateTimeOffset.Parse("2026-11-12T12:00:00Z");
        archive.Add(Sample(end));
        var original = System.Text.Json.JsonSerializer.Serialize(archive.Export());

        archive.Add(Sample(end.AddSeconds(1)) with { IntervalSeconds = seconds });

        Assert.That(System.Text.Json.JsonSerializer.Serialize(archive.Export()), Is.EqualTo(original));
    }

    [TestCase("duplicate days")]
    [TestCase("too many days")]
    [TestCase("negative observed time")]
    [TestCase("infinite observed time")]
    [TestCase("negative machine ticks")]
    [TestCase("nonfinite attributed ticks")]
    [TestCase("duplicate app keys")]
    [TestCase("foreign identity policy")]
    [TestCase("invalid lifetime app")]
    [TestCase("negative lifetime absences")]
    [TestCase("negative io")]
    [TestCase("overlong app key")]
    [TestCase("null app")]
    [TestCase("null app name")]
    [TestCase("null app path")]
    [TestCase("null app display")]
    [TestCase("null app publisher")]
    [TestCase("overlong app name")]
    [TestCase("nan cpu")]
    [TestCase("infinite io")]
    [TestCase("negative gpu")]
    [TestCase("excessive lifetimes")]
    public async Task InvalidSavedStateCannotReplaceExistingData(string corruption)
    {
        var archive = new UsageArchive(TimeZoneInfo.Utc);
        archive.Add(Sample(DateTimeOffset.Parse("2026-11-12T12:00:00Z")));
        var state = archive.Export();
        var day = state.Days.Single();
        var totals = day.Totals;
        var app = totals.Apps.Single();
        var invalid = corruption switch
        {
            "duplicate days" => state with { Days = [day, day] },
            "too many days" => state with { Days = Enumerable.Range(0, UsageArchive.RetainedDays + 1)
                .Select(i => day with { Day = day.Day.AddDays(-i) }).ToArray() },
            "negative observed time" => state with { Days = [day with { ObservedSeconds = -1 }] },
            "infinite observed time" => state with { Days = [day with { ObservedSeconds = double.PositiveInfinity }] },
            "negative machine ticks" => state with { Days = [day with { Totals = totals with { MachineBusyTicks = -1 } }] },
            "nonfinite attributed ticks" => state with { Days = [day with { Totals = totals with { AttributedTicks = double.NaN } }] },
            "duplicate app keys" => state with { Days = [day with { Totals = totals with
                { Apps = [app, app with { Key = app.Key.ToUpperInvariant() }] } }] },
            "foreign identity policy" => state with { Days = [day with { Totals = totals with { IdentityPolicyId = "foreign" } }] },
            "invalid lifetime app" => state with { Days = [day with { Totals = totals with
                { Lifetimes = [totals.Lifetimes.Single() with { App = totals.Apps.Count }] } }] },
            "negative lifetime absences" => state with { Days = [day with { Totals = totals with
                { Lifetimes = [totals.Lifetimes.Single() with { Absences = -1 }] } }] },
            "negative io" => state with { Days = [day with { Totals = totals with { Apps = [app with { IoBytes = -1 }] } }] },
            "overlong app key" => state with { Days = [day with { Totals = totals with { Apps = [app with { Key = new string('x', 4097) }] } }] },
            "null app" => state with { Days = [day with { Totals = totals with { Apps = [null!] } }] },
            "null app name" => state with { Days = [day with { Totals = totals with { Apps = [app with { Name = null! }] } }] },
            "null app path" => state with { Days = [day with { Totals = totals with { Apps = [app with { ImagePath = null! }] } }] },
            "null app display" => state with { Days = [day with { Totals = totals with { Apps = [app with { AppName = null! }] } }] },
            "null app publisher" => state with { Days = [day with { Totals = totals with { Apps = [app with { Publisher = null! }] } }] },
            "overlong app name" => state with { Days = [day with { Totals = totals with { Apps = [app with { Name = new string('x', 4097) }] } }] },
            "nan cpu" => state with { Days = [day with { Totals = totals with { Apps = [app with { CpuTicks = double.NaN }] } }] },
            "infinite io" => state with { Days = [day with { Totals = totals with { Apps = [app with { IoBytes = double.PositiveInfinity }] } }] },
            "negative gpu" => state with { Days = [day with { Totals = totals with { Apps = [app with { GpuSeconds = -1 }] } }] },
            "excessive lifetimes" => state with { Days = [day with { Totals = totals with { Apps = [app with { Lifetimes = 60_000_001 }] } }] },
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        var folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "usage-invalid-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "usage.json");
        try
        {
            await UsageArchive.SaveAsync(path, state);
            var original = await File.ReadAllBytesAsync(path);

            Assert.Throws<InvalidDataException>(() => UsageArchive.Restore(invalid));
            Assert.ThrowsAsync<InvalidDataException>(async () => await UsageArchive.SaveAsync(path, invalid));
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
            Assert.That(Directory.GetFiles(folder, "*.tmp"), Is.Empty);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [TestCase("observed", 1e9)]
    [TestCase("machine", 1e17)]
    [TestCase("attributed", 1e17)]
    [TestCase("cpu", 1e17)]
    [TestCase("io", 1e24)]
    [TestCase("gpu", 1e10)]
    public void Restore_AcceptsInclusiveNumericLimitsAndRejectsNonfiniteOrOutsideValues(string field, double maximum)
    {
        var archive = new UsageArchive(TimeZoneInfo.Utc);
        archive.Add(Sample(DateTimeOffset.Parse("2026-11-12T12:00:00Z")));
        var state = archive.Export();
        var day = state.Days.Single();
        var totals = day.Totals;
        var app = totals.Apps.Single();
        UsageArchiveState WithValue(double value) => state with { Days = [field switch
        {
            "observed" => day with { ObservedSeconds = value },
            "machine" => day with { Totals = totals with { MachineBusyTicks = value } },
            "attributed" => day with { Totals = totals with { AttributedTicks = value } },
            "cpu" => day with { Totals = totals with { Apps = [app with { CpuTicks = value }] } },
            "io" => day with { Totals = totals with { Apps = [app with { IoBytes = value }] } },
            "gpu" => day with { Totals = totals with { Apps = [app with { GpuSeconds = value }] } },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        }] };

        foreach (var value in new[] { 0, maximum })
            Assert.That(UsageArchive.Restore(WithValue(value)).Export().Days, Has.Length.EqualTo(1));
        foreach (var value in new[] { -1, double.BitIncrement(maximum), double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.That(() => UsageArchive.Restore(WithValue(value)),
                Throws.TypeOf<InvalidDataException>().With.Message.EqualTo("Invalid usage totals."));
    }

    [Test]
    public async Task CancelledSavePreservesExistingArchiveAndRemovesTemporaryFiles()
    {
        var folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "usage-cancel-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "usage.json");
        var archive = new UsageArchive(TimeZoneInfo.Utc);
        var end = DateTimeOffset.Parse("2026-11-12T12:00:00Z");
        archive.Add(Sample(end));
        try
        {
            await UsageArchive.SaveAsync(path, archive.Export());
            var original = await File.ReadAllBytesAsync(path);
            archive.Add(Sample(end.AddSeconds(2)));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await UsageArchive.SaveAsync(path, archive.Export(), cancellation.Token));

            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
            Assert.That(Directory.GetFiles(folder, "*.tmp"), Is.Empty);
            Assert.That((await UsageArchive.LoadAsync(path)).Query(new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 12)).ObservedSeconds,
                Is.EqualTo(2));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Test]
    public async Task LoadRejectsOversizedFilesBeforeDeserialization()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "usage-large-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await using (var stream = File.Create(path)) stream.SetLength(UsageArchive.MaximumFileBytes + 1);
            Assert.ThrowsAsync<InvalidDataException>(async () => await UsageArchive.LoadAsync(path));
            Assert.That(new FileInfo(path).Length, Is.EqualTo(UsageArchive.MaximumFileBytes + 1));
        }
        finally { File.Delete(path); }
    }
}
