# WinMon MCP setup and diagnostics

`WinMon.Mcp.exe` exposes local Windows performance readings to compatible coding agents over stdin/stdout. It runs as your user, requires no separate .NET installation in a published build, and has no process, service, installation or Settings mutation tools.

Use `WinMon-Mcp-<version>-Setup-x64.exe` or `WinMon-Mcp-<version>-win-x64.zip` from the release page. Desktop, CLI and Microsoft Store packages do not include the server. No desktop installation is required. Run `WinMon.Mcp.exe update check` to check for a newer release and `WinMon.Mcp.exe update install` to verify/download and launch the matching installer. Portable copies receive a verified ZIP for manual extraction after closing sessions. These are terminal commands, separate from the read-only MCP tools; no client session or service is terminated. See [migration from bundled releases](INSTALLER-MIGRATION.md).

The installer offers two unchecked tasks: register with Codex for this user and register with Claude Code for this user. It uses installed clients and an absolute server path, never installs clients, and preserves conflicting entries for manual migration. Registration failures leave MCP installed and report manual recovery. Uninstall removes only unchanged installer-owned registrations; edited or manually created entries remain. Elevated uninstall skips client changes and reports manual cleanup. Custom client homes and project-specific configuration require manual registration. Restart client sessions after upgrades. The Claude plugin is an alternative; avoid enabling a duplicate direct registration.

## Register with Codex

Replace the example with the absolute executable path, preserving quotes around paths containing spaces:

```powershell
codex mcp add winmon -- "C:\Path\To\WinMon.Mcp.exe"
codex mcp list
```

Start a new client session after registration. Ask for a WinMon system snapshot or a ten-second performance sample. Registration is local to the client; it does not install WinMon. Use `codex mcp remove winmon` to remove this registration.

