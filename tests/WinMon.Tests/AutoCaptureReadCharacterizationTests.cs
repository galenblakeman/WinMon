using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, Apartment(ApartmentState.STA), NonParallelizable]
[SetCulture("en-US"), SetUICulture("en-US")]
public sealed class AutoCaptureReadCharacterizationTests
{
    private const string DefaultFolder = @"C:\WinMon-test\Reports";

    [TestCase(CaptureMetric.CpuPerformanceLimited)]
    [TestCase(CaptureMetric.GpuThermalLimited)]
    public void LimitRulesHaveFixedActiveStateAndEditableDuration(CaptureMetric metric)
    {
        var section = new AutoCaptureSection(new AutoCapturePreferences { Enabled = true, Rules = [new(metric, true, 1, 10)] },
            DefaultFolder, "C:", "D:");
        Assert.That(Field(section, 1, 1).Visibility, Is.EqualTo(System.Windows.Visibility.Collapsed));
        Assert.That(Field(section, 1, 1).IsEnabled, Is.False);
        Field(section, 1, 3).Text = "20";
        var rule = ReadValid(section).Rules.Single();
        Assert.That(rule.Metric, Is.EqualTo(metric));
        Assert.That(rule.Threshold, Is.EqualTo(1));
        Assert.That(rule.Seconds, Is.EqualTo(20));
    }

    public static IEnumerable<TestCaseData> ThresholdCases()
    {
        foreach (var metric in Enum.GetValues<CaptureMetric>())
        {
            var network = metric is CaptureMetric.Network or CaptureMetric.ApplicationNetwork or CaptureMetric.ServiceNetwork;
            var maximum = metric switch
            {
                CaptureMetric.Network or CaptureMetric.ApplicationNetwork or CaptureMetric.ServiceNetwork => 12500d,
                CaptureMetric.Process or CaptureMetric.ApplicationCpu or CaptureMetric.ServiceCpu => 25600d,
                CaptureMetric.ApplicationMemory or CaptureMetric.ServiceMemory => 1048576d,
                CaptureMetric.CpuTemperature or CaptureMetric.GpuTemperature or CaptureMetric.PrimaryDiskTemperature or CaptureMetric.SecondaryDiskTemperature => 150d,
                CaptureMetric.CpuPerformanceLimited or CaptureMetric.GpuThermalLimited => 1d,
                _ => 100d
            };
            var minimum = network ? .0125 : 1;
            yield return Case("Below", minimum - .001, false, 0);
            yield return Case("Minimum", minimum, true, network ? .1 : 1);
            yield return Case("Maximum", maximum, true, network ? 100000 : maximum);
            yield return Case("Above", maximum + .001, false, 0);

            TestCaseData Case(string boundary, double input, bool accepted, double stored) =>
                new TestCaseData(metric, input, accepted, stored, minimum, maximum)
                    .SetName($"Threshold_{metric}_{boundary}");
        }
    }

