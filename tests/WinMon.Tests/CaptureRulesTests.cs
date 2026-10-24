using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class CaptureRulesTests
{
    private static readonly DateTimeOffset Anchor = DateTimeOffset.Parse("2026-10-24T14:00:00Z");

    [Test]
    public void Observe_FiresExactlyWhenTheDurationCompletes()
    {
        var rules = Rules(Rule(CaptureMetric.Cpu, 90, 3));

        var fired = Feed(rules, 0, 95, 96, 97, 98);

        Assert.Multiple(() =>
        {
            Assert.That(fired.Select(f => f.Second), Is.EqualTo(new[] { 2 }), "The third consecutive sample fires.");
            Assert.That(fired[0].Fired.Run, Is.EqualTo(new[] { 95d, 96, 97 }));
        });
    }

    [Test]
    public void Observe_TheRunsPeakIsItsHighestSample()
    {
        var rules = Rules(Rule(CaptureMetric.Cpu, 90, 3));

        var fired = Feed(rules, 0, 92, 99, 94).Single().Fired;

        Assert.Multiple(() =>
        {
            Assert.That(fired.PeakValue, Is.EqualTo(99));
            Assert.That(fired.Peak.TotalCpuPercent, Is.EqualTo(99));
            Assert.That(fired.Fired.TotalCpuPercent, Is.EqualTo(94));
        });
    }

    [Test]
    public void Observe_AGapOrMissingReadingBreaksTheRun()
    {
        var rules = Rules(Rule(CaptureMetric.Cpu, 90, 3));

        var fired = Feed(rules, 0, 95, 95, null, 95, 95);

        Assert.That(fired, Is.Empty);
    }

    [Test]
    public void Observe_TheCooldownHoldsASecondFiring()
    {
        // Hysteresis is satisfied after one sample below 85, so only the cooldown can hold the second firing.
        var rules = new CaptureRules([Rule(CaptureMetric.Cpu, 90, 1)], TimeSpan.FromSeconds(10), 4);

        var fired = Feed(rules, 0, 95, 50, 95, 50, 95, 50, 95, 50, 95, 50, 95, 50, 95);

        Assert.That(fired.Select(f => f.Second), Is.EqualTo(new[] { 0, 10 }),
            "Re-armed at once, but held until ten seconds after each firing.");
    }

    [Test]
    public void Observe_HysteresisNeedsTheValueBelowTheMarginForTheDuration()
    {
        var rules = Rules(Rule(CaptureMetric.Cpu, 90, 2));

        // 88 is below the threshold but above 90 − 5, so it never re-arms the rule.
        var hovering = Feed(rules, 0, 95, 95, 88, 88, 88, 95, 95);
        var fresh = Rules(Rule(CaptureMetric.Cpu, 90, 2));
        var recovered = Feed(fresh, 0, 95, 95, 80, 95, 80, 80, 95, 95);

        Assert.Multiple(() =>
        {
            Assert.That(hovering.Select(f => f.Second), Is.EqualTo(new[] { 1 }));
            Assert.That(recovered.Select(f => f.Second), Is.EqualTo(new[] { 1, 7 }),
                "One sample below the margin is not enough; two in a row re-arm it.");
        });
    }

    [Test]
    public void Observe_AnyProcessUsesPercentOfOneLogicalProcessor()
    {
        // 6.25% of a 16-processor machine is one full core.
        var rules = new CaptureRules([Rule(CaptureMetric.Process, 90, 2)], TimeSpan.FromMinutes(5), 16);
        CaptureFired? fired = null;
        for (var second = 0; second < 2; second++)
            fired = rules.Observe(Sample(second, 30, [Process(7, 70, "spin", 6.25), Process(8, 80, "idle", 0.5)]))
                .SingleOrDefault() ?? fired;

        Assert.Multiple(() =>
        {
            Assert.That(fired?.Process?.Name, Is.EqualTo("spin"));
            Assert.That(fired?.PeakValue, Is.EqualTo(100));
        });
    }

    [Test]
    public void Observe_AReusedPidStartsAFreshProcessRun()
    {
        var rules = new CaptureRules([Rule(CaptureMetric.Process, 90, 2)], TimeSpan.FromMinutes(5), 1);

        var first = rules.Observe(Sample(0, 30, [Process(7, 70, "old", 95)]));
        var second = rules.Observe(Sample(1, 30, [Process(7, 71, "new", 95)]));
        var third = rules.Observe(Sample(2, 30, [Process(7, 71, "new", 95)]));

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Empty);
            Assert.That(second, Is.Empty, "The new lifetime has only its first sample.");
            Assert.That(third.Single().Process?.CreationTicks, Is.EqualTo(71));
        });
    }

    [Test]
    public void Observe_AProcessGapBreaksItsRun()
    {
        var rules = new CaptureRules([Rule(CaptureMetric.Process, 90, 2)], TimeSpan.Zero, 1);
        var process = Process(7, 70, "spin", 95);

        var beforeGap = rules.Observe(Sample(0, 30, [process]));
        var gap = rules.Observe(new MonitorSample(Anchor.AddSeconds(1), null, [], 0, true,
            Stopwatch.Frequency * 2));
        var afterGap = rules.Observe(Sample(2, 30, [process]));
        var completed = rules.Observe(Sample(3, 30, [process]));

        Assert.Multiple(() =>
        {
            Assert.That(beforeGap, Is.Empty);
            Assert.That(gap, Is.Empty);
            Assert.That(afterGap, Is.Empty, "A gap cannot supply the second consecutive reading.");
            Assert.That(completed.Single().Run, Is.EqualTo(new[] { 95d, 95d }));
        });
    }

    [Test]
    public void Observe_AProcessGapDoesNotRearmAnAlreadyFiredLifetime()
    {
        var rules = new CaptureRules([Rule(CaptureMetric.Process, 90, 2)], TimeSpan.Zero, 1);
        var process = Process(7, 70, "spin", 95);

        rules.Observe(Sample(0, 30, [process]));
        var initial = rules.Observe(Sample(1, 30, [process]));
        rules.Observe(new MonitorSample(Anchor.AddSeconds(2), null, [], 0, true,
            Stopwatch.Frequency * 3));
        var afterGap = rules.Observe(Sample(3, 30, [process]));
        var continued = rules.Observe(Sample(4, 30, [process]));

        Assert.Multiple(() =>
        {
            Assert.That(initial, Has.Count.EqualTo(1));
            Assert.That(afterGap, Is.Empty);
            Assert.That(continued, Is.Empty, "The gap must not make the same lifetime eligible again.");
        });
    }

    [Test]
    public void Observe_AValidSampleWithoutAProcessEndsItsRun()
    {
        var rules = new CaptureRules([Rule(CaptureMetric.Process, 90, 2)], TimeSpan.Zero, 1);
        var process = Process(7, 70, "spin", 95);

        rules.Observe(Sample(0, 30, [process]));
        rules.Observe(Sample(1, 30, []));
        var returned = rules.Observe(Sample(2, 30, [process]));
        var completed = rules.Observe(Sample(3, 30, [process]));

        Assert.Multiple(() =>
        {
            Assert.That(returned, Is.Empty);
            Assert.That(completed.Single().Process, Is.EqualTo(process));
        });
    }

    [Test]
    public void Observe_ResourceRulesCanFireOnTheSameSample()
    {
        var rules = Rules(Rule(CaptureMetric.Cpu, 90, 2), Rule(CaptureMetric.Memory, 80, 2));
        var first = Sample(0, 95, []) with
        {
            Resources = new ResourceSample { TotalMemoryBytes = 100, UsedMemoryBytes = 85 }
        };
        var second = Sample(1, 96, []) with
        {
            Resources = new ResourceSample { TotalMemoryBytes = 100, UsedMemoryBytes = 86 }
        };

        Assert.That(rules.Observe(first), Is.Empty);
        var fired = rules.Observe(second);

        Assert.Multiple(() =>
        {
            Assert.That(fired.Select(result => result.Rule.Metric),
                Is.EqualTo(new[] { CaptureMetric.Cpu, CaptureMetric.Memory }));
            Assert.That(fired.Select(result => result.Run),
                Is.EqualTo(new[] { new[] { 95d, 96d }, new[] { 85d, 86d } }));
        });
    }

    [Test]
    public void Observe_IgnoresDisabledRulesAndAnUnconfiguredSecondDisk()
    {
        var rules = new CaptureRules(
        [
            Rule(CaptureMetric.Cpu, 50, 1) with { Enabled = false },
            Rule(CaptureMetric.SecondaryDisk, 50, 1)
        ], TimeSpan.FromMinutes(5), 1);

        var sample = Sample(0, 99, []) with
        {
            Resources = new ResourceSample { SecondaryDiskActivePercent = 99, SecondaryDiskName = "" }
        };

        Assert.That(rules.Observe(sample), Is.Empty);
    }

    [Test]
    public void Value_NetworkIsTheBusierDirectionInMbps()
    {
        var sample = Sample(0, 10, []) with
        {
            Resources = new ResourceSample { ReceiveBytesPerSecond = 2_500_000, SendBytesPerSecond = 125_000 }
        };

        Assert.That(CaptureRules.Value(CaptureMetric.Network, sample), Is.EqualTo(20));
    }

    [Test]
    public void Normalized_ClampsThresholdsAndDurations()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Rule(CaptureMetric.Cpu, 500, 0).Normalized(), Is.EqualTo(Rule(CaptureMetric.Cpu, 100, 1)));
            Assert.That(Rule(CaptureMetric.Network, double.NaN, 999).Normalized(),
                Is.EqualTo(Rule(CaptureMetric.Network, 50, 300)));
        });
    }

    private static CaptureRules Rules(params CaptureRuleSetting[] rules)
    {
        return new CaptureRules(rules, TimeSpan.Zero, 4);
    }

    private static CaptureRuleSetting Rule(CaptureMetric metric, double threshold, int seconds)
    {
        return new CaptureRuleSetting(metric, true, threshold, seconds);
    }

    private static List<(int Second, CaptureFired Fired)> Feed(CaptureRules rules, int start, params double?[] cpu)
    {
        var fired = new List<(int, CaptureFired)>();
        for (var i = 0; i < cpu.Length; i++)
        {
            var sample = cpu[i] is { } value
                ? Sample(start + i, value, [])
                : new MonitorSample(Anchor.AddSeconds(start + i), null, [], 0, true, Stopwatch.Frequency * (start + i + 1));
            fired.AddRange(rules.Observe(sample).Select(f => (start + i, f)));
        }

        return fired;
    }

    private static MonitorSample Sample(int second, double cpu, IReadOnlyList<ProcessUsage> processes)
    {
        return new MonitorSample(Anchor.AddSeconds(second), cpu, processes, 0, false,
            Stopwatch.Frequency * (second + 1));
    }

    private static ProcessUsage Process(int id, long creation, string name, double cpu)
    {
        return new ProcessUsage(id, creation, name, cpu);
    }
}
