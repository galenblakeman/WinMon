# Improve capture information

Status: implemented with verification limits on 2026-11-11; see the [implementation record](CAPTURE-INFO-IMPLEMENTATION.md). This note supersedes the fixed report-limit proposal in [Automatic captures](AUTO-CAPTURE.md). Native endpoint attribution and sustained collector performance acceptance remain open.

## Agreed scope

Improve both automatic saved reports and manual **Copy capture**. Reuse the existing Processes and Connections **Top** dropdown selections for their respective report sections. Do not introduce report-specific limits, defaults, settings controls or preference keys.

The priority for connection information is traffic by app and connection. App traffic and endpoint details can use existing observations. Endpoint-pair traffic is technically feasible through an extension of the existing ETW service, but is not currently collected or exposed. The investigation below separates that extension from the initial report work.

## Existing settings and persistence

| Dropdown | Preference | Existing default | Meaning |
|---|---|---|---|
| Processes Top | `MonitorPreferences.TopProcessCount` | 5 | Ranked unpinned process rows, or executable groups in Grouped mode |
| Connections Top | `MonitorPreferences.TopConnectionCount` | 10 | Ranked unpinned app/process groups, not individual endpoints |

Both use `TopCountChoices`: **5, 10, 15, 20, 50, All**. All is stored as integer `0`. Preference validation preserves supported values and falls back to 5 processes or 10 connection groups for unsupported values.

`MainWindow.InitializeProcesses` and `InitializeConnections` update the in-memory `_preferences` as soon as a dropdown changes. They refresh the presentation but do not immediately write the settings file. `PreferenceStore` serializes these properties with the rest of `MonitorPreferences`:

- Normally to `%LOCALAPPDATA%\WinMon\settings.json`.
- Beside the executable as `settings.json` when `portable.flag` exists at startup.
- Normal shutdown saves the current preferences; the update-restart path saves them before proceeding. Applying Settings also writes the supplied preferences through the same store.

Reports must use the current in-memory values, including a selection changed since the last disk save. After relaunch, they use the validated loaded values. Leave the existing save lifecycle unchanged: an abrupt termination can lose dropdown changes that have not yet been saved.

Sources: [choices](../../src/WinMon/Preferences/TopCountChoices.cs), [preferences](../../src/WinMon/Preferences/MonitorPreferences.cs), [store](../../src/WinMon/Preferences/PreferenceStore.cs), [process changes](../../src/WinMon/MainWindow.Processes.cs), [connection changes](../../src/WinMon/MainWindow.Connections.cs), and [save lifecycle](../../src/WinMon/MainWindow.xaml.cs).

## Report behavior

### Processes

- **Copy capture:** keep the existing displayed process rows, grouping, sort, filters, pins and remainder summary. This already follows Processes Top; do not apply another truncation after the view has selected its rows.
- **Automatic reports:** replace the fixed top 20 with the current `TopProcessCount`. Keep Individual mode, no search or app filters, and ranking by the triggering resource. These choices keep an automatic diagnostic report useful when an unrelated view filter is active.
- Preserve inclusion of the triggering process for an Any process rule. If it falls outside the selected Top count, include it as an explicitly identified extra row. Report the actual count so this exception is clear.
- State the selected limit, actual included count and ranking. Explain that pinned rows in Copy capture are additional to the ranked Top rows, matching the existing list behavior.

### Connections

- Add a connection section to both outputs, using observations aligned with the report sample: the selected sample for Copy capture and the peak sample for automatic reports.
- Use `TopConnectionCount` to limit **ranked app/process groups**, independently of the process section. Include those groups' observed endpoints; do not reinterpret the existing selection as an endpoint-row cap or restrict it to the top resource processes.
- **Copy capture:** follow the Connections view's current sorting, search, Apps and pinned-only choices, including pins outside the Top count. Include endpoint details even when a group is collapsed onscreen.
- **Automatic reports:** use the selected Top count but ignore view search, Apps and pinned-only filters. Proposed ranking: combined receive + send rate, with missing readings clearly marked and deterministic ordering for ties. This ranking is a proposal, not an existing dropdown option.
- Freeze the effective limits and observations when creating the report. Later preference changes do not rewrite saved reports.
- Label the section explicitly, for example: `Connections: Top 10 app groups; 10 groups and 48 endpoints included`. Where available, report eligible and omitted group counts separately from endpoint counts.
- All means all eligible retained groups, subject to existing collection/history limits. It does not recover unavailable or expired data.

