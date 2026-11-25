using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;
using Button = System.Windows.Controls.Button;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using CheckBox = System.Windows.Controls.CheckBox;
using MenuItem = System.Windows.Controls.MenuItem;
using Point = System.Windows.Point;
using RadioButton = System.Windows.Controls.RadioButton;
using Size = System.Windows.Size;
using ToolTip = System.Windows.Controls.ToolTip;

// ReSharper disable StringLiteralTypo

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    private async Task CaptureFeatureTabHelpAsync(string output, CancellationToken cancellationToken)
    {
        foreach (var (tab, help, name) in new[]
                 {
                     (window.ProcessesTab, Strings.ProcessesTabHelp, "processes"),
                     (window.ServicesTab, Strings.ServicesTabHelp, "services"),
                     (window.UsageTab, Strings.UsageTabHelp, "usage"),
                     (window.ConnectionsTab, Strings.ConnectionsTabHelp, "connections")
                 })
        {
            if (tab.ToolTip is not string text || text != help)
                throw new InvalidOperationException($"The {name} tab did not explain its view.");
            var tooltip = new ToolTip { Content = text, PlacementTarget = tab, Placement = PlacementMode.Bottom };
            try
            {
                tooltip.IsOpen = true;
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
                tooltip.UpdateLayout();
                await CaptureVisualAsync(tooltip, Path.Combine(output, $"tab-help-{name}.png"), 1, cancellationToken);
            }
            finally
            {
                tooltip.IsOpen = false;
            }
        }
    }

    private void VerifyFeatureHeaderWidths()
    {
        // Check the compact process toolbar at the minimum and wider supported window sizes.
        var headerWidth = window.Width;
        try
        {
            foreach (var width in new[] { window.MinWidth, 886, 1000 })
            {
                window.Width = width;
                window.UpdateLayout();
                ValidateTitleBar();
            }
        }
        finally
        {
            window.Width = headerWidth;
            window.UpdateLayout();
        }
    }

    private async Task CaptureFeatureExportsAsync(string output, CancellationToken cancellationToken)
    {
        var exportTheme = window.Preferences;
        try
        {
            foreach (var (theme, kind) in new[]
                     {
                         ("Dark", CsvExportKind.UsageTotals), ("Light", CsvExportKind.ResourceHistory),
                         ("Slate", CsvExportKind.ProcessSamples), ("Dark", CsvExportKind.Services),
                         ("Light", CsvExportKind.Connections), ("Dark", CsvExportKind.NetworkTraffic)
                     })
            {
                window.Preferences = exportTheme with { Theme = theme };
                window.ApplyTheme();
                var export = new CsvExportDialog(kind) { Owner = window };
                try
                {
                    export.Show();
                    export.UpdateLayout();
                    if (export.TimeChoices.Visibility != (kind == CsvExportKind.UsageTotals
                            ? Visibility.Collapsed
                            : Visibility.Visible) ||
                        export.FileNameBox.Text.Length == 0)
                        throw new InvalidOperationException(
                            "Export dialog showed the wrong time choices or file name.");
                    if (ButtonIcon.GetData(export.SaveButton) is null ||
                        ButtonIcon.GetData(export.CancelButton) is null ||
                        !export.SaveButton.IsEnabled ||
                        export.SaveButton.Background is not SolidColorBrush saveBackground ||
                        export.SaveButton.Foreground is not SolidColorBrush saveText ||
                        ChartPalettes.ContrastRatio(saveBackground.Color, saveText.Color) < 4.5)
                        throw new InvalidOperationException("Export Save button lacks readable text contrast.");
                    await CaptureVisualAsync((FrameworkElement)export.Content,
                        Path.Combine(output, $"export-{theme.ToLowerInvariant()}-{kind}.png"),
                        1, cancellationToken);
                }
                finally
                {
                    export.Close();
                }
            }
        }
        finally
        {
            window.Preferences = exportTheme;
            window.ApplyTheme();
        }
    }

    private async Task CaptureFeatureHardwareAsync(string output, CancellationToken cancellationToken)
    {
        var hardware = await Task.Run(WindowsHardwareInfo.Read, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(output, "hardware.json"),
            JsonSerializer.Serialize(hardware, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        string? copiedInfo = null;
        var info = new HardwareInfoWindow(text => copiedInfo = text) { Owner = window };
        try
        {
            info.Show();
            await info.Ready.WaitAsync(cancellationToken);
            info.UpdateLayout();
            var copyButton = info.CopyButton;
            if (!copyButton.IsEnabled)
                throw new InvalidOperationException("Copy did not become available after loading information.");
            copyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (copiedInfo is null || !copiedInfo.StartsWith(Strings.Format(Strings.ProductVersion, AppInfo.Version),
                    StringComparison.Ordinal) ||
                !copiedInfo.Contains(Strings.InstalledMemory, StringComparison.Ordinal) ||
                !copiedInfo.Contains(Strings.PhysicalDrives, StringComparison.Ordinal))
                throw new InvalidOperationException("Copy did not export the loaded information.");
            await File.WriteAllTextAsync(Path.Combine(output, "copied-info.txt"), copiedInfo, cancellationToken);
            await CaptureVisualAsync((FrameworkElement)info.Content, Path.Combine(output, "hardware-info.png"), 1,
                cancellationToken);
            await CaptureFeatureHardwareSectionsAsync(info, output, cancellationToken);
            await CaptureFeatureInfoThemesAsync(info, output, cancellationToken);
        }
        finally
        {
            info.Close();
        }
    }

    private static async Task CaptureFeatureHardwareSectionsAsync(HardwareInfoWindow info, string output, CancellationToken cancellationToken)
    {
        var hardwareScroll = info.HardwareScroll;
        var sections = info.Sections;
        var sectionIndex = 0;
        foreach (var expander in sections.Children.OfType<Expander>())
        {
            expander.IsExpanded = true;
            info.UpdateLayout();
            hardwareScroll.ScrollToVerticalOffset(expander.TransformToAncestor(sections).Transform(new Point()).Y -
                                                  24);
            await CaptureVisualAsync((FrameworkElement)info.Content, Path.Combine(output,
                $"hardware-section-{sectionIndex++}.png"), 1, cancellationToken);
            expander.IsExpanded = false;
        }
    }

    private async Task CaptureFeatureInfoThemesAsync(HardwareInfoWindow info, string output, CancellationToken cancellationToken)
    {
        var infoPreferences = window.Preferences;
        try
        {
            foreach (var theme in new[] { "Dark", "Light" })
            {
                window.Preferences = infoPreferences with { Theme = theme };
                window.ApplyTheme();
                WindowTheme.Apply(info);
                for (var tab = 0; tab < info.Tabs.Items.Count; tab++)
                {
                    info.Tabs.SelectedIndex = tab;
                    info.SetUpdateState(Strings.Format(Strings.UpdateAvailable, "99.0.0"), true);
                    info.UpdateLayout();
                    await CaptureVisualAsync((FrameworkElement)info.Content,
                        Path.Combine(output, $"info-{theme.ToLowerInvariant()}-{tab}.png"), 1, cancellationToken);
                }

                info.Tabs.SelectedIndex = 1;
                var topicIndex = 0;
                var topics = info.GuideTopics.Children.OfType<Expander>().ToArray();
                foreach (var topic in topics)
                    if (((StackPanel)topic.Content).Children[^1] is not Button { Tag: string showMe } ||
                        window.GuideTarget(showMe) is null)
                        throw new InvalidOperationException($"Guide topic {topic.Header} has no Show me target.");
                foreach (var topic in topics)
                {
                    foreach (var other in topics) other.IsExpanded = other == topic;
                    info.GuideScroll.ScrollToTop();
                    info.UpdateLayout();
                    topic.BringIntoView();
                    info.UpdateLayout();
                    await CaptureVisualAsync((FrameworkElement)info.Content,
                        Path.Combine(output, $"guide-{theme.ToLowerInvariant()}-{topicIndex++}.png"), 1,
                        cancellationToken);
                }

                foreach (var topic in topics) topic.IsExpanded = topic == topics[0];
            }

            info.Width = info.MinWidth;
            info.Height = info.MinHeight;
            for (var tab = 0; tab < info.Tabs.Items.Count; tab++)
            {
                info.Tabs.SelectedIndex = tab;
                info.UpdateLayout();
                await CaptureVisualAsync((FrameworkElement)info.Content,
                    Path.Combine(output, $"info-minimum-{tab}.png"), 1, cancellationToken);
            }
        }
        finally
        {
            window.Preferences = infoPreferences;
            window.ApplyTheme();
        }
    }

    private async Task CaptureFeaturePalettesAsync(MonitorPreferences original, string output, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var theme in new[] { "Dark", "Light" })
            foreach (var palette in ChartPalettes.Names)
            {
                window.Preferences = original with { Theme = theme, ChartPalette = palette };
                window.ApplyTheme();
                window.RefreshPresentation();
                var fileName = string.Concat(palette.ToLowerInvariant()
                    .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
                await CaptureAsync(Path.Combine(output, $"palette-{fileName}-{theme.ToLowerInvariant()}.png"), 1,
                    cancellationToken);
            }
        }
        finally
        {
            window.Preferences = original;
            window.ApplyTheme();
            window.RefreshPresentation();
        }
    }

    private async Task CaptureFeatureProcessMenuAsync(MonitorPreferences original, string output, CancellationToken cancellationToken)
    {
        var row = window.Processes.Rows.First(r => r.Visibility == Visibility.Visible);
        // Exercise menu creation without invoking the destructive command.
        row.PrepareProcessMenu();
        var menu = row.ContextMenu ?? throw new InvalidOperationException("Process context menu was not created.");
        var items = menu.Items.OfType<MenuItem>().ToArray();
        var item = items.Last();
        var priority = items.SingleOrDefault(i => Equals(i.Header, Strings.SetPriority));
        var requiredActions = new[] { Strings.ProcessProperties + "…", Strings.CopyDetails,
            Strings.ShowInConnections, Strings.ShowInUsage };
        if (requiredActions.Any(label => items.Count(i => Equals(i.Header, label)) != 1) ||
            priority?.Items.Count != ProcessPriorityChanger.Choices.Length ||
            items.Where(i => i != priority).Any(i => i.Icon is null) ||
            item.ToolTip as string != Strings.AvailableForLiveProcesses)
            throw new InvalidOperationException("Process context menu did not capture its target.");
        try
        {
            menu.PlacementTarget = row;
            menu.IsOpen = true;
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
            menu.UpdateLayout();
            await CaptureVisualAsync(menu, Path.Combine(output, "process-context-menu.png"), 1, cancellationToken);
            var enabled = item.IsEnabled;
            try
            {
                foreach (var theme in new[] { "Dark", "Light" })
                {
                    window.Preferences = original with { Theme = theme };
                    window.ApplyTheme();
                    foreach (var isEnabled in new[] { true, false })
                    {
                        item.IsEnabled = isEnabled;
                        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
                        menu.UpdateLayout();
                        await CaptureVisualAsync(menu, Path.Combine(output,
                                $"process-context-menu-{theme.ToLowerInvariant()}-{(isEnabled ? "enabled" : "disabled")}.png"),
                            1, cancellationToken);
                    }
                }
            }
            finally
            {
                item.IsEnabled = enabled;
                window.Preferences = original;
                window.ApplyTheme();
            }
        }
        finally
        {
            menu.IsOpen = false;
        }

        var selection = window.SelectedSample;
        try
        {
            window.SelectedSample = window.Session.History.Latest;
            window.RefreshPresentation();
            row.PrepareProcessMenu();
            if (menu.Items.OfType<MenuItem>().Last().IsEnabled ||
                menu.Items.OfType<MenuItem>().Single(i => (string)i.Header == Strings.CreateDumpFile + "…").IsEnabled)
                throw new InvalidOperationException("Historical rows allowed process termination.");
            window.SelectedSample = null;
            window.RefreshPresentation();
            window.Processes.GroupedChip.IsChecked = true;
            window.RefreshPresentation();
            row.PrepareProcessMenu();
            if (menu.Items.OfType<MenuItem>().Last().Header is not string groupHeader ||
                !groupHeader.Contains(Strings.Format(Strings.EndGroupMenu, "").Trim(), StringComparison.Ordinal))
                throw new InvalidOperationException("Grouped rows did not offer group termination.");
        }
        finally
        {
            window.Processes.IndividualChip.IsChecked = true;
            window.SelectedSample = selection;
            window.RefreshPresentation();
        }
    }

    private async Task CaptureFeatureProcessDialogsAsync(MonitorPreferences original, string output, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var theme in new[] { "Dark", "Light" })
            {
                window.Preferences = original with { Theme = theme };
                window.ApplyTheme();
                var example = new ProcessUsage(12345, DateTime.UtcNow.AddHours(-2).ToFileTimeUtc(), "example.exe", 4.2,
                    100 * 1_048_576, 230_000)
                {
                    ImagePath = @"C:\Program Files\Example\example.exe", AppName = "Example application",
                    FileVersion = "1.2.3.4", ProductVersion = "1.2 release",
                    Publisher = "Example publisher", ThreadCount = 12, HandleCount = 240, PrivateBytes = 80 * 1_048_576
                };
                var snapshot = new ProcessDetailsSnapshot(example, false, DateTimeOffset.Now, false, true, "")
                {
                    Inspection = new ProcessInspection(false, "example.exe --mode smoke",
                        new ProcessOwner("EXAMPLE\\user", 1, true),
                        new ProcessSignature(ProcessSignatureStatus.Verified, "Example publisher"))
                };
                foreach (var dialog in new Window[]
                         {
                             new ProcessPropertiesWindow(snapshot, _ => { }),
                             new ProcessAffinityDialog(0b0011, 0b1111, false),
                             new SuspendedProcessCloseDialog(),
                             new ProcessDumpDialog(example, () => false)
                         })
                {
                    dialog.Owner = window;
                    try
                    {
                        dialog.Show();
                        dialog.UpdateLayout();
                        var dialogName = dialog switch
                        {
                            ProcessPropertiesWindow => "process-properties",
                            ProcessAffinityDialog => "process-affinity",
                            SuspendedProcessCloseDialog => "process-suspension-close",
                            _ => "process-dump"
                        };
                        await CaptureVisualAsync(dialog, Path.Combine(output,
                                $"{dialogName}-{theme.ToLowerInvariant()}.png"),
                            1, cancellationToken);
                        if (dialog is ProcessAffinityDialog)
                        {
                            dialog.SizeToContent = SizeToContent.Manual;
                            dialog.Width = dialog.MinWidth;
                            dialog.Height = dialog.MinHeight;
                            dialog.UpdateLayout();
                            await CaptureVisualAsync(dialog, Path.Combine(output,
                                $"{dialogName}-{theme.ToLowerInvariant()}-minimum.png"), 1, cancellationToken);
                        }
                    }
                    finally
                    {
                        dialog.Close();
                    }
                }

                foreach (var confirmation in new[] { true, false })
                {
                    var text = confirmation
                        ? Strings.Format(Strings.EndGroupConfirmation, 2, "Example") +
                          "\n\nExample (PID 12345)\nExample (PID 12346)"
                        : Strings.AccessDeniedWindowsDidNotAllowWinMonToEndThis;
                    var dialog = new ProcessActionDialog(text, confirmation) { Owner = window };
                    try
                    {
                        // Show non-modally for a screenshot; never invoke termination in diagnostics.
                        dialog.Show();
                        dialog.UpdateLayout();
                        await CaptureVisualAsync((FrameworkElement)dialog.Content, Path.Combine(output,
                                $"process-dialog-{theme.ToLowerInvariant()}-{(confirmation ? "confirmation" : "result")}.png"),
                            1, cancellationToken);
                    }
                    finally
                    {
                        dialog.Close();
                    }
                }
            }
        }
        finally
        {
            window.Preferences = original;
            window.ApplyTheme();
        }
    }

}
