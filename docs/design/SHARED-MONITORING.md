# Shared monitoring library for WinMon and MacMon

Status: Windows extraction implemented and verified; a macOS MonCore test run is outstanding. Portable sampling, history, ranking, usage, capture rules, CSV and restart persistence now live in MonCore; Windows samplers, service enumeration, WMI queries, native process actions and hardware probes live in MonCore.Windows.

## Objective

Extract WinMon's portable monitoring logic into a shared C# library before building MacMon with Avalonia. Implement the extraction in the WinMon repository first, verify that the existing Windows application retains its behavior and performance, then build MacMon against the extracted library.

Source repository: https://github.com/galenblakeman/WinMon

This design was informed by inspection of WinMon's monitoring code and developer documentation. The implementing agent must inspect the current checkout and its AGENTS.md before making changes; repository contents may have changed since this design was written.

## Project structure and dependencies

Initial structure inside the WinMon repository:

```text
src/
  MonCore/                 net10.0
  MonCore.Windows/         net10.0-windows
  WinMon/                  existing WPF application

tests/
  MonCore.Tests/           net10.0
  MonCore.Windows.Tests/   net10.0-windows
  WinMon.Tests/            existing UI/integration tests
```

Future MacMon projects:

```text
MonCore.MacOS/             macOS collectors
MacMon/                    Avalonia application
```

Dependency direction:

```text
WinMon WPF ------> MonCore.Windows ------> MonCore
      +---------------------------------> MonCore

MacMon Avalonia -> MonCore.MacOS --------> MonCore
      +---------------------------------> MonCore
```

MonCore references neither application nor platform adapter. It has no WPF, Avalonia, Windows Forms, native API calls, registry access, or System.Management dependency.

Use the project's current supported .NET version if it has changed. Preserve the Store build's Windows target requirements when creating the Windows adapter project.

## MonCore responsibilities

The library owns what happens after the operating system has supplied an observation.

| Responsibility | Existing starting points |
| --- | --- |
| Sample models and identities | MonitorSample, ResourceSample, ProcessUsage, SampleKey |
| Sampling orchestration | MonitorSession, IFrameSource, SamplingOptions |
| History and retention | MonitorHistory, ProcessRetention, SampleList |
| Ranking and grouping | ProcessRanking |
| Search, pins, and selection | ProcessSearch, ProcessPins, ProcessSelection |
| Session usage accounting | UsageLedger |
| Automatic capture evaluation | CaptureRules |
| CSV serialization | CsvExport |
| History persistence | RestartHistory |

The existing IFrameSource seam is the starting point: a collector supplies a frame, and MonitorSession queues it for processing on the application's chosen thread. Preserve this architecture rather than introducing a new dependency injection framework or a large service hierarchy.

Keep portable file serialization in the library. The application chooses paths, save dialogs, retention settings, and when to save. Localized report presentation remains in the application.

Evaluate CaptureReports separately from CaptureRules: report filenames, ownership markers, pruning, and WinMon branding are application policy. Do not accidentally allow MacMon to prune WinMon reports by generalizing the naming rules.

## MonCore.Windows responsibilities

Move Windows collectors and native integrations into the Windows adapter:

- CPU, resource, GPU, and service collectors.
- Process executable paths, Windows executable metadata, and window detection.
- Hardware inventory and WMI queries.
- Process termination, priority changes, and dump creation.

Audit dependencies rather than moving only files named Windows*. Some platform-specific files have neutral names, including HardwareInventory, ProcessImagePathProvider, ProcessDumpWriter, ProcessPriorityChanger, and ProcessTerminator. Separate reusable data models from native implementations where necessary.

The WPF application retains views, preferences UI, themes, icons, tray integration, shortcuts, startup registration, and the updater.

## Portability boundaries

### Process lifetime and executable identity

PID plus creation time must continue to identify a process lifetime. Preserve this protection in history, pins, selection, and process actions. A reused PID must never inherit an earlier process's identity or become an unintended action target.

Grouping and usage accounting currently use case-insensitive executable-path comparisons. Platform adapters should provide the executable identity key and comparison policy, used consistently across ranking, pins, selection, and usage totals.

For the initial extraction, preserve Windows behavior. Before adding the Mac adapter, introduce the platform-provided identity policy. Do not assume every Mac volume has the same case behavior.

