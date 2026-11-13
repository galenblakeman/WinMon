# Scriptable investigations

Implement feature gaps 2, 3 and 4: structured CLI reports for Services/Connections/Usage, read-only MCP Services/Usage access, and retrospective spike/capture queries.

Reuse existing collectors, ServiceRanking, UsageLedger, immutable MCP publications and desktop capture-report recognition. Preserve the original CLI snapshot schema and in-progress Hardware functionality. Keep sampling bounded, cancellation/disposal intact, process lifetimes explicit, unavailable values distinct from zero, and MCP output privacy/size limits intact.

## Result and departures

Landed standalone CLI `services`, `connections` and `usage` commands with table/JSON output, filtering, ranking, row limits and 1–60-second collection. Usage describes the command's collection. Connections report endpoints and family availability, not invented per-socket transfer counters.

MCP adds `list_services`, `get_usage`, `get_performance_history`, `list_capture_reports` and `read_capture_report`. Usage is server-session scoped and published as one replaceable immutable snapshot. Historical resource/process observations reuse the existing 120-frame buffer. Saved desktop reports require an explicit local folder and sensitive-data consent before reading bounded report text. All tools are read-only and preserve structured/text size limits. The stdio discovery gate and Claude diagnostics instructions include them.

Scope limits: no continuous CLI recording, desktop live-session bridge, automatic preference discovery, remote paths or interactive incident replay. The retrospective tool cannot reconstruct observations from before this MCP process started. Saved reports supply earlier desktop evidence when automatic captures were enabled.

Verification: focused CLI/MCP tests cover parsing, baseline/disposal, cancellation, raw units, missing sources, lifetime/privacy behavior, response bounds, history windows, capture recognition, consent and path traversal. Full-gate results are recorded in the final task report. Focused before/after Usage publication measurements are in PERFORMANCE-HISTORY.md.

Final acceptance: all 11 Verify.ps1 steps passed for desktop (1,766 tests) and Store (1,709 tests), using temporary copies of the unchanged verification steps with isolated artifact roots to avoid concurrent restore/build collisions. Temporary runners were removed. The final normal MCP rerun passed 63 tests, including UTC offset boundary handling. Native Services/Connections/Usage JSON checks, inspected table output, and the real MCP stdio discovery/snapshot/shutdown gate passed. Signed installation, release publication and explicit elevated ETW experiments were not run.
