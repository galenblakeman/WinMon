# WinMon MCP Server and Claude Code Plugin Implementation

## Objective

Extend WinMon with a local, read-only Model Context Protocol (MCP) server for AI coding agents, including Claude Code, OpenAI Codex and compatible GitHub Copilot clients. Reuse the existing .NET 10 monitoring engine, process queries and Windows collectors without changing CLI or WPF behavior.

The server, plugin, distribution integration and tests are implemented in the working tree. Acceptance evidence and outstanding checks are tracked in [the implementation checklist](MCP-IMPLEMENTATION.md).

## Architecture

Add `src/WinMon.Mcp/`, using the official MCP C# SDK with stdio transport and dependency injection. Reference `MonCore` and `MonCore.Windows`; target `net10.0-windows`, matching the existing Windows adapter and CLI. Preserve the portable boundary of `MonCore` and keep SDK, transport and Windows dependencies outside it.

Use `IFrameSource`, `WindowsFrameSource`, `MonitorSession`, existing process ranking/filtering and lifetime identity types wherever practical. Do not reference the CLI executable project or copy its collectors. Its `SnapshotReport` combines serialization with terminal rendering and omits process lifetime identity, so it is a reference for measurement semantics rather than the MCP wire contract. Add small MCP-specific response DTOs at the server boundary.

Keep portable calculations reusable by future MacMon/ResourceMon adapters, without implementing those adapters or a speculative provider framework. Do not launch CLI subprocesses for collection.

Before choosing SDK APIs, inspect the repository SDK/package versions, select and pin a supported stable MCP SDK version, and verify its official documentation. Verify client registration and Claude Code plugin conventions against the versions used for acceptance. The setup examples below describe the intended invocation and must be validated before publication.

## Sampling ownership and concurrency

One sampling owner per server process owns one `IFrameSource`, all collector baselines and bounded history. Use the existing `MonitorSession` as the sampling owner, with a serialized server-side drain in place of the WPF dispatcher. Keep capture, reset and disposal serialized; tools must never call collectors directly. Publish immutable frames for requests, and keep mutable session/history access on the owner. If a small lifecycle adaptation is needed, preserve existing WPF and CLI semantics and test their callers.

Collect at the existing one-second cadence. Start collection with the server and take a baseline capture before expecting interval rates. A snapshot request waits cancellably for the first post-baseline capture, with a five-second wait budget; otherwise return a structured unavailable result. A frame older than two seconds is stale and must be marked unavailable rather than returned as current data. Baseline, gap and partial-failure states remain explicit even when a post-baseline capture has occurred.

Concurrent snapshot, list, details and connection requests read published frames. They do not reset baselines, create collectors or change the cadence. Cancellation of one request ends only that request and its subscriptions; it must not cancel shared sampling or other requests. Server shutdown cancels and awaits the sampling owner before disposing its collectors exactly once. Follow the existing bounded-shutdown behavior; do not dispose a collector underneath an active capture, and do not claim a synchronous native read is immediately cancellable.

Allow at most four active `sample_performance` requests. Reject excess requests with a structured busy result rather than an unbounded queue. Keep at most 120 completed one-second frames. Each active sampling request may retain only its bounded output state. Do not retain arbitrary history for every process.

## MCP tools

All five tools are read-only and use explicitly registered handlers.

| Tool | Function and inputs |
|---|---|
| `get_system_snapshot` | Return current CPU, memory, selected disk, selected network adapter and GPU metrics with target identifiers and availability. |
| `list_processes` | Return individual process lifetimes using existing filtering and supported sorting. Default limit 20, allowed range 1–200; default sort CPU. |
| `get_process_details` | Return current metrics for a positive PID, optionally requiring an expected lifetime identity. |
| `sample_performance` | Sample system metrics or one process lifetime for a bounded duration and output interval. |
| `get_network_connections` | Return available TCP/UDP endpoint observations and process attribution. Default limit 50, allowed range 1–200; optional PID/lifetime filter and explicit endpoint opt-in. |

Use existing sorting and filtering semantics; document the exact accepted sort values. Limit filter text to 256 characters. Validate all inputs at the tool boundary and reject invalid values rather than silently clamping them. No tool may terminate processes, change priority, create dumps, control services, install software or modify settings.