ProcessUsage.CreationTicks and related formatting currently carry Windows FILETIME assumptions. Keep their current Windows meaning during extraction, document it, and isolate conversions such as DateTime.FromFileTimeUtc. Before Mac sampling begins, establish a platform-neutral lifetime representation and an explicit optional start timestamp for display. Any model or persistence change requires a compatibility plan.

### Measurement definitions

Document shared measurement contracts precisely:

- CPU percentage represents total machine capacity.
- Rates use bytes per second.
- Durations and CPU accounting have explicit units.
- Unavailable readings remain unavailable, rather than becoming zero.
- Resident memory, private committed memory, and macOS memory footprint remain distinct measurements.

Initially retain existing Windows fields and persistence formats. A Mac collector must not put a different measurement into a Windows-specific field merely to fill a column. This applies to memory, process I/O, disk utilization, handle counts, and GPU measurements.

### Capabilities and per-sample availability

Introduce a small capability description alongside the collector, covering supported system metrics, process metrics, and process actions.

Capabilities describe what the implementation supports. Nullable readings describe what was available in a particular sample. Action support does not guarantee permission to act on every process; platform implementations still enforce permissions and verify identity at execution time.

Before Mac sampling begins, review the current non-nullable process CPU value and algorithms that treat missing system CPU as invalidating an entire sample. Other resources should remain useful during partial collection failures. Keep this semantic change separate from the mechanical extraction unless needed for a clean dependency boundary.

### Services

Avoid redesigning services during extraction.

Move plain service observation models and existing aggregation/history algorithms into a clearly marked compatibility area in MonCore. Move enumeration and control into MonCore.Windows. Some service DTOs currently live with WindowsServices and will need to be separated.

This preserves the existing CapturedFrame and Services tab without inventing a universal service abstraction. These compatibility types retain their Windows semantics; their presence in a portable assembly does not mean they model launchd. MacMon can initially leave service frames absent. Design launchd support separately.

### Time and persistence

Use wall time for display and monotonic time for intervals. Preserve existing Windows history fixtures and the serialized format during extraction.

Before enabling persistence on macOS, verify restart, reboot, and sleep behavior, including assumptions around Stopwatch timestamps, clock frequency, and rebasing. Sharing a serializer does not make saved histories interchangeable across machines or operating systems. Cross-platform history transfer is not a requirement for the first milestone.

## Public API and threading

Expose only consumer-facing types and operations:

- Sample and identity models.
- Collector contract and sampling options.
- Session lifecycle and published snapshots.
- Ranking, history, usage, and capture operations.
- Persistence and export entry points.

Keep native layouts, scratch buffers, caches, and helper algorithms internal. Use InternalsVisibleTo for tests and benchmarks where justified. Application assemblies should consume the deliberate public API rather than receiving blanket friend access.

Keep the caller-supplied dispatch delegate so WPF and Avalonia can each select their UI dispatcher. Document which thread owns session state and which operations callers may invoke from other threads. Preserve the sampling queue's ordering and the existing rule that capture does not wait for the UI.

Prefer explicit project references and physical source ownership. Avoid duplicate source copies or linked compilation of the same implementation into both applications.

## Implementation sequence

1. Record a WinMon baseline using the current repository's required verification, relevant smoke modes, and performance measurements.
2. Extract MonCore, move portable tests, establish public entry points, and preserve behavior and serialized formats.
3. Extract MonCore.Windows, move native collectors and actions, and update WinMon's composition code to construct the Windows frame source.
4. Verify the boundary: build and run MonCore.Tests on Windows and macOS, check for forbidden framework dependencies and native imports, and preserve Windows integration coverage.
5. Revalidate WinMon using normal and Store verification, inspect smoke screenshots, and compare performance with the baseline.
6. Package MonCore as a versioned NuGet package. A local NuGet feed is sufficient during development; publishing to an external registry is a separate delivery step.

Keep shared source in the WinMon repository initially, where it can be verified against the working application. MacMon should consume a pinned package version rather than maintain a copied source directory. MonCore.MacOS can initially live with MacMon and reference that package.

Implement in reviewable steps. Separate mechanical moves from changes to measurement semantics, grouping policies, and persisted models so regressions can be traced.

## Verification and acceptance criteria

The first milestone is complete when:

