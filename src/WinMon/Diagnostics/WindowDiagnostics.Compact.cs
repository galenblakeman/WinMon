using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using WinMon.Preferences;

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    internal async Task CaptureCompactDiagnosticsAsync(string output, CancellationToken cancellationToken)
    {
        if (window.Session.History.Latest?.Resources.TotalMemoryBytes is null)
            throw new InvalidOperationException("Compact diagnostics require native resource samples.");
        await VerifyCompactWindowRoundTripAsync(output, cancellationToken);
        var fixture = CreateCompactScenarioFixture();
        InitializeCompactScenarioReadouts(fixture);
        var visual = (FrameworkElement)fixture.Content;
        await CaptureCompactGaugeStylesAsync(fixture, visual, output, cancellationToken);
        await CaptureCompactThemeFinishesAsync(fixture, visual, output, cancellationToken);
        await CaptureCompactNativePeaksAsync(fixture, visual, output, cancellationToken);
        await CaptureCompactUnavailableReadingsAsync(fixture, visual, output, cancellationToken);
        VerifyCompactFullViewRestoration(fixture);
        await File.WriteAllTextAsync(Path.Combine(output, "compact.json"), JsonSerializer.Serialize(new
        {
            NativeReadings = true, ButtonRoundTrip = true, PreferenceRoundTrip = true,
            FullSizeRestored = true, ChartTogglePreserved = true, OptionalDisk = true, MissingReadings = true,
            GapInFixture = true, AlwaysNativePeakLabelsAndTooltips = true, MemoryPressureDetails = true,
            Themes = new[] { "Dark", "Light" }.Concat(ThemeFinishes.Names),
            RenderScales = new[] { 1, 1.5, 2 }
        }), cancellationToken);
        return;
    }

    private async Task CheckWindowPositionRecoveryAsync(string output, CancellationToken cancellationToken)
    {
        var handle = new WindowInteropHelper(window).Handle;
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
