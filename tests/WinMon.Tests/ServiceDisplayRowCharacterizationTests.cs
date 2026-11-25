using System.ComponentModel;
using System.Windows;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ServiceDisplayRowCharacterizationTests
{
    private static readonly ServiceObservation Alpha = new("alpha", "Alpha service", 4, 10, 1);
    private static readonly ServiceObservation Beta = new("beta", "Beta service", 1, 10, 1);
    private static readonly ServiceMetrics Readings = new(12.5, 1024, 2048);

    private static ProcessUsage Process(int pid) => ServiceRankingTests.P(pid, 1000, 5, 100, 1024);

    private static ServiceGroup Group(bool shared = false, bool host = true, int processCount = 1) =>
        new("host:10:1000", shared ? [Alpha, Beta] : [Alpha], host ? Process(10) : null,
            Enumerable.Range(10, processCount).Select(Process).ToArray());

    [TestCase("dedicated-host")]
    [TestCase("dedicated-workers")]
    [TestCase("dedicated-workers-disabled")]
    [TestCase("dedicated-empty-process-list")]
    [TestCase("dedicated-hostless")]
    [TestCase("shared-host")]
    [TestCase("shared-hostless")]
    [TestCase("shared-service")]
    [TestCase("dedicated-service")]
    [TestCase("host-process")]
    [TestCase("worker-process")]
    public void RolesKeepIdentityLabelsExpansionAndHostFallbackDistinct(string role)
    {
        var shared = role.StartsWith("shared", StringComparison.Ordinal);
        var header = !role.EndsWith("service", StringComparison.Ordinal) &&
                     !role.EndsWith("process", StringComparison.Ordinal);
        var group = Group(shared, !role.EndsWith("hostless", StringComparison.Ordinal),
            role == "dedicated-empty-process-list" ? 0 : role.Contains("workers", StringComparison.Ordinal) ? 3 : 1);
        var process = role == "host-process" ? group.Host : role == "worker-process" ? Process(20) : null;
        var service = role == "shared-service" ? Beta : role == "dedicated-service" ? Alpha : null;
        var row = new ServiceDisplayRow("retained-key");
        var graph = row.Graph;
        var graphValues = row.GraphValues(2);
        graphValues[0] = 91;
        row.UpdatePin(true, true, true, "retained ranking");

        row.Update(group, process, service, !header, header, true,
            role != "dedicated-workers-disabled", Readings);

        var expectedName = process?.Name ?? (shared && header
            ? Strings.Format(Strings.SharedServiceHost, group.Host?.Name ?? "", 10)
            : service?.DisplayName ?? Alpha.DisplayName);
        var expectedDetail = process is not null ? Strings.Format(Strings.ServiceProcessPid, process.ProcessId)
            : !header ? service!.Name + " · " + Strings.IncludedInHostTotal
            : shared ? Strings.Format(Strings.SharedServiceCount, 2)
            : "alpha · " + (role == "dedicated-workers"
                ? Strings.Format(Strings.ServiceHostWorkers, 2) : Strings.ServiceDedicatedHost);
        var expectedStatus = process is not null || shared && header ? ""
            : service == Beta ? Strings.ServiceStopped : Strings.ServiceRunning;
        Assert.Multiple(() =>
        {
            Assert.That(row.Key, Is.EqualTo("retained-key"));
            Assert.That(row.Group, Is.SameAs(group));
            Assert.That(row.Service, Is.SameAs(service ?? (header && !shared ? Alpha : null)));
            Assert.That(row.Process, Is.SameAs(process ?? group.Host));
            Assert.That(row.Name, Is.EqualTo(expectedName));
            Assert.That(row.Detail, Is.EqualTo(expectedDetail));
            Assert.That(row.Status, Is.EqualTo(expectedStatus));
            Assert.That(row.IsRunning, Is.EqualTo(expectedStatus == Strings.ServiceRunning));
            Assert.That(row.Icon, Is.EqualTo(process is null ? "\uE713" : "\uE8A5"));
            Assert.That(row.ExpandLabel, Is.EqualTo(Strings.Format(Strings.ExpandService, expectedName)));
            Assert.That(row.ExpandGlyph, Is.EqualTo("⌄"));
            Assert.That(row.ExpandVisibility, Is.EqualTo(!header ? Visibility.Collapsed
                : group.Host is not null || shared ? Visibility.Visible : Visibility.Hidden));
            Assert.That(row.Indent, Is.EqualTo(new Thickness(header ? 0 : 30, 4, 8, 4)));
            Assert.That(row.Cpu, Is.EqualTo(Strings.Format(Strings.PercentOneDecimal, 12.5)));
            Assert.That(row.Memory, Is.EqualTo(Strings.Format(Strings.TableKiB, 1d)));
            Assert.That(row.Io, Is.EqualTo(Strings.Format(Strings.TablePerSecond, "2.0 KB")));
            Assert.That((row.CanPin, row.Pinned, row.PinnedHeader, row.RankedLabel),
                Is.EqualTo((true, true, true, "retained ranking")));
            Assert.That(row.PinLabel, Is.EqualTo(shared && service is null
                ? Strings.UnpinServiceHost : Strings.UnpinService));
            Assert.That(row.PinAccessibleName, Is.EqualTo(row.PinLabel + ": " + expectedName));
            Assert.That(row.Graph, Is.SameAs(graph));
            Assert.That(row.GraphValues(2), Is.SameAs(graphValues));
            Assert.That(graphValues, Is.EqualTo(new double?[] { 91, null }));
        });
    }

    [TestCase(0u)]
    [TestCase(1u)]
    [TestCase(2u)]
    [TestCase(3u)]
    [TestCase(4u)]
    [TestCase(5u)]
    [TestCase(6u)]
    [TestCase(7u)]
    [TestCase(8u)]
    [TestCase(uint.MaxValue)]
    public void ServiceStatesKeepOnlyRunningActiveAndUnknownStatesUnavailable(uint state)
    {
        var service = Alpha with { State = state };
        var group = Group() with { Services = new[] { service } };
        var row = new ServiceDisplayRow("state");
        row.Update(group, null, null, false, true, false, false, ServiceMetrics.None);
        string[] known = [Strings.Unavailable, Strings.ServiceStopped, Strings.ServiceStarting,
            Strings.ServiceStopping, Strings.ServiceRunning, Strings.ServiceContinuing,
            Strings.ServicePausing, Strings.ServicePaused];
        Assert.Multiple(() =>
        {
            Assert.That(row.Status, Is.EqualTo(state < known.Length ? known[state] : Strings.Unavailable));
            Assert.That(row.IsRunning, Is.EqualTo(state == 4));
            Assert.That(row.ExpandGlyph, Is.EqualTo("›"));
            Assert.That(row.Cpu, Is.EqualTo(Strings.MissingReading));
            Assert.That(row.Memory, Is.EqualTo(Strings.MissingReading));
            Assert.That(row.Io, Is.EqualTo(Strings.MissingReading));
        });
    }

    [Test]
    public void RepeatingEquivalentValuesUpdatesReferencesWithoutNotifications()
    {
        var group = Group();
        var row = Initialized(group);
        var next = group with { Host = Process(10), Services = new[] { Alpha with { } } };
        var changes = Listen(row);
        row.Update(next, null, null, false, true, false, false, Readings with { });
        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.Empty);
            Assert.That(row.Group, Is.SameAs(next));
            Assert.That(row.Process, Is.SameAs(next.Host));
            Assert.That(row.Service, Is.SameAs(next.Services[0]));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MetricChangesPublishOnlyChangedCellsInOrderIncludingMissingAndZero(bool missing)
    {
        var group = Group();
        var row = Initialized(group);
        var changes = Listen(row);
        row.Update(group, null, null, false, true, false, false,
            missing ? ServiceMetrics.None : new ServiceMetrics(0, 0, 0));
        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.EqualTo(new[] { "Cpu", "Memory", "Io" }));
            Assert.That(row.Cpu, Is.EqualTo(missing ? Strings.MissingReading
                : Strings.Format(Strings.PercentOneDecimal, 0d)));
            Assert.That(row.Memory, Is.EqualTo(missing ? Strings.MissingReading
                : Strings.Format(Strings.TableBytes, 0d)));
            Assert.That(row.Io, Is.EqualTo(missing ? Strings.MissingReading
                : Strings.Format(Strings.TablePerSecond, "0 B")));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    public void IndependentMetricCellsNotifyOnlyTheirOwnChangedValues(int changedMask)
    {
        var group = Group();
        var row = Initialized(group);
        var changes = Listen(row);
        row.Update(group, null, null, false, true, false, false,
            new ServiceMetrics((changedMask & 1) == 0 ? 12.5 : null,
                (changedMask & 2) == 0 ? 1024UL : null, (changedMask & 4) == 0 ? 2048 : null));
        var expected = new List<string>();
        if ((changedMask & 1) != 0) expected.Add("Cpu");
        if ((changedMask & 2) != 0) expected.Add("Memory");
        if ((changedMask & 4) != 0) expected.Add("Io");
        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.EqualTo(expected));
            Assert.That(row.Cpu, Is.EqualTo((changedMask & 1) == 0
                ? Strings.Format(Strings.PercentOneDecimal, 12.5) : Strings.MissingReading));
            Assert.That(row.Memory, Is.EqualTo((changedMask & 2) == 0
                ? Strings.Format(Strings.TableKiB, 1d) : Strings.MissingReading));
            Assert.That(row.Io, Is.EqualTo((changedMask & 4) == 0
                ? Strings.Format(Strings.TablePerSecond, "2.0 KB") : Strings.MissingReading));
        });
    }

    [Test]
    public void InitialAssignmentPublishesIdentityBeforeMetricsAndExpansion()
    {
        var row = new ServiceDisplayRow("new");
        var changes = Listen(row);
        row.Update(Group(), null, null, false, true, false, false, Readings);
        Assert.That(changes, Is.EqualTo(new[] { "Name", "PinLabel", "PinAccessibleName", "Detail", "Status",
            "IsRunning", "Cpu", "Memory", "Io", "Icon", "ExpandGlyph", "ExpandLabel", "Indent" }));
    }

    [Test]
    public void SharedTransitionRefreshesPinLabelsEvenWhenDisplayedNameIsUnchanged()
    {
        var group = Group();
        var process = Process(20);
        var row = new ServiceDisplayRow("process");
        row.Update(group, process, null, true, false, false, false, Readings);
        var changes = Listen(row);
        row.Update(Group(true), process, null, true, false, false, false, Readings);
        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.EqualTo(new[] { "PinLabel", "PinAccessibleName" }));
            Assert.That(row.PinLabel, Is.EqualTo(Strings.PinServiceHost));
        });
    }

    [TestCase("Name")]
    [TestCase("PinLabel")]
    [TestCase("Cpu")]
    [TestCase("Indent")]
    public void ThrowingObserversSeeCompleteNewStateStopPublicationAndRetryWithoutNotifications(string stopAt)
    {
        var row = Initialized(Group());
        var group = Group(true);
        var changes = new List<string?>();
        var failure = new InvalidOperationException("row notification fixture");
        void Observer(object? sender, PropertyChangedEventArgs args)
        {
            changes.Add(args.PropertyName);
            Assert.Multiple(() =>
            {
                Assert.That(sender, Is.SameAs(row));
                Assert.That(row.Group, Is.SameAs(group));
                Assert.That(row.Service, Is.SameAs(Beta));
                Assert.That(row.Process, Is.SameAs(group.Host));
                Assert.That(row.Name, Is.EqualTo(Beta.DisplayName));
                Assert.That(row.Detail, Is.EqualTo("beta · " + Strings.IncludedInHostTotal));
                Assert.That(row.Status, Is.EqualTo(Strings.ServiceStopped));
                Assert.That(row.IsRunning, Is.False);
                Assert.That(row.Cpu, Is.EqualTo(Strings.MissingReading));
                Assert.That(row.Memory, Is.EqualTo(Strings.MissingReading));
                Assert.That(row.Io, Is.EqualTo(Strings.MissingReading));
                Assert.That(row.Icon, Is.EqualTo("\uE713"));
                Assert.That(row.ExpandGlyph, Is.EqualTo("⌄"));
                Assert.That(row.ExpandLabel, Is.EqualTo(Strings.Format(Strings.ExpandService, Beta.DisplayName)));
                Assert.That(row.ExpandVisibility, Is.EqualTo(Visibility.Collapsed));
                Assert.That(row.Indent, Is.EqualTo(new Thickness(30, 4, 8, 4)));
            });
            if (args.PropertyName == stopAt) throw failure;
        }
        row.PropertyChanged += Observer;
        try
        {
            Assert.That(Assert.Throws<InvalidOperationException>(() =>
                row.Update(group, null, Beta, true, false, true, false, ServiceMetrics.None)), Is.SameAs(failure));
        }
        finally { row.PropertyChanged -= Observer; }
        string[] order = ["Name", "PinLabel", "PinAccessibleName", "Detail", "Status", "IsRunning", "Cpu",
            "Memory", "Io", "ExpandGlyph", "ExpandLabel", "ExpandVisibility", "Indent"];
        Assert.That(changes, Is.EqualTo(order.Take(Array.IndexOf(order, stopAt) + 1)));
        var retryChanges = Listen(row);
        row.Update(group, null, Beta, true, false, true, false, ServiceMetrics.None);
        Assert.That(retryChanges, Is.Empty, "Committed values do not replay notifications lost to an observer exception.");
    }

    [Test]
    public void MissingServiceChildFailsAfterIdentityAndExpansionLabelBeforeDetailOrNotifications()
    {
        var row = Initialized(Group());
        var oldDetail = row.Detail;
        var group = Group() with { Services = new[] { Beta } };
        var changes = Listen(row);
        Assert.Throws<NullReferenceException>(() =>
            row.Update(group, null, null, true, false, true, false, ServiceMetrics.None));
        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.Empty);
            Assert.That(row.Group, Is.SameAs(group));
            Assert.That(row.Service, Is.Null);
            Assert.That(row.Process, Is.SameAs(group.Host));
            Assert.That(row.Name, Is.EqualTo(Beta.DisplayName));
            Assert.That(row.ExpandLabel, Is.EqualTo(Strings.Format(Strings.ExpandService, Beta.DisplayName)));
            Assert.That(row.Detail, Is.EqualTo(oldDetail));
            Assert.That(row.Status, Is.EqualTo(Strings.ServiceRunning));
            Assert.That(row.ExpandGlyph, Is.EqualTo("›"));
            Assert.That(row.Indent, Is.EqualTo(new Thickness(0, 4, 8, 4)));
            Assert.That(row.Cpu, Is.EqualTo(Strings.Format(Strings.PercentOneDecimal, 12.5)));
        });
    }

    [Test]
    public void NullMetricsFailsAfterPresentationAssignmentsBeforeAnyMetricOrNotification()
    {
        var row = Initialized(Group());
        var group = Group(true);
        var process = Process(20);
        var changes = Listen(row);
        Assert.Throws<NullReferenceException>(() => row.Update(group, process, null, true, false, true, false, null!));
        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.Empty);
            Assert.That(row.Group, Is.SameAs(group));
            Assert.That(row.Service, Is.Null);
            Assert.That(row.Process, Is.SameAs(process));
            Assert.That(row.Name, Is.EqualTo(process.Name));
            Assert.That(row.Detail, Is.EqualTo(Strings.Format(Strings.ServiceProcessPid, 20)));
            Assert.That(row.Status, Is.Empty);
            Assert.That(row.IsRunning, Is.False);
            Assert.That(row.Icon, Is.EqualTo("\uE8A5"));
            Assert.That(row.ExpandGlyph, Is.EqualTo("⌄"));
            Assert.That(row.ExpandVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(row.Indent, Is.EqualTo(new Thickness(30, 4, 8, 4)));
            Assert.That(row.Cpu, Is.EqualTo(Strings.Format(Strings.PercentOneDecimal, 12.5)));
            Assert.That(row.Memory, Is.EqualTo(Strings.Format(Strings.TableKiB, 1d)));
            Assert.That(row.Io, Is.EqualTo(Strings.Format(Strings.TablePerSecond, "2.0 KB")));
        });
    }

    [Test]
    public void NullGroupFailsAfterReplacingGroupWithoutReplacingOtherStateOrNotifying()
    {
        var group = Group();
        var row = Initialized(group);
        var changes = Listen(row);
        Assert.Throws<NullReferenceException>(() =>
            row.Update(null!, null, null, false, true, false, false, ServiceMetrics.None));
        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.Empty);
            Assert.That(row.Group, Is.Null);
            Assert.That(row.Service, Is.SameAs(Alpha));
            Assert.That(row.Process, Is.SameAs(group.Host));
            Assert.That(row.Name, Is.EqualTo(Alpha.DisplayName));
            Assert.That(row.Cpu, Is.EqualTo(Strings.Format(Strings.PercentOneDecimal, 12.5)));
        });
    }

    [Test]
    public void EmptyDedicatedGroupFailsBeforeReplacingServiceOrOtherDisplayedValues()
    {
        var group = Group();
        var row = Initialized(group);
        var empty = group with { Services = Array.Empty<ServiceObservation>() };
        var changes = Listen(row);
        var failure = Assert.Throws<ArgumentOutOfRangeException>(() =>
            row.Update(empty, null, null, false, true, false, false, ServiceMetrics.None));
        Assert.Multiple(() =>
        {
            Assert.That(failure!.ParamName, Is.EqualTo("index"));
            Assert.That(changes, Is.Empty);
            Assert.That(row.Group, Is.SameAs(empty));
            Assert.That(row.Service, Is.SameAs(Alpha));
            Assert.That(row.Process, Is.SameAs(group.Host));
            Assert.That(row.Name, Is.EqualTo(Alpha.DisplayName));
            Assert.That(row.Status, Is.EqualTo(Strings.ServiceRunning));
        });
    }

    private static ServiceDisplayRow Initialized(ServiceGroup group)
    {
        var row = new ServiceDisplayRow("retained-key");
        row.Update(group, null, null, false, true, false, false, Readings);
        return row;
    }

    private static List<string?> Listen(ServiceDisplayRow row)
    {
        var changes = new List<string?>();
        row.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        return changes;
    }
}
