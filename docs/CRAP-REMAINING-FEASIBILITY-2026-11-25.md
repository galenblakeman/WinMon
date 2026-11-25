# Remaining CRAP feasibility — November 25, 2026

The current mixed inventory contains **142 methods above 30, including 32 above 50**. This analysis preserves every finding and changes no score, production code or test. The 198 original methods now scoring at most30 remain a cumulative numerical count, not proof that every behavior or secondary coverage target is complete.

**113 remaining methods have cyclomatic complexity above 30.** With the CRAP formula `c² × (1 − coverage)³ + c`, their minimum possible score at full line coverage is already above 30. Testing can still improve regression safety and branch coverage, but reaching30 requires a justified reduction in complexity. Of 32 methods above 50, 19 have complexity above 50; 13 could mathematically reach 50 through coverage alone. The other 29 methods in the 142 could mathematically reach 30 through tests. These categories are mathematical bounds, not recommendations to force otherwise unreachable conditions.

The audit joins each remaining row to exactly one inherited whole-method observation by module, source file, class, method and signature; verifies complexity and score agreement; applies exact decimal threshold inequalities using covered/total sequence lines; hashes current source files; and verifies all 30 monitored production assembly copies remain unchanged. Source hashes identify the files inspected; this pass does not refresh PDB or coverage evidence. No coverage union, fresh global coverage, build, test, benchmark, native operation or remediation claim is introduced.

## Above50 methods that could mathematically reach 50 through tests

The required count is the smallest executable-line count satisfying the formula, independent of whether that combination is reachable. It does not establish branch coverage or safe access to native resources. Detailed existing dispositions and next work remain in the JSON audit and unchanged inventory.

| Module / method | CRAP unchanged | Complexity | Covered / total lines | Minimum lines for 50 | Additional lines |
|---|---:|---:|---:|---:|---:|
| `MonCore.Windows / WinMon.Diagnostics.DiagnosticLog.StartAppCore | 72 | 8 | 0 / 11 | 2` | 2 |
| `WinMon / WinMon.Controls.ProcessDumpDialog.<>c__DisplayClass3_1.<<-ctor>b__2>d.MoveNext | 79.48 | 10 | 4 / 35 | 10` | 6 |
| `WinMon / WinMon.Diagnostics.WindowDiagnostics.<CaptureExamplesAsync>d__32.MoveNext | 57.9 | 16 | 53 / 117 | 58` | 5 |
| `WinMon / WinMon.Monitoring.NetworkSetupBroker.<RunAsync>d__5.MoveNext | 894.48 | 30 | 1 / 75 | 54` | 53 |
| `WinMon.Cli / WinMon.Monitoring.NetworkSetupBroker.<RunAsync>d__5.MoveNext | 894.48 | 30 | 1 / 75 | 54` | 53 |
| `WinMon.NetworkProbe / WinMon.NetworkProbe.NetworkCapture.<DrainBytesAsync>d__21.MoveNext | 110 | 10 | 0 / 14 | 4` | 4 |
| `WinMon.NetworkProbe / WinMon.NetworkProbe.Program.<Main>d__1.MoveNext | 1710.77 | 50 | 13 / 102 | 102` | 89 |
| `WinMon.NetworkSetup / WinMon.NetworkSetup.NativeService.CreateOrUpdateOwned | 210 | 14 | 0 / 25 | 11` | 11 |
| `WinMon.NetworkSetup / WinMon.NetworkSetup.SetupService.DeleteInstallation | 125.26 | 12 | 1 / 13 | 5` | 4 |
| `WinMon.NetworkSetup / WinMon.NetworkSetup.SetupService.ProtectTree | 156 | 12 | 0 / 24 | 9` | 9 |
| `WinMon.NetworkSetup / WinMon.NetworkSetup.SetupService.<>c__DisplayClass7_0.<Install>b__1 | 552.03 | 24 | 1 / 35 | 23` | 22 |
| `WinMon.NetworkSetup / WinMon.NetworkSetup.SetupService.<BeginAsync>d__4.MoveNext | 117.98 | 14 | 4 / 21 | 10` | 6 |
| `WinMon.NetworkSetup / WinMon.NetworkSetup.SetupService.<RevokeAsync>d__6.MoveNext | 97.35 | 12 | 4 / 25 | 9` | 5 |

## Sampling follow-up

`WindowsResourceSampler.DiskCounter.Read` remains 35.73 at complexity 32,84.6% line and53.1% branch coverage. It owns real PDH handles and starts an asynchronous drive-space read; its reset discards the task reference. A missing-drive test must observe and finish its own work and must not assume a particular provider error or alter a real drive. Additional tests alone cannot cross 30. No private fields were changed to force provider failures and no production seam was added in this pass.

`WindowsGpuMemory.Attach` remains 48.24 at complexity 24,65.2% line and54.2% branch coverage. It already meets the revised 50 maximum. Uncovered alternatives depend on DXGI/PDH failures, absent shared counters and adapter capacity. Existing read-only GPU integration tests are hardware dependent; repeating them cannot deterministically establish those alternatives. A controlled collection boundary would require original-implementation characterization and performance evidence before adoption. No adapter, driver or counter configuration was changed.

## Next actions and limits

Continue to prioritize the 32 above 50. For the 13 without a complexity floor above 50, establish owned native/process/package/UI fixtures before executing their normal paths; retain broker, dump-dialog, foreground capture, native ETW and protected installer blockers. For the 19 with complexity above 50, only cohesive maintainability improvements can meet 50. Preserve earlier rejected performance candidates and their frozen gates; do not rerun identical candidates or split methods merely for the score. The 113 floor-above 30 findings remain visible even where the revised maximum is met. All 38 additional open verification scopes and eight supplemental Store findings remain open.

Evidence: `artifacts/crap/remaining-feasibility-2026-11-25/{audit.ps1,current-audit.json,method-audit.json}`. Re-run the audit after the inventory or source changes. The source metric snapshot is `artifacts/crap/broker-unavailable-bundle/final-metrics.json`; it contains inherited observations, not newly collected coverage. The remaining inventory and its hash are unchanged. Production files were unchanged, so no benchmark was necessary and no performance improvement is claimed.
