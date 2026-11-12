# Unit-test verification

## Goal

Make the test suite's execution complete, repeatable and useful in pull-request review without turning native or elevated checks into ordinary unit tests.

## November 11 follow-through

The eight-project checkout uses NUnit 4.6.1, NUnit3TestAdapter 5.2.0 and Microsoft.NET.Test.Sdk 18.12.0. This work used SDK 10.0.404 through the pinned SDK's latest-patch roll-forward. Existing package upgrades and unrelated working-tree edits were preserved.

| Requirement | Status and evidence |
| --- | --- |
| Establish Desktop and Store unit baselines | Verified: 1,677 and 1,625 passed respectively, with zero failures; TRX snapshots are `20261111-210103-153-desktop` and `20261111-210131-481-store` under `artifacts/verify` |
| Remove incidental MCP sampling waits | Verified: wait for published-frame notifications rather than sleeping/polling; retention waits for publication rather than the source's earlier capture count |
| Control CLI debounce time | Verified: an internal `TimeProvider` preserves the production 500 ms delay; tests check 499/500 ms, restart on a later change, and forced shutdown flushing |
| Separate filesystem/native integration work | Verified: persistence, history files, extraction, update transactions and native helpers are categorized as Integration; pure normalization/ranking/state tests remain in Unit where fixture setup permits |
| Preserve shared-state isolation | Verified by source inspection: process-wide PATH/CI/default-culture/Trace changes already restore their state and are nonparallel; setup scratch data now uses unique OS-temp directories rather than build output; no global parallelism was enabled |
| Add missing high-impact boundary checks | Verified: future/current/1,999/2,000/2,001 ms freshness cases, readers blocked until the second capture, debounce restart/force-flush, and release of blocked captures even on assertion failure |
| Prevent omitted projects and misleading green reports | Verified: automatic test-project discovery with solution membership checks; exact expected skip reasons; counter/result consistency; rejection of missing/malformed/aborted reports and completely empty selections; dependency-free self-checks run before every build |
| Retain existing recovery and data-integrity coverage | Verified: complete suites and scope-identity audit retain PID reuse, history expiry, settings preservation, update signature/cancellation and rollback cases |
| Update current verification guidance | Verified: `AGENTS.md` lists eight projects and `docs/DEVELOPMENT.md` describes discovery, categories, skip policy and empty-filter behavior |

The baseline's slowest unit cases included the 125-frame retention exercise, settings concurrent writes, snapshot sampling and network diagnostic recovery. The live sampling and real file-lock tests retain their timing requirements. The debounce case changed from 623.4 ms in the Desktop baseline to 16.8 ms in the fifth repeated run; these are individual observed durations, not a speed guarantee. No dependency or public API was added, and sampling/rendering code was not changed.

Existing tests already cover process lifetime mismatch (`DiagnosticToolsTests`, `PortableIdentityTests`, `ProcessNetworkSessionTests`), history expiry on either side of the boundary (`ConnectionHistoryTests`, `MonitorHistoryTests`), corrupt/locked settings (`PreferenceStoreTests`, `CliSettingsTests`), and cancellation/signatures/rollback (`UpdateDownloadTests`, `UpdateHelperTests`, `SetupTransactionTests`). Those cases were retained rather than duplicated. Four deliberately introduced errors were detected by the intended assertions: rejecting exactly two-second-old frames, accepting only one warmup frame, delaying the exact 500 ms write, and failing to restart debounce. All four mutations were restored; their expected-failure reports are under `artifacts/unit-improvements/mutations`.

The verifier self-check covers passing/failed/aborted/inconsistent/missing/malformed reports, expected and unexpected skips, altered skip reasons, variant-specific skips and solution omissions. An actual `Verify.ps1 -TestFilter FullyQualifiedName~IntentionallyNoSuchWinMonTest` run exited 1 with `FAIL test selection: 0 tests executed`.

Desktop Unit passed 1,322 tests and Integration passed 408; Store Unit passed 1,324 and Integration passed 349. Comparing full test identities, including skipped cases, found zero overlap and zero missing cases between the two scopes and the full suites. The audit script and JSON are under `artifacts/unit-improvements`. Desktop has seven expected skips; Store has sixteen. Native elevated and fixture-generation checks remain opt-in.

Final `./tools/Verify.ps1` passed 1,730 tests (`artifacts/verify/20261111-211756-616-desktop`); final `./tools/Verify.ps1 -Store` passed 1,673 (`20261111-211900-238-store`). Both built the solution and benchmark with zero warnings/errors and reported zero unexpected skips. Final MCP sampling tests and CLI settings tests each passed five repetitions; together with ten setup-suite repetitions, the final repeated reports contain 845 passed test executions. `git diff --check` and the verifier self-check also passed. Test execution used unrestricted processes after the sandboxed baseline host stalled.

Store runs exposed intermittent `File.Replace` and directory-rename failures in rollback/removal tests that created scratch files beneath test binaries. Five isolated rollback reruns and one integration rerun passed, but a later full run reproduced three failures. Setup scratch roots were then moved into unique OS-temp directories, matching the other filesystem fixtures. Ten complete Store setup-suite runs passed (650 test executions). The real file operations and assertions were retained; no blanket retry, new skip or setup runtime change was added. Failed reports remain in `artifacts/verify/20261111-211114-564-store` and `20261111-211511-332-store`. The external cause of the file access failures is unconfirmed.

UI smoke windows, hosted CI, elevated ETW/setup checks and application performance benchmarks were not run for these test/tooling changes. The following sections retain the November 8 record and describe the smaller checkout measured then.

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