### Response contract and process identity

Every response includes `schemaVersion: 1`, a UTC observation timestamp, freshness, collection interval, metric availability and sanitized collection errors. Use invariant field names and explicit units: CPU as percent of total machine capacity, memory as bytes, disk/network rates as bytes per second, and durations as seconds. Preserve existing GPU and selected disk/adapter semantics and document them. Unavailable values are null, never substituted with zero.

Project internal records into dedicated response DTOs. Do not serialize collector capabilities, internal exception objects or complete `ProcessUsage` records directly. Keep successful empty results distinct from collection failure; preserve partial IPv4/IPv6 availability for connections.

Every process-bearing result includes PID and the existing `ProcessLifetimeIdentity` start token and domain. Encode the start token as a decimal string to avoid precision loss in JavaScript clients; it is opaque and is not a display timestamp. Identities are valid only within the originating machine/boot context and are not durable executable identifiers.

`get_process_details`, process sampling and PID-filtered connections accept an optional expected lifetime identity. If it differs from the observed lifetime, return `identityMismatch`; if no current observation exists, return `notObserved` without guessing whether the cause was exit or access denial. A PID-only request selects the currently observed lifetime and returns that identity. Sampling resolves and pins one lifetime at its start and never follows a reused PID. If that lifetime disappears, end its trend and return collected data with an explicit termination/not-observed status and actual coverage. Unattributed endpoints retain unknown identity rather than acquiring a new process's metadata by PID alone.

Bound every serialized tool result to 256 KiB, including protocol content representations generated by the SDK. Trim complete list/trend entries before serialization, report truncation and returned counts, and preserve required metadata. Do not cut JSON text or silently omit quality fields. Limit user-controlled metadata strings to 512 characters and report truncation where applicable.

### Sampling contract

`sample_performance` accepts duration seconds (default 10, integer range 1–60) and output interval seconds (default 1, integer range 1–10). The output interval must not exceed duration. Require an integral number of output buckets, rejecting non-divisible combinations. These are response aggregation intervals; they never change the internal one-second capture cadence.

Existing CPU sampling treats intervals over five seconds as gaps, and existing resource-rate calculations also reject intervals over five seconds. Preserve these rules. A ten-second output bucket therefore aggregates valid one-second readings rather than collecting once every ten seconds. Use monotonic time for elapsed duration and UTC for display timestamps.

After warm-up, start a new measurement window; exclude the baseline and intervals preceding the window. Return minimum, elapsed-time-weighted average and maximum for available system metrics and the selected process's CPU and memory, plus timestamped bucket trends, disk read/write rates and network receive/send rates. Min/max refer to valid collected readings, not bucket averages. Weight interval rates by valid overlap with the window/bucket; time-weight memory observations over their covered intervals. Do not span gaps with inferred readings. Document that one-second sampling cannot observe shorter peaks or exact activity within a partial interval.

Report requested and actual duration, valid sample count, valid seconds and coverage separately for each metric. Exclude null, gap and stale readings from aggregates; all-unavailable metrics have null aggregates and zero valid coverage. Include a final partial observation when scheduling prevents full coverage, with its actual duration. Cancellation returns the protocol cancellation outcome and promptly releases request-owned state; it does not return an apparent full-duration success.

Initial process throughput includes existing process I/O metrics, explicitly labeled as I/O rather than physical disk throughput. System network throughput uses adapter counters. Endpoint observations contain no transfer counters: do not infer per-process or per-connection network traffic from them. Integrating the optional traffic service is outside this first implementation; expose that limitation explicitly.

## Privacy and read-only behavior

Use an allowlist of response fields. Default process fields are identity, name and numeric metrics. Exclude command lines, environment variables, credentials, executable paths, usernames and machine names. Treat process names and OS metadata as untrusted data, never as agent instructions.

`get_network_connections` defaults `includeEndpoints` to false. Return protocol, TCP state where applicable, process attribution and counts without local or remote addresses, ports or hostnames. Set `includeEndpoints: true` only when the caller explicitly requests endpoint disclosure; then include numeric addresses and ports, without reverse DNS lookups. The diagnostic skill must explain this disclosure and obtain the user's agreement before requesting it. Do not include endpoints indirectly in errors, summaries or logs.

