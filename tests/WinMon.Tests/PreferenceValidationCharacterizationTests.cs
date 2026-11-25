using System.Text.Json;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture, SetCulture("en-US"), SetUICulture("en-US")]
public sealed class PreferenceValidationCharacterizationTests
{
    [TestCase(0, true)]
    [TestCase(5, true)]
    [TestCase(10, true)]
    [TestCase(15, true)]
    [TestCase(20, true)]
    [TestCase(50, true)]
    [TestCase(-1, false)]
    [TestCase(7, false)]
    [TestCase(int.MaxValue, false)]
    public void CountChoicesPreserveSupportedValuesAndUseEachFieldsOwnFallback(int count, bool supported)
    {
        var result = PreferenceStore.Validate(new MonitorPreferences
        {
            TopProcessCount = count, TopServiceCount = count, TopUsageCount = count, TopConnectionCount = count
        });
        Assert.That((result.TopProcessCount, result.TopServiceCount, result.TopUsageCount, result.TopConnectionCount),
            Is.EqualTo(supported ? (count, count, count, count) : (5, 20, 20, 10)));
    }

    [Test]
    public void IndependentCountsRetainDifferentSupportedChoices()
    {
        var result = PreferenceStore.Validate(new MonitorPreferences
            { TopProcessCount = 50, TopServiceCount = 0, TopUsageCount = 15, TopConnectionCount = 10 });
        Assert.That((result.TopProcessCount, result.TopServiceCount, result.TopUsageCount, result.TopConnectionCount),
            Is.EqualTo((50, 0, 15, 10)));
    }

    public static IEnumerable<TestCaseData> EnumCases()
    {
        foreach (var (field, type, fallback) in new (string, Type, string)[]
        {
            ("startup", typeof(StartupViewMode), "LastUsed"),
            ("gauge", typeof(GaugeStyle), "Bowl"),
            ("chart", typeof(ResourceChartType), "Overlay"),
            ("process", typeof(ProcessSort), "Cpu")
        })
        {
            foreach (var value in Enum.GetValues(type))
                yield return new TestCaseData(field, Convert.ToInt32(value), value.ToString())
                    .SetName($"DefinedEnum_{field}_{value}");
            yield return new TestCaseData(field, -1, fallback).SetName($"UndefinedEnum_{field}_Negative");
            yield return new TestCaseData(field, int.MaxValue, fallback).SetName($"UndefinedEnum_{field}_Large");
        }
    }

