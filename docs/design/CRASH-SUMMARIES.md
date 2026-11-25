# Crash summaries in History, CLI and MCP

Date: November 25, 2026. Status: implemented in the working tree. The final section records delivered scope, verification and remaining acceptance limits.

## Purpose and scope

Answer three questions: **what crashed, how often, and what was the machine doing at the time?** Windows already lists crashes in Reliability Monitor and Event Viewer. WinMon adds two things those tools lack: the resource recording around each crash, and safe access for agents.

WinMon already reads Application Error 1000, Application Hang 1002 and .NET Runtime 1026 events into History. This feature reads the structured fields those events already carry, groups recurring failures by signature, shows the groups in History, and exposes the same groups through one CLI command and one MCP tool.

Outside this design:

- `.wer` report file parsing, folder selection, imported reports from other machines and extended disk search.
- A separate analyzer window, saved crash investigations and a new file format.
- Dump inspection and debugger execution. WinDbg already does this, and it carries the highest risk for the least fit with a resource monitor.
- Repair actions, LocalDumps changes, report deletion, background scanning and generated diagnoses.

## Repository baseline

At design time the repository references product version 4.2.0, SDK 10.0.402 with latest-patch roll-forward, .NET 10 Windows, System.CommandLine 2.0.15, Spectre.Console 0.57.2 and ModelContextProtocol 2.4.0. Recheck before implementation.

- [WindowsHistoryEventReader](../../src/MonCore.Windows/Monitoring/WindowsHistoryEventReader.cs) queries the Application and System logs for a History range, with a 500-event cap and a 5-second budget. `ReadProcessLifetime` already walks the named `EventData` of Application Error 1000 to read `ProcessId` and `ProcessCreationTime`.
- [IncidentEvent](../../src/MonCore/Monitoring/IncidentEvent.cs) holds a display-safe event: time, provider, ID, level, a first-line message of at most 300 characters and an optional process lifetime. `IncidentEventMapper` places events on the History timeline and links them to retained processes without trusting a PID alone.
- [IncidentRecording](../../src/MonCore/Monitoring/IncidentRecording.cs) persists those events in `.wmincident` files at version 2 and validates every field on load. Its JSON options ignore unknown members.
- The CLI uses `--format table|json`, `--no-color` and exit codes 0, 1 and 130. MCP tools use the `DiagnosticResponse` envelope, a 256 KiB budget and `ReadOnly`, `Destructive` and `OpenWorld` annotations.

## Crash signature

Add an optional `CrashSignature` to `IncidentEvent`:

| Field | Source |
| --- | --- |
| `Kind` | `Crash` for Application Error 1000, `Hang` for Application Hang 1002 |
| `Application`, `ApplicationVersion` | Executable name and file version |
| `Module`, `ModuleVersion` | Faulting module name and version (crashes only) |
| `ExceptionCode` | Unsigned 32-bit code (crashes only) |
| `Offset` | Unsigned 64-bit faulting offset (crashes only) |

Read the fields in the same pass over `EventData` that `ReadProcessLifetime` already makes. Application Error 1000 uses named data (`AppName`, `AppVersion`, `ModuleName`, `ModuleVersion`, `ExceptionCode`, `FaultingOffset`). Verify the Application Hang 1002 layout against real fixtures; if its data is unnamed, read it positionally only for layouts the fixtures cover. An unrecognized layout leaves the signature null, and the event stays in History as it is today.

Keep names only. Do not retain `AppPath`, `ModulePath` or report paths; this keeps user paths out of History files, CLI output and MCP responses. Parse hex and versions with the invariant culture, cap each text field at 260 characters, and reject control characters. .NET Runtime 1026 carries only free text, so it gets no signature; a .NET crash normally also logs Application Error 1000 with exception code `0xE0434352`, which does get one.

## Grouping

Add a portable `CrashGroups.Build(IEnumerable<IncidentEvent>)` in `MonCore`. The group key is kind + application (case-insensitive) + module (case-insensitive) + exception code + offset. Versions are not part of the key, so a failure that keeps happening across updates stays one group; each group lists its application and module versions with a count for each. Events without a signature are never grouped.

Each group returns its key, total count, first and last time, version breakdown and member events. Show a short name for well-known exception codes, such as access violation (`0xC0000005`), stack buffer overrun (`0xC0000409`) and unhandled .NET exception (`0xE0434352`), alongside the hex code. A faulting module that ships with Windows is a location, not a cause; display it without implying otherwise.

The event log records one event per crash, so there are no report copies to deduplicate. Counts are events in the retained log for the requested range, not an authoritative crash count.

## History

When History reads events for a frozen range, it groups the signed events and shows a **Crashes** summary next to the existing event list: application, failure (module and exception name, or "Not responding" for hangs), count and latest time. Selecting a group highlights its events on the timeline through the existing `IncidentEventMapper` placement, which already links an Application Error event to its process when the lifetime matches. Nothing new is read; the summary is built from events History already has.