### Information hierarchy

1. Capture time and WinMon version; for automatic reports, trigger, firing time and peak time/value.
2. System resource readings and observation/coverage notes.
3. Processes, with effective Top selection and ranking.
4. Connection groups: app name, process identities, receive/send rates, traffic quality and endpoint count.
5. Under each group: owning PID, TCP/UDP, local address/port, remote address/port where observed, and TCP state where applicable.

Reuse existing traffic formatting and measurement semantics. State the rate interval and scope rather than implying a point-in-time counter. If period totals are included, label their actual coverage and keep them distinct from rates. Never divide an app's traffic among its endpoints or present app totals as connection measurements.

Missing endpoint or traffic observations must say why they are unavailable when known. Distinguish monitoring disabled, warming up, incomplete collection and missing historical data. Never substitute live connections for a historical capture. Preserve the existing warning that process I/O does not identify the cause of a selected drive or adapter spike. Endpoint addresses are included in the copied/saved report, so sharing that report also shares those addresses.

## Individual connection traffic: feasibility investigation

### Conclusion and delivery boundary

**Feasible with service changes:** per-process endpoint-pair receive/send rates and observed byte totals for TCP and UDP, IPv4 and IPv6. Reuse the existing ETW session; no new capture driver or dependency is indicated by this investigation. This is a source-based feasibility conclusion, not a native validation result.

**Available for the first report increment:** app-level rates/totals, endpoint metadata and existing traffic-quality flags. Reports must label endpoint traffic unavailable until the service supplies it. Do not block the agreed Top selection reuse on the service extension.

**Recommended service increment:** both receive/send rates and bytes observed since the client's monitoring baseline, grouped by process lifetime and endpoint pair. Call UDP entries **flows** and describe TCP entries as endpoint-pair traffic. Do not promise unique socket sessions, complete lifetime totals, exact connection duration, remote hostnames, application requests or packet payloads.

### What the current code establishes

- The service targets `net10.0-windows` and references `Microsoft.Diagnostics.Tracing.TraceEvent` **3.1.23**. The Store compile target is `net10.0-windows10.0.17763.0`.
- `EtwNetworkCollector.RegisterCallbacks` already subscribes to TCP/UDP send and receive events for both address families. `Enqueue(TraceEvent, int, bool)` reduces them to PID, event QPC timestamp, bytes and direction. Protocol and endpoints are discarded before they reach `NetworkTrafficLedger`.
- `NetworkTrafficLedger` resolves events to verified process lifetimes and owner SIDs, then maintains process byte totals and one-second rate buckets. Rates use a five-second event-time window with a three-second delivery holdback; retained bucket history is 30 seconds. Preserve these semantics for flow rates rather than measuring snapshot arrival intervals.
- `ProcessNetworkSnapshot`, `ProcessNetworkSession` and `ProcessNetworkFrame` contain process readings only. The client adjusts service-generation counters into client-session totals and handles gaps, stale data, reconnects and counter discontinuities. Equivalent flow handling is required.
- `WindowsConnections` polls the OS TCP/UDP tables separately. TCP rows include endpoints and state; UDP rows contain the local endpoint only. A polled table can miss a short-lived flow entirely. `NetworkConnection` explicitly contains no byte counters.
- Protocol version 1 uses a bounded JSON named-pipe response with a **2 MiB** message limit. History currently counts endpoint, owner and process-network rows against its entry budget; it does not account for a new flow collection.

Owning sources: [collector](../../src/WinMon.NetworkService/EtwNetworkCollector.cs), [ledger](../../src/WinMon.NetworkService/NetworkTrafficLedger.cs), [snapshot](../../src/MonCore/Monitoring/ProcessNetworkSnapshot.cs), [client session](../../src/MonCore/Monitoring/ProcessNetworkSession.cs), [OS endpoint tables](../../src/MonCore.Windows/Monitoring/WindowsConnections.cs), [protocol](../../src/MonCore.Windows/Monitoring/NetworkServiceProtocol.cs), and [history](../../src/MonCore/Monitoring/ConnectionHistory.cs).