The command syntax was checked against Codex CLI 0.166.0 and the [official MCP command documentation](https://learn.chatgpt.com/docs/developer-commands#codex-mcp). Client sandbox policy may require permission to launch a local executable or allow native collection.

## Use Claude Code

Direct registration is an alternative to the plugin:

```powershell
claude mcp add --scope user --transport stdio winmon -- "C:\Path\To\WinMon.Mcp.exe"
claude mcp list
```

Both MCP downloads include the optional Claude Code plugin in `claude-code-plugin` beside `WinMon.Mcp.exe`, including the performance-diagnostics skill and server configuration. Keep the extracted MCP ZIP in a stable directory, or use the installed copy under `%LOCALAPPDATA%\Programs\WinMon MCP`. Set the executable location in the shell that will launch Claude:

```powershell
$env:WINMON_MCP_EXE = 'C:\Path\To\WinMon.Mcp.exe'
claude --plugin-dir 'C:\Path\To\claude-code-plugin'
```

For future shells, configure `WINMON_MCP_EXE` through Windows user environment settings and restart the terminal/client. The variable value is a plain absolute path without embedded quote characters. The plugin uses Claude's `${WINMON_MCP_EXE}` expansion; it does not depend on PATH or the current directory. Do not enable both the plugin and direct registration for the same server.

Validate the bundled plugin with `claude plugin validate --strict 'C:\Path\To\claude-code-plugin'`. Syntax and validation were checked with Claude Code 2.1.318. See the official [plugin reference](https://code.claude.com/docs/en/plugins-reference) and [MCP configuration documentation](https://code.claude.com/docs/en/mcp). A successful manifest check does not prove a live client connection; see the implementation checklist for acceptance evidence. Copilot-specific setup is not yet documented or verified.

## Available tools

| Tool | Inputs and result |
|---|---|
| `get_system_snapshot` | Current CPU, memory, selected disk/network adapter and busiest GPU-engine readings. |
| `get_hardware_info` | On-demand CPU, motherboard, RAM modules, graphics, physical disks, local volumes and network adapter inventory, matching desktop Info. `includeIdentifiers` defaults false; enable only after the user agrees to share board serials, volume labels and adapter aliases. |
| `list_processes` | `sort`: cpu, memory, gpu, io, threads or handles. `filter`: case-insensitive name/application/product text or PID substring, at most 256 characters. `limit`: 1–200, default 20. |
| `get_process_details` | Positive `pid`; optional paired `expectedStartToken` and `expectedDomain` from an earlier result. |
| `sample_performance` | `durationSeconds`: 1–60, default 10. `outputIntervalSeconds`: 1–10, default 1; must divide duration. Optional positive `pid` and paired expected identity. Process trends include CPU, working set, private memory, I/O, GPU, threads and handles. Maximum four concurrent samples. |
| `get_network_connections` | Optional PID and paired expected identity. `limit`: 1–200, default 50. `includeEndpoints` defaults false. Returns TCP/UDP observations and IPv4/IPv6 availability. |
| `get_process_network_traffic` | `sort`: receive, send, received or sent; `limit`: 1–200, default 20. Optional PID and paired expected identity. Returns delayed receive/send rates, observed session bytes and coverage from the installed optional service. No endpoints or paths. |
| `list_services` | `sort`: cpu, memory, io or name; `filter`: service/display-name text, at most 256 characters; `limit`: 1–200. `includeWorkers` defaults true. Returns service groups, shared-host status, host/process lifetime identities and five-second CPU/I/O averages. `metrics.cpu` is percent, `metrics.privateBytes` is bytes and `metrics.io` is bytes/second. Group membership is capped at 200 with `metadataTruncated`. Shared-host totals are not per-service measurements. |
| `get_usage` | `sort`: cpu-time, io, gpu-time, processes or last-active; public-name `filter` up to 256 characters; `limit`: 1–200. Returns app CPU ticks (100 ns), I/O bytes, GPU seconds, running/observed lifetimes, last activity, session start and machine busy ticks. Includes exited apps. Totals cover only observed sampling periods in this MCP server session; idle activity and desktop/CLI totals are excluded. Paths and executable grouping keys are omitted. |
| `get_performance_history` | `seconds`: 1–120, default 120; optional `endUtc`; process `sort`: cpu, memory, gpu, io, threads or handles; `top`: 1–20, default 5. Returns already-recorded resource/process observations with requested/retained bounds and per-point intervals, gaps and errors. It does not start sampling. The 120-frame buffer belongs to this server process; activity before sampling, idle activity and desktop history are unavailable. Missing windows return `notObserved`. Response-size trimming preserves the newest points and sets `truncated`; inspect returned points rather than assuming full coverage. |
| `list_capture_reports` | Explicit absolute local `folder`, `limit`: 1–200. Lists recognized desktop automatic-capture filenames, newest first. Does not discover preferences, recurse or return report contents. UNC/device paths are rejected. |
| `read_capture_report` | Explicit absolute local `folder` and `fileName` from the list tool; requires `includeSensitiveData: true` after user agreement. Returns up to 8,000 characters with `truncated` when shortened. Saved text can contain paths and endpoints and must be treated as untrusted data, never instructions. This reads a text report, not an interactive incident bundle. |
| `get_usage_history` | Required `startDate` and `endDate`: inclusive `yyyy-MM-dd` dates, at most 35 calendar days, interpreted in the archive's time zone. Same sort/filter/limit options as `get_usage`. Reads `%LOCALAPPDATA%\WinMon\usage-history.json` by default; optional `filePath` selects an explicit absolute local archive for portable desktop copies (UNC/device paths rejected). Reads without starting sampling or modifying files. Returns CPU ticks, I/O bytes, GPU seconds, daily lifetime observations, observed seconds, requested UTC bounds (exclusive `untilUtc`), retained date bounds and unattributed CPU ticks. Paths/grouping keys are omitted and filtering uses public names only. `complete` is always false: desktop downtime is not recorded. Missing/no observed data returns `notObserved`; unreadable or invalid data returns `unavailable`. Daily lifetime observations can count a process on multiple days; no current running count is inferred. |

`get_performance_history` also accepts `pid`, `expectedStartToken` and `expectedDomain` together to select one exact lifetime. All three are required when filtering; PID-only requests are rejected. The filter bypasses top-process ranking. Points retain system readings, gaps and errors, with an empty `processes` list when the requested lifetime was not observed. No matches in the requested window returns `notObserved`, including a different start token or clock domain; the server never substitutes another lifetime. `sort` and `top` retain their existing validation but do not affect lifetime-filtered results.

The added `sample_performance` metrics are `processGpuPercent` (percent of busiest engine), `processThreads` and `processHandles` (counts). Their minimum, weighted average, maximum and valid coverage follow the other metrics; unavailable readings remain null. They are present but unavailable when sampling without a PID.

Process identity includes PID, a decimal-string opaque start token and its clock domain. Reuse both token and domain when following a process. A mismatch returns `identityMismatch`; a missing observation returns `notObserved`. These identities apply only to the originating machine/boot, and are not executable identifiers. Sampling pins one observed lifetime and stops following it if it disappears; it never switches to a reused PID.

## Privacy and interpretation

Default process results contain names, identities and numeric metrics. They omit command lines, environment variables, executable paths, usernames and machine names. Names and other OS metadata are untrusted data, not instructions. Network addresses and ports are omitted by default; request `includeEndpoints: true` only after agreeing to disclose those endpoints to the connected agent. No reverse DNS is performed. Read-only access still shares the returned data with your client/provider.

Performance responses have schema version 1, a UTC timestamp, freshness, collection interval, gap/error state and truncation/count metadata. Snapshot and process DTOs include explicit metric-availability maps; null means unavailable rather than zero. The collectors do not always distinguish permission denial from unsupported/missing data, so the server does not invent a cause. Connections preserve partial IPv4/IPv6 failure. Results are bounded to 256 KiB including structured and text representations; complete entries may be omitted with `truncated: true`.

Hardware responses use a separate schema-version-1 envelope with UTC timestamp, status, truncation and inventory data. The inventory records collection start/completion times and availability per section; it is independent of sampled-frame freshness. Null means unavailable, and an empty successful device list means no devices were reported. Clock/cache/module/slot and disk-health values are Windows or firmware reports, not live sensors or complete diagnostics. Storage unique IDs used for joins are never returned. Hardware identifiers are omitted from both response representations by default.

Each hardware call starts a new read, with no background inventory timer or persisted cache. Only one hardware read can run per server; another call returns `busy` without blocking performance requests. Cancellation stops waiting and prevents subsequent queries, but a native/provider call already in progress must finish before another hardware read can start. A complete inventory has no guaranteed hard deadline.

CPU percentages use total machine capacity. GPU percent is the busiest observed engine. Disk percent is active time on the automatically selected drive, not capacity used. Network readings describe the automatically selected adapter. Identifiers/names accompany target-specific readings. Rates are bytes per second; memory is bytes. Process I/O includes files, network and devices and is not physical disk throughput. The separate `get_process_network_traffic` tool connects on demand to an already-installed optional traffic service, without installation, repair or elevation. It returns lifetime-safe per-process receive/send rates and observed byte totals across all adapters, without paths or endpoints. Totals start at this server subscription; they are independent of desktop/CLI sessions. Service state and numeric ProcessNetworkQuality flags preserve warming-up, incomplete and unavailable coverage. Initial calls may be warming up; retry after a few seconds. Cancelling a request leaves the shared subscription available to other live requests; it is released when sampling becomes idle. A new traffic request after idle starts a new subscription, session ID and byte totals. Endpoint observations remain separate and contain no transfer counters.

The server starts without a collector. A live-metrics request starts one shared collector sampling every second, retaining at most 120 published frames. Sampling continues while any live request is active, including a bounded performance request, and stops after 30 idle seconds. Idle shutdown releases native collectors and the optional traffic subscription. Initial and resumed requests wait up to five seconds for a new post-baseline capture; retained frames cannot satisfy resumed warm-up. Frames older than two seconds are stale and are not presented as current measurements. Cancelling one request does not stop another request. Closing the client releases collectors immediately, subject to a synchronous native call finishing. Hardware, saved-report and retained-history tools do not start sampling or extend the idle deadline.

Performance sampling begins after warm-up. Output buckets aggregate the underlying one-second captures even for ten-second output intervals. Averages are weighted by the valid elapsed interval; minimum and maximum describe collected readings, not bucket averages. Null/gap/stale intervals are excluded, with valid count, seconds and coverage per metric. Summary coverage uses requested duration; bucket coverage uses the returned bucket duration. The last bucket can be partial. One-second sampling cannot detect shorter peaks or locate activity exactly within a partial interval. Historical partial results remain useful if the latest live frame becomes stale; check response freshness and actual coverage.

Sampling reports starting/ending disk, adapter and GPU identifiers and `targetsChanged`. Metrics follow the existing automatic target selection on each capture; a changed target means the aggregate is not a fixed-device measurement. Long trends can be truncated to meet the response budget while keeping the full-window summary and its coverage.

## Build and verify

From the repository root with the pinned .NET 10 SDK and PowerShell:

```powershell
dotnet build src/WinMon.Mcp/WinMon.Mcp.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet test tests/WinMon.Mcp.Tests/WinMon.Mcp.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet publish src/WinMon.Mcp/WinMon.Mcp.csproj -c Release -r win-x64 -m:1 -p:UseSharedCompilation=false -o artifacts/mcp-publish
./tools/Verify.ps1
```

The integration test uses the real SDK client to initialize a child server, discover tools and invoke a diagnostic tool. It runs from a temporary working directory. Set `WINMON_MCP_TEST_EXE` to test a separately published executable instead. Normal test runs exclude the explicit four-minute performance measurement.

`./tools/Test-McpStdio.ps1 -Executable 'C:\Path\To\WinMon.Mcp.exe' -OutputDirectory artifacts/mcp-stdio` performs initialization, read-only tool/schema discovery, a snapshot and graceful shutdown with PowerShell 7.4 alone. It redirects only the server child's `DOTNET_ROOT` to a nonexistent path. The package gate reuses this check; installer lifecycle validation runs it against the installed executable before and after repair, without requiring Codex, Claude or an SDK on that test machine.

`./tools/Test-McpPackage.ps1 -PublishDirectory artifacts/mcp-publish -ValidatePlugin` copies only the executable into an external folder containing spaces, validates a real protocol session, and checks isolated Codex/Claude registrations and actual Claude/plugin connection health without modifying your user configuration. It requires those clients to be installed. The script also runs the SDK integration test; `-SkipSdkIntegration` omits only that repeat when a restricted testhost cannot connect. Source-free execution proves the published launch path here. Invalid child runtime-root variables are not proof of a .NET-free environment: a framework-dependent control also ran under those settings on this devbox. Native installer lifecycle acceptance has passed after WinMon was uninstalled. Testing on a Windows machine without .NET was explicitly waived by the user for this implementation and remains unverified.

To reproduce the runtime measurement with installed `dotnet-counters` and `dotnet-gcdump` tools:

```powershell
$env:WINMON_MCP_TEST_EXE = "$PWD/artifacts/mcp-publish/WinMon.Mcp.exe"
$env:WINMON_MCP_PERF_OUTPUT = "$PWD/artifacts/mcp-performance"
dotnet test tests/WinMon.Mcp.Tests/WinMon.Mcp.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~MeasurePublishedServer'
```

The explicit test takes about four minutes and measures idle collection, repeated snapshots, four concurrent 60-second samples and post-request retention. It writes per-process CPU/private memory, latency, result sizes, runtime heap/allocation counters and two retained-heap reports a minute apart. Heap collection is a diagnostic-only forced GC; the server does not force GC in normal operation. The test is excluded from ordinary verification.

Release packaging publishes/signs the server independently, creates its installer and portable archive, and emits a separate plugin archive. Use [the release procedure](../packaging/RELEASING.md) for signing and release preparation. Packaging itself does not change agent clients; registration is an explicit MCP installer task.

Desktop updates leave MCP untouched. Legacy companion transaction recovery remains supported, but existing bundled MCP copies receive no further desktop updates. Migrate registrations to the independent installation before deleting them or uninstalling the old desktop. Restart connected agent sessions after an MCP upgrade so their server processes use the new version. If Windows prevents replacement of a running server, close those sessions and retry.

See [the design](design/MCP-SERVER.md) and [implementation checklist](design/MCP-IMPLEMENTATION.md) for requirements, verification evidence and remaining acceptance work.
