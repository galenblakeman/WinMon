# README and screenshot examples

## Purpose

Make the repository introduction explain WinMon's value quickly and show realistic investigations with reproducible illustrative data.

## Scope

- Keep download instructions, a spike walkthrough, feature overview, customization and material limitations in the root README.
- Preserve detailed instructions in `docs/USER-GUIDE.md`, with network behavior in one section.
- Group clickable gallery previews by user task; keep capture instructions in `docs/example/REGENERATING.md`.
- Regenerate native-composed screenshots at the documented product version. Give the CPU spike a clearly busy app, accumulate traffic counters, and provide available example clock/network readings.
- Load installed app icons for illustrative process rows when available, without changing their synthetic identities or the live icon provider.
- Separate packaging OS minimums, .NET support policy and actual validation evidence. Record fresh whole-app profile results with their conditions.

## What landed

The root README now leads with download and historical investigation. The user guide retains advanced behavior, and the gallery provides task-based previews and a separate regeneration guide. Changes to sample data are limited to diagnostic paths; `ProcessRow.SetExampleIconAsync` lets example capture await icon loading without changing normal lookup behavior. The CPU recording and accumulated network totals have diagnostic assertions.

Validation: `Verify.ps1 -Smoke services,usage,connections` passed all six desktop test projects (1,355 tests) and three native smoke modes. `Verify.ps1 -Store` passed all six Store test projects (1,311 tests) on the final run. An earlier Store rollback test hit a temporary-file replacement error; both its focused suite retry and the complete rerun passed. Both ordinary and Store example captures completed, all 20 gallery images were visually inspected, local document links were checked, and a 150-second whole-app profile completed. NuGet vulnerability auditing was disabled for these commands because the configured audit endpoints were unreachable; compiler warnings remained errors. Explicit elevated/native acceptance experiments were not run.

No live monitoring, settings, service setup or release metadata behavior changed. Icon availability depends on locally installed executables; missing icons retain the existing fallback. The gallery still uses illustrative data, and captures do not establish older-Windows or installed-Store compatibility. Performance measurements are observational, not before/after claims about this documentation change.
