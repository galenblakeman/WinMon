using System.Diagnostics;
using System.IO.Compression;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class ResourceTrendArchiveTests
{
    [Test]
    public void DiskResponseTrendsKeepPerVolumeAveragesAndPeaksInMilliseconds()
    {
        var archive = new ResourceTrendArchive();
        var anchor = DateTimeOffset.UnixEpoch;
        var resource = new ResourceSample { DiskName = "C:", DiskResponseSeconds = .002,
            SecondaryDiskName = "D:", SecondaryDiskResponseSeconds = .020 };
        archive.Add(Sample(1, 0, anchor.AddSeconds(1)) with { Resources = resource });
        archive.Add(Sample(1, 0, anchor.AddSeconds(2)) with { Resources = resource with
            { DiskResponseSeconds = .038, SecondaryDiskResponseSeconds = null } });
        archive.Add(Sample(1, 0, anchor.AddSeconds(61)) with { Resources = resource with { DiskName = "E:" } });

        var buckets = archive.Snapshot().Buckets;
        var primary = buckets[0].Values[ResourceTrendMetric.PrimaryDiskLatency];
        var secondary = buckets[0].Values[ResourceTrendMetric.SecondaryDiskLatency];
        Assert.Multiple(() =>
        {
            Assert.That(primary.Average, Is.EqualTo(20));
            Assert.That(primary.Maximum, Is.EqualTo(38));
            Assert.That(secondary.Average, Is.EqualTo(20));
            Assert.That(secondary.ObservedSeconds, Is.EqualTo(1));
            Assert.That(buckets[1].Values[ResourceTrendMetric.PrimaryDiskLatency].DeviceLabel, Is.EqualTo("E:"));
            Assert.That(buckets[1].Values[ResourceTrendMetric.PrimaryDiskLatency].Break, Is.EqualTo(ResourceTrendBreak.ResourceChanged));
        });
    }

    [TestCase(-0.0001)]
    [TestCase(0.0001)]
    public void Add_JitteredWallClock_KeepsElapsedTimeCoverage(double jitter)
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(1, 10, DateTimeOffset.UnixEpoch.AddSeconds(1), Stopwatch.Frequency));
        archive.Add(Sample(1, 90, DateTimeOffset.UnixEpoch.AddSeconds(2 + jitter), 2 * Stopwatch.Frequency));
        archive.Add(Sample(1, 10, DateTimeOffset.UnixEpoch.AddSeconds(3), 3 * Stopwatch.Frequency));

        var value = archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu];
        Assert.Multiple(() =>
        {
            Assert.That(value.ObservedSeconds, Is.EqualTo(3));
            Assert.That(value.Average, Is.EqualTo(110d / 3).Within(1e-10));
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    public void Add_DuplicateOrBackwardElapsedClock_RejectsTheObservation(int currentSecond)
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(1, 10, DateTimeOffset.UnixEpoch.AddSeconds(1), 2 * Stopwatch.Frequency));
        archive.Add(Sample(1, 90, DateTimeOffset.UnixEpoch.AddSeconds(2), currentSecond * Stopwatch.Frequency));

        var value = archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu];
        Assert.That(value.ObservedSeconds, Is.EqualTo(1));
        Assert.That(value.Average, Is.EqualTo(10));
    }

    [Test]
    public void Add_OverlappingElapsedInterval_RejectsItAndKeepsTheNextValidReading()
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(1, 10, DateTimeOffset.UnixEpoch.AddSeconds(2), 2 * Stopwatch.Frequency));
        archive.Add(Sample(2, 90, DateTimeOffset.UnixEpoch.AddSeconds(4), 3 * Stopwatch.Frequency));
        archive.Add(Sample(1, 20, DateTimeOffset.UnixEpoch.AddSeconds(5), 4 * Stopwatch.Frequency));

        var value = archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu];
        Assert.That(value.ObservedSeconds, Is.EqualTo(2));
        Assert.That(value.Average, Is.EqualTo(15));
    }

    [Test]
    public void Add_JitterAcrossMinuteBoundaries_KeepsEveryMinuteValidForSaving()
    {
        var archive = new ResourceTrendArchive();
        for (var second = 1; second <= 120; second++)
            archive.Add(Sample(1, 42, DateTimeOffset.UnixEpoch.AddSeconds(second + (second % 2 == 0 ? 0.0001 : -0.0001)),
                second * Stopwatch.Frequency));

        var state = archive.Snapshot(DateTimeOffset.UnixEpoch.AddMinutes(3));
        var values = state.Buckets.Select(bucket => bucket.Values[ResourceTrendMetric.Cpu]).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(values.Sum(value => value.ObservedSeconds), Is.EqualTo(120).Within(0.001));
            Assert.That(values.All(value => value.ObservedSeconds <= 60), Is.True);
            Assert.That(values.All(value => Math.Abs(value.Average!.Value - 42) < 1e-10), Is.True);
            Assert.That(() => ResourceTrendArchive.Restore(state), Throws.Nothing);
        });
    }

    [Test]
    public void Add_UnequalIntervals_UsesWeightedAverage()
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(1, 10));
        archive.Add(Sample(3, 50, DateTimeOffset.UnixEpoch.AddSeconds(4)));

        var value = archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu];

        Assert.Multiple(() =>
        {
            Assert.That(value.Average, Is.EqualTo(40));
            Assert.That(value.ObservedSeconds, Is.EqualTo(4));
            Assert.That(value.Minimum, Is.EqualTo(10));
            Assert.That(value.Maximum, Is.EqualTo(50));
            Assert.That(value.PeakUtcTicks, Is.EqualTo(DateTimeOffset.UnixEpoch.AddSeconds(4).UtcTicks));
        });
    }

    [Test]
    public void Add_IntervalCrossingMinute_SplitsWeightedValue()
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(4, 30, DateTimeOffset.UnixEpoch.AddMinutes(1).AddSeconds(2)));

        var buckets = archive.Snapshot().Buckets;

        Assert.Multiple(() =>
        {
            Assert.That(buckets, Has.Length.EqualTo(2));
            Assert.That(buckets[0].Values[ResourceTrendMetric.Cpu].ObservedSeconds, Is.EqualTo(2));
            Assert.That(buckets[1].Values[ResourceTrendMetric.Cpu].ObservedSeconds, Is.EqualTo(2));
            Assert.That(buckets.Select(bucket => bucket.Values[ResourceTrendMetric.Cpu].PeakUtcTicks),
                Is.All.EqualTo(DateTimeOffset.UnixEpoch.AddSeconds(62).UtcTicks));
        });
    }

    [Test]
    public async Task BriefPeakAndItsTimestampSurviveSaveAndRestore()
    {
        var archive = new ResourceTrendArchive();
        for (var second = 1; second <= 59; second++)
            archive.Add(Sample(1, second == 5 ? 98 : 2, DateTimeOffset.UnixEpoch.AddSeconds(second)));
        var path = Path.Combine(Path.GetTempPath(), $"WinMon-trends-{Guid.NewGuid():N}.json.gz");
        try
        {
            await ResourceTrendArchive.SaveAsync(path, archive.Snapshot());
            var restored = await ResourceTrendArchive.LoadAsync(path);
            var value = restored.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu];
            Assert.Multiple(() =>
            {
                Assert.That(value.Average, Is.LessThan(4));
                Assert.That(value.Maximum, Is.EqualTo(98));
                Assert.That(value.PeakUtcTicks, Is.EqualTo(DateTimeOffset.UnixEpoch.AddSeconds(5).UtcTicks));
            });
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void LegacyPartialMinuteKeepsUnknownPeakWhenMoreSamplesArrive()
    {
        var anchor = DateTimeOffset.UnixEpoch;
        var values = new Dictionary<ResourceTrendMetric, ResourceTrendValue>
        {
            [ResourceTrendMetric.Cpu] = new(20, 1, 20, 20, "", ResourceTrendBreak.None)
        };
        var restored = ResourceTrendArchive.Restore(new ResourceTrendArchiveState(2, anchor.AddSeconds(1), [new(anchor, values)]));
        restored.Add(Sample(1, 90, anchor.AddSeconds(2)));
        restored.Add(Sample(1, 60, anchor.AddSeconds(61)));
        var buckets = restored.Snapshot().Buckets;
        Assert.Multiple(() =>
        {
            Assert.That(buckets[0].Values[ResourceTrendMetric.Cpu].Average, Is.EqualTo(55));
            Assert.That(buckets[0].Values[ResourceTrendMetric.Cpu].PeakUtcTicks, Is.Null);
            Assert.That(buckets[1].Values[ResourceTrendMetric.Cpu].PeakUtcTicks, Is.EqualTo(anchor.AddSeconds(61).UtcTicks));
        });
    }

    [Test]
    public void Add_UnavailableMetric_DoesNotSuppressAvailableMetrics()
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(1, 20) with { Resources = new ResourceSample { UsedMemoryBytes = 12_000 } });

        var values = archive.Snapshot().Buckets.Single().Values;

        Assert.Multiple(() =>
        {
            Assert.That(values[ResourceTrendMetric.Cpu].Average, Is.EqualTo(20));
            Assert.That(values[ResourceTrendMetric.MemoryBytes].Average, Is.EqualTo(12_000));
            Assert.That(values.ContainsKey(ResourceTrendMetric.Gpu), Is.False);
        });
    }

    [Test]
    public void Add_ResourceChangesWithinMinute_MarksOnlyThatMetricAsBreak()
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(1, 10) with { Resources = new ResourceSample { DiskName = "C:", DiskActivePercent = 20 } });
        archive.Add(Sample(1, 10, DateTimeOffset.UnixEpoch.AddSeconds(2)) with { Resources = new ResourceSample { DiskName = "D:", DiskActivePercent = 30 } });

        var values = archive.Snapshot().Buckets.Single().Values;

        Assert.Multiple(() =>
        {
            Assert.That(values[ResourceTrendMetric.PrimaryDiskActive].Break, Is.EqualTo(ResourceTrendBreak.ResourceChanged));
            Assert.That(values[ResourceTrendMetric.PrimaryDiskActive].Average, Is.Null);
            Assert.That(values[ResourceTrendMetric.Cpu].Average, Is.EqualTo(10));
        });
    }

    [Test]
    public void Add_ResourceChangesBetweenMinutes_BreaksNewDeviceAndKeepsItsFriendlyLabel()
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(1, 10) with { Resources = new ResourceSample { NetworkId = "ethernet-a", NetworkName = "Ethernet", ReceiveBytesPerSecond = 20 } });
        archive.Add(Sample(1, 10, DateTimeOffset.UnixEpoch.AddMinutes(1).AddSeconds(1)) with
        { Resources = new ResourceSample { NetworkId = "ethernet-b", NetworkName = "Ethernet 2", ReceiveBytesPerSecond = 30 } });

        var value = archive.Snapshot().Buckets.Last().Values[ResourceTrendMetric.Receive];

        Assert.Multiple(() =>
        {
            Assert.That(value.Break, Is.EqualTo(ResourceTrendBreak.ResourceChanged));
            Assert.That(value.DeviceLabel, Is.EqualTo("Ethernet 2"));
            Assert.That(value.DeviceId, Is.EqualTo("ethernet-b"));
        });
    }

    [Test]
    public void BreakContinuity_PreservesTheNextReadingAndMarksItAsDisconnected()
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(1, 10));
        archive.BreakContinuity();
        archive.Add(Sample(1, 20, DateTimeOffset.UnixEpoch.AddSeconds(2)));

        var value = archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu];

        Assert.Multiple(() =>
        {
            Assert.That(value.Break, Is.EqualTo(ResourceTrendBreak.CollectionRestart));
            Assert.That(value.Average, Is.EqualTo(15));
            Assert.That(value.ObservedSeconds, Is.EqualTo(2));
        });
    }

    [Test]
    public void Add_GapOrOverlap_DoesNotInventCoverage()
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(2, 10, DateTimeOffset.UnixEpoch.AddSeconds(2)));
        archive.Add(Sample(2, 30, DateTimeOffset.UnixEpoch.AddSeconds(3)));
        archive.Add(Sample(2, 10, DateTimeOffset.UnixEpoch.AddSeconds(5)) with { IsGap = true });

        var value = archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu];

        Assert.Multiple(() =>
        {
            Assert.That(value.Break, Is.EqualTo(ResourceTrendBreak.None));
            Assert.That(value.ObservedSeconds, Is.EqualTo(2));
        });
    }

    [Test]
    public void Add_RepeatedOverlaps_AreIgnoredWithoutPoisoningLaterCoverage()
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(1, 10));
        archive.Add(Sample(1, 30));
        archive.Add(Sample(1, 30));
        archive.Add(Sample(1, 20, DateTimeOffset.UnixEpoch.AddSeconds(2)));

        var value = archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu];

        Assert.Multiple(() =>
        {
            Assert.That(value.Average, Is.EqualTo(15));
            Assert.That(value.ObservedSeconds, Is.EqualTo(2));
        });
    }

    [Test]
    public void Add_BackwardWallClockWithNewerMonotonicTime_MarksTheOldSegmentWithoutRejectingIt()
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(1, 10, DateTimeOffset.UnixEpoch.AddMinutes(2), Stopwatch.Frequency));
        archive.Add(Sample(1, 20, DateTimeOffset.UnixEpoch.AddSeconds(1), 2 * Stopwatch.Frequency));
        archive.Add(Sample(1, 30, DateTimeOffset.UnixEpoch.AddSeconds(2), 3 * Stopwatch.Frequency));

        var buckets = archive.Snapshot(DateTimeOffset.UnixEpoch.AddMinutes(2)).Buckets;

        Assert.That(buckets.Single(bucket => bucket.StartUtc == DateTimeOffset.UnixEpoch).Values[ResourceTrendMetric.Cpu].Break,
            Is.EqualTo(ResourceTrendBreak.ClockChanged));
    }

    [Test]
    public void Add_ExceedsSevenDays_PrunesOldBuckets()
    {
        var archive = new ResourceTrendArchive();
        for (var minute = 0; minute <= ResourceTrendArchive.RetainedMinutes; minute++)
            archive.Add(Sample(1, 10, DateTimeOffset.UnixEpoch.AddMinutes(minute + 1)));

        var buckets = archive.Snapshot().Buckets;

        Assert.Multiple(() =>
        {
            Assert.That(buckets.Length, Is.LessThanOrEqualTo(ResourceTrendArchive.RetainedMinutes));
            Assert.That(buckets[0].StartUtc, Is.GreaterThanOrEqualTo(DateTimeOffset.UnixEpoch.AddMinutes(2)));
        });
    }

    [Test]
    public async Task SaveAndLoad_RoundTripsAndRejectsCorruptReplacement()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var archive = new ResourceTrendArchive();
            archive.Add(Sample(1, 42));
            await ResourceTrendArchive.SaveAsync(path, archive.Snapshot());
            var saved = await File.ReadAllBytesAsync(path);

            var restored = await ResourceTrendArchive.LoadAsync(path);
            Assert.That(restored.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu].Average, Is.EqualTo(42));

            Assert.That(() => ResourceTrendArchive.SaveAsync(path,
                new ResourceTrendArchiveState(4, DateTimeOffset.UtcNow, [])).GetAwaiter().GetResult(), Throws.TypeOf<InvalidDataException>());
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(saved));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public async Task LoadAndSave_InvalidOrCancelledFiles_PreserveExistingArchive()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var archive = new ResourceTrendArchive();
            archive.Add(Sample(1, 42));
            await ResourceTrendArchive.SaveAsync(path, archive.Snapshot());
            var original = await File.ReadAllBytesAsync(path);

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.That(async () => await ResourceTrendArchive.SaveAsync(path, archive.Snapshot(), cancelled.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));

            await using (var stream = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None)) stream.SetLength(ResourceTrendArchive.MaximumFileBytes + 1);
            Assert.ThrowsAsync<InvalidDataException>(async () => await ResourceTrendArchive.LoadAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void Restore_RejectsUnknownMetricAndImpossibleDuration()
    {
        var value = new ResourceTrendValue(10, 61, 10, 10, "", ResourceTrendBreak.None);
        var state = new ResourceTrendArchiveState(1, DateTimeOffset.UtcNow, [new ResourceTrendBucket(DateTimeOffset.UnixEpoch,
            new Dictionary<ResourceTrendMetric, ResourceTrendValue> { [(ResourceTrendMetric)99] = value })]);

        Assert.That(() => ResourceTrendArchive.Restore(state), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase("null-value")]
    [TestCase("device")]
    [TestCase("device-id")]
    [TestCase("device-label")]
    [TestCase("long-device")]
    [TestCase("long-device-id")]
    [TestCase("long-device-label")]
    [TestCase("nan-sum")]
    [TestCase("infinite-duration")]
    [TestCase("nan-minimum")]
    [TestCase("nan-maximum")]
    [TestCase("zero-duration")]
    [TestCase("long-duration")]
    [TestCase("negative-sum")]
    [TestCase("negative-minimum")]
    [TestCase("inverted-range")]
    [TestCase("excessive-sum")]
    [TestCase("excessive-maximum")]
    [TestCase("sum-below-range")]
    [TestCase("sum-above-range")]
    [TestCase("unknown-break")]
    public async Task Save_InvalidValuePreservesArchiveAndError(string kind)
    {
        var value = new ResourceTrendValue(10, 1, 10, 10, "device", ResourceTrendBreak.None);
        var invalid = kind switch
        {
            "null-value" => null!,
            "device" => value with { Device = null! },
            "device-id" => value with { DeviceId = null! },
            "device-label" => value with { DeviceLabel = null! },
            "long-device" => value with { Device = new string('x', 257) },
            "long-device-id" => value with { DeviceId = new string('x', 257) },
            "long-device-label" => value with { DeviceLabel = new string('x', 257) },
            "nan-sum" => value with { WeightedSum = double.NaN },
            "infinite-duration" => value with { ObservedSeconds = double.PositiveInfinity },
            "nan-minimum" => value with { Minimum = double.NaN },
            "nan-maximum" => value with { Maximum = double.NaN },
            "zero-duration" => value with { ObservedSeconds = 0 },
            "long-duration" => value with { ObservedSeconds = 60.001 },
            "negative-sum" => value with { WeightedSum = -1 },
            "negative-minimum" => value with { Minimum = -1 },
            "inverted-range" => value with { Maximum = 9 },
            "excessive-sum" => value with { WeightedSum = 6e21 + 1e10 },
            "excessive-maximum" => value with { Maximum = 1e21 },
            "sum-below-range" => value with { WeightedSum = 9.998 },
            "sum-above-range" => value with { WeightedSum = 10.002 },
            "unknown-break" => value with { Break = (ResourceTrendBreak)999 },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var bucket = new ResourceTrendBucket(DateTimeOffset.UnixEpoch,
            new Dictionary<ResourceTrendMetric, ResourceTrendValue> { [ResourceTrendMetric.Receive] = value });
        var state = new ResourceTrendArchiveState(1, DateTimeOffset.UnixEpoch.AddMinutes(1), [bucket]);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json.gz");
        try
        {
            await ResourceTrendArchive.SaveAsync(path, state);
            var original = await File.ReadAllBytesAsync(path);
            var corrupted = state with { Buckets = [bucket with { Values =
                new Dictionary<ResourceTrendMetric, ResourceTrendValue> { [ResourceTrendMetric.Receive] = invalid } }] };
            Assert.That(() => ResourceTrendArchive.Restore(corrupted),
                Throws.TypeOf<InvalidDataException>().With.Message.EqualTo("Invalid resource trend value."));
            Assert.That(async () => await ResourceTrendArchive.SaveAsync(path, corrupted),
                Throws.TypeOf<InvalidDataException>().With.Message.EqualTo("Invalid resource trend value."));
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
        }
        finally { File.Delete(path); }
    }

    [TestCase(ResourceTrendMetric.Cpu)]
    [TestCase(ResourceTrendMetric.MemoryPercent)]
    [TestCase(ResourceTrendMetric.PrimaryDiskActive)]
    [TestCase(ResourceTrendMetric.SecondaryDiskActive)]
    [TestCase(ResourceTrendMetric.Gpu)]
    public void Restore_PercentageMaximumIsInclusive(ResourceTrendMetric metric)
    {
        var value = new ResourceTrendValue(6000, 60, 100, 100, "device", ResourceTrendBreak.None);
        var state = new ResourceTrendArchiveState(1, DateTimeOffset.UnixEpoch.AddMinutes(1),
            [new(DateTimeOffset.UnixEpoch, new Dictionary<ResourceTrendMetric, ResourceTrendValue> { [metric] = value })]);
        Assert.That(ResourceTrendArchive.Restore(state).Snapshot().Buckets.Single().Values[metric].Average, Is.EqualTo(100));
        var above = value with { Maximum = double.BitIncrement(100) };
        Assert.That(() => ResourceTrendArchive.Restore(state with { Buckets =
            [new(DateTimeOffset.UnixEpoch, new Dictionary<ResourceTrendMetric, ResourceTrendValue> { [metric] = above })] }),
            Throws.TypeOf<InvalidDataException>().With.Message.EqualTo("Invalid resource trend value."));
    }

    [TestCase("non-utc")]
    [TestCase("unaligned")]
    [TestCase("duplicate")]
    [TestCase("backward")]
    [TestCase("null-values")]
    [TestCase("too-many-values")]
    public void Restore_RejectsMalformedOrUnorderedBuckets(string kind)
    {
        var value = new ResourceTrendValue(10, 1, 10, 10, "", ResourceTrendBreak.None);
        var bucket = new ResourceTrendBucket(DateTimeOffset.UnixEpoch,
            new Dictionary<ResourceTrendMetric, ResourceTrendValue> { [ResourceTrendMetric.Cpu] = value });
        ResourceTrendBucket[] buckets = kind switch
        {
            "non-utc" => [bucket with { StartUtc = bucket.StartUtc.ToOffset(TimeSpan.FromHours(1)) }],
            "unaligned" => [bucket with { StartUtc = bucket.StartUtc.AddSeconds(1) }],
            "duplicate" => [bucket, bucket],
            "backward" => [bucket, bucket with { StartUtc = bucket.StartUtc.AddMinutes(-1) }],
            "null-values" => [bucket with { Values = null! }],
            "too-many-values" => [bucket with { Values = Enumerable.Range(0, Enum.GetValues<ResourceTrendMetric>().Length + 1)
                .ToDictionary(i => (ResourceTrendMetric)i, _ => value) }],
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        Assert.That(() => ResourceTrendArchive.Restore(new(1, DateTimeOffset.UnixEpoch.AddMinutes(1), buckets)),
            Throws.TypeOf<InvalidDataException>().With.Message.EqualTo("Invalid resource trend bucket."));
    }

    [Test]
    public async Task Load_RejectsTruncatedAndOverexpandedCompressedFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json.gz");
        try
        {
            await File.WriteAllBytesAsync(path, [0x1f, 0x8b, 0x08]);
            Assert.ThrowsAsync<InvalidDataException>(async () => await ResourceTrendArchive.LoadAsync(path));

            await using (var output = File.Create(path))
            await using (var compressed = new GZipStream(output, CompressionLevel.SmallestSize))
            {
                var block = new byte[8192];
                for (var remaining = ResourceTrendArchive.MaximumDecodedBytes + 1; remaining > 0;)
                {
                    var count = (int)Math.Min(block.Length, remaining);
                    await compressed.WriteAsync(block.AsMemory(0, count));
                    remaining -= count;
                }
            }
            Assert.ThrowsAsync<InvalidDataException>(async () => await ResourceTrendArchive.LoadAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void Restore_DeviceChangesInSameMinute_RemainsUnavailableAfterRestartMarker()
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(1, 10) with { Resources = new ResourceSample { NetworkId = "old", NetworkName = "Ethernet", ReceiveBytesPerSecond = 100 } });
        var restored = ResourceTrendArchive.Restore(archive.Snapshot());
        restored.Add(Sample(1, 20, DateTimeOffset.UnixEpoch.AddSeconds(3)) with
        { Resources = new ResourceSample { NetworkId = "new", NetworkName = "Ethernet", ReceiveBytesPerSecond = 200 } });
        var bucket = restored.Snapshot().Buckets.Single();
        Assert.Multiple(() =>
        {
            Assert.That(bucket.Values[ResourceTrendMetric.Receive].Break, Is.EqualTo(ResourceTrendBreak.ResourceChanged));
            Assert.That(bucket.Values[ResourceTrendMetric.Receive].Average, Is.Null);
            Assert.That(bucket.Values[ResourceTrendMetric.Cpu].Average, Is.EqualTo(15));
        });
    }

    private static MonitorSample Sample(double seconds, double cpu, DateTimeOffset? timestamp = null, long monotonic = 0) =>
        new(timestamp ?? DateTimeOffset.UnixEpoch.AddSeconds(seconds), cpu, [], 0, false, monotonic) { IntervalSeconds = seconds };
}
