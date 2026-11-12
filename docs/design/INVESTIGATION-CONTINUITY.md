# Investigation continuity

Close review items 1, 2, 4 and 5 without adding an incident viewer or new settings:

- Services follow configured resource-history retention under the existing 120,000-observation budget. Their row charts keep the existing two-minute window; memory limits remain explicit.
- Copy capture appends the active Services or Usage view. Reuse existing service details and Usage report formatting, including filters, Top, pins, host/worker attribution, period bounds and missing coverage.
- Extend the existing restart file with optional checksummed investigation chunks. Preserve aligned service groups, endpoint/traffic captures and uncompacted Usage intervals for normal exits, Windows session end and update restarts. Keep existing deadlines and the normal file size cap. Older files remain readable; corrupt optional chunks must not cost resource history. Rebase sample keys after reboot. Never resume live traffic from a historical counter. Normal-exit session Usage totals still reset.
- Add read-only MCP `get_process_network_traffic`, reusing NetworkTrafficClient and MonitorSession accounting. Subscribe lazily only on a traffic request; never install, repair or elevate. Preserve lifetime identity, session scope, delayed rates, quality flags, null/unavailable readings, response bounds and privacy defaults.

## Result and departures

Implemented in ServiceHistory/MonitorSession, MainWindow capture formatting, InvestigationHistory/RestartHistory and the MCP SamplingOwner/NetworkTrafficTools. Existing saved settings are unchanged. All ten resource languages explain the changed persistence and endpoint storage. The Claude diagnostics skill and stdio discovery gate include the new tool.

Persisted observations are best effort within existing time, memory and file limits. A save/read deadline may shorten history; missing observations remain unavailable. Live traffic begins a new client session, while period Usage continues to use its original recorded intervals. Reopenable incident bundles remain outside this change.

Validation and performance results are recorded in PERFORMANCE-HISTORY.md; the final task report distinguishes completed checks from any remaining native acceptance limits.

Final verification: desktop `Verify.ps1 -Smoke services,usage` passed all 12 steps; `Verify.ps1 -Store` passed all 10 steps. The final core rerun passed 235 tests after preserving restored Usage owner sharing. The MCP suite passed 49 tests, including real stdio discovery/invocation and deterministic traffic lifetime/privacy/cancellation checks. Services and Usage copy assertions passed, and their final PNGs were inspected. Earlier snapshot (64 renders) and Connections (23 renders) smoke passed. Automatic capture smoke stops at the existing Settings Monitoring overflow of 47 DIPs; an untouched HEAD copy reproduced the identical failure. No real reboot, elevated ETW acceptance, signed installer or Store deployment was performed.
