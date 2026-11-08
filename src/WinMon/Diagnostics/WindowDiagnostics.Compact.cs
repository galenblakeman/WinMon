using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls.Primitives;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    internal async Task CaptureCompactDiagnosticsAsync(string output, CancellationToken cancellationToken)
    {
        if (window.Session.History.Latest?.Resources.TotalMemoryBytes is null)
            throw new InvalidOperationException("Compact diagnostics require native resource samples.");
        var fullSize = new Size(window.Width, window.Height);
        await CheckWindowPositionRecoveryAsync(output, cancellationToken);
        window.CompactViewButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await CaptureAsync(Path.Combine(output, "strip-live.png"), 1, cancellationToken);
        if (!window.CompactMode || window.FullView.Visibility != Visibility.Collapsed ||
            window.CompactStrip.Visibility != Visibility.Visible ||
            window.ExportButton.Visibility != Visibility.Collapsed ||
            Math.Abs(window.Height - MainWindow.CompactHeight) > 0.001 ||
            window.CompactTiles[0].Readout.ValueLabel.Text != window.CpuMetric.ValueLabel.Text)
            throw new InvalidOperationException("Compact mode did not display the native live readings.");
        var savedSize = window.SavedFullViewSize(new Rect(window.Left, window.Top, window.Width, window.Height));
        await window.Store.SaveAsync(window.Preferences with { Width = savedSize.Width, Height = savedSize.Height },
            cancellationToken);
        var saved = await window.Store.LoadAsync(cancellationToken);
        // Sizes round-trip unchanged, so exact comparison is intended.
        // ReSharper disable CompareOfFloatsByEqualityOperator
        if (!saved.CompactMode || saved.Width != fullSize.Width || saved.Height != fullSize.Height)
            throw new InvalidOperationException("Compact preferences overwrote full-view dimensions.");
        window.CompactViewButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (window.CompactMode || window.Width != fullSize.Width || window.Height != fullSize.Height ||
            window.FullView.Visibility != Visibility.Visible ||
            window.ExportButton.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Full-view restoration failed.");
        // ReSharper restore CompareOfFloatsByEqualityOperator

        var fixture = new MainWindow(true)
        {
            Left = 40, Top = 40, Width = 1000, Height = 840,
            Preferences = new MonitorPreferences { PrimaryDrive = "C:", SecondaryDrive = "D:" }
        };
        var now = DateTimeOffset.Now;
        for (var i = 0; i < 120; i++)
        {
            var pulse = Math.Exp(-Math.Pow((i - 94) / 3d, 2));
            var wobble = 2 * Math.Sin(i * 1.7);
            fixture.Session.History.Add(new MonitorSample(now.AddSeconds(i - 119), 32.8 + 45 * pulse + wobble, [], 0,
                i == 45, Stopwatch.Frequency * (i + 1))
            {
                Resources = new ResourceSample
                {
                    TotalMemoryBytes = 32UL * 1_073_741_824, UsedMemoryBytes = 23_056_165_683,
                    DiskName = "C:", DiskActivePercent = 6.4 + 62 * pulse + wobble,
                    DiskBytesPerSecond = Math.Abs(i - 94) <= 5 ? 200_000 : 900_000,
                    SecondaryDiskName = "D:", SecondaryDiskActivePercent = 3.5 + 44 * pulse + wobble,
                    GpuPercent = 10.5 + 60 * pulse + wobble,
                    GpuMemoryUsedBytes = Math.Abs(i - 94) <= 5 ? null : 4UL * 1_073_741_824,
                    NetworkId = "fixture", NetworkName = "Ethernet", LinkBitsPerSecond = 1_000_000_000,
                    ReceiveBytesPerSecond = 955_000 + 8_000_000 * pulse, SendBytesPerSecond = 150_000
                }
            });
        }

        fixture.GpuMetric.Toggle.IsChecked = false;
        if (fixture.Preferences.ShowGpu)
            throw new InvalidOperationException("The Full-view GPU chart toggle did not update its setting.");
        fixture.SetCompactView(true);
        fixture.RefreshVisiblePresentation();
        if (fixture.CompactTiles[5].Box.Visibility != Visibility.Visible)
            throw new InvalidOperationException("A hidden chart series also hid its compact resource gauge.");
        var networkValue = new TextRange(
            fixture.CompactTiles[4].Readout.ValueLabel.ContentStart,
            fixture.CompactTiles[4].Readout.ValueLabel.ContentEnd).Text;
        var fullNetworkValue = new TextRange(fixture.NetworkMetric.ValueLabel.ContentStart,
            fixture.NetworkMetric.ValueLabel.ContentEnd).Text;
        if (networkValue == Strings.MissingReading || networkValue != fullNetworkValue)
            throw new InvalidOperationException($"The compact gauges lost the formatted network rate: {networkValue}.");
        var visual = (FrameworkElement)fixture.Content;
        foreach (var theme in new[] { "Dark", "Light" })
        {
            fixture.Preferences = fixture.Preferences with { Theme = theme };
            fixture.ApplyTheme();
            foreach (var style in Enum.GetValues<GaugeStyle>())
            foreach (var width in new[] { 1000d, 836d })
            {
                fixture.Preferences = fixture.Preferences with { GaugeStyle = style };
                fixture.ApplyGaugeStyle();
                Draw(width);
                if (fixture.CompactTiles.Where(tile => tile.Box.Visibility == Visibility.Visible).Any(tile =>
                        tile.Readout.Label != tile.Source.Label ||
                        tile.Readout.DetailLabel.Text != tile.Source.DetailLabel.Text ||
                        new TextRange(tile.Readout.ValueLabel.ContentStart,
                            tile.Readout.ValueLabel.ContentEnd).Text !=
                        new TextRange(tile.Source.ValueLabel.ContentStart,
                            tile.Source.ValueLabel.ContentEnd).Text))
                    throw new InvalidOperationException("Compact readouts did not mirror the Full-view readings.");
                if (fixture.CompactTiles.Any(tile => tile.Readout.GaugeStyle != style ||
                    tile.Readout.Toggle.Parent is not null ||
                    tile.Readout.Gauge.Visibility != (style == GaugeStyle.None
                        ? Visibility.Hidden : Visibility.Visible)))
                    throw new InvalidOperationException($"Compact readouts did not follow {style}.");
                foreach (var scale in new[] { 1d, 1.5, 2d })
                    await CaptureVisualAsync(visual,
                        Path.Combine(output, $"gauges-{style.ToString().ToLowerInvariant()}-{theme.ToLowerInvariant()}-{width}-{scale * 100:0}.png"), scale,
                        cancellationToken);
            }
        }

        foreach (var theme in ThemeFinishes.Names)
        {
            fixture.Preferences = fixture.Preferences with { Theme = theme, GaugeStyle = GaugeStyle.Bowl };
            fixture.ApplyTheme();
            Draw(836);
            await CaptureVisualAsync(visual, Path.Combine(output, $"theme-{theme.ToLowerInvariant()}.png"),
                1, cancellationToken);
        }

        foreach (var theme in new[] { "Dark", "Light" })
        foreach (var width in new[] { 1000d, 836d })
        {
            fixture.Preferences = fixture.Preferences with
                { Theme = theme, GaugeStyle = GaugeStyle.Bowl };
            fixture.ApplyTheme();
            Draw(width);
            var memory = fixture.CompactTiles.Single(tile => tile.Source == fixture.MemoryMetric).Readout;
            var disk = fixture.CompactTiles.Single(tile => tile.Source == fixture.DiskMetric).Readout;
            var gpu = fixture.CompactTiles.Single(tile => tile.Source == fixture.GpuMetric).Readout;
            var secondary = fixture.CompactTiles.Single(tile => tile.Source == fixture.SecondaryDiskMetric).Readout;
            var network = fixture.CompactTiles.Single(tile => tile.Source == fixture.NetworkMetric).Readout;
            var expectedDisk = TableMetricText.IoRate(200_000);
            if (memory.ScaleLabel.Text != TableMetricText.Memory(23_056_165_683) ||
                disk.ScaleLabel.Text != expectedDisk ||
                !gpu.ScaleLabel.Text.Contains('%') ||
                !secondary.ScaleLabel.Text.Contains('%') ||
                network.ScaleLabel.Text != RateText.Short(71_640_000) ||
                !memory.PeakIconVisible || !disk.PeakIconVisible || !gpu.PeakIconVisible || !network.PeakIconVisible ||
                !ReferenceEquals(memory.ScaleLabel.ToolTip, fixture.MemoryMetric.ToolTip))
                throw new InvalidOperationException("Compact native peak label did not share Full-view details.");
            await CaptureVisualAsync(visual,
                Path.Combine(output, $"gauges-native-{theme.ToLowerInvariant()}-{width}.png"), 1, cancellationToken);
        }

        fixture.Preferences = fixture.Preferences with
            { SecondaryDrive = null };
        Draw(836);
        if (fixture.CompactTiles[3].Box.Visibility != Visibility.Collapsed ||
            fixture.CompactStrip.ColumnDefinitions[3].Width.Value != 0)
            throw new InvalidOperationException("Unconfigured second disk left an empty strip column.");
        fixture.ShownSamplingError = SamplingError.ReadingsUnavailable;
        Draw(836);
        if (fixture.CompactTiles.Any(t =>
                t.Box.Visibility == Visibility.Visible && t.Readout.ValueLabel.Text != Strings.MissingReading))
            throw new InvalidOperationException("Unavailable readings were presented as live values.");
        await CaptureVisualAsync(visual, Path.Combine(output, "strip-unavailable.png"), 1, cancellationToken);
        fixture.SetCompactView(false);
        if (Math.Abs(fixture.Height - 840) > 0.001 || Math.Abs(fixture.Width - 1000) > 0.001 ||
            fixture.GpuMetric.Toggle.IsChecked != false)
            throw new InvalidOperationException("Fixture full-view dimensions were not restored.");
        await File.WriteAllTextAsync(Path.Combine(output, "compact.json"), JsonSerializer.Serialize(new
        {
            NativeReadings = true, ButtonRoundTrip = true, PreferenceRoundTrip = true,
            FullSizeRestored = true, ChartTogglePreserved = true, OptionalDisk = true, MissingReadings = true,
            GapInFixture = true, AlwaysNativePeakLabelsAndTooltips = true,
            Themes = new[] { "Dark", "Light" }.Concat(ThemeFinishes.Names),
            RenderScales = new[] { 1, 1.5, 2 }
        }), cancellationToken);
        return;

        void Draw(double width)
        {
            fixture.RefreshVisiblePresentation();
            visual.Measure(new Size(width, MainWindow.CompactHeight));
            visual.Arrange(new Rect(0, 0, width, MainWindow.CompactHeight));
            visual.UpdateLayout();
        }
    }

    private async Task CheckWindowPositionRecoveryAsync(string output, CancellationToken cancellationToken)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (!GetWindowRect(handle, out var original)) throw new InvalidOperationException("Window bounds unavailable.");
        window.RememberWindowPositionForFixture();
        window.Left += 40;
        window.NotifyDisplayChangeForFixture();
        await Task.Delay(500, cancellationToken);
        if (!GetWindowRect(handle, out var moved) || moved.Left == original.Left)
            throw new InvalidOperationException("Display recovery moved the window before monitors settled.");
        window.NotifyDisplayChangeForFixture();
        await Task.Delay(2700, cancellationToken);
        if (!GetWindowRect(handle, out var waiting) || waiting.Left != moved.Left)
            throw new InvalidOperationException("A later display change did not restart the settling delay.");
        await Task.Delay(600, cancellationToken);
        if (!GetWindowRect(handle, out var restored) || Math.Abs(restored.Left - original.Left) > 2 ||
            Math.Abs(restored.Top - original.Top) > 2)
            throw new InvalidOperationException("Display recovery did not restore the remembered native position.");

        window.Left += 40;
        SendMessageW(handle, 0x0232, IntPtr.Zero, IntPtr.Zero); // User finishes moving the window.
        if (!GetWindowRect(handle, out var chosen)) throw new InvalidOperationException("Window bounds unavailable.");
        window.NotifyDisplayChangeForFixture();
        await Task.Delay(3300, cancellationToken);
        if (!GetWindowRect(handle, out var retained) || retained.Left != chosen.Left || retained.Top != chosen.Top)
            throw new InvalidOperationException("Display recovery overrode the user's new position.");
        window.Left -= 40;
        window.RememberWindowPositionForFixture();
        await CaptureAsync(Path.Combine(output, "position-recovery.png"), 1, cancellationToken);
    }
}