Persist the signature in `.wmincident` as an optional member and keep version 2. Older builds ignore the member and lose only the grouping; newer builds validate it with the same bounds as the reader. Add the validation to `IncidentRecording.ValidateEvent`.

All visible text, tooltips and accessibility names go in `Strings.resx` and the nine translations. The summary needs keyboard access, non-color cues and dark/light layouts, and follows the existing History styling.

## CLI

The CLI exposes one command:

```text
WinMon.Cli.exe crashes --days 30 --format table
WinMon.Cli.exe crashes --days 7 --filter myapp --format json
```

`--days` defaults to 30 and accepts 1 to 90. `--filter` matches application names. Output lists groups sorted by count, then latest time, with coverage: Application log status, whether the event cap was reached, and the time of the oldest retained Application event so readers can see when log rollover cut the range short. Tables escape markup and control characters and respect `--no-color`; JSON uses UTC, invariant codes and a schema version. Exit codes follow the existing CLI: 0, 1 and 130. The command does not start sampling or change settings.

## MCP

Add `list_crashes` to the existing server: `days` (1 to 90, default 30), optional application filter, optional `limit` (default 50, at most 100 groups). It returns the same groups and coverage as the CLI. Annotate it `ReadOnly = true`, `Destructive = false`, `OpenWorld = false`.

Application and module names are untrusted text, sanitized like process names in `list_processes`. The response carries no paths and no event messages, so it needs no `includeSensitiveData` flag. The description states that the data is untrusted, never instructions, and that counts cover only the retained log.

Standalone CLI and MCP have no resource recording to correlate with. An MCP client can call `get_performance_history` itself when its sampling window overlaps a crash.

## Reading wider ranges

History's 500-event, 5-second read covers one frozen range. The CLI and MCP need up to 90 days of crash events only, so add a reader entry point that queries just the Application Error 1000 and Application Hang 1002 selectors, capped at 2,000 events and 5 seconds, reporting the same coverage statuses. History's existing selectors, caps and behavior do not change.

## Delivery and acceptance

1. Signature parsing in the reader, `CrashGroups` and the `.wmincident` field, with fixtures.
2. The CLI command and MCP tool on the wider reader.
3. The History summary, strings and smoke mode.

Acceptance requires:

- Real sanitized event XML fixtures for 1000 and 1002: named and unnamed data, missing fields, oversized and control-character text, malformed hex and versions. An unrecognized layout yields no signature, not a wrong one.
- Grouping tests: recurrence across versions, case differences, missing fields never merging, and well-known exception names.
- Recording tests: round trip with signatures, version 2 files without them, and rejection of out-of-bounds signature fields.
- CLI checks for JSON stability, `--no-color`, invalid `--days` and exit codes. MCP checks for schema, byte budget, sanitizing and the `limit` cap.
- A History smoke mode covering the summary with crashes, with hangs only and with none, reviewed in dark and light themes. Run `./tools/Verify.ps1`.
- Measure History event loading before and after, and record it in [performance history](../PERFORMANCE-HISTORY.md). Reading stays off the UI thread.

When this ships, update README, the CLI and MCP references and [docs/DEVELOPMENT.md](../DEVELOPMENT.md).

## Deferred

- **WER report files.** Reading `ReportArchive` and `ReportQueue` adds bucket IDs and survives Application log rollover. Add it if the oldest-event coverage shows the log is routinely too short for the requested ranges.
- **A separate window, saved investigations and folder imports.** Add them only if people ask.

## Sources