### Event fields verified for the installed library version

The [official TraceEvent 3.1.23 parser source](https://github.com/microsoft/perfview/blob/v3.1.23/src/TraceEvent/Parsers/KernelTraceEventParser.cs) exposes source/destination addresses and ports on the relevant typed events, including the IPv6 UDP type spelled `UpdIpV6TraceData`. Its typed port accessors already convert byte order. TCP exposes a connection identifier, but IPv4 UDP's typed event does not expose an equivalent stable identifier. Do not infer one from a generic context field.

Microsoft's [TCP IPv4 event schema](https://learn.microsoft.com/en-us/windows/win32/etw/tcpip-typegroup1), [UDP IPv4 schema](https://learn.microsoft.com/en-us/windows/win32/etw/udpip-typegroup1) and [TCP IPv6 send schema](https://learn.microsoft.com/en-us/windows/win32/etw/tcpip-sendipv6) document endpoint and packet-size fields. These establish available event data, not complete capture coverage or application-payload accounting.

### Required changes

1. **Preserve event metadata.** Copy typed event fields during the callback into a bounded immutable event value; never retain TraceEvent callback objects. Include protocol, address family, source/destination addresses and ports. Verify source/destination orientation in native tests before normalizing send as local-to-remote and receive as remote-to-local. Keep ownership probing off callbacks and avoid DNS or formatted address strings on the hot path.
2. **Aggregate flows after ownership resolution.** Key by service generation, process lifetime (PID plus creation time), protocol, address family and normalized local/remote endpoints. Preserve IPv6 scope information when available; flag ambiguous link-local matches instead of guessing. Maintain receive/send totals and the same delayed rate buckets as process accounting. Unknown endpoint attribution must not discard otherwise valid process bytes.
3. **Bound flow state independently.** Process limits cannot bound endpoints per process. Set and measure limits for retained flows, rate buckets, queued event bytes and serialized response bytes. Retire inactive flow state using a documented policy, mark lost coverage, and prevent a retired key from silently inheriting an old baseline when it reappears. Preserve app totals when endpoint detail is capped. Top is a report/view limit, not a collection limit; a later Top change must still work within retained coverage.
4. **Extend the wire and portable model.** Add optional flow readings and explicit capability/coverage metadata, then validate addresses, ports, identity, counts, counters, rates and timestamps at the client boundary. Prefer an additive protocol-1 extension only if old/new client and service compatibility tests prove it safe; missing capability must mean unsupported, not zero traffic. Never send an oversized response: apply a deterministic byte-budget policy and report omitted flow coverage. If a protocol bump is necessary, cover service upgrade and mixed-version behavior explicitly.
5. **Carry observations into history.** Add flow baselines/reset handling to the client session and immutable flow snapshots to `ConnectionCapture`. Include flow memory in history accounting. Preserve the actual service window bounds in the delivered frame/report; `ProcessNetworkFrame` currently drops them. A sample-aligned snapshot still contains delayed traffic, so state both the capture time and traffic interval.
6. **Join conservatively for reports.** Match a flow to an OS endpoint row only when lifetime, protocol and endpoints are unambiguous. Retain traffic-bearing flows absent from the table as `Observed traffic; endpoint not in sampled table`, without inventing TCP state or a close time. For UDP, report the ETW-observed peer even though the OS row only identifies a local binding. Add owners with retained flow traffic to the eligible app-group set so short-lived activity is not lost by an inner join to current endpoint rows.

### Measurement and presentation limits

- A TCP endpoint tuple may be reused within one process lifetime; the first service increment aggregates that endpoint pair. Separating successive socket sessions would require validated connection-ID/lifecycle handling, including connections already open when monitoring starts, missed events and ID reuse. That is deferred.
- UDP flow totals describe observed datagrams for a peer pair, not a connected socket or a QUIC stream. Do not infer domains, URLs or user activity from a remote IP.
- Label totals **Observed received/sent since monitoring began**, with incomplete coverage where appropriate. Monitoring started after a connection opened cannot recover earlier bytes. After retention loss or reset, identify the new baseline/coverage rather than presenting a lifetime total.
- Report ETW-accounted network bytes, not guaranteed application payload or physical adapter bytes. Do not add retransmission events to existing send/receive accounting. Validate retransmission, offload, loopback, VPN and proxy behavior before documenting stronger equivalence. App and endpoint totals may differ when attribution, time coverage or capacity differs; show that distinction.
- Unobserved traffic is unavailable, not necessarily zero. Zero rates require a valid observed flow and a complete rate window. Preserve process-level loss/discontinuity flags and add flow-specific omissions/ambiguity so endpoint uncertainty does not incorrectly erase sound app totals.
- Initially sort flows within each included app by combined receive/send rate, then stable endpoint identity; leave the existing Connections Top meaning unchanged. All may produce a large report, but still only includes retained observations. Report omitted groups and collection-limited flow detail explicitly.

Longer selected-period flow totals are feasible after history and baseline handling exist, using the existing Connections period selection rather than a new setting. Defer them from the first service increment: a 30-second bucket retention alone cannot answer 1/5/30-minute queries, and cumulative snapshot differences need retained flow identities, gap/reset detection and an honest coverage interval. First/last observed traffic timestamps are also feasible; they must not be labeled connection start/end times.

### Acceptance before enabling endpoint traffic

Extend the existing ledger/session/protocol tests and [native collector test](../../tests/WinMon.NetworkService.Tests/NativeNetworkCollectorTests.cs), reusing `tools/WinMon.NetworkProbe`. Verify multiple simultaneous peers in one process, send/receive orientation, TCP/UDP IPv4/IPv6, IPv6 scope ambiguity, short-lived flows missing from OS tables, repeated endpoint tuples, PID reuse, late/out-of-order events, loss, capacity limits, subscriber reconnect and service restart. Prove one SID cannot obtain another SID's endpoints, including flow-only rows. Exercise old/new client/service combinations, malformed flow payloads and the 2 MiB response boundary.

Native tests must compare controlled peer byte counts and rates against flow and app totals over the same event-time interval, with explicit exceptions for deliberate loss. Measure queue allocation, collector CPU, snapshot serialization and retained history memory before/after under sustained high flow churn; the current compact queue's memory estimate no longer applies once endpoints are added. Set numeric flow budgets from these measurements before shipping. No elevated capture, service modification, performance experiment or native acceptance was performed for this design investigation.

## Implementation reach and verification

The implementation must reach `CaptureReportText.Snapshot` and `NetworkLeaders` (currently fixed at 20), `MainWindow.RecordCapture` (effective preferences and peak-aligned connections), and the manual snapshot report path (`SnapshotReport`, `SnapshotText`, `MainWindow.Snapshot`). Reuse the existing connection query/grouping and historical observations rather than depending on whether a tab is currently visible.

Add focused coverage for each finite choice and All, independent limits, a dropdown change before disk save, loaded/validated preferences, grouped connection counts versus endpoint counts, pins, an out-of-limit triggering process, exact historical alignment, missing/partial traffic and limits changed during report writing. Reuse existing preference round-trip tests where they already cover these properties. Include diagnostics for Copy capture from a non-Processes tab and automatic capture while hidden or in Compact mode.

When implementing, update user documentation and localized report strings; run the repository verification gate and affected capture/connections smoke modes, inspect their PNGs, and run the Store gate if Settings or update code changes. Measure structural or hot-path changes as required by the repository.

## What landed and departures

Implemented both report paths using the current Top selections, localized counts and coverage, historical endpoints, app traffic, and optional endpoint-pair rates/totals through the collector, ledger, protocol and client history. Connections Top retains its app/process-group meaning; no new preference keys or report-specific endpoint cap were added. The service instead bounds collected endpoint-pair keys globally at 8,192. It retains keys until their process lifetime expires to preserve cumulative identity, rather than recycling inactive keys; cap omissions are explicit and app totals continue. Scope-less link-local IPv6 traffic is not attributed to endpoints. Unique socket lifetimes and longer-period flow totals remain deferred. See the implementation record for passing deterministic checks and the outstanding native/performance acceptance and baseline Settings-smoke failure.
