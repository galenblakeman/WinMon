# Unit-test verification

## Goal

Make the test suite's execution complete, repeatable and useful in pull-request review without turning native or elevated checks into ordinary unit tests.

## Requirements

| Requirement | Status |
| --- | --- |
| Build and run every test project through the normal verification command | Verified |
| Write independently identifiable test results, including skipped-test reasons | Verified |
| Run unit and integration tests separately | Verified locally; hosted CI execution remains unverified |
| Keep the native ETW experiment opt-in and elevated | Verified excluded from normal and filtered runs; native experiment not run |
| Replace deterministic recovery-test timing waits with controllable execution | Verified |
| Add recovery, shutdown and history-boundary regression coverage | Verified |
| Preserve existing persistence, update-signature and rollback regressions | Verified in Desktop and Store suites |
| Repeat timing-sensitive verification runs | Verified, five repetitions per fixture group |

## Verification record

On November 8, 2026, using SDK 10.0.403 (the pinned 10.0.402 SDK permits latest-patch roll-forward):

- Baseline: solution and benchmark built with zero warnings; the five previously included projects passed 1,285 tests. Three Explicit cases were not executed.
- `./tools/Verify.ps1`: six projects, 1,307 passed, zero failed; solution and benchmark builds had zero warnings.
- `./tools/Verify.ps1 -Store`: six projects, 1,309 passed, zero failed; solution and benchmark builds had zero warnings.
- `./tools/Verify.ps1 -TestScope Unit`: 1,265 passed. `-TestScope Integration`: 42 passed. Comparing passed test identities in the TRX files confirmed the scopes are disjoint and their union equals the complete Desktop run. Empty integration projects still produce result files.
- Five repeated runs each of `MonitorSessionTests` plus `ConnectionHistoryTests`, `NetworkMonitoringControllerTests`, and `DiagnosticLogTests`: 415 passed test executions across 15 runs, zero failed.
- The result-summary function was checked against real Explicit and all-passed reports, plus a missing path. The adapter's TRX `notExecuted` counter was zero despite an actual `NotExecuted` result; reporting therefore counts individual results and includes their reasons.

Test runs used an unrestricted process after the sandboxed VSTest host stalled. The normal final verification retained NuGet auditing. The WinMon test project took 56 seconds in the baseline and 11 seconds after replacing polling waits; these are observed run durations, not an application performance benchmark.

Local evidence is under `artifacts/test-baseline`, `artifacts/test-repeat`, and the timestamped `artifacts/verify` directories. Hosted CI requires a branch push or pull request and was not executed. UI smoke modes, native elevated ETW/setup checks, and macOS portability were not exercised by this test-focused change.

## What landed

`Verify.ps1` builds and tests `WinMon.NetworkProbe.Tests` with the existing five projects. It can select all tests, non-integration tests, or `Integration` tests and writes one TRX file per project in a unique run directory. It reads TRX counters rather than inferring skipped tests from the adapter's console output, and forces NUnit3TestAdapter 5.2.0's `NUnit.ExplicitMode=None` so filters cannot run an elevated or fixture-generation check. Pull requests and branches run Desktop and Store unit/integration matrices without UI smoke windows.

The network controller retains its production one-second polling and five-second retry intervals, with an internal delay delegate for controlled tests. Regressions cover repeated outages, cancellation of pending/in-flight recovery, a connection completing after disable/shutdown, capture cancellation with a late frame, elapsed retention immediately before/at/after expiry on both clocks, and reclaiming history-entry budget after resizing. The UNC free-space check is separate from the local case, so a missing share cannot hide a passed local assertion behind an ignored test.

The logger rotation-failure test observes dropped entries instead of sleeping for an assumed writer duration; disposal provides completion before file assertions. Verification also exposed a shutdown race where the writer could exit before the caller queued repeated-error summaries. The writer now performs a final summary and drain before closing; the existing summary regression and five repeated logger runs pass.

## Departure from the design

No coverage percentage gate was added. A percentage would measure lines without showing whether native Windows boundaries or failure recovery are covered.

Existing persistence tests already cover concurrent writes, interrupted replacements and rollback recovery, so they were retained rather than duplicated. No dependency, user setting, public API or visible layout was added. Production sampling/rendering paths were not restructured; the runtime change is limited to the final logger drain and the controller's internal delay seam.
