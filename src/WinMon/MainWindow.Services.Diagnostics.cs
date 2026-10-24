using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

// ReSharper disable StringLiteralTypo

namespace WinMon;

public partial class App
{
    private async Task RunServicesDiagnosticsAsync(MainWindow window, string output)
    {
        var exit = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            Directory.CreateDirectory(output);
            await Task.Delay(5000, timeout.Token);
            await window.CaptureServicesDiagnosticsAsync(output, timeout.Token);
        }
        catch (Exception ex)
        {
            exit = 1;
            Directory.CreateDirectory(output);
            // Record the failure even when it was the timeout.
            await File.WriteAllTextAsync(Path.Combine(output, "failure.txt"), ex.ToString(), CancellationToken.None);
        }
        finally
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Close();
            await closed.Task;
            Shutdown(exit);
        }
    }
}

public partial class MainWindow
{
    private static async Task CaptureServiceWindowAsync(Window window, string path, CancellationToken cancellationToken)
    {
        try
        {
            await CaptureExampleWindowAsync(window, path, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("must be foreground", StringComparison.Ordinal))
        {
            // Content-only QA remains useful for the controls when Windows denies foreground activation.
            // Keep it clearly separate from native-composed screenshots; never substitute it in the gallery.
            var contentPath = Path.Combine(Path.GetDirectoryName(path)!,
                Path.GetFileNameWithoutExtension(path) + "-content.png");
            await CaptureVisualAsync(window, contentPath, 1, cancellationToken);
            await File.AppendAllTextAsync(Path.Combine(Path.GetDirectoryName(path)!, "content-capture-notes.txt"),
                Path.GetFileName(contentPath) + ": WPF content capture; native caption not verified.\n",
                cancellationToken);
        }
    }

    internal async Task CaptureServicesDiagnosticsAsync(string output, CancellationToken cancellationToken)
    {
        var live = _serviceHistory.Frames.LastOrDefault();
        if (live is null || !live.Available || !live.Groups.Any(g => g.Host is not null))
            throw new InvalidOperationException(
                "Live SCM enumeration and process mapping did not produce service readings.");
        var catalog = await Task.Run(WindowsServices.Capture, cancellationToken);
        using var sampler = new WindowsCpuSampler();
        sampler.Sample();
        await Task.Delay(1100, cancellationToken);
        var current = sampler.Sample().Processes.Single(p => p.ProcessId == Environment.ProcessId);
        if (current.ParentProcessId <= 0) throw new InvalidOperationException("Native parent PID was not captured.");
        await File.WriteAllTextAsync(Path.Combine(output, "live-services.json"), JsonSerializer.Serialize(new
        {
            Services = catalog.Services.Count,
            MappedHosts = live.Groups.Count(g => g.Host is not null),
            SharedHosts = live.Groups.Count(g => g.Shared),
            WorkerProcesses = live.Groups.Sum(g => Math.Max(0, g.Processes.Count - 1)),
            current.ProcessId, current.ParentProcessId, current.CreationTicks,
            ServiceActionsExecuted = false
        }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        ServicesTab.IsChecked = true;
        Width = 1200;
        Height = 950;
        await CaptureServiceWindowAsync(this, Path.Combine(output, "services-live.png"), cancellationToken);

        var fixture = new MainWindow(true) { Width = 1200, Height = 950 };
        fixture.Loaded -= fixture.OnLoaded;
        fixture.Left = SystemParameters.WorkArea.Left + 20;
        fixture.Top = SystemParameters.WorkArea.Top + 20;
        try
        {
            var tree = new ServiceTree();
            var anchor = DateTimeOffset.UtcNow;
            ServiceObservation[] services =
            [
                new("BuildAgent", "Build Agent", 4, 6240, 1),
                new("WinDefend", "Microsoft Defender Antivirus Service", 4, 3096, 0),
                new("wuauserv", "Windows Update", 4, 4820, 1),
                new("BITS", "Background Intelligent Transfer Service", 4, 4820, 1),
                new("WSearch", "Windows Search", 4, 6340, 1),
                new("Audiosrv", "Windows Audio", 4, 2160, 1),
                new("Spooler", "Print Spooler", 1, 0, 0)
            ];
            for (var i = 0; i < 120; i++)
            {
                var factor = 0.6 + 0.4 * Math.Abs(Math.Sin(i / 8d));
                ProcessUsage[] processes =
                [
                    P(6240, 100, 0, "build-agent.exe", 1, 80),
                    P(7312, 200, 6240, "compiler.exe", 8 * factor, 400),
                    P(7460, 300, 6240, "worker.exe", 3 * factor, 160),
                    P(3096, 110, 0, "MsMpEng.exe", 3.2, 286),
                    P(4820, 120, 0, "svchost.exe", 0.8 * factor, 68),
                    P(6340, 130, 0, "SearchIndexer.exe", 1.6, 164),
                    P(2160, 140, 0, "svchost.exe", 0.2, 28)
                ];
                var sample = new MonitorSample(anchor.AddSeconds(i - 119), 28 + factor * 10, processes, 0,
                    MonotonicTimestamp: Stopwatch.Frequency * (i + 1))
                {
                    Resources = new ResourceSample
                    {
                        UsedMemoryBytes = 21_000_000_000, TotalMemoryBytes = 32UL * 1073741824,
                        DiskName = "C:", DiskActivePercent = 8 + factor * 4, DiskBytesPerSecond = 54_000_000,
                        NetworkId = "fixture", NetworkName = "Ethernet", LinkBitsPerSecond = 1_000_000_000,
                        ReceiveBytesPerSecond = 1_200_000, SendBytesPerSecond = 130_000,
                        GpuPercent = 11, GpuEngine = "3D", GpuId = "illustrative"
                    }
                };
                fixture._history.Add(sample);
                fixture._serviceHistory.Add(tree.Capture(sample, new ServiceCatalog(long.MaxValue, services)));
            }

            fixture.Show();
            fixture.ServicesTab.IsChecked = true;
            foreach (var group in fixture._serviceHistory.Frames[^1].Groups
                         .Where(g => g.Shared || g.Services[0].Name == "BuildAgent"))
                fixture.ServiceView.ExpandGroup(group.Key);
            fixture.ServiceView.Rows.SelectedIndex = 0;
            foreach (var theme in new[] { "Dark", "Light" })
            {
                fixture._preferences = fixture._preferences with { Theme = theme };
                fixture.ApplyTheme();
                fixture.RefreshVisiblePresentation();
                await CaptureServiceWindowAsync(fixture,
                    Path.Combine(output, $"services-{theme.ToLowerInvariant()}.png"), cancellationToken);
                VerifyToolbar(fixture.ServiceView);
                fixture.OnPin(fixture, new RoutedEventArgs());
                try
                {
                    if (!fixture.Topmost || fixture.PinButton.Background != fixture.FindResource("SelectedSurface"))
                        throw new InvalidOperationException(
                            "Always on top must show its highlighted enabled background.");
                    await CaptureServiceWindowAsync(fixture,
                        Path.Combine(output, $"always-on-top-{theme.ToLowerInvariant()}.png"), cancellationToken);
                }
                finally
                {
                    fixture.OnPin(fixture, new RoutedEventArgs());
                }

                if (fixture.Topmost || fixture.PinButton.ReadLocalValue(BackgroundProperty) !=
                    DependencyProperty.UnsetValue)
                    throw new InvalidOperationException(
                        "Disabling Always on top must restore the title button's default background.");
                fixture.PinButton.Focus();
                fixture.UpdateLayout();
                var buttonBody = (Border)fixture.PinButton.Template.FindName("Body", fixture.PinButton);
                if (buttonBody.BorderBrush is not SolidColorBrush offBorder || offBorder.Color.A != 0)
                    throw new InvalidOperationException(
                        "Always on top must have no selected outline when off, even while focused.");
                await CaptureServiceWindowAsync(fixture,
                    Path.Combine(output, $"always-on-top-off-focused-{theme.ToLowerInvariant()}.png"),
                    cancellationToken);
                if (fixture.ServiceView.Rows.Items[0] is not ServiceDisplayRow target)
                    throw new InvalidOperationException("The first service row is missing.");
                var menu = fixture.ServiceView.CreateServiceMenu(target)!;
                menu.PlacementTarget = fixture.ServiceView.Rows;
                menu.Placement = PlacementMode.Relative;
                menu.HorizontalOffset = 80;
                menu.VerticalOffset = 30;
                try
                {
                    menu.IsOpen = true;
                    await CaptureServiceWindowAsync(fixture,
                        Path.Combine(output, $"services-menu-{theme.ToLowerInvariant()}.png"), cancellationToken);
                }
                finally
                {
                    menu.IsOpen = false;
                }
            }

            fixture.Width = 836;
            fixture.Height = 840;
            fixture.RefreshVisiblePresentation();
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "services-narrow.png"), cancellationToken);
            VerifyToolbar(fixture.ServiceView);
            fixture.ServiceView.Search.Text = "Build";
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "services-search.png"), cancellationToken);
            fixture.ServiceView.ClearServiceSearchButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (fixture.ServiceView.Search.Text.Length != 0 || !fixture.ServiceView.Search.IsKeyboardFocusWithin)
                throw new InvalidOperationException("Service search clear must reset the query and restore focus.");
            // Constrain only the control, keeping the main window's minimum size and caption behavior intact.
            fixture.ServiceView.Width = 600;
            fixture.ServiceView.HorizontalAlignment = HorizontalAlignment.Left;
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "services-header-wrapped.png"),
                cancellationToken);
            VerifyToolbar(fixture.ServiceView);
            if (Grid.GetRow(fixture.ServiceView.SearchBorder) != 1)
                throw new InvalidOperationException(
                    "Constrained service toolbar must wrap search below the ranking controls.");
            fixture.ServiceView.Width = double.NaN;
            fixture.ServiceView.HorizontalAlignment = HorizontalAlignment.Stretch;
            AppLanguages.Apply("de");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "services-header-german.png"),
                cancellationToken);
            VerifyToolbar(fixture.ServiceView);
            AppLanguages.Apply("en");
            fixture._selectedSample = fixture._history.Samples[90];
            fixture.RefreshVisiblePresentation();
            if (fixture.ServiceView.Stop.IsEnabled || fixture.ServiceView.Restart.IsEnabled ||
                fixture.ServiceView.Start.IsEnabled)
                throw new InvalidOperationException("Historical service actions must be disabled.");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "services-snapshot.png"), cancellationToken);
            foreach (var theme in new[] { "Dark", "Light" })
            {
                fixture._preferences = fixture._preferences with { Theme = theme };
                fixture.ApplyTheme();
                foreach (var confirmation in new[] { true, false })
                {
                    var message = confirmation
                        ? Strings.Format(Strings.ConfirmServiceAction, Strings.StopService, "Example Service")
                        : Strings.TheClipboardIsUnavailableTryCopyAgain;
                    var dialog = new MessageDialog(confirmation ? Strings.Services : Strings.CopyDetails,
                        message, confirmation ? Strings.StopService : null) { Owner = fixture };
                    try
                    {
                        dialog.Show();
                        await CaptureServiceWindowAsync(dialog, Path.Combine(output,
                                $"dialog-{theme.ToLowerInvariant()}-{(confirmation ? "confirmation" : "notice")}.png"),
                            cancellationToken);
                    }
                    finally
                    {
                        dialog.Close();
                    }
                }
            }

            fixture._serviceActionBusy = true;
            fixture.Close();
            if (!fixture.IsVisible || fixture._closing || !fixture._closeAfterServiceAction)
                throw new InvalidOperationException("Closing must wait for an accepted service action to finish.");
            fixture._serviceActionBusy = false;
            fixture._closeAfterServiceAction = false;
        }
        finally
        {
            fixture.Closing -= fixture.OnClosing;
            fixture.Close();
        }

        return;

        static ProcessUsage P(int pid, long created, int parent, string name, double cpu, ulong memory)
        {
            return new ProcessUsage(pid, created, name, cpu, memory * 1048576, cpu * 250000)
                { ParentProcessId = parent, PrivateBytes = memory * 1048576 };
        }

        static void VerifyToolbar(ServicesView view)
        {
            view.UpdateLayout();
            FrameworkElement[] parts = [view.RankingControls, view.SearchBorder, view.ServiceFilters];
            var bounds = parts.Select(p => p.TransformToAncestor(view).TransformBounds(new Rect(p.RenderSize)))
                .ToArray();
            if (bounds.Any(b => b.Left < -1 || b.Right > view.ActualWidth + 1) ||
                bounds.SelectMany((b, i) => bounds.Skip(i + 1).Select(other => Rect.Intersect(b, other)))
                    .Any(intersection => intersection is { IsEmpty: false, Width: > 1, Height: > 1 }))
                throw new InvalidOperationException(
                    "Service toolbar controls overlap or extend beyond the available width.");
            var identity = view.SelectionIdentity.TransformToAncestor(view)
                .TransformBounds(new Rect(view.SelectionIdentity.RenderSize));
            var actions = view.ActionControls.TransformToAncestor(view)
                .TransformBounds(new Rect(view.ActionControls.RenderSize));
            var overlap = Rect.Intersect(identity, actions);
            if (identity.Width < 20 || view.Summary.ActualWidth < 12 || view.Summary.ActualHeight < 1 ||
                identity.Left < -1 || actions.Right > view.ActualWidth + 1 ||
                overlap is { IsEmpty: false, Width: > 1, Height: > 1 })
                throw new InvalidOperationException(
                    "Service identity and action buttons must remain visible without overlap.");
        }
    }
}