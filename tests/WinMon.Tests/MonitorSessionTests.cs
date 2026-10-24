using System.Collections.Concurrent;
using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class MonitorSessionTests
{
    private const int Processes = 60;

    [Test]
    public async Task Drain_RecordsQueuedCapturesInOrderWithOneCallback()
    {
        var harness = new Harness(Frame(1), Frame(2), Frame(3));

        await harness.RunToEnd();

        Assert.That(harness.Posted, Has.Count.EqualTo(1), "Captures queued behind one pending drain.");
        harness.Drain();
        var session = harness.Session;
        Assert.Multiple(() =>
        {
            Assert.That(session.History.Samples.Select(s => s.Timestamp.ToUnixTimeSeconds()), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(harness.RecordedCount, Is.EqualTo(1));
            Assert.That(session.Latest, Is.SameAs(session.History.Latest));
            Assert.That(session.LatestFull!.Processes, Has.Count.EqualTo(Processes), "Live ranking uses the full sample.");
            Assert.That(session.Latest!.Processes, Has.Count.LessThan(Processes), "History keeps the compacted sample.");
            Assert.That(session.Services.Frames, Has.Count.EqualTo(3));
            Assert.That(session.Error, Is.Null);
            Assert.That(harness.Source.Disposed, Is.True);
        });
    }

    [Test]
    public async Task FailedCapture_ResetsSourceAndReportsReadingsUnavailable()
    {
        var harness = new Harness(Frame(1), _ => throw new InvalidOperationException("Native read failed."));

        await harness.RunToEnd();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.Error, Is.EqualTo(SamplingError.ReadingsUnavailable));
            Assert.That(harness.Source.Resets, Is.EqualTo(1));
            Assert.That(harness.Session.History.Samples, Has.Count.EqualTo(1));
            Assert.That(harness.RecordedCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SuccessfulCapture_ClearsAnEarlierFailure()
    {
        var harness = new Harness(_ => throw new InvalidOperationException("Native read failed."), Frame(2));

        await harness.RunToEnd();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.Error, Is.Null);
            Assert.That(harness.Session.History.Samples, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task PartialFailure_RecordsTheSampleWithItsError()
    {
        var harness = new Harness(options => Frame(1)(options) with { Error = SamplingError.CpuUnavailable });

        await harness.RunToEnd();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.Error, Is.EqualTo(SamplingError.CpuUnavailable));
            Assert.That(harness.Session.History.Samples, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Close_DropsQueuedCapturesWithoutRaisingRecorded()
    {
        var harness = new Harness(Frame(1), Frame(2));

        await harness.RunToEnd();
        harness.Session.Close();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.History.Samples, Is.Empty);
            Assert.That(harness.Session.Latest, Is.Null);
            Assert.That(harness.RecordedCount, Is.Zero);
        });
    }

    [Test]
    public async Task Options_AreReadForEachCaptureAndApplied()
    {
        var options = new SamplingOptions("C:", "D:", process => process.ProcessId == 0);
        var harness = new Harness(options, Frame(1), Frame(2));

        await harness.RunToEnd();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(harness.OptionReads, Is.EqualTo(3), "Two captures and the read that ended the run.");
            Assert.That(harness.Source.Options, Has.All.SameAs(options));
            Assert.That(harness.Session.Latest!.Processes.Select(p => p.ProcessId), Does.Contain(0));
            Assert.That(harness.Session.Latest.Processes.Select(p => p.ProcessId), Does.Not.Contain(1));
        });
    }

    [Test]
    public async Task RetainAll_KeepsEveryProcessInHistory()
    {
        var harness = new Harness(new SamplingOptions(RetainAll: true), Frame(1));

        await harness.RunToEnd();
        harness.Drain();

        Assert.That(harness.Session.Latest!.Processes, Has.Count.EqualTo(Processes));
    }

    [Test]
    public void Record_AddsAFrameWithoutRaisingRecorded()
    {
        var harness = new Harness();
        var full = Frame(1)(new SamplingOptions()).Full;
        var compacted = ProcessRetention.Compact(full);

        harness.Session.Record(compacted, full, new ServiceFrame(full, []));

        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.Latest, Is.SameAs(compacted));
            Assert.That(harness.Session.LatestFull, Is.SameAs(full));
            Assert.That(harness.Session.History.Samples, Has.Count.EqualTo(1));
            Assert.That(harness.Session.Services.Frames, Has.Count.EqualTo(1));
            Assert.That(harness.RecordedCount, Is.Zero);
            Assert.That(harness.Posted, Is.Empty);
        });
    }

    [Test]
    public async Task HasLiveSample_IsSetByADrainButNotByARestoredSample()
    {
        var harness = new Harness(Frame(2));
        harness.Session.Record(ProcessRetention.Compact(Frame(1)(new SamplingOptions()).Full));
        var restoredOnly = harness.Session.HasLiveSample;

        await harness.RunToEnd();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(restoredOnly, Is.False, "A restored sample sets Latest but cannot prove a new version healthy.");
            Assert.That(harness.Session.HasLiveSample, Is.True);
        });
    }

    [Test]
    public async Task HasLiveSample_StaysFalseWhenTheFirstCaptureFails()
    {
        var harness = new Harness(_ => throw new InvalidOperationException("Native read failed."));
        harness.Session.Record(ProcessRetention.Compact(Frame(1)(new SamplingOptions()).Full));

        await harness.RunToEnd();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.Latest, Is.Not.Null);
            Assert.That(harness.Session.HasLiveSample, Is.False);
        });
    }

    [Test]
    public void Record_FeedsUsageOnlyFromAFullSample()
    {
        var harness = new Harness();
        var first = Frame(1)(new SamplingOptions()).Full;
        var second = Frame(2)(new SamplingOptions()).Full;

        harness.Session.Record(ProcessRetention.Compact(first));
        harness.Session.Record(ProcessRetention.Compact(second), second);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.History.Samples, Has.Count.EqualTo(2));
            Assert.That(harness.Session.Usage.Apps.Sum(app => app.Lifetimes), Is.EqualTo(Processes),
                "A compacted-only frame, as restored after an update, is not counted.");
            Assert.That(harness.Session.Usage.Apps.Sum(app => app.Running), Is.EqualTo(Processes));
        });
    }

    // Working sets rise with the PID, so compaction drops the ten smallest idle processes.
    private static Func<SamplingOptions, CapturedFrame> Frame(int second)
    {
        return _ =>
        {
            var processes = Enumerable.Range(0, Processes)
                .Select(i => new ProcessUsage(i, i + 1, $"fixture-{i}", 0, (ulong)(i + 1) * 1000))
                .ToArray();
            var sample = new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(second), 10, processes, 0, false,
                Stopwatch.Frequency * second);
            return new CapturedFrame(sample, new ServiceFrame(sample, []));
        };
    }

    private sealed class Harness
    {
        private readonly CancellationTokenSource _cancellation = new();
        private int _optionReads;

        internal Harness(params Func<SamplingOptions, CapturedFrame>[] script) : this(new SamplingOptions(), script)
        {
        }

        internal Harness(SamplingOptions options, params Func<SamplingOptions, CapturedFrame>[] script)
        {
            Source = new FakeSource(script, _cancellation);
            Session = new MonitorSession(() => Source, () =>
            {
                Interlocked.Increment(ref _optionReads);
                return options;
            }, Posted.Enqueue, TimeSpan.FromMilliseconds(1));
            Session.Recorded += () => RecordedCount++;
        }

        internal MonitorSession Session { get; }
        internal FakeSource Source { get; }
        internal ConcurrentQueue<Action> Posted { get; } = new();
        internal int RecordedCount { get; private set; }
        internal int OptionReads => Volatile.Read(ref _optionReads);

        internal async Task RunToEnd()
        {
            await Session.Run(_cancellation.Token).WaitAsync(TimeSpan.FromSeconds(10));
        }

        internal void Drain()
        {
            while (Posted.TryDequeue(out var action)) action();
        }
    }

    // Plays its script, one entry per capture, then ends the run the way cancellation does.
    private sealed class FakeSource(Func<SamplingOptions, CapturedFrame>[] script, CancellationTokenSource end)
        : IFrameSource
    {
        private int _next;

        internal List<SamplingOptions> Options { get; } = [];
        internal int Resets { get; private set; }
        internal bool Disposed { get; private set; }

        public CapturedFrame Capture(SamplingOptions options)
        {
            if (_next == script.Length)
            {
                end.Cancel();
                end.Token.ThrowIfCancellationRequested();
            }

            Options.Add(options);
            return script[_next++](options);
        }

        public void Reset()
        {
            Resets++;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