    [TestCaseSource(nameof(EnumCases))]
    public void EnumFieldsPreserveEveryDefinedValueAndUseTheirOwnFallback(string field, int value, string expected)
    {
        var input = new MonitorPreferences();
        input = field switch
        {
            "startup" => input with { StartupView = (StartupViewMode)value },
            "gauge" => input with { GaugeStyle = (GaugeStyle)value },
            "chart" => input with { ResourceChartType = (ResourceChartType)value },
            "process" => input with { ProcessSort = (ProcessSort)value },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var result = PreferenceStore.Validate(input);
        var actual = field switch
        {
            "startup" => result.StartupView.ToString(),
            "gauge" => result.GaugeStyle.ToString(),
            "chart" => result.ResourceChartType.ToString(),
            "process" => result.ProcessSort.ToString(),
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(result.ChartConfigurations.Single(chart => chart.Type == result.ResourceChartType).Type,
            Is.EqualTo(result.ResourceChartType));
    }

    [TestCase(ProcessSort.Cpu, true, ProcessSort.Gpu)]
    [TestCase(ProcessSort.Cpu, false, ProcessSort.Cpu)]
    [TestCase(ProcessSort.Memory, true, ProcessSort.Memory)]
    [TestCase(ProcessSort.Gpu, true, ProcessSort.Gpu)]
    [TestCase((ProcessSort)99, true, ProcessSort.Cpu)]
    [TestCase((ProcessSort)(-1), false, ProcessSort.Cpu)]
    public void LegacyGpuMigrationOnlyOverridesCpuAndAlwaysClearsTheLegacyFlag(
        ProcessSort sort, bool legacy, ProcessSort expected)
    {
        var result = PreferenceStore.Validate(new MonitorPreferences { ProcessSort = sort, SortByGpu = legacy });
        Assert.That(result.ProcessSort, Is.EqualTo(expected));
        Assert.That(result.SortByGpu, Is.False);
    }

    public static IEnumerable<TestCaseData> IndexCases()
    {
        foreach (var (field, maximum) in new[] { ("tab", 3), ("status", 2), ("usage", 4), ("connection", 5) })
            foreach (var (value, expected, boundary) in new[]
            {
                (-1, 0, "Before"), (0, 0, "Minimum"), (1, 1, "Interior"),
                (maximum, maximum, "Maximum"), (maximum + 1, 0, "After"), (int.MaxValue, 0, "Large")
            })
                yield return new TestCaseData(field, value, expected).SetName($"Column_{field}_{boundary}");
    }

    [TestCaseSource(nameof(IndexCases))]
    public void ColumnRangesResetUnsupportedValuesRatherThanClampingThem(string field, int value, int expected)
    {
        var input = new MonitorPreferences
            { SelectedActivityTab = 3, ServiceStatusFilter = 2, UsageSortColumn = 4, ConnectionSortColumn = 5 };
        input = field switch
        {
            "tab" => input with { SelectedActivityTab = value },
            "status" => input with { ServiceStatusFilter = value },
            "usage" => input with { UsageSortColumn = value },
            "connection" => input with { ConnectionSortColumn = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var result = PreferenceStore.Validate(input);
        Assert.That((result.SelectedActivityTab, result.ServiceStatusFilter, result.UsageSortColumn, result.ConnectionSortColumn),
            Is.EqualTo((field == "tab" ? expected : 3, field == "status" ? expected : 2,
                field == "usage" ? expected : 4, field == "connection" ? expected : 5)));
    }

    [TestCase(0, -1, 0, 0)]
    [TestCase(0, 0, 0, 0)]
    [TestCase(0, 1, 0, 1)]
    [TestCase(0, 2, 0, 2)]
    [TestCase(0, 3, 0, 3)]
    [TestCase(0, 4, 0, 0)]
    [TestCase(5, 2, 5, 2)]
    [TestCase(5, 3, 5, 0)]
    [TestCase(20, 3, 20, 0)]
    [TestCase(-1, 2, 20, 2)]
    [TestCase(-1, 3, 20, 0)]
    [TestCase(7, 3, 20, 0)]
    [TestCase(int.MaxValue, int.MaxValue, 20, 0)]
    public void ServiceNameSortingRequiresTheRawAllServicesCount(int count, int column, int expectedCount, int expectedColumn)
    {
        var result = PreferenceStore.Validate(new MonitorPreferences { TopServiceCount = count, ServiceSortColumn = column });
        Assert.That((result.TopServiceCount, result.ServiceSortColumn), Is.EqualTo((expectedCount, expectedColumn)));
    }

    [TestCase(null, 5, null, 0)]
    [TestCase("invalid", 5, null, 0)]
    [TestCase("D:", 5, "D:", 5)]
    [TestCase("D:", 6, "D:", 5)]
    [TestCase("D:", -1, "D:", 0)]
    [TestCase(null, 6, null, 5)]
    public void FocusUsesTheNormalizedRawSecondaryDriveBeforeClamping(
        string? secondary, int focus, string? expectedDrive, int expectedFocus)
    {
        var result = PreferenceStore.Validate(new MonitorPreferences { PrimaryDrive = "C:", SecondaryDrive = secondary, FocusMetric = focus });
        Assert.That((result.SecondaryDrive, result.FocusMetric), Is.EqualTo((expectedDrive, expectedFocus)));
    }

    [Test]
    public void DuplicateDriveRemovalLeavesFocusFiveUntilTheNextValidation()
    {
        var once = PreferenceStore.Validate(new MonitorPreferences { PrimaryDrive = "c:\\", SecondaryDrive = " C:\\ ", FocusMetric = 5 });
        Assert.That((once.PrimaryDrive, once.SecondaryDrive, once.FocusMetric), Is.EqualTo(("C:", (string?)null, 5)));
        var twice = PreferenceStore.Validate(once);
        Assert.That((twice.PrimaryDrive, twice.SecondaryDrive, twice.FocusMetric), Is.EqualTo(("C:", (string?)null, 0)));
    }

    [TestCase(double.NaN, 600, 840)]
    [TestCase(double.PositiveInfinity, 600, 840)]
    [TestCase(double.NegativeInfinity, 600, 840)]
    [TestCase(0, 600, 160)]
    [TestCase(160, 600, 160)]
    [TestCase(600, 600, 600)]
    [TestCase(800, 800, 800)]
    [TestCase(1500, 1500, 1500)]
    [TestCase(2000, 2000, 1500)]
    [TestCase(2001, 2000, 1500)]
    public void WindowDimensionsDistinguishNonFiniteDefaultsFromFiniteClamps(double value, double width, double height)
    {
        var input = new MonitorPreferences { Width = value, Height = value };
        var result = PreferenceStore.Validate(input);
        Assert.That((result.Width, result.Height), Is.EqualTo((width, height)));
        Assert.That(input.Width, Is.EqualTo(value));
        Assert.That(input.Height, Is.EqualTo(value));
    }

    [TestCase(60, 60)]
    [TestCase(120, 120)]
    [TestCase(300, 300)]
    [TestCase(600, 600)]
    [TestCase(0, 120)]
    [TestCase(1800, 120)]
    public void GaugePeakWindowAcceptsOnlyItsFourSavedPeriods(int value, int expected)
    {
        Assert.That(PreferenceStore.Validate(new MonitorPreferences { GaugePeakWindowSeconds = value }).GaugePeakWindowSeconds,
            Is.EqualTo(expected));
    }

    [TestCase(0, 0)]
    [TestCase(60, 60)]
    [TestCase(300, 300)]
    [TestCase(1800, 1800)]
    [TestCase(-1, 300)]
    [TestCase(600, 300)]
    public void ConnectionTrafficWindowPreservesSinceStartAndOnlySupportedPeriods(int value, int expected)
    {
        Assert.That(PreferenceStore.Validate(new MonitorPreferences { ConnectionTrafficSeconds = value }).ConnectionTrafficSeconds,
            Is.EqualTo(expected));
    }

    [TestCase(null, null)]
    [TestCase(-100d, -100d)]
    [TestCase(2000d, 2000d)]
    [TestCase(double.NaN, null)]
    [TestCase(double.PositiveInfinity, null)]
    [TestCase(double.NegativeInfinity, null)]
    public void SavedCoordinatesPreserveEveryFiniteValueAndDropNonFiniteValues(double? value, double? expected)
    {
        var result = PreferenceStore.Validate(new MonitorPreferences { Left = value, Top = value });
        Assert.That((result.Left, result.Top), Is.EqualTo((expected, expected)));
    }

    [Test]
    public void ValidationCopiesPreferencesAndArraysWithoutMutatingTheSuppliedData()
    {
        string[] pins = [" Agent ", "agent", "", null!];
        var input = new MonitorPreferences
        {
            PrimaryDrive = " c:\\ ", SecondaryDrive = "d:", NetworkInterfaceId = " adapter ",
            TopProcessCount = 50, Theme = "light", PinnedServices = pins, AutoCapture = null!,
            ChartConfigurations = null!, CompactMode = true, AlwaysOnTop = true, ProcessActionsHidden = true,
            KeepHistoryAcrossRestarts = true, ProcessNetworkMonitoringEnabled = true
        };
        var result = PreferenceStore.Validate(input);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.SameAs(input));
            Assert.That(input.PrimaryDrive, Is.EqualTo(" c:\\ "));
            Assert.That(input.PinnedServices, Is.SameAs(pins));
            Assert.That(pins, Is.EqualTo(new[] { " Agent ", "agent", "", null }));
            Assert.That(input.AutoCapture, Is.Null);
            Assert.That(input.ChartConfigurations, Is.Null);
            Assert.That(result.PinnedServices, Is.EqualTo(new[] { "Agent" }));
            Assert.That(result.PinnedServices, Is.Not.SameAs(pins));
            Assert.That(result.NetworkInterfaceId, Is.EqualTo("adapter"));
            Assert.That(result.AutoCapture.Enabled, Is.False);
            Assert.That(result.ChartConfigurations.Select(chart => chart.Type), Is.EqualTo(Enum.GetValues<ResourceChartType>()));
            Assert.That((result.CompactMode, result.AlwaysOnTop, result.ProcessActionsHidden,
                result.KeepHistoryAcrossRestarts, result.ProcessNetworkMonitoringEnabled), Is.EqualTo((true, true, true, true, true)));
            Assert.That(JsonSerializer.Serialize(PreferenceStore.Validate(result)), Is.EqualTo(JsonSerializer.Serialize(result)));
        });
    }

    [Test]
    public void NullPreferencesThrowWithTheOriginalParameterName()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => PreferenceStore.Validate(null!));
        Assert.That(exception!.ParamName, Is.EqualTo("preferences"));
    }
}