- WinMon runs against MonCore and MonCore.Windows.
- The existing Windows behavior and history compatibility are preserved.
- Required normal and Store checks pass, including relevant visual smoke checks.
- Performance comparisons show no unexplained regression, following the repository's measurement procedure.
- MonCore builds and its portable test suite runs on Windows and macOS.
- MonCore has no UI framework references, Windows-only dependencies, or native imports.
- Existing meaningful coverage for ranking, retention, PID reuse, pins, usage accounting, capture rules, missing values, gaps, exports, and persistence remains intact.
- Repository architecture documentation and applicable architecture tests describe the new assembly boundary.

A Windows implementation session cannot claim the macOS test run passed without evidence from a Mac. Report that check as outstanding if no Mac execution environment is available.

## Extraction record (October 25, 2026)

- `MonCore` targets `net10.0` and owns the session, samples, histories, ranking, pins, selection, usage, capture rules, CSV and restart-history serializer. It keeps the existing `WinMon.Monitoring` namespace and the serialized history format. `ServiceObservation` and `ServiceCatalog` remain Windows-compatible DTOs beside the existing service aggregation code.
- `MonCore.Windows` targets `net10.0-windows` (and the Store variant's Windows SDK target). It owns the frame source, CPU/resource/GPU/service samplers, SCM access, WMI queries, hardware probes and native process dump, termination and priority handles. WinMon keeps localized reports, hardware text and process-action error messages.
- `IFrameSource.Capabilities` advertises supported metrics and actions; `WindowsFrameSource` supplies the Windows set. Per-sample nullable readings and action permission checks are unchanged. `MonitorSession` still queues frames and posts ordered drains without making capture wait for the UI thread.
- The existing Windows process identity remains PID plus Windows FILETIME creation ticks. Grouping and usage retain case-insensitive executable-path comparisons. Windows measurements and saved-history bytes were not redefined. Mac sampling and persistence require the identity and time compatibility decisions in this design before use.
- The local package is `MonCore` version 3.4.1, derived from `Directory.Build.props`; `dotnet pack src/MonCore/MonCore.csproj -c Release -o artifacts/local-feed` produces it. A plain `net10.0` consumer restored that pinned package from the local feed, built and ran. External publication is not part of this milestone.
- Automated Windows verification covers `MonCore.Tests`, `MonCore.Windows.Tests`, `WinMon.Tests` and the Store variant. The `AssemblyBoundaryTests` check references and native imports. A native macOS build and test run remain outstanding.
- Normal and Store `Verify.ps1` gates passed on Windows. Usage, capture and chart smoke modes passed and representative PNGs were inspected. The Services smoke still fails at live SCM/process mapping on this machine; the identical failure occurred in the pre-extraction baseline. The full smoke likewise failed before extraction because the machine's unavailable `A:` volume disrupted the native resource diagnostic. Four paired UI benchmark runs found matching rendered pixel hashes and no measured regression; see [performance history](../PERFORMANCE-HISTORY.md#shared-monitoring-assembly-extraction-october-25-2026).

Avalonia UI work and macOS collectors begin after this milestone. GPU feature parity, launchd management, a new history format, and a generalized service model are outside the initial extraction.

## Handoff prompt for Codex on Windows

Open the local WinMon checkout in Codex, supply this file, and use:

> Implement the shared-library extraction described in SHARED-MONITORING-DESIGN.md. Read AGENTS.md first and inspect the current code and dependencies. Save this design to docs/design/SHARED-MONITORING.md and reconcile any necessary adjustments with the current repository. Extract MonCore and MonCore.Windows in reviewable steps, preserving existing Windows behavior and history compatibility. Run the repository's required verification, relevant visual smoke checks, and performance comparisons. Establish portable MonCore tests and document any verification that requires a separate Mac run. Do not start Avalonia UI work or macOS collectors as part of this milestone.

## Source references

- Developer guide: https://github.com/galenblakeman/WinMon/blob/main/docs/DEVELOPMENT.md
- Repository instructions: https://github.com/galenblakeman/WinMon/blob/main/AGENTS.md
- Session and collector contract: https://github.com/galenblakeman/WinMon/blob/main/src/MonCore/Monitoring/MonitorSession.cs
- Windows frame source: https://github.com/galenblakeman/WinMon/blob/main/src/MonCore.Windows/Monitoring/WindowsFrameSource.cs
- Process ranking: https://github.com/galenblakeman/WinMon/blob/main/src/MonCore/Monitoring/ProcessRanking.cs
- History persistence: https://github.com/galenblakeman/WinMon/blob/main/src/MonCore/Monitoring/RestartHistory.cs
