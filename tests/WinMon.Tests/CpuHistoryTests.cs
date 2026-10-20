using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class CpuHistoryTests
{
    [Test]
    public void Latest_TracksNewestRetainedObservationAcrossTrimming()
    {
        var history = new CpuHistory(2);
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
        var history = new CpuHistory(2);
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
        var history = new CpuHistory(3);
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
        var history = new CpuHistory();
        var first = Sample(1);
        history.Add(first);
        var before = history.Samples;
        history.Add(Sample(2));

        Assert.That(before, Is.EqualTo([first]));
    }

    [Test]
    public void Resize_Zero_Throws()
    {
        var history = new CpuHistory();

        Assert.That(() => history.Resize(0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Add_LongRecordingGap_ExpiresElapsedHistory()
    {
        var history = new CpuHistory(600);
        history.Add(Sample(1));
        var resumed = Sample(900);
        history.Add(resumed);

        Assert.That(history.Samples, Is.EqualTo([resumed]));
    }

    [Test]
    public void Add_WallClockMovesBackward_ExpiresByMonotonicTime()
    {
        var history = new CpuHistory(600);
        history.Add(new CpuSample(DateTimeOffset.UnixEpoch.AddHours(2), 5, [], 0, false, Stopwatch.Frequency));
        var next = new CpuSample(DateTimeOffset.UnixEpoch, 5, [], 0, false, 902 * Stopwatch.Frequency);
        history.Add(next);

        Assert.That(history.Samples, Is.EqualTo([next]));
    }

    [Test]
    public void Add_ProcessEntryBudgetExceeded_EvictsOldestAndReportsMemoryLimit()
    {
        var history = new CpuHistory(600, 3);
        var processes = new[] { new ProcessUsage(1, 100, "app", 5), new ProcessUsage(2, 200, "other", 3) };
        history.Add(new CpuSample(DateTimeOffset.UnixEpoch, 10, processes, 0));
        var next = new CpuSample(DateTimeOffset.UnixEpoch.AddSeconds(1), 10, processes, 0);
        history.Add(next);

        Assert.That(history.Samples, Is.EqualTo([next]));
        Assert.That(history.WasMemoryLimited, Is.True);
    }

    [Test]
    public void Add_ProcessExits_RetainsHistoricalAttribution()
    {
        var history = new CpuHistory();
        var earlier = new CpuSample(DateTimeOffset.UnixEpoch, 50, [new ProcessUsage(22, 100, "exited", 40)], 0);
        history.Add(earlier);
        history.Add(Sample(1));

        Assert.That(history.Samples[0], Is.SameAs(earlier));
        Assert.That(history.Samples[0].Processes[0].Name, Is.EqualTo("exited"));
    }

    private static CpuSample Sample(int seconds) => new(
        DateTimeOffset.UnixEpoch.AddSeconds(seconds),
        seconds,
        [],
        0);
}


