# CRAP above-50 feasibility review — November 25, 2026

This is a source and existing-metrics audit, not a remediation batch or a fresh coverage run. All32 above50 findings remain. At the time of this audit,the mixed inventory contained5878 compiled methods,151 above30,32 above50 and119 in the30–50 band;189 original reported methods scored≤30. These are historical snapshot counts;see the [main report](CRAP-REMEDIATION-2026-11-21.md) for current counts. Thirty-three additional open verification scopes and eight supplemental Store findings remain.

The exact formula gives CRAP=complexity at100% line coverage. **19 of32 findings have a minimum score above50**, including seven cohesive literal mappings/allowlists. Further tests can improve their behavioral protection but cannot meet the revised maximum without production changes. The remaining13 can mathematically reach50 with more line coverage; that does not prove a safe test path exists, that those line combinations are reachable, or that branch/business coverage targets will be met. NetworkProbe.Main has complexity50 and requires all102 sequence lines to reach50.

## Production risk and next work

1. Setup service ownership, deployment and revocation: retain current protected-lock/tree/security boundaries. An injected lock alone cannot make downstream protected inventory/policy operations safe; no seam was implemented. A useful future fixture must own all affected protected files and service/registry state. Do not run against the installed service for coverage.
2. Broker installation workflow (desktop and CLI): signed resources, runas child and authenticated pipe require an isolated owned end-to-end boundary. Preserve cancellation, rollback, authentication and publisher verification.
3. Logging startup: the default paths and global Trace/event attachment are real process/user state. Existing owned-path CreateApp tests do not authorize rotating or attaching to user logs.
4. Process dump UI and diagnostic examples: two dump seams already failed fixed performance acceptance; examples retain their foreground/window constraints. Do not replay withdrawn candidates or change diagnostic assertions to reach a score.
5. Probe/ETW tools: DrainBytesAsync calls concrete session.Flush before its cancellation-aware delay. An already-canceled token does not avoid that native call. Existing passive accounting and worker-entry tests are not active ETW drain evidence.
6. Complexity-floor methods: use cohesive refactoring only where responsibilities improve and the existing regression/performance gates can pass. Retain the seven literal maps as findings rather than introducing lookup caches/tables solely to reduce compiler-derived scores. Existing rejected candidates and appearance defects remain blockers.

## Methods where tests could mathematically reach50

The rows below are ordered by production responsibility before numerical score. Required lines are the smallest integer satisfying the exact decimal CRAP inequality at the current complexity. They are planning bounds, not acceptance thresholds or substitutes for meaningful assertions.

| Method | Current CRAP | Complexity | Covered lines | Minimum lines for ≤50 | Additional lines | Minimum line coverage |
|---|---:|---:|---:|---:|---:|---:|
| WinMon.NetworkSetup: WinMon.NetworkSetup.SetupService.<>c__DisplayClass7_0.<Install>b__1 | 552.03 | 24 | 1/35 | 23 | 22 | 65.71% |
| WinMon.NetworkSetup: WinMon.NetworkSetup.NativeService.CreateOrUpdateOwned | 210 | 14 | 0/25 | 11 | 11 | 44% |
| WinMon.NetworkSetup: WinMon.NetworkSetup.SetupService.ProtectTree | 156 | 12 | 0/24 | 9 | 9 | 37.5% |
| WinMon.NetworkSetup: WinMon.NetworkSetup.SetupService.DeleteInstallation | 125.26 | 12 | 1/13 | 5 | 4 | 38.46% |
| WinMon.NetworkSetup: WinMon.NetworkSetup.SetupService.<BeginAsync>d__4.MoveNext | 117.98 | 14 | 4/21 | 10 | 6 | 47.62% |
| WinMon.NetworkSetup: WinMon.NetworkSetup.SetupService.<RevokeAsync>d__6.MoveNext | 97.35 | 12 | 4/25 | 9 | 5 | 36% |
| WinMon.Cli: WinMon.Monitoring.NetworkSetupBroker.<RunAsync>d__5.MoveNext | 930 | 30 | 0/75 | 54 | 54 | 72% |
| WinMon: WinMon.Monitoring.NetworkSetupBroker.<RunAsync>d__5.MoveNext | 930 | 30 | 0/75 | 54 | 54 | 72% |
| MonCore.Windows: WinMon.Diagnostics.DiagnosticLog.StartAppCore | 72 | 8 | 0/11 | 2 | 2 | 18.18% |
| WinMon: WinMon.Controls.ProcessDumpDialog.<>c__DisplayClass3_1.<<-ctor>b__2>d.MoveNext | 79.48 | 10 | 4/35 | 10 | 6 | 28.57% |
| WinMon: WinMon.Diagnostics.WindowDiagnostics.<CaptureExamplesAsync>d__32.MoveNext | 57.9 | 16 | 53/117 | 58 | 5 | 49.57% |
| WinMon.NetworkProbe: WinMon.NetworkProbe.Program.<Main>d__1.MoveNext | 1710.77 | 50 | 13/102 | 102 | 89 | 100% |
| WinMon.NetworkProbe: WinMon.NetworkProbe.NetworkCapture.<DrainBytesAsync>d__21.MoveNext | 110 | 10 | 0/14 | 4 | 4 | 28.57% |