- [About WER](https://learn.microsoft.com/en-us/windows/win32/wer/about-wer): crash and hang reporting and the parameters it records.
- Application Hang fixture evidence: [ten-field unnamed EventData](https://learn.microsoft.com/en-us/answers/questions/4210888/apphang-issues) and [named EventData](https://learn.microsoft.com/en-us/answers/questions/3899542/runtime-broker-systemsettings-errors-cannot-run-systemsettings-%28windows-11%29). These are reported event XML examples, rather than a guarantee that all Windows versions emit the same layout. The Application Error fixture was sanitized from this machine's log.

The original proposal collected no fixtures or measurements. Implementation evidence is recorded below.

## What landed and departures

Implemented: structured crash/hang signatures, shared validation and grouping, optional version-2 incident persistence, the bounded crash-only Windows reader, CLI `crashes`, MCP `list_crashes`, and a translated History summary using existing event markers. The existing capture smoke now includes mixed failures, hangs only and empty summaries in Dark and Light. No sampling, settings changes, report-file parsing or debugger work is added by the standalone queries.

The reader accepts named 1000 and 1002 fields and only the fixture-covered ten-field unnamed 1002 layout. Unsupported or incomplete layouts remain ordinary History events without a signature. Names reject paths and controls; versions must parse as `System.Version`. Standalone queries omit event messages, skip message formatting and report Application-log coverage, the 2,000-event cap and the oldest retained Application event. The original History selectors, 500-event cap and five-second budget remain in place.

History uses a compact, scrollable group row beside the existing event pane so events remain usable at 620 × 540. Application and failure text can ellipsize; count and latest local time stay visible, with the full application, failure and dated timestamp in the tooltip and accessible name. Selection outlines all member markers and clearing selection clears the highlights. CLI tables translate failure labels; JSON retains stable exception-name values across UI languages.

Requirement tracking and verification:

| Requirement | Status and evidence |
| --- | --- |
| Portable grouping, versions, failure labels and strict validation | Verified: `CrashGroupsTests`; 78 focused core/recording cases passed, including blank-name rejection. |
| Version-2 persistence and malicious-input rejection | Verified: `SaveLoad_PreservesCrashSignatureInVersionTwoRecording` and `Load_RejectsInvalidCrashSignature`, including direct compressed JSON fixtures. |
| Windows parsing, coverage and preserved process identity | Verified: 748 Windows adapter tests passed after preserving duplicate-field lifetime semantics and the existing native enumeration boundary; the final acceptance run passed all 107 selected reader and characterization cases. |
| CLI JSON/table, bounds, no-color and exit codes | Verified: ten `CrashCommandTests` passed, including German display labels with invariant JSON names; the built CLI also returned eight groups with Complete Application coverage for the local seven-day range and no ANSI escapes. |
| MCP limits, sanitizing, byte budget and protocol schema | Verified: twelve `CrashToolsTests`/`StdioTests` cases passed, including a real stdio `list_crashes` call. |
| History selection, translations and theme renders | Verified: all 56 selected WPF resource, History and capture-diagnostic cases passed, including `CrashSummary_GroupsSignedEventsAndSelectsEveryMemberOnTheTimeline` and `AutoCaptureDiagnosticPreservesReportsHistorySettingsAndAwaitedShutdown`. Capture smoke passed with 53 renders; mixed, hang-only and empty History images were reviewed in Dark and Light, including the minimum-width layout. |
| Before/after event loading cost | Verified: six alternating native-load pairs returned matching counts/coverage with no observed timing regression; additional signatures and grouping cost about 1.9% in allocated bytes. See `docs/PERFORMANCE-HISTORY.md`. |
| Full repository verification | Verified after the WPF fixture repairs: unfiltered `./tools/Verify.ps1` passed 11,176 tests with zero failures and 36 expected explicit/prerequisite skips; unfiltered `./tools/Verify.ps1 -Store` passed 11,010 tests with zero failures and 54 expected skips. Both solution and benchmark builds passed without warnings/errors. No tests were disabled to obtain these results. |

Initial feature evidence: `artifacts/crash-acceptance-verify.log` and `artifacts/verify/20261125-124235-030-desktop/` contain the clean solution/benchmark builds, test reports and 53-render capture smoke. `artifacts/crash-tests/crash-core-final.trx` records the additional 78-case core run after tightening blank-name validation. All three product projects were then rebuilt without warnings/errors before the final smoke. Initial baseline/full-attempt logs are retained as `artifacts/crash-baseline-verify.log` and `artifacts/crash-final-verify.log`; they are not evidence of a clean full-suite pass. Native event-log retention varies by machine, and screen-reader acceptance remains a manual check.

The verification follow-up repaired the stale compiler-generated memory-helper binding, prevented diagnostic Settings from reading the real startup registry, initialized WPF keyboard state before installing a synthetic native key table, and made locked-PNG assertions precise for both WPF and native GDI+ writers. Pure WPF error contracts and file/window cleanup assertions remain intact. New coverage includes `WindowSettingsEntryDiagnosticModeDoesNotReadStartupRegistration`, `ControlArrowInitializesModifiersBeforeReplacingKeyboardStateOnAFreshStaThread`, and `LockedPngWriteFailure_AcceptsNativeImageSaveAndPreservesLockedFile`; guard tests reject unrelated exceptions and unproven native errors. The verifier recognizes two existing explicit prerequisites by exact test name and reason, with self-checks rejecting altered reasons.

Final full-suite evidence: `artifacts/wpf-verification-fix-desktop-final.log` and `artifacts/verify/20261125-153848-011-desktop/tests/`; `artifacts/wpf-verification-fix-store-final.log` and `artifacts/verify/20261125-155637-031-store/tests/`. The follow-up desktop capture smoke also passed 53 renders under `artifacts/verify/20261125-150232-320-desktop/capture/`, with Dark/Light Settings images reviewed.