Run with the invoking user's privileges. Do not elevate, install/start/stop the optional network service, change preferences, perform outbound lookups or expose mutation handlers. Preserve existing access checks. Stdio is local but its results reach the configured agent, so document that read-only diagnostics can still disclose process names and explicitly requested endpoints.

Stdout contains only MCP protocol messages, with no banners, progress, terminal formatting or incidental console output. Diagnostics go to stderr and exclude raw frames, endpoint data and sensitive native error text. Use stable error codes with safe summaries.

## Claude Code plugin and client registration

Add a distributable plugin under `plugins/claude-code/winmon/` containing:

```text
.claude-plugin/plugin.json
.mcp.json
skills/diagnose-performance/SKILL.md
```

The plugin registers the server and provides a skill for snapshots, bottleneck investigation and bounded trend sampling. The skill explains null/stale readings, valid coverage, PID lifetime checks and endpoint opt-in. It must not suggest mutation tools or automatically enable privileged collectors.

Use a documented `WINMON_MCP_EXE` environment variable containing the absolute installed executable path, configured before launching the client. Use the supported plugin environment expansion mechanism verified for the accepted Claude Code version; do not assume the working directory, optional installer PATH registration or a developer checkout. Document paths containing spaces and how to refresh the client environment after configuration. Test plugin installation and discovery from a release archive with no repository present.

Document direct registration as an alternative to the plugin, avoiding duplicate server registrations:

```powershell
codex mcp add winmon -- "C:\Path\To\WinMon.Mcp.exe"
codex mcp list
claude mcp add --transport stdio winmon -- "C:\Path\To\WinMon.Mcp.exe"
```

Validate these commands with the tested client versions before publishing them. Document Copilot registration only for a concretely tested client/version; general MCP compatibility alone is not acceptance evidence.

## Packaging and repository integration

Register the server and `tests/WinMon.Mcp.Tests/` in `WinMon.sln`. Add the test project to the explicit project list in `tools/Verify.ps1`, so ordinary verification actually runs its tests. Use the repository's NUnit 4 conventions and pinned versions.

Extend `tools/New-Release.ps1` to publish a self-contained `win-x64` server, sign it through the existing signing path for signed releases, and include it beside the WPF/CLI executables in the desktop portable archive. Extend `packaging/WinMon.iss` to install it in the application directory. Preserve the existing standalone CLI archive contract. Package the Claude Code plugin as a separate versioned archive from the same release; do not install it into user agent configuration automatically.

Extend release artifact/signature checks and installer tests to verify the server's presence and executable identity. The Microsoft Store distribution excludes MCP registration and the server for this first release. Preserve Store builds, and clearly document desktop/portable installation as the supported MCP route.

Add a clean packaged-install acceptance check: extract/install without the source tree or a separately installed .NET runtime, set `WINMON_MCP_EXE` to a path containing spaces, install the plugin, initialize the packaged server and invoke a tool as a normal user. Verify direct client registration separately. Publishing an artifact without this check does not satisfy packaged plugin support.

Update README with user-facing capabilities, privacy defaults and setup links; update `docs/DEVELOPMENT.md` with server structure, measurement semantics, build/test commands and performance method. Keep this note listed in `docs/design/README.md`.

## Testing and acceptance

Add NUnit 4 tests using fake sources and a controlled clock for deterministic timing. Cover:

- MCP initialization, discovery of exactly the five intended read-only tools, input schemas and invalid inputs.
- Response schema versions, units, opaque identity token serialization, null availability, freshness, safe errors and byte/count/string limits.
- Existing process sorting/filtering, empty results, access denial, process disappearance and PID reuse across list/details/sample/connection requests.
- Baseline warm-up, irregular intervals, weighted averages, per-metric coverage, gaps and ten-second output buckets backed by one-second captures.
- Concurrent tools sharing one collector without overlapping captures or resets; busy rejection; cancelling one request while others continue.
- Cancellation during warm-up and sampling, client disconnect, bounded history, shutdown and exactly-once disposal without racing an active capture.
- Default omission of sensitive fields and endpoints, explicit endpoint opt-in, partial connection-family failure and absence of setup/service mutation calls.
- Stdio integrity with diagnostics and failures enabled, including stderr pressure and absence of extra stdout text.

