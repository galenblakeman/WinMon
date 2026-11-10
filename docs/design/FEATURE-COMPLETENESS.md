# Complete existing investigation workflows

Implement and verify these independent increments in order, with one commit each.

| Requirement | Status |
| --- | --- |
| 1. Export retained process network bytes, rates and quality | Verified |
| 2. Include actual network leaders in automatic network reports | Verified |
| 3. Show Usage for a recorded period ending at the selected capture | Verified |
| 4. Explain actual retained history across activity views | Not started |
| 5. Navigate from Processes to the same app in Connections and Usage | Not started |

Traffic exports are separate from endpoint exports: per-process counters must not be repeated for every socket. Preserve existing endpoint columns and export raw units, UTC timestamps and coverage flags.

Usage periods must come from uncompacted observations, never infer exact totals from compacted process history. Keep new storage bounded and make missing coverage explicit. Capture selects the end of the period; the chart range supplies its duration.

Coverage information explains the existing retention limits and restart behavior. Reopenable incident bundles (review item 6) are outside this change.

## Validation

Run Verify.ps1 per increment and relevant visible smoke modes; inspect generated PNGs. Test lifetime identity, unavailable/partial data, capture boundaries and navigation under filtering. Measure before/after for hot-path or structural changes and record results in PERFORMANCE-HISTORY.md.

## Result and departures

Item 1 landed as a separate Process network traffic CSV, preserving the endpoint schema. Verify passed with the independently reproduced CLI SettingsCommandsShowSetAndResetWithoutSamplingOrInstalling file-access failure excluded; the unfiltered run failed only there. Feature smoke passed (125 renders), including the inspected traffic export dialog. No incident persistence changes are planned.

Item 2 appends grouped receive/send leaders from the exact peak, preserving unavailable/incomplete status and adapter scope. All build/test steps passed with the same CLI baseline exclusion (991 WPF tests). Capture card Dark/Light PNGs were inspected. The capture smoke stops later at Settings Monitoring overflow (47 DIPs); the pre-change 10:54 app binary reproduced the identical failure in artifacts/goal-baseline-capture. No Settings code changed.

Item 3 records bounded uncompacted intervals only for desktop sessions. Capture Usage, copied details and CSV share actual period bounds and coverage; Live restores session totals. Final core (222), WPF (1003) and Usage smoke passed with the standing CLI exclusion. An unchanged network-setup file-replacement failure passed on a complete 60-test retry. Quiet before/after UI and focused recording/query measurements are recorded in PERFORMANCE-HISTORY.md. Whole-process profiling is not part of these synthetic measurements.
