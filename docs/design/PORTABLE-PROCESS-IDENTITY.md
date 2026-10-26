# Portable process identity and observations

## Contract

`ProcessLifetimeIdentity` combines PID, an opaque start token and a `ProcessLifetimeDomain`. A lifetime is valid only on its originating machine/boot. Windows defaults use the existing FILETIME token. macOS uses Unix start microseconds, with a separate optional `StartedAtUtc` for display. `CreationTicks` remains as a source-compatible legacy name for the token; portable callers must not apply FILETIME conversion to it.

`ProcessIdentityPolicy` supplies executable keys and a comparer to ranking, pins, selection and usage. Its default preserves Windows path/name grouping and case-insensitive comparison. `ExactPaths` uses ordinal paths and a lifetime fallback for unreadable paths, preventing false merges on case-sensitive volumes. It conservatively keeps aliases separate. Adapters may supply a canonical executable/file identity policy later.

Portable observers set `CpuAvailable=false` when interval attribution is unavailable. `ObservedCpuPercent` is the nullable consumer API; the original constructor and `CpuPercent` remain source-compatible. `IndependentProcessReadings` opts a sample into process ranking when system CPU is missing. Compaction retains unavailable process CPU observations. Usage totals continue to use frames with a system CPU denominator, skip unavailable process CPU contributions and reassign future usage when exec changes an executable within one lifetime.

Process capture rules use lifetime identities and break their runs on unavailable CPU. Portable CSV adds domain/start columns and leaves unknown CPU empty. Windows CSV columns and default behavior remain unchanged. Pins and usage snapshots preserve portable identity metadata and reject mismatched executable policies for grouped restoration.

## Compatibility and verification

The Windows history binary format stays at version 1. Saving observations that require portable domains, timestamps, availability or sample semantics returns false before opening a file. The existing Windows fixture and all portable tests continue to pass. Mac history persistence and cross-platform state transfer remain out of scope.

On October 25, 2026, macOS Apple Silicon with SDK 10.0.401:

- All 152 portable MonCore tests passed, including Windows history compatibility, policy switching, case-distinct paths, PID/domain reuse, pins, selection, partial CPU, exec usage, captures and CSV.
- The Windows adapter tests and the entire WinMon solution cross-built with `EnableWindowsTargeting=true`, without warnings.
- A synthetic 850-process microbenchmark found essentially unchanged grouped-ranking time after eliminating duplicate dictionary lookups; see the performance history. Full Windows UI/pixel/performance checks and `Verify.ps1` remain outstanding and must run on Windows.

## Landed

The implementation is in `src/MonCore/Monitoring/ProcessIdentityPolicy.cs` and the session/ranking/pin/selection/usage/capture/export consumers. `PortableIdentityTests` covers the new contracts. The MonCore project packages as **3.4.1-macos.1** for development without changing the Windows app's product version. MacMon consumes that pinned package; its native collector and UI remain in MacMon.

Departures from a new universal model: the legacy constructor/property names and binary format are preserved; nullable CPU is exposed through an availability flag plus `ObservedCpuPercent`; exact path equality deliberately does not merge aliases. No Windows collector behavior, service model or app process actions were redesigned.
