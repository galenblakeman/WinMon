# Update lifecycle coverage

## Goal

Exercise the real download and helper transaction paths with temporary files, signed fixture manifests and controlled OS boundaries. Keep the production trust, published-layout, process-identity and readiness checks in place.

## Requirements

| Requirement | Status |
| --- | --- |
| Establish updater test and coverage baseline | Verified: 39 existing Desktop updater/channel tests; DownloadAsync and RunHelper had no execution |
| Exercise signed installed and portable downloads, stream failures, cancellation and cleanup | Verified |
| Exercise helper rejection, commit/cancel, replacement, readiness and recovery | Verified |
| Preserve the original download error when staging cleanup fails | Verified with a real locked temporary file |
| Retain the original backup and avoid a success claim when rollback fails | Verified with a real locked replacement |
| Reject staging-volume low space before HTTP after accepting destination space | Verified |
| Keep Store updates disabled before environment or network access | Verified |
| Verify Desktop and Store suites and remeasure coverage | Verified |

## Boundaries

`UpdateClient` uses instance delegates for the process path, application directory, installation lookup and free-space query, with an instance stall timeout. Their defaults still use the running process, `AppContext.BaseDirectory`, uninstall registration, the native free-space query and the existing 60-second stall timeout. Tests provide a temporary published-layout target and fake HTTP responses; verification, copying, extraction, hashing and transaction files remain real.

`RunHelper` uses an internal `UpdateHelperHost` for process identity/launch, OS handles, clock and state writes. The default host preserves the existing Win32/.NET operations. The helper still validates the staging root and GUID, its own location, job token, exact target name, signed manifest, version, registered installation, asset size/hash and parent lifetime/path before touching the target. `UpdateFiles.ValidateDirectory` accepts the host's root internally; all existing callers retain the production root.

Helper tests use private named events/mutexes and fake parent/child/installer processes, so they never start downloaded bytes, install software, kill a real app or alter the installed WinMon. Replacement, rollback, preserved adjacent files, state journaling and cleanup use actual temporary files. Portable fixtures package the existing managed assembly as an executable-version fixture; it is inspected and copied, never launched.

## Verification

Verified on November 8, 2026, with SDK 10.0.403, NUnit 4.6.1, dotnet-coverage 18.6.2 and ReportGenerator 5.5.10:

- Full `tools/Verify.ps1` under coverage after the three failure-path additions: 1,354 Desktop tests passed; solution and benchmark built with zero warnings/errors.
- Full `tools/Verify.ps1 -Store` under coverage: 1,310 tests passed; solution and benchmark built with zero warnings/errors.
- The initial 44 Desktop cases (13 download, 31 helper) passed three additional repeated runs, 132 passed executions. The three subsequent failure-path cases also passed a focused run and the full profiled suite, bringing the new Desktop case count to 47. Store adds a helper case that proves rejection before any host access; existing Store download-policy tests remain in place.
- The three Explicit fixture-generation/elevated checks in each full run remained unexecuted. VSTest ran outside the restricted sandbox, consistent with the repository's testhost guidance.
- The production-path diff received a separate read-only review; no actionable defect was found. `git diff --check` passed.

Using the same Release coverage settings as the earlier baseline, excluding test/third-party assemblies and generated code:

| Method | Original lines / branches | Initial lifecycle tests | With failure-path tests |
| --- | --- | --- | --- |
| `DownloadAsync` (async state machine) | 0% / 0% | 91.8% / 95.5% | 96.7% / 100% |
| `RunHelper` | 0% / 0% | 97.7% / 96.5% | 100% / 96.5% |

The initial lifecycle tests raised `UpdateClient` as a whole from 45.7% to 91.9% line coverage and `UpdateRuntime` from 14.1% to 64.7%; the subsequent three cases further exercise both classes. Result notices and other startup methods outside these two lifecycle paths still have gaps. Baseline evidence remains in `artifacts/coverage`; initial lifecycle reports and repeated-run evidence are in `artifacts/update-lifecycle`. The latest Desktop/Store HTML, Cobertura, method comparison and focused test evidence are in `artifacts/update-failure-paths`. Full-run TRX files are in timestamped `artifacts/verify` directories.

The cleanup-failure test holds a staging file open without delete sharing and verifies that the exact original HTTP exception survives, the incomplete transaction remains unprepared and the target is untouched. The rollback-failure test locks the replaced executable, then verifies a nonzero helper result, the intact original backup, no recovery relaunch and no `succeeded` or `rolled-back` journal entry. The journal remains `awaiting-ready`; the original readiness failure is retained in `helper-error.log`. The low-space test independently accepts the destination query, rejects the staging query and verifies zero HTTP requests and cleanup.

Only the download cleanup handler for `UnauthorizedAccessException` remains uncovered at the line level in these two methods. The collector reports 100% branch coverage for `DownloadAsync` despite that unexecuted catch handler; branch coverage alone does not prove every exception path ran. No coverage-percentage gate was added. Real signed installer execution, UAC, cross-process handle behavior and packaged update acceptance remain separate integration requirements; UI smokes were not run because layout and visible behavior were unchanged.

## Result and departures

November 10, 2026: startup cleanup now reclaims abandoned/terminal staging directories from removed app copies after seven days without directory changes. It requires the original application directory to be absent and its parent process to have exited, and acquires the update mutex before deleting staging files. Active tokens, existing application directories, recent transactions and pending recovery states are preserved. `UpdateTests` covers these conditions and competing helper ownership. Cleanup does not follow the job into another application directory or remove recovery backups there.

Landed `UpdateDownloadTests`, `UpdateHelperTests` and the internal seams in `UpdateClient`, `UpdateFiles`, `UpdateHelperHost` and `UpdateRuntime`. Coverage includes signed-metadata selection despite a forged cached manifest object, malformed/tampered signatures before HTTP, size/hash failures, allowed/disallowed/excessive redirects, cancellation, low space, stalled reads, portable extraction, parent identity, guard ownership, handoff abort, installed and portable readiness, rollback, and healthy-update journal failure.

No user setting, command-line override, public API, dependency, sampling path or visible UI change was added. Platform seams are internal and supplied per instance/call; the installed trust key and production timeouts remain the defaults. The change preserves the lifecycle algorithm rather than redesigning the updater. Test coverage is evidence for transaction behavior, not a replacement for signed installed-update acceptance.