## Complete above50 ledger

Original and final metrics are identical for this review. Coverage is existing mixed-inventory evidence, not freshly collected. Complexity is unchanged. No finding has been suppressed or removed.

| Method | Original CRAP | Final CRAP | Line / branch coverage before and after | Complexity before and after | Minimum CRAP at full line coverage | Action |
|---|---:|---:|---:|---:|---:|---|
| MonCore.Windows: WinMon.Diagnostics.DiagnosticLog.StartAppCore | 72 | 72 | 0% / 0% | 8 | 8 | Owned boundary required before more tests |
| WinMon.Cli: WinMon.Cli.CliCommand.<>c__DisplayClass0_0.<<Create>b__3>d.MoveNext | 60.64 | 60.64 | 86.8% / 70.4% | 54 | 54 | Cohesive refactoring and verification required |
| WinMon.Cli: WinMon.Cli.InteractiveMonitor.<>c__DisplayClass0_0.<Run>b__6 | 302.86 | 302.86 | 57.1% / 62.5% | 56 | 56 | Cohesive refactoring and verification required |
| WinMon.Cli: WinMon.Cli.InteractiveState.Handle | 231 | 231 | 100% / 98.7% | 231 | 231 | Cohesive refactoring and verification required |
| WinMon.Cli: WinMon.Monitoring.NetworkSetupBroker.<RunAsync>d__5.MoveNext | 930 | 930 | 0% / 0% | 30 | 30 | Owned boundary required before more tests |
| WinMon.Cli: WinMon.Monitoring.NetworkSetupBroker.IsFailurePhase | 58 | 58 | 100% / 87.9% | 58 | 58 | Retain cohesive literal dispatch |
| WinMon.NetworkProbe: WinMon.NetworkProbe.NetworkCapture.<DrainBytesAsync>d__21.MoveNext | 110 | 110 | 0% / 0% | 10 | 10 | Owned boundary required before more tests |
| WinMon.NetworkProbe: WinMon.NetworkProbe.Program.<Main>d__1.MoveNext | 1710.77 | 1710.77 | 12.7% / 30% | 50 | 50 | Owned boundary required before more tests |
| WinMon.NetworkSetup: WinMon.NetworkSetup.NativeService.CreateOrUpdateOwned | 210 | 210 | 0% / 0% | 14 | 14 | Owned boundary required before more tests |
| WinMon.NetworkSetup: WinMon.NetworkSetup.SetupService.<>c__DisplayClass7_0.<Install>b__1 | 552.03 | 552.03 | 2.9% / 0% | 24 | 24 | Owned boundary required before more tests |
| WinMon.NetworkSetup: WinMon.NetworkSetup.SetupService.<BeginAsync>d__4.MoveNext | 117.98 | 117.98 | 19% / 50% | 14 | 14 | Owned boundary required before more tests |
| WinMon.NetworkSetup: WinMon.NetworkSetup.SetupService.<RevokeAsync>d__6.MoveNext | 97.35 | 97.35 | 16% / 8.3% | 12 | 12 | Owned boundary required before more tests |
| WinMon.NetworkSetup: WinMon.NetworkSetup.SetupService.DeleteInstallation | 125.26 | 125.26 | 7.7% / 0% | 12 | 12 | Owned boundary required before more tests |
| WinMon.NetworkSetup: WinMon.NetworkSetup.SetupService.ProtectTree | 156 | 156 | 0% / 0% | 12 | 12 | Owned boundary required before more tests |
| WinMon: WinMon.App.<OnStartup>d__5.MoveNext | 14280 | 14280 | 0% / 0% | 119 | 119 | Cohesive refactoring and verification required |
| WinMon: WinMon.Controls.ConnectionsView.RefreshRows | 130 | 130 | 100% / 99.2% | 130 | 130 | Cohesive refactoring and verification required |
| WinMon: WinMon.Controls.ProcessActionBar.CreateMoreMenu | 68 | 68 | 100% / 100% | 68 | 68 | Cohesive refactoring and verification required |
| WinMon: WinMon.Controls.ProcessDumpDialog.<>c__DisplayClass3_1.<<-ctor>b__2>d.MoveNext | 79.48 | 79.48 | 11.4% / 30% | 10 | 10 | Owned boundary required before more tests |
| WinMon: WinMon.Controls.ProcessRow.Update | 98 | 98 | 100% / 99% | 98 | 98 | Cohesive refactoring and verification required |
| WinMon: WinMon.Diagnostics.WindowDiagnostics.<CaptureActivityPanelAsync>d__0.MoveNext | 127.79 | 127.79 | 90.4% / 74.1% | 116 | 116 | Cohesive refactoring and verification required |
| WinMon: WinMon.Diagnostics.WindowDiagnostics.<CaptureExamplesAsync>d__32.MoveNext | 57.9 | 57.9 | 45.3% / 62.5% | 16 | 16 | Owned boundary required before more tests |
| WinMon: WinMon.Diagnostics.WindowDiagnostics.<CaptureGaugeDiagnosticsAsync>d__35.MoveNext | 3448.98 | 3448.98 | 30.6% / 24% | 100 | 100 | Cohesive refactoring and verification required |
| WinMon: WinMon.Diagnostics.WindowDiagnostics.<CapturePinsFixtureAsync>d__37.MoveNext | 106.14 | 106.14 | 89.7% / 68.8% | 96 | 96 | Cohesive refactoring and verification required |
| WinMon: WinMon.MainWindow.UpdateMetricDetails | 156 | 156 | 100% / 91% | 156 | 156 | Cohesive refactoring and verification required |
| WinMon: WinMon.MainWindow.<OnExport>d__79.MoveNext | 63 | 63 | 100% / 100% | 63 | 63 | Cohesive refactoring and verification required |
| WinMon: WinMon.Monitoring.NetworkSetupBroker.<RunAsync>d__5.MoveNext | 930 | 930 | 0% / 0% | 30 | 30 | Owned boundary required before more tests |
| WinMon: WinMon.Monitoring.NetworkSetupBroker.IsFailurePhase | 58 | 58 | 100% / 87.9% | 58 | 58 | Retain cohesive literal dispatch |
| WinMon: WinMon.Preferences.ChartPalettes.Colors | 116 | 116 | 100% / 78.4% | 116 | 116 | Retain cohesive literal dispatch |
| WinMon: WinMon.Preferences.ChartPalettes.DisplayName | 86 | 86 | 100% / 70.9% | 86 | 86 | Retain cohesive literal dispatch |
| WinMon: WinMon.Preferences.ChartPalettes.ForTheme | 52 | 52 | 100% / 73.1% | 52 | 52 | Retain cohesive literal dispatch |
| WinMon: WinMon.Preferences.PreferenceStore.NormalizeTheme | 76 | 76 | 100% / 71.1% | 76 | 76 | Retain cohesive literal dispatch |
| WinMon: WinMon.Preferences.ThemeFinishes.For | 52.03 | 52.03 | 97.8% / 57.7% | 52 | 52 | Retain cohesive literal dispatch |

## Verification and limits

All24 existing production DLL copies match the previous batch, all720 prior inputs remain unchanged, and the last added trend fixture still matches its tested PDB hash. Each reviewed source file is SHA256-pinned in artifacts/crap/high-risk-triage-2026-11-25/method-audit.json. The full metrics and remaining CSV hashes are unchanged; the32 priority entries match the exact above50 method set. Priority metadata now records complexity floors, whether tests alone can mathematically reach50, and minimum line counts. The seven literal-map entries now name their concrete source-based disposition instead of saying only that they were not remediated.

No production or test source changed, no build/test/native operation ran, and no performance timing was needed. This review adds no remediation count and does not establish full behavioral equivalence, global coverage, appearance acceptance or production performance. Keep the goal active; continue meaningful safe remediation and retain every unresolved score/coverage/behavioral gap.

Evidence: artifacts/crap/high-risk-triage-2026-11-25/{current-audit,method-audit,context-before,priority-before}.json. Latest measured batch remains trend-navigation; this separate audit does not replace its passing test evidence.
