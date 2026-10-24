using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls.Primitives;
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
        window.CompactViewButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await CaptureAsync(Path.Combine(output, "strip-live.png"), 1, cancellationToken);
        if (!window.CompactMode || window.FullView.Visibility != Visibility.Collapsed ||
            window.CompactStrip.Visibility != Visibility.Visible || Math.Abs(window.Height - 156) > 0.001 ||
            window.StripTiles[0].Value.Text != window.CpuMetric.ValueLabel.Text)
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
            window.FullView.Visibility != Visibility.Visible)
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
                    SecondaryDiskName = "D:", SecondaryDiskActivePercent = 3.5 + 44 * pulse + wobble,
                    GpuPercent = 10.5 + 60 * pulse + wobble,
                    NetworkId = "fixture", NetworkName = "Ethernet", LinkBitsPerSecond = 1_000_000_000,
                    ReceiveBytesPerSecond = 955_000 + 8_000_000 * pulse, SendBytesPerSecond = 150_000
                }
            });
        }

        fixture.SetCompactView(true);
        fixture.RefreshVisiblePresentation();
        if (fixture.StripTiles[4].Value.Text == Strings.MissingReading ||
            !fixture.StripTiles[4].Value.Text.Contains("Mbps", StringComparison.Ordinal))
            throw new InvalidOperationException("The strip lost the formatted network rate.");
        var visual = (FrameworkElement)fixture.Content;
        foreach (var theme in new[] { "Dark", "Light" })
        {
            fixture.Preferences = fixture.Preferences with { Theme = theme };
            fixture.ApplyTheme();
            foreach (var width in new[] { 1000d, 836d })
            {
                Draw(width);
                foreach (var scale in new[] { 1d, 1.5, 2d })
                    await CaptureVisualAsync(visual,
                        Path.Combine(output, $"strip-{theme.ToLowerInvariant()}-{width}-{scale * 100:0}.png"), scale,
                        cancellationToken);
            }
        }

        fixture.Preferences = fixture.Preferences with { SecondaryDrive = null };
        Draw(836);
        if (fixture.StripTiles[3].Box.Visibility != Visibility.Collapsed ||
            fixture.CompactStrip.ColumnDefinitions[3].Width.Value != 0)
            throw new InvalidOperationException("Unconfigured second disk left an empty strip column.");
        fixture.ShownSamplingError = SamplingError.ReadingsUnavailable;
        Draw(836);
        if (fixture.StripTiles.Any(t =>
                t.Box.Visibility == Visibility.Visible && t.Value.Text != Strings.MissingReading))
            throw new InvalidOperationException("Unavailable readings were presented as live values.");
        await CaptureVisualAsync(visual, Path.Combine(output, "strip-unavailable.png"), 1, cancellationToken);
        fixture.SetCompactView(false);
        if (Math.Abs(fixture.Height - 840) > 0.001 || Math.Abs(fixture.Width - 1000) > 0.001)
            throw new InvalidOperationException("Fixture full-view dimensions were not restored.");
        await File.WriteAllTextAsync(Path.Combine(output, "compact.json"), JsonSerializer.Serialize(new
        {
            NativeReadings = true, ButtonRoundTrip = true, PreferenceRoundTrip = true,
            FullSizeRestored = true, OptionalDisk = true, MissingReadings = true,
            GapInFixture = true, Themes = new[] { "Dark", "Light" }, RenderScales = new[] { 1, 1.5, 2 }
        }), cancellationToken);
        return;

        void Draw(double width)
        {
            fixture.RefreshVisiblePresentation();
            visual.Measure(new Size(width, 156));
            visual.Arrange(new Rect(0, 0, width, 156));
            visual.UpdateLayout();
        }
    }
}