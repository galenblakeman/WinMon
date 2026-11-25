using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration")]
public sealed class CsvDialogCharacterizationTests
{
    private readonly List<CsvExportDialog> _dialogs = [];

    private CsvExportDialog Create(CsvExportKind kind, string? usageNote = null)
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var dialog = new CsvExportDialog(kind, usageNote) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        _dialogs.Add(dialog);
        foreach (var key in new[] { "Surface", "SearchSurface" }) dialog.Resources[key] = Brushes.White;
        foreach (var key in new[] { "Text", "Muted", "Line" }) dialog.Resources[key] = Brushes.Black;
        dialog.Resources["Accent"] = Brushes.Blue;
        return dialog;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var dialog in _dialogs) dialog.Close();
        _dialogs.Clear();
    }

    private static RadioButton[] Choices(CsvExportDialog dialog) =>
        [dialog.ResourceChoice, dialog.ProcessChoice, dialog.UsageChoice,
            dialog.ServiceChoice, dialog.ConnectionChoice, dialog.TrafficChoice];

    private static void CheckFileName(string actual, string prefix)
    {
        Assert.That(actual, Does.StartWith($"winmon-{prefix}-").And.EndWith(".csv"));
        var date = actual[$"winmon-{prefix}-".Length..^4];
        Assert.That(DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _), Is.True, "The generated date uses the invariant calendar and format");
    }

    private static bool? RunModal(CsvExportDialog dialog, Action loaded)
    {
        Exception? failure = null;
        var timedOut = false;
        RoutedEventHandler run = (_, _) =>
        {
            try { loaded(); }
            catch (Exception ex) { failure = ex; dialog.Close(); }
        };
        var deadline = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(10) };
        EventHandler expire = (_, _) => { timedOut = true; dialog.Close(); };
        deadline.Tick += expire;
        dialog.Loaded += run;
        bool? result;
        try
        {
            deadline.Start();
            result = dialog.ShowDialog();
        }
        finally
        {
            deadline.Stop();
            deadline.Tick -= expire;
            dialog.Loaded -= run;
            if (dialog.IsVisible) dialog.Close();
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.That(timedOut, Is.False, "The owned modal dialog must close through the tested action");
        return result;
    }

    private static void Save(CsvExportDialog dialog) =>
        dialog.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [TestCase(CsvExportKind.ResourceHistory, 0, "resources", nameof(Strings.ExportResourceNote))]
    [TestCase(CsvExportKind.ProcessSamples, 1, "processes", nameof(Strings.ExportProcessNote))]
    [TestCase(CsvExportKind.UsageTotals, 2, "usage", nameof(Strings.ExportUsageNote))]
    [TestCase(CsvExportKind.Services, 3, "services", nameof(Strings.ExportServiceNote))]
    [TestCase(CsvExportKind.Connections, 4, "connections", nameof(Strings.ExportConnectionNote))]
    [TestCase(CsvExportKind.NetworkTraffic, 5, "traffic", nameof(Strings.ExportTrafficNote))]
    [TestCase((CsvExportKind)(-1), 2, "usage", nameof(Strings.ExportUsageNote))]
    [TestCase((CsvExportKind)999, 2, "usage", nameof(Strings.ExportUsageNote))]
    public void InitialChoiceSelectsExactlyOneTypeAndItsExistingPresentation(
        CsvExportKind kind, int selected, string prefix, string noteKey)
    {
        var dialog = Create(kind);
        var choices = Choices(dialog);
        Assert.Multiple(() =>
        {
            Assert.That(choices.Select(choice => choice.IsChecked).ToArray(),
                Is.EqualTo(Enumerable.Range(0, choices.Length).Select(i => (bool?)(i == selected)).ToArray()));
            Assert.That(dialog.DataNote.Text, Is.EqualTo(Strings.Text[noteKey]));
            Assert.That(dialog.TimeChoices.Visibility, Is.EqualTo(selected == 2 ? Visibility.Collapsed : Visibility.Visible));
            Assert.That(dialog.MinHeight, Is.EqualTo(selected == 2 ? 500 : 630));
            Assert.That(dialog.RetainedChoice.IsChecked, Is.True);
            Assert.That(dialog.RangeChoice.IsChecked, Is.False);
            Assert.That(dialog.Choice, Is.Null);
            Assert.That(new WindowInteropHelper(dialog).Handle, Is.EqualTo(IntPtr.Zero));
        });
        CheckFileName(dialog.FileNameBox.Text, prefix);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("Scoped usage: selected day")]
    public void UsageNoteOverridesOnlyTheUsageTypeAndSurvivesSwitchingBack(string? note)
    {
        var dialog = Create(CsvExportKind.UsageTotals, note);
        Assert.That(dialog.DataNote.Text, Is.EqualTo(note ?? Strings.ExportUsageNote));
        dialog.ConnectionChoice.IsChecked = true;
        Assert.That(dialog.DataNote.Text, Is.EqualTo(Strings.ExportConnectionNote));
        dialog.UsageChoice.IsChecked = true;
        Assert.That(dialog.DataNote.Text, Is.EqualTo(note ?? Strings.ExportUsageNote));
    }

    [Test]
    public void ChangingTypesReplacesEditedFileNamesButPreservesTheHiddenRangeSelection()
    {
        var dialog = Create(CsvExportKind.ResourceHistory);
        dialog.RangeChoice.IsChecked = true;
        dialog.FileNameBox.Text = "edited.csv";
        dialog.UsageChoice.IsChecked = true;
        CheckFileName(dialog.FileNameBox.Text, "usage");
        Assert.That(dialog.TimeChoices.Visibility, Is.EqualTo(Visibility.Collapsed));
        Assert.That(dialog.RangeChoice.IsChecked, Is.True);
        dialog.FileNameBox.Text = "edited-again.csv";
        dialog.TrafficChoice.IsChecked = true;
        CheckFileName(dialog.FileNameBox.Text, "traffic");
        Assert.Multiple(() =>
        {
            Assert.That(dialog.TimeChoices.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(dialog.RangeChoice.IsChecked, Is.True);
            Assert.That(dialog.RetainedChoice.IsChecked, Is.False);
            Assert.That(dialog.DataNote.Text, Is.EqualTo(Strings.ExportTrafficNote));
            Assert.That(dialog.MinHeight, Is.EqualTo(630));
            Assert.That(dialog.Choice, Is.Null);
        });
    }

    [TestCase(CsvExportKind.ResourceHistory, false)]
    [TestCase(CsvExportKind.ResourceHistory, true)]
    [TestCase(CsvExportKind.ProcessSamples, false)]
    [TestCase(CsvExportKind.ProcessSamples, true)]
    [TestCase(CsvExportKind.UsageTotals, false)]
    [TestCase(CsvExportKind.UsageTotals, true)]
    [TestCase(CsvExportKind.Services, false)]
    [TestCase(CsvExportKind.Services, true)]
    [TestCase(CsvExportKind.Connections, false)]
    [TestCase(CsvExportKind.Connections, true)]
    [TestCase(CsvExportKind.NetworkTraffic, false)]
    [TestCase(CsvExportKind.NetworkTraffic, true)]
    public void ModalSaveReturnsTheSelectedTypeAndRangeExceptForUsageTotals(CsvExportKind kind, bool range)
    {
        var dialog = Create(kind);
        dialog.RangeChoice.IsChecked = range;
        dialog.FileNameBox.Text = "chosen.csv";
        Assert.That(RunModal(dialog, () => Save(dialog)), Is.True);
        Assert.That(dialog.Choice, Is.EqualTo(new CsvExportChoice(kind, range && kind != CsvExportKind.UsageTotals, "chosen.csv")));
    }

    [TestCase(" report ", "report.csv")]
    [TestCase(" report.csv ", "report.csv")]
    [TestCase("report.CSV", "report.CSV")]
    [TestCase("report.txt", "report.txt.csv")]
    [TestCase(".csv", ".csv")]
    [TestCase("report.csv.csv", "report.csv.csv")]
    public void ModalSaveTrimsTheNameAndPreservesAnExistingCaseInsensitiveCsvSuffix(string name, string expected)
    {
        var dialog = Create(CsvExportKind.ResourceHistory);
        dialog.FileNameBox.Text = name;
        Assert.That(RunModal(dialog, () => Save(dialog)), Is.True);
        Assert.That(dialog.Choice, Is.EqualTo(new CsvExportChoice(CsvExportKind.ResourceHistory, false, expected)));
    }

    [TestCase("Empty")]
    [TestCase("Whitespace")]
    [TestCase("Directory")]
    [TestCase("Colon")]
    [TestCase("Wildcard")]
    [TestCase("NullCharacter")]
    public void InvalidNamesKeepTheModalDialogOpenAndCanBeCorrectedWithoutChangingItsType(string input)
    {
        var name = input switch
        {
            "Empty" => "", "Whitespace" => " \t ", "Directory" => @"folder\report.csv",
            "Colon" => "report:part.csv", "Wildcard" => "report?.csv", "NullCharacter" => "report\0.csv",
            _ => throw new ArgumentOutOfRangeException(nameof(input))
        };
        var dialog = Create(CsvExportKind.Connections);
        dialog.RangeChoice.IsChecked = true;
        dialog.FileNameBox.Text = name;
        var result = RunModal(dialog, () =>
        {
            Save(dialog);
            Assert.Multiple(() =>
            {
                Assert.That(dialog.IsVisible, Is.True);
                Assert.That(dialog.Choice, Is.Null);
                Assert.That(dialog.FileNameBox.Text, Is.EqualTo(name));
                Assert.That(dialog.FileNameBox.SelectionStart, Is.Zero);
                Assert.That(dialog.FileNameBox.SelectionLength, Is.EqualTo(name.Length));
            });
            dialog.FileNameBox.Text = "corrected";
            Save(dialog);
        });
        Assert.That(result, Is.True);
        Assert.That(dialog.Choice, Is.EqualTo(new CsvExportChoice(CsvExportKind.Connections, true, "corrected.csv")));
    }

    [Test]
    public void UnknownInitialTypeSavesAsUsageEvenWhenChartRangeIsSelected()
    {
        var dialog = Create((CsvExportKind)999);
        dialog.RangeChoice.IsChecked = true;
        dialog.FileNameBox.Text = "fallback";
        Assert.That(RunModal(dialog, () => Save(dialog)), Is.True);
        Assert.That(dialog.Choice, Is.EqualTo(new CsvExportChoice(CsvExportKind.UsageTotals, false, "fallback.csv")));
    }

    [Test]
    public void ClosingTheModalDialogWithoutSavingReturnsNoChoice()
    {
        var dialog = Create(CsvExportKind.NetworkTraffic);
        Assert.That(RunModal(dialog, dialog.Close), Is.False);
        Assert.That(dialog.Choice, Is.Null);
    }

    [TestCase("en-US")]
    [TestCase("ar-SA")]
    public void GeneratedFileNameUsesInvariantDateSyntaxRegardlessOfTheCurrentCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CheckFileName(Create(CsvExportKind.ResourceHistory).FileNameBox.Text, "resources");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