    [TestCaseSource(nameof(ThresholdCases))]
    public void ThresholdsUseMetricSpecificInclusiveBoundsAndNetworkUnits(CaptureMetric metric, double input,
        bool accepted, double stored, double minimum, double maximum)
    {
        var section = Section(metric);
        var threshold = Field(section, 1, 1);
        threshold.Text = input.ToString("G17", CultureInfo.CurrentCulture);

        var result = section.Read(out var error, out var invalid);

        if (accepted)
        {
            Assert.Multiple(() =>
            {
                Assert.That(error, Is.Null);
                Assert.That(invalid, Is.Null);
                Assert.That(result!.Rules.Single().Threshold, Is.EqualTo(stored));
                Assert.That(result.Rules.Single().Metric, Is.EqualTo(metric));
                Assert.That(result.Rules.Single().Enabled, Is.True);
                Assert.That(result.Rules.Single().Seconds, Is.EqualTo(10));
                Assert.That(result.Rules.Single().Target, Is.EqualTo(IsTargeted(metric) ? "worker" : ""));
            });
        }
        else
        {
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Null);
                Assert.That(invalid, Is.SameAs(threshold));
                Assert.That(error, Is.EqualTo(Strings.Format(Strings.AutoCaptureInvalidThreshold, minimum, maximum)));
            });
        }
    }

    [TestCase("empty"), TestCase("text"), TestCase("NaN"), TestCase("positive-infinity")]
    [TestCase("negative-infinity"), TestCase("overflow"), TestCase("grouped")]
    public void InvalidThresholdSyntaxAndNonFiniteValuesIdentifyTheThreshold(string kind)
    {
        var section = Section(CaptureMetric.Cpu);
        var threshold = Field(section, 1, 1);
        threshold.Text = kind switch
        {
            "empty" => "", "text" => "no", "NaN" => "NaN", "positive-infinity" => "Infinity",
            "negative-infinity" => "-Infinity", "overflow" => "1e999", _ => "0,090"
        };
        AssertRejected(section, threshold, Strings.Format(Strings.AutoCaptureInvalidThreshold, 1, 100));
    }

    [TestCase("0", false), TestCase("1", true), TestCase("300", true), TestCase("301", false)]
    [TestCase("1.5", false), TestCase("2147483648", false), TestCase("bad", false), TestCase("", false)]
    [TestCase(" +2 ", true)]
    public void SecondsUseInclusiveIntegerBounds(string text, bool accepted)
    {
        var section = Section(CaptureMetric.Cpu);
        var seconds = Field(section, 1, 3);
        seconds.Text = text;
        if (!accepted) AssertRejected(section, seconds, Strings.AutoCaptureInvalidSeconds);
        else
        {
            var result = ReadValid(section);
            Assert.That(result.Rules.Single().Seconds, Is.EqualTo(int.Parse(text, CultureInfo.CurrentCulture)));
        }
    }

    [TestCase("0", false), TestCase("1", true), TestCase("1440", true), TestCase("1441", false)]
    [TestCase("1.5", false), TestCase("2147483648", false), TestCase("bad", false), TestCase("", false)]
    public void CooldownUsesInclusiveIntegerBounds(string text, bool accepted)
    {
        var section = Section(CaptureMetric.Cpu);
        var cooldown = Cooldown(section);
        cooldown.Text = text;
        if (!accepted) AssertRejected(section, cooldown, Strings.AutoCaptureInvalidCooldown);
        else Assert.That(ReadValid(section).CooldownMinutes, Is.EqualTo(int.Parse(text, CultureInfo.CurrentCulture)));
    }

    [TestCase(CaptureMetric.ApplicationCpu), TestCase(CaptureMetric.ApplicationMemory)]
    [TestCase(CaptureMetric.ApplicationNetwork), TestCase(CaptureMetric.ServiceCpu)]
    [TestCase(CaptureMetric.ServiceMemory), TestCase(CaptureMetric.ServiceNetwork)]
    public void EnabledTargetsAreRequiredAndTrimmedWhileDisabledTargetsMayBeEmpty(CaptureMetric metric)
    {
        var section = Section(metric);
        var target = Field(section, 2, 0);
        var enabled = RuleSwitch(section, 1);
        target.Text = " \t ";
        AssertRejected(section, target, Strings.CaptureTargetRequired);
        target.Text = "  exact target.exe  ";
        Assert.That(ReadValid(section).Rules.Single().Target, Is.EqualTo("exact target.exe"));
        enabled.IsChecked = false;
        target.Text = " \t ";
        var result = ReadValid(section);
        Assert.Multiple(() =>
        {
            Assert.That(result.Rules.Single().Enabled, Is.False);
            Assert.That(result.Rules.Single().Target, Is.Empty);
        });
        enabled.IsChecked = null;
        Assert.That(ReadValid(section).Rules.Single().Enabled, Is.False);
    }

    [Test]
    public void DisabledCaptureAndDisabledRuleStillValidateNumbersAndLeaveTextUnchanged()
    {
        var section = Section(CaptureMetric.ApplicationCpu);
        section.EnabledSwitch.IsChecked = false;
        RuleSwitch(section, 1).IsChecked = false;
        var threshold = Field(section, 1, 1);
        var seconds = Field(section, 1, 3);
        threshold.Text = "invalid";
        seconds.Text = "0";
        AssertRejected(section, threshold, Strings.Format(Strings.AutoCaptureInvalidThreshold, 1, 25600));
        Assert.That(threshold.Text, Is.EqualTo("invalid"));
        threshold.Text = "90";
        AssertRejected(section, seconds, Strings.AutoCaptureInvalidSeconds);
        seconds.Text = "1";
        Cooldown(section).Text = "0";
        AssertRejected(section, Cooldown(section), Strings.AutoCaptureInvalidCooldown);
        Cooldown(section).Text = "1";
        section.EnabledSwitch.IsChecked = null;
        Assert.That(ReadValid(section).Enabled, Is.False);
    }

    [Test]
    public void FirstInvalidRuleAndFieldWinBeforeCooldownAndFolderAreNormalized()
    {
        var section = new AutoCaptureSection(new AutoCapturePreferences
        {
            Rules = [new(CaptureMetric.ApplicationCpu, true, 90, 10), new(CaptureMetric.Cpu, true, 80, 10)],
            Folder = "relative"
        }, DefaultFolder, "C:", "D:");
        var first = Field(section, 1, 1);
        var seconds = Field(section, 1, 3);
        var target = Field(section, 2, 0);
        var second = Field(section, 3, 1);
        first.Text = second.Text = "invalid";
        seconds.Text = "0";
        Cooldown(section).Text = "0";
        AssertRejected(section, first, Strings.Format(Strings.AutoCaptureInvalidThreshold, 1, 25600));
        first.Text = "90";
        AssertRejected(section, seconds, Strings.AutoCaptureInvalidSeconds);
        seconds.Text = "10";
        AssertRejected(section, target, Strings.CaptureTargetRequired);
        target.Text = " worker ";
        AssertRejected(section, second, Strings.Format(Strings.AutoCaptureInvalidThreshold, 1, 100));
        second.Text = "80";
        AssertRejected(section, Cooldown(section), Strings.AutoCaptureInvalidCooldown);
        Assert.That(section.Folder, Is.EqualTo("relative"));
        Cooldown(section).Text = "1";
        var result = ReadValid(section);
        Assert.Multiple(() =>
        {
            Assert.That(result.Rules.Select(rule => rule.Metric), Is.EqualTo(new[] { CaptureMetric.ApplicationCpu, CaptureMetric.Cpu }));
            Assert.That(result.Rules.Select(rule => rule.Threshold), Is.EqualTo(new[] { 90d, 80d }));
            Assert.That(result.Rules[0].Target, Is.EqualTo("worker"));
            Assert.That(result.Folder, Is.EqualTo(@"C:\WinMon-test\relative"));
            Assert.That(section.Folder, Is.EqualTo(result.Folder));
        });
    }

    [TestCase("default"), TestCase("case-default"), TestCase("absolute"), TestCase("relative")]
    [TestCase("empty"), TestCase("whitespace"), TestCase("invalid")]
    public void FoldersPreserveDefaultSentinelNormalizeRelativePathsAndRetainInvalidInput(string kind)
    {
        var folder = kind switch
        {
            "default" => DefaultFolder, "case-default" => DefaultFolder.ToUpperInvariant(),
            "absolute" => @"  C:\chosen\Reports  ", "relative" => @"  nested\..\incidents  ",
            "empty" => "", "whitespace" => "   ", _ => "bad\0folder"
        };
        var section = new AutoCaptureSection(new AutoCapturePreferences { Rules = [], Folder = folder }, DefaultFolder, "C:", null);
        var expected = kind switch
        {
            "default" or "case-default" => null, "absolute" => @"C:\chosen\Reports",
            "relative" => @"C:\WinMon-test\incidents", "empty" or "whitespace" => "", _ => folder
        };
        var result = ReadValid(section);
        Assert.Multiple(() =>
        {
            Assert.That(result.Folder, Is.EqualTo(expected));
            Assert.That(section.Folder, Is.EqualTo(kind == "relative" ? expected : folder.Trim()));
            Assert.That(result.Rules, Is.Empty);
            Assert.That(ReadValid(section).Folder, Is.EqualTo(expected), "A second read keeps normalized folder semantics.");
        });
    }

    [TestCase("en-US", "1.25"), TestCase("de-DE", "1,25")]
    [TestCase("en-US", "  1.25e0  ")]
    public void ThresholdParsingUsesCurrentCultureAndPreservesNetworkPrecision(string culture, string text)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var section = Section(CaptureMetric.ServiceNetwork);
            Field(section, 1, 1).Text = text;
            Field(section, 1, 3).Text = " +2 ";
            var result = ReadValid(section);
            Assert.Multiple(() =>
            {
                Assert.That(result.Rules.Single().Threshold, Is.EqualTo(10));
                Assert.That(result.Rules.Single().Seconds, Is.EqualTo(2));
            });
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestCase(true, true), TestCase(true, false), TestCase(false, true), TestCase(false, false)]
    public void ReadReturnsEditedEnableAndNotificationValuesWithoutChangingTheInput(bool enabled, bool notify)
    {
        var preferences = new AutoCapturePreferences { Enabled = !enabled, Notify = !notify, Rules = [] };
        var section = new AutoCaptureSection(preferences, DefaultFolder, "C:", null);
        section.EnabledSwitch.IsChecked = enabled;
        section.Children.OfType<CheckBox>().Single().IsChecked = notify;
        var result = ReadValid(section);
        Assert.Multiple(() =>
        {
            Assert.That(result.Enabled, Is.EqualTo(enabled));
            Assert.That(result.Notify, Is.EqualTo(notify));
            Assert.That(result.Folder, Is.Null);
            Assert.That(preferences.Enabled, Is.EqualTo(!enabled));
            Assert.That(preferences.Notify, Is.EqualTo(!notify));
        });
    }

    [Test]
    public void RelativeFolderFallsBackToApplicationDirectoryWhenTheDefaultHasNoParent()
    {
        var section = new AutoCaptureSection(new AutoCapturePreferences { Folder = "incidents", Rules = [] },
            @"C:\", "C:", null);
        var expected = Path.Combine(AppContext.BaseDirectory, "incidents");
        Assert.Multiple(() =>
        {
            Assert.That(ReadValid(section).Folder, Is.EqualTo(expected));
            Assert.That(section.Folder, Is.EqualTo(expected));
        });
    }

    private static AutoCaptureSection Section(CaptureMetric metric) => new(new AutoCapturePreferences
    {
        Enabled = true,
        Rules = [new(metric, true, 80, 10) { Target = "worker" }]
    }, DefaultFolder, "C:", "D:");

    private static bool IsTargeted(CaptureMetric metric) => metric is CaptureMetric.ApplicationCpu or CaptureMetric.ApplicationMemory or
        CaptureMetric.ApplicationNetwork or CaptureMetric.ServiceCpu or CaptureMetric.ServiceMemory or CaptureMetric.ServiceNetwork;

    private static Grid RuleTable(AutoCaptureSection section) => (Grid)section.Children.OfType<Border>().Single().Child;

    private static TextBox Field(AutoCaptureSection section, int row, int column) => RuleTable(section).Children
        .OfType<TextBox>().Single(box => Grid.GetRow(box) == row && Grid.GetColumn(box) == column);

    private static CheckBox RuleSwitch(AutoCaptureSection section, int row) => RuleTable(section).Children
        .OfType<CheckBox>().Single(box => Grid.GetRow(box) == row);

    private static TextBox Cooldown(AutoCaptureSection section) => section.Children.OfType<WrapPanel>()
        .SelectMany(panel => panel.Children.OfType<TextBox>()).Single(box => AutomationProperties.GetName(box) == Strings.AutoCaptureCooldown);

    private static AutoCapturePreferences ReadValid(AutoCaptureSection section)
    {
        var result = section.Read(out var error, out var invalid);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(error, Is.Null);
            Assert.That(invalid, Is.Null);
        });
        return result!;
    }

    private static void AssertRejected(AutoCaptureSection section, Control field, string message)
    {
        var result = section.Read(out var error, out var invalid);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(error, Is.EqualTo(message));
            Assert.That(invalid, Is.SameAs(field));
        });
    }
}