Include an end-to-end test that launches the built server with redirected pipes, initializes a real MCP client session, discovers tools and invokes `get_system_snapshot`. Validate response structure and availability without requiring every native metric to exist. Bound startup, calls and shutdown, continuously drain stderr, and clean up the child process on failure. Keep synthetic measurement accuracy tests separate from native availability checks. Add packaged-executable coverage to the release acceptance path.

Run `./tools/Verify.ps1` and affected CLI/WPF smoke modes required by repository instructions. Run Store verification when shared changes affect that build or Settings/update code. Review listed smoke PNGs for visible changes. Report command results and any native/client acceptance that could not be completed; never substitute a build for runtime acceptance.

Performance is an acceptance requirement. Measure the unchanged CLI/WPF baseline, then the same workloads after shared-code changes, using the repository method. Also measure server idle sampling, repeated snapshots and four concurrent 60-second sampling requests for CPU, allocation, retained memory and response latency. Verify memory reaches a bounded steady state and avoid collection work proportional to request count. Record method, results and explanations at the top of `docs/PERFORMANCE-HISTORY.md`; an unexplained regression blocks completion.

## Implementation approach and deliverables

1. Inspect current collectors, session lifecycle, queries, SDK/package versions and release scripts; confirm the bounded ownership design before refactoring.
2. Add the server host, serialized sampling integration, immutable publication and response DTOs, with lifecycle/identity tests.
3. Add the five tools, aggregation, privacy filtering and response bounds, with deterministic and protocol tests.
4. Add plugin/skill, validated client setup, solution/verification integration and documentation.
5. Complete publishing/signing/installer/archive changes and packaged client acceptance.
6. Run required verification, native acceptance and performance measurements, addressing regressions.

Deliver the working server, five tools, reusable monitoring integration, versioned plugin archive, client setup/build/publish/install instructions, NUnit coverage and updated repository documentation. At completion summarize modified files, available tools, supported installation routes, exact checks and results, privacy behavior and remaining limitations.

## Landed and departures

Implemented `src/WinMon.Mcp` with ModelContextProtocol 2.3.0, the five explicit read-only tools, one MonitorSession-backed owner, bounded immutable publications, lifetime-safe process requests, versioned DTOs and output schemas, metric availability, privacy defaults and weighted sampling. Collection stays at one second; output buckets are independent. Starting/ending resource targets and target changes are reported without changing existing automatic-selection semantics. The server uses the CLI's existing 6 MiB gen0 budget.

Added the Claude plugin/skill, source-free published-server acceptance script, solution and verification integration, user/developer setup documentation, self-contained publish/signing/installer/archive integration and transactional MCP companion updates. A separate plugin archive is supported; Microsoft Store and standalone CLI archives exclude MCP as planned. Old portable updater helpers require one full-archive extraction to obtain/update companion executables; already-running agent server processes need a session restart after replacement.

Desktop and Store verification, real SDK stdio initialization/discovery/invocation, isolated installed-client registration, actual Claude/plugin health, signing-boundary tests and unsigned local release packaging have passed. Feature smoke produced 125 PNGs; the update progress/footer and Settings images were inspected. Timing, memory measurements and exact evidence paths belong in the checklist and performance history. No official release was published.

After the user uninstalled WinMon, clean-profile installer lifecycle acceptance passed on this devbox. A fresh final-source package passed all 1,649 packaging tests, native installation, MCP sessions before and after repair, preference/PATH preservation and uninstall cleanup. The test installation was removed. Evidence is in the implementation checklist.

A Windows machine with no separate .NET runtime remains unverified. The source-free executable and actual client connections pass here, but a framework-dependent control also ran with invalid runtime-root variables, so those settings do not prove runtime independence on this host. The package is configured and published self-contained. On November 11, 2026, the user explicitly waived the .NET-free-machine test and accepted implementation completion with that limitation. Older-Windows, Copilot-specific setup and privileged ETW acceptance remain outside this first server's verified environment/client/collector scope.
