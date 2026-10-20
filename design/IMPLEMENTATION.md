# Implementation and validation record

> Original CPU-only implementation record. The selected resource-overlay expansion and current validation are documented in [RESOURCE-OVERLAY.md](RESOURCE-OVERLAY.md).

Updated: 2026-10-19. Selected direction: WPF, concept A compact by default, expanding into concept B.

## Delivered developer build

- [Runnable executable](../artifacts/app/WinMon.exe), framework-dependent: Windows and .NET 10 Desktop Runtime required. Keep all files in `artifacts/app` together.
- [Solution](../WinMon.sln) and [run instructions](../README.md).
- [Compact live render](../artifacts/validation/compact.png), [expanded historical render](../artifacts/validation/investigate.png), [light render](../artifacts/validation/light.png), [settings render](../artifacts/validation/settings.png).
- [Native/UI smoke report](../artifacts/validation/smoke.json).
- [NUnit TRX](../tests/WinMon.Tests/TestResults/winmon-tests.trx).

The screenshots contain real short-run readings, so the initial history occupies only a small part of the configured time range. They are WPF visual renders, not desktop screenshots including native title-bar chrome.

## Requirements

| Requirement | Status | Evidence or remaining dependency |
|---|---|---|
| WPF selected, A default and B expansion | Verified | Live smoke retains the same history while expanding; rendered layouts inspected |
| Total CPU and five busiest individual processes | Verified | Native smoke and deterministic normalization/ranking tests |
| PID plus creation identity, process churn and unavailable observations | Verified | NUnit identity/churn/counter tests; query-only Windows handles; UI reports unavailable/omitted processes |
| Initial baseline, long gaps and pause/resume baseline resets | Verified | NUnit gap/reset tests and live pause/resume smoke |
| Fixed 0–100% graph, bounded drawing and per-process trends | Verified | Source review and 100%/150%/200% bitmap renders; values use total machine capacity |
| Monotonic graph positions and elapsed retention | Verified | Forward/backward clock regression tests and monotonic history eviction test |
| Select historical sample, return to live, and show selection expiry | Verified | Published executable smoke validates each state |
| Preserve attribution after process exit | Verified | Retained immutable observations and historical attribution test |
| Explicit ownership and cancellation of background collection | Verified | Owned worker task, serialized sampling and successful smoke exit |
| Preferences: async atomic write, corrupt input recovery, cancellation | Verified | NUnit roundtrip, cleanup, invalid-data and cancellation tests |
| Light/dark settings and appearance | Verified | Render inspection; theme resources shared with settings |
| Folder distribution requiring installed .NET runtime | Verified | Publish completed and published executable passed native/UI smoke |
| Keyboard graph navigation and focus behavior | Blocked | Implemented; needs interactive keyboard/screen-reader validation on a Windows desktop |
| Actual DPI transitions, monitor removal and window restoration | Blocked | Implemented bounds recovery and per-monitor DPI manifest; needs real multi-monitor/display-scaling exercise. Bitmap scaling alone is insufficient |
| High-contrast and system-theme transitions | Blocked | Implemented dynamic system resources and preference-change handling; needs actual OS accessibility/theme setting changes |
| Tray restore/exit and opt-in startup at sign-in | Blocked | Implemented; needs interactive tray and real sign-in validation. Smoke intentionally bypasses startup registration and user preferences |
| Sustained overhead and controlled-load metric comparison | Blocked | Needs a longer representative workload and aligned independent Windows counter capture; short observation below is not a production benchmark |

Blocked rows identify validation limits of this developer build, not known failing behavior. No claim of production-release readiness is made.

## Validation completed

Baseline scaffold: Release build passed, zero warnings/errors; no pre-existing tests. The first sandbox build could not write outside the original workspace; the corresponding permission-enabled build succeeded.

Final commands completed successfully:

```powershell
dotnet build WinMon.sln -c Release -m:1 -p:UseSharedCompilation=false
dotnet test tests/WinMon.Tests/WinMon.Tests.csproj -c Release --no-build --logger 'trx;LogFileName=winmon-tests.trx'
dotnet publish src/WinMon/WinMon.csproj -c Release --no-restore --self-contained false -o artifacts/app -p:UseSharedCompilation=false
.\artifacts\app\WinMon.exe --smoke-output D:\Projects\WinMon\artifacts\validation
```

Release build: **0 warnings, 0 errors**. NUnit: **32 passed, 0 failed, 0 skipped**. Published native/UI smoke: **exit code 0**. Preferences were not persisted and startup registration was not modified during this smoke run.

The published smoke included a separate 15.0-second quiet observation: approximately **0.47% of total machine CPU**; final working set approximately **149 MiB**. These are one-host, short-duration observations after PNG rendering, not startup, steady-state memory, or sustained overhead guarantees. Full smoke elapsed 28.3 seconds and included captures and interaction checks.

The first native smoke exposed a diagnostic startup URI error, corrected before the successful runs. Test execution exposed an incorrect synthetic kernel-time fixture; the fixture was corrected because Windows kernel time includes idle time. A regression now prevents decreasing idle counters from fabricating a saturated CPU reading.

## Bounds and semantics

- One-second sampling; compact range defaults to two minutes; retention defaults to ten minutes.
- Settings offer 1/2/5-minute compact ranges and 10/30/60-minute upper retention limits.
- History is bounded by elapsed time, observation count, and 500,000 process measurements. When that memory budget shortens history, the UI reports it.
- Each native pass considers at most 20,000 non-idle processes. Inaccessible or omitted processes are counted in the UI; their CPU is never shown as a fabricated zero row.
- Total CPU is collected independently. Top-five values need not add to the total.
- Process interval history retains individual identities rather than grouping by display name.
- Native machine timing supports one active processor group. Multiple groups fail with an unavailable state rather than presenting a partial total. This is a known platform limitation.
- Pauses, sampling errors and long delays create baselines/gaps. Wall-clock changes affect displayed labels, not plotting distance, history identity, or CPU normalization.
- Closing always exits; minimize-to-tray and startup are opt-in settings. Tray Exit always exits.
- Data stays local; history is volatile. No credentials, command lines, network collection or process-control features are added.

Native API references: [GetSystemTimes](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes), [GetProcessTimes](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes). The latter supports `PROCESS_QUERY_LIMITED_INFORMATION`, used here with owned safe handles.

## Delegation and integration

Monitoring agent supplied the initial timing provider, sampler/history API and tests. Preferences agent supplied settings persistence, startup registration, settings UI and persistence tests. Review agent identified the wall-clock history defect and checked pause/selection integration. The primary agent implemented the WPF UI, ownership, rendering and diagnostics, integrated the modules, corrected native query access and counter fixtures, added elapsed/memory bounds and clock regressions, and ran final build/test/publish/smoke validation.

## Deferred optional features

Grouped process ranking, persistent recording/export, an installer, and signed/updateable distribution remain outside this initial developer build. The framework and visual direction are decided; the next useful step is interactive feedback on the running utility and the manual checks listed above.
