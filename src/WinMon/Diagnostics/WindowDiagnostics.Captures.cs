// Exact stored layout values and channels returned by Max require exact comparison.
// ReSharper disable CompareOfFloatsByEqualityOperator

using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using WinMon.Monitoring;
using WinMon.Preferences;

// ReSharper disable StringLiteralTypo

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    /// <summary>
    /// Inject illustrative CPU and disk runs into a fixture window with automatic captures on, then check the
    /// markers, report files, Capture-mode selection, the card banner and the captures menu, and render them.
    /// </summary>
    internal static async Task CaptureAutoCaptureDiagnosticsAsync(string output, CancellationToken cancellationToken)
    {
        var reports = Path.Combine(output, "reports");
        var fixture = new MainWindow(true) { Width = 1200, Height = 950 };
        fixture.Loaded -= fixture.OnLoaded;
        fixture.Left = SystemParameters.WorkArea.Left + 20;
        fixture.Top = SystemParameters.WorkArea.Top + 20;
        fixture.Preferences = fixture.Preferences with
        {
            AutoCapture = new AutoCapturePreferences
            {
                Enabled = true, Folder = reports, CooldownMinutes = 1,
                Rules =
                [
                    .. CaptureRuleSetting.Defaults.Select(rule => rule.Metric switch
                    {
                        CaptureMetric.Cpu => rule with { Seconds = 5 },
                        CaptureMetric.PrimaryDisk => rule with { Enabled = true, Seconds = 5 },
                        _ => rule
                    })
                ]
            }
        };
        try
        {
            SeedAutoCaptureScenario(fixture);
            var markers = fixture.CaptureMarkers;
            var files = await VerifyAutoCaptureReportsAsync(markers, reports, cancellationToken);
            await CaptureAutoCaptureToolbarAsync(fixture, output, cancellationToken);
            await CaptureAutoCaptureMarkersAsync(fixture, markers, output, cancellationToken);
            await CaptureFlightRecorderAsync(fixture, output, cancellationToken);
            var overflow = await CaptureAutoCaptureSettingsAsync(fixture, reports, output, cancellationToken);

            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Closed += (_, _) => closed.TrySetResult();
            var delayed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var write = fixture.WriteCaptureForFixtureAsync(markers[0], delayed.Task);
            try
            {
                fixture.Close();
                await Dispatcher.Yield(
                    DispatcherPriority.ApplicationIdle);
                if (closed.Task.IsCompleted)
                    throw new InvalidOperationException("Shutdown must wait for an automatic capture report write.");
                delayed.SetResult(markers[0].ReportPath!);
                await write.WaitAsync(cancellationToken);
                await closed.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                delayed.TrySetResult(markers[0].ReportPath!);
            }

            await File.WriteAllTextAsync(Path.Combine(output, "captures.json"), JsonSerializer.Serialize(new
            {
                Markers = markers.Select(m => new { m.Rule, m.ReportPath, Peak = m.Peak.Timestamp }),
                Reports = files.Select(Path.GetFileName),
                SettingsOverflow = overflow,
                ShutdownWaitedForReport = true
            }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        }
        finally
        {
            fixture.CloseForFixture();
        }
    }
}
