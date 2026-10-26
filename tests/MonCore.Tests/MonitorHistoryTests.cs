using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class MonitorHistoryTests
{
    [Test]
    public void Latest_TracksNewestRetainedObservationAcrossTrimming()
    {
        var history = new MonitorHistory(2);
        Assert.That(history.Latest, Is.Null);
        history.Add(Sample(1));
        history.Add(Sample(2));
        var latest = Sample(3);
        history.Add(latest);
        history.Resize(1);
        Assert.That(history.Latest, Is.SameAs(latest));
    }

    [Test]
    public void Add_ExceedsCapacity_EvictsOldestSample()
    {
        var history = new MonitorHistory(2);
        var first = Sample(1);
        var second = Sample(2);
        var third = Sample(3);

        history.Add(first);
        history.Add(second);
        history.Add(third);

        Assert.That(history.Samples, Is.EqualTo([second, third]));
    }

    [Test]
    public void Resize_SmallerCapacity_ImmediatelyEvictsOldestSamples()
    {
        var history = new MonitorHistory(3);
        var first = Sample(1);
        var second = Sample(2);
        var third = Sample(3);

        history.Add(first);
        history.Add(second);
        history.Add(third);
        history.Resize(2);

        Assert.Multiple(() =>
        {
            Assert.That(history.Capacity, Is.EqualTo(2));
            Assert.That(history.Samples, Is.EqualTo([second, third]));
        });
    }

    [Test]
    public void Samples_ReturnsStableCopy()
    {
        var history = new MonitorHistory();
        var first = Sample(1);
        history.Add(first);
        var before = history.Samples;
        history.Add(Sample(2));

        Assert.That(before, Is.EqualTo([first]));
    }

    [Test]
    public void Resize_Zero_Throws()
    {
        var history = new MonitorHistory();

        Assert.That(() => history.Resize(0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Add_LongRecordingGap_ExpiresElapsedHistory()
    {
        var history = new MonitorHistory(600);
        history.Add(Sample(1));
        var resumed = Sample(900);
        history.Add(resumed);

        Assert.That(history.Samples, Is.EqualTo([resumed]));
    }

    [Test]
    public void Add_WallClockMovesBackward_ExpiresByMonotonicTime()
    {
        var history = new MonitorHistory(600);
        history.Add(new MonitorSample(DateTimeOffset.UnixEpoch.AddHours(2), 5, [], 0, false, Stopwatch.Frequency));
        var next = new MonitorSample(DateTimeOffset.UnixEpoch, 5, [], 0, false, 902 * Stopwatch.Frequency);
        history.Add(next);

        Assert.That(history.Samples, Is.EqualTo([next]));
    }

    [Test]
    public void Add_ProcessEntryBudgetExceeded_EvictsOldestAndReportsMemoryLimit()
    {
        var history = new MonitorHistory(600, 3);
        var processes = new[] { new ProcessUsage(1, 100, "app", 5), new ProcessUsage(2, 200, "other", 3) };
        history.Add(new MonitorSample(DateTimeOffset.UnixEpoch, 10, processes, 0));
        var next = new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(1), 10, processes, 0);
        history.Add(next);

        Assert.That(history.Samples, Is.EqualTo([next]));
        Assert.That(history.WasMemoryLimited, Is.True);
    }

    [Test]
    public void Add_ProcessExits_RetainsHistoricalAttribution()
    {
        var history = new MonitorHistory();
        var earlier = new MonitorSample(DateTimeOffset.UnixEpoch, 50, [new ProcessUsage(22, 100, "exited", 40)], 0);
        history.Add(earlier);
        history.Add(Sample(1));

        Assert.That(history.Samples[0], Is.SameAs(earlier));
        Assert.That(history.Samples[0].Processes[0].Name, Is.EqualTo("exited"));
    }

    [Test]
    public void Samples_IsSharedUntilHistoryChangesAndEarlierCopiesStayStable()
    {
        var history = new MonitorHistory(2);
        var first = Sample(1);
        history.Add(first);
        var copy = history.Samples;

        Assert.That(history.Samples, Is.SameAs(copy));
        history.Add(Sample(2));
        history.Add(Sample(3));

        Assert.Multiple(() =>
        {
            Assert.That(history.Samples, Is.Not.SameAs(copy));
            Assert.That(copy, Is.EqualTo([first]));
            Assert.That(history.Samples.Select(s => s.TotalCpuPercent), Is.EqualTo([2d, 3d]));
        });
    }

    [Test]
    public void SampleList_ShowsAPrefixOrReplacesTheLastSampleWithoutCopying()
    {
        MonitorSample[] samples = [Sample(1), Sample(2), Sample(3)];
        var replacement = Sample(4);

        var prefix = new SampleList(samples, 2);
        var replaced = new SampleList(samples, 3, replacement);

        Assert.Multiple(() =>
        {
            Assert.That(prefix, Is.EqualTo(samples[..2]));
            Assert.That(prefix.LastOrDefault(), Is.SameAs(samples[1]));
            Assert.That(replaced, Is.EqualTo([samples[0], samples[1], replacement]));
            Assert.That(replaced[^1], Is.SameAs(replacement));
            Assert.That(replaced.ToArray(), Is.EqualTo([samples[0], samples[1], replacement]));
            Assert.That(new SampleList(samples, 0), Is.Empty);
            Assert.That(() => prefix[2], Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new SampleList(samples, 0, replacement), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    private static MonitorSample Sample(int seconds) => new(
        DateTimeOffset.UnixEpoch.AddSeconds(seconds),
        seconds,
        [],
        0);
}


