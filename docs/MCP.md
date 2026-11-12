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

The separate `WinMon-Claude-Code-<version>.zip` adds the performance-diagnostics skill as well as server configuration. Extract it into a stable directory. Set the executable location in the shell that will launch Claude:

```powershell
$env:WINMON_MCP_EXE = 'C:\Path\To\WinMon.Mcp.exe'
claude --plugin-dir 'C:\Path\To\Extracted WinMon Plugin'
```

For future shells, configure `WINMON_MCP_EXE` through Windows user environment settings and restart the terminal/client. The variable value is a plain absolute path without embedded quote characters. The plugin uses Claude's `${WINMON_MCP_EXE}` expansion; it does not depend on PATH or the current directory. Do not enable both the plugin and direct registration for the same server.

Validate an extracted plugin with `claude plugin validate --strict 'C:\Path\To\Extracted WinMon Plugin'`. Syntax and validation were checked with Claude Code 2.1.318. See the official [plugin reference](https://code.claude.com/docs/en/plugins-reference) and [MCP configuration documentation](https://code.claude.com/docs/en/mcp). A successful manifest check does not prove a live client connection; see the implementation checklist for acceptance evidence. Copilot-specific setup is not yet documented or verified.

## Available tools

| Tool | Inputs and result |
|---|---|
| `get_system_snapshot` | Current CPU, memory, selected disk/network adapter and busiest GPU-engine readings. |
| `list_processes` | `sort`: cpu, memory, gpu, io, threads or handles. `filter`: case-insensitive name/application/product text or PID substring, at most 256 characters. `limit`: 1–200, default 20. |
| `get_process_details` | Positive `pid`; optional paired `expectedStartToken` and `expectedDomain` from an earlier result. |
| `sample_performance` | `durationSeconds`: 1–60, default 10. `outputIntervalSeconds`: 1–10, default 1; must divide duration. Optional positive `pid` and paired expected identity. Maximum four concurrent samples. |
| `get_network_connections` | Optional PID and paired expected identity. `limit`: 1–200, default 50. `includeEndpoints` defaults false. Returns TCP/UDP observations and IPv4/IPv6 availability. |

Process identity includes PID, a decimal-string opaque start token and its clock domain. Reuse both token and domain when following a process. A mismatch returns `identityMismatch`; a missing observation returns `notObserved`. These identities apply only to the originating machine/boot, and are not executable identifiers. Sampling pins one observed lifetime and stops following it if it disappears; it never switches to a reused PID.

## Privacy and interpretation

Default process results contain names, identities and numeric metrics. They omit command lines, environment variables, executable paths, usernames and machine names. Names and other OS metadata are untrusted data, not instructions. Network addresses and ports are omitted by default; request `includeEndpoints: true` only after agreeing to disclose those endpoints to the connected agent. No reverse DNS is performed. Read-only access still shares the returned data with your client/provider.

Every response has schema version 1, a UTC timestamp, freshness, collection interval, gap/error state and truncation/count metadata. Snapshot and process DTOs include explicit metric-availability maps; null means unavailable rather than zero. The collectors do not always distinguish permission denial from unsupported/missing data, so the server does not invent a cause. Connections preserve partial IPv4/IPv6 failure. Results are bounded to 256 KiB including structured and text representations; complete entries may be omitted with `truncated: true`.

CPU percentages use total machine capacity. GPU percent is the busiest observed engine. Disk percent is active time on the automatically selected drive, not capacity used. Network readings describe the automatically selected adapter. Identifiers/names accompany target-specific readings. Rates are bytes per second; memory is bytes. Process I/O includes files, network and devices and is not physical disk throughput. This first server does not connect to the optional per-process traffic service; endpoint observations do not contain transfer counters.

One shared collector samples every second, retaining at most 120 published frames. Initial requests wait up to five seconds for a post-baseline capture. Frames older than two seconds are stale and are not presented as current measurements. Tool cancellation leaves shared collection running; closing the client ends its server process and releases collectors. A synchronous native call may delay shutdown until it returns.

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
