# Audit recovery and export range fixes

Keep live monitoring available when optional saved state cannot be accepted, and keep CSV exports aligned with the charts through system-clock changes.

## Result

- Desktop archive load/save recovery handles InvalidDataException. Rejected files remain untouched; failed loads release their writer lock. The Usage smoke checks invalid-schema and oversized archive recovery before rendering.
- Resource/process CSV selection and service/connection/traffic export filtering use ProcessRanking.Moment, including its wall-clock-only fallback. Tests cover both clock directions and the inclusive elapsed-time boundary.
- CLI rejected-settings loads use defaults with a warning. Save failures are reported without replacing unsupported or oversized files. Command and interactive-save regression tests cover both rejection cases.

These fixes landed in the existing recovery and filtering paths. No new persistence format, dependency or user setting was needed; there are no design departures.

## Verification

Focused regression runs passed: CsvExportTests (12) and CliSettingsTests (34). Final full desktop tests passed 2,041 with 29 expected skips; final full Store verification passed 1,975 with 47 expected skips and exit 0. Both builds and benchmarks built with zero warnings/errors.

`Verify.ps1 -Smoke all` passed ten smoke modes, including Usage archive recovery. Relevant Usage and CSV export PNGs were inspected. Gauges failed twice at the existing CPU centering assertion (column 83.3, bowl/value 84.3 at width 1000 DIPs). The final `Verify.ps1 -Smoke gauges` run passed all desktop tests but returned exit 1 for that unrelated smoke failure. Gauge implementation and tolerance were left unchanged.

Evidence is under `artifacts/audit-fixes/` and `artifacts/verify/20261119-222352-457-desktop/`, with final desktop/Store test reports in `20261119-223115-043-desktop` and `20261119-222938-201-store`.
