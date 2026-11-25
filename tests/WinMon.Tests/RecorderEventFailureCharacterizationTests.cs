using System.IO;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class RecorderEventFailureCharacterizationTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch.AddDays(1);

    [TestCase("sync-failure", IncidentEventCoverageStatus.Unreadable, false)]
    [TestCase("faulted-task", IncidentEventCoverageStatus.Unreadable, false)]
    [TestCase("sync-timeout", IncidentEventCoverageStatus.TimedOut, true)]
    [TestCase("faulted-timeout", IncidentEventCoverageStatus.TimedOut, true)]
    [TestCase("foreign-cancellation", IncidentEventCoverageStatus.Unreadable, false)]
    [TestCase("null-task", IncidentEventCoverageStatus.Unreadable, false)]
    public void ImmediateReaderFailuresPublishPerLogCoverageAndKeepTheDetailedSelection(
        string mode, IncidentEventCoverageStatus expected, bool canceled)
    {
        var calls = 0;
        CancellationToken captured = default;
        var samples = Samples(0, 10);
        var window = new FlightRecorderWindow(samples, eventReader: (start, end, token) =>
        {
            calls++;
            Assert.That(start, Is.EqualTo(Start));
            Assert.That(end, Is.EqualTo(Start.AddSeconds(10)));
            captured = token;
            return mode switch
            {
                "sync-failure" => throw new InvalidOperationException("owned reader failure"),
                "faulted-task" => Task.FromException<IncidentEventResult>(new IOException("owned task failure")),
                "sync-timeout" => throw new TimeoutException("owned reader timeout"),
                "faulted-timeout" => Task.FromException<IncidentEventResult>(new TimeoutException("owned task timeout")),
                "foreign-cancellation" => Task.FromCanceled<IncidentEventResult>(new CancellationToken(true)),
                "null-task" => null!,
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
        });
        try
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(captured.CanBeCanceled, Is.True);
            Assert.That(captured.IsCancellationRequested, Is.EqualTo(canceled));
            Assert.That(window.EventReadPending, Is.False);
            var recording = window.Recording();
            Assert.That(recording.WindowsEvents.Events, Is.Empty);
            Assert.That(recording.WindowsEvents.Coverage.Select(item => item.Log), Is.EqualTo(new[] { "Application", "System" }));
            Assert.That(recording.WindowsEvents.Coverage.Select(item => item.Status), Is.EqualTo(new[] { expected, expected }));
            Assert.That(recording.WindowsEvents.LimitReached, Is.False);
            Assert.That(window.EventMappings, Is.Empty);
            Assert.That(window.Selection, Is.SameAs(samples[^1]));
            var status = expected == IncidentEventCoverageStatus.TimedOut
                ? Strings.FlightRecorderEventTimedOut : Strings.FlightRecorderEventUnreadable;
            Assert.That(window.EventCoverageTextValue, Is.EqualTo(string.Join(Environment.NewLine,
                Strings.Format(Strings.FlightRecorderEventLogCoverage, "Application", status),
                Strings.Format(Strings.FlightRecorderEventLogCoverage, "System", status))));
            Assert.That(window.IsVisible, Is.False);
        }
        finally { window.Close(); }
    }

    [TestCase("timeout-cancellation-refresh")]
    [TestCase("failure-after-refresh")]
    public void ReentrantRefreshKeepsTheNewGenerationWhenTheOldReaderFails(string mode)
    {
        var calls = 0;
        var currentSamples = Samples(20, 30);
        var fresh = new IncidentEventResult(
            [new IncidentEvent(Start.AddSeconds(20), "fresh generation", 7, IncidentEventLevel.Information, "fresh message")],
            [new("Application", IncidentEventCoverageStatus.Complete), new("System", IncidentEventCoverageStatus.Complete)], false);
        var registrations = new List<CancellationTokenRegistration>();
        var tokens = new List<CancellationToken>();
        FlightRecorderWindow? window = null;
        window = new FlightRecorderWindow([], eventReader: (_, _, token) =>
        {
            calls++;
            tokens.Add(token);
            if (calls != 1) return Task.FromResult(fresh);
            if (mode == "timeout-cancellation-refresh")
            {
                registrations.Add(token.Register(() => window!.Update(currentSamples)));
                return Task.FromException<IncidentEventResult>(new TimeoutException("old timeout"));
            }
            window!.Update(currentSamples);
            throw new IOException("old failure after new result");
        });
        try
        {
            window.Update(Samples(0, 10));
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(tokens[0].IsCancellationRequested, Is.True);
            Assert.That(tokens[1].IsCancellationRequested, Is.False);
            Assert.That(window.EventReadPending, Is.False);
            Assert.That(window.Recording().WindowsEvents, Is.SameAs(fresh));
            Assert.That(window.EventMappings.Single().Event.Provider, Is.EqualTo("fresh generation"));
            Assert.That(window.EventCoverageTextValue, Does.Contain(Strings.FlightRecorderEventComplete));
            Assert.That(window.EventCoverageTextValue, Does.Not.Contain(Strings.FlightRecorderEventTimedOut));
            Assert.That(window.EventCoverageTextValue, Does.Not.Contain(Strings.FlightRecorderEventUnreadable));
            Assert.That(currentSamples, Does.Contain(window.Selection));
            Assert.That(window.IsVisible, Is.False);
        }
        finally
        {
            foreach (var registration in registrations) registration.Dispose();
            window.Close();
        }
    }

    private static MonitorSample[] Samples(params int[] seconds) => seconds.Select(second =>
        new MonitorSample(Start.AddSeconds(second), second + 10, [], 0)).ToArray();
}
