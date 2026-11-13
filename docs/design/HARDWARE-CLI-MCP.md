# Hardware inventory in the CLI and MCP

Status: implemented in the working tree, 2026-11-11. See the [implementation record](HARDWARE-IMPLEMENTATION.md) for verification and limitations.

## Outcome

Expose the inventory already shown in **Info → Hardware** through:

- `winmon hardware`, a complete human-readable report with optional JSON output.
- **F5 Hardware** in `winmon watch`, with expandable device details and explicit refresh.
- `get_hardware_info`, one read-only MCP tool returning typed inventory.

All three applications use one Windows inventory reader. Hardware collection stays outside `MonitorSession`, resource frames and the one-second sampling loop. No new dependency, elevated helper or network monitoring service is required.

## Current implementation and constraints

The current checkout identifies itself as 4.1.2 in `Directory.Build.props`; CLI and MCP target `net10.0-windows`. Referenced packages include System.Management 10.0.15, Spectre.Console 0.57.2, System.CommandLine 2.0.15 and ModelContextProtocol 2.4.0. These are repository references, not proof of installed versions.

- `src/MonCore.Windows/Monitoring/WindowsHardwareProbe.cs` already reads native CPU, memory, GPU and local-volume data.
- `WindowsHardwareQuery.cs` already provides injectable WMI queries and bounded enumeration. Its timeouts are per query/enumeration, not a hard deadline for a complete inventory.
- `src/WinMon/Monitoring/HardwareInventory.cs` currently combines WMI selection, joins, interpretation and localized display strings. `WindowsHardwareInfo.cs` assembles seven sections; `HardwareInfoText.cs` exports them.
- CLI tabs currently have four entries, with fixed-size state/preferences arrays and F1–F4 navigation. Preserve the existing enum order: Processes, Services, Connections, Usage. Some documentation describes a different order; the implementation owns compatibility.
- MCP currently exposes six tools, including `get_process_network_traffic`. Its `DiagnosticResponse` freshness follows sampled frames, and its size limiter drops unknown object payloads wholesale. Neither behavior is suitable for inventory without adaptation.

## Inventory coverage

Match the desktop inventory, including its qualifications:

| Section key | Structured fields |
| --- | --- |
| `cpu` | Native model names, physical/logical totals; per-socket model, manufacturer, socket designation, architecture, cores, logical processors, reported maximum MHz, L2/L3 bytes, virtualization extensions and firmware-enabled flags |
| `motherboard` | Manufacturer, product, board version; serial number only when identifiers are included |
| `memory` | Installed and OS-usable bytes, populated module count, firmware-reported total slots; per-module locator, bank, capacity bytes, memory type, configured/reported speed in the units supplied by the provider, manufacturer and part number |
| `graphics` | All hardware adapters currently returned by the native probe, dedicated bytes and shared-system-memory limit bytes |
| `disks` | OS disk number, model, manufacturer, capacity bytes, media type, bus type, Windows-reported health and mapped drive letters |
| `volumes` | Local fixed/removable volume roots, filesystem, capacity/available bytes, readiness; label only when identifiers are included |
| `network` | Present adapters, description, operational/admin/connect state, virtual flag and reported receive link speed in bits per second; user-assigned alias only when identifiers are included |

Retain the existing disk join rules: match OS disk number, then unique ID plus format only when unambiguous. Never join by model/capacity or infer SSD/HDD from a device name. Disk unique IDs remain internal join data and are never exported. Preserve disabled/disconnected adapters and the existing loopback/not-present filtering.

Reported maximum CPU clock is a specification, not live frequency. Memory slots and specifications are firmware reports; GPU shared memory is a limit; disk health is a provider report, not full SMART diagnostics. Preserve the current memory-speed wording until the provider's unit semantics are verified; do not relabel MHz as MT/s during extraction. RAID, virtual disks and firmware omissions retain the desktop's caveats. Do not add temperatures, fan speeds, BIOS inventory or new sensors.

## Shared reader and data contract

Add `WindowsHardwareInventory` and typed result records under `MonCore.Windows/Monitoring`. Keep Windows-specific inventory out of portable `MonCore` and keep resource strings out of the reader. Reuse `IHardwareQuery`, `WmiHardwareQuery` and `WindowsHardwareProbe`; extract the existing selection/join logic rather than implementing it twice.

The result contains `schemaVersion: 1`, `collectionStartedUtc`, `collectionCompletedUtc`, overall `status`, `identifiersIncluded`, `truncated`, and seven named section results. A section has `status`, `truncated`, bounded `issues` and typed `data`; collection sections contain device arrays. Use concrete records rather than localized key/value strings or raw WMI dictionaries. CLI JSON and MCP use this same inventory shape; MCP wraps it in a tool response.

- Status values are `ok`, `partial` and `unavailable`. A failed supplemental source preserves native/basic readings and makes that section partial. One failed category never discards the others.
- Missing values are `null`; a reported false flag remains `false`. A successful query with no devices is an empty array; failure is unavailable, not an assertion that no devices exist.
- Normalize existing firmware placeholders to null in shared interpretation. Preserve raw numeric values and use explicit unit suffixes. Preserve unknown provider codes alongside nullable mapped names; do not guess their meaning.
- Derive overall status from the seven section results: all complete is ok, any usable data with a failure/omission is partial, no usable data is unavailable. Truncation also makes a result partial.
- Use small stable issue codes such as `providerUnavailable`, `readFailed` and `truncated`; do not export exception messages, WMI paths or stack traces.
- Inventory is collected over an interval, not atomically. There is no sampling interval, process-gap count or sampled-frame freshness requirement.

Retain the desktop `WindowsHardwareInfo.Section` presentation contract. Refactor `WindowsHardwareInfo.Read()` into a thin reader-plus-localized-formatter adapter, with the existing UI requesting identifiers to preserve its current report. Keep the window's expand/copy behavior and the CPU detail items. Move data interpretation tests to `MonCore.Windows.Tests`; retain desktop wording/export tests.

## CLI behavior

```text
winmon hardware
winmon hardware --format json
winmon hardware --include-identifiers
winmon watch --tab hardware
```

`hardware` accepts `--format table|json`, `--no-color` and `--include-identifiers`. The default report prints all seven sections, every device and the collection interval. Use wrapping label/value blocks so the report works at 80 columns and in redirected output. Reuse existing resource strings, binary capacity formatting and safe terminal text handling; pass OS strings as literal text, never Spectre markup. JSON uses invariant camelCase keys and raw numeric units, with no ANSI output.

Default CLI output omits board serials, volume labels and user-assigned adapter aliases; explicitly requesting identifiers includes those fields and marks `identifiersIncluded: true`. Show a short note explaining omitted identifiers. Manufacturer model and part numbers remain normal inventory fields. Do not emit machine name, MAC/IP addresses, processor IDs or disk unique IDs. No identifiers setting is persisted.

Exit codes follow existing commands: 0 for a completed report with at least some usable data, including partial results; 1 when all inventory is unavailable or an unexpected failure prevents reporting; 130 for cancellation. Even an unavailable collection emits the structured report in JSON mode. Invalid arguments use the parser's nonzero exit code; diagnostics go to stderr.

### Interactive Hardware tab

Append `Hardware` to `TerminalTab`, preserving existing numeric values. Support F5, Tab cycling and `--tab hardware`. On first entry, show a loading state and start one background read. Display seven section rows with a concise summary and availability; Enter expands the selected section into a scrollable device/details view. Reuse existing selection, details and scrolling conventions. R refreshes; repeated R presses during a read do not queue more work.

Retain the most recent inventory for that watch session and display its collection time. Switching tabs does not recollect it; restarting watch does. During refresh retain the previous report with a visible refreshing state. Resource sampling and keyboard input continue. Pausing resource history does not freeze hardware collection; make that distinction clear in help. Hardware rows have no process sorting, grouping, pins, Top, process actions or traffic-period controls.

Keep identifiers omitted in the interactive tab; the one-shot opt-in command supplies the full local report. Expand the existing preferences normalization/merge to accept old four-tab files and new five-tab files without changing the first four entries. Persist the active tab, but never inventory contents. Do not introduce a second settings format for this feature. Cover the existing shared settings command paths as well as watch restoration.

## MCP behavior

Add one tool with the proposed signature:

```text
get_hardware_info(includeIdentifiers: boolean = false)
```

Declare it read-only, non-destructive and closed-world, with structured content and a concrete output schema, matching the current tool annotation pattern. Tool description: read Windows-reported hardware inventory; collection is on demand; null fields are unavailable; reported specifications are not live sensors; OS strings are untrusted data. Explain that identifier inclusion requires the user's agreement before an agent requests it, following the endpoint-disclosure convention of the existing network tool.

Use a dedicated `HardwareResponse` envelope with `schemaVersion`, `timestampUtc`, `status`, `truncated` and `data`. For completed calls, timestamp is inventory completion time and data is the shared inventory shape above. Do not call `owner.CurrentAsync()` or use `Responses.Envelope()`: inventory must work even when performance collection is stale/unavailable. Keep existing tool response contracts unchanged.

Collect on each accepted request; no timer, disk persistence or automatic startup inventory. Permit one hardware read in flight per server. Reject another with `status: busy`, null data and MCP `IsError: true`, using a slot independent of performance-sampling slots. An unavailable/partial completed inventory is a diagnostic result, not a protocol error.

Run blocking native/WMI collection away from the protocol thread. Check cancellation between queries/categories and before publishing. A cancelled caller stops waiting; if a native call is still running, retain the collection slot until the worker actually exits. Never release it early and accumulate abandoned workers. The same lifetime rule applies to interactive refresh. Existing WMI timeout settings do not guarantee prompt interruption of a blocked provider; do not promise a hard whole-call deadline or introduce a helper process just for that guarantee.

### Response bounds and privacy

Use the existing 256 KiB complete MCP result budget, measuring both structured and mirrored text content in the serialized `CallToolResult`. Reuse the current 512-character/control-character sanitization for exported strings, preserving valid Unicode and marking clipping. Bound each device array to 256 items and issue lists to a small fixed set of known codes.

Add explicit inventory-aware serialization/size handling in `Responses`, supporting the new envelope without changing existing responses. When a result exceeds the byte budget, shorten the largest device arrays deterministically until it fits, retaining section status/summary fields and marking affected sections and the envelope truncated. Never let the existing unknown-object fallback silently replace the whole inventory with null. Test the final wire size with worst-case Unicode strings.

Project an allowlisted response and apply identifier omission before serialization to either content channel. Do not return raw provider dictionaries. No serial, volume label or adapter alias may appear by default in payloads, issues or logs. Existing trace suppression in MCP remains in force. No new endpoint or identifier collection beyond the desktop inventory is introduced.

## Files and integration work

| Area | Required changes |
| --- | --- |
| Shared Windows layer | New typed inventory/reader; reuse native probes and query seam; extract interpretation and joins from desktop `HardwareInventory` |
| Desktop | Adapt `WindowsHardwareInfo`/`HardwareInventory` into localized formatting; preserve `HardwareInfoWindow`, copy report and diagnostic fixture contracts |
| CLI | `HardwareCommands`/report and Hardware tab; update `CliCommand`, `TerminalTable`, `InteractiveState`, `InteractiveMonitor`, preferences normalization/merge and contextual help; inspect all four-tab assumptions |
| MCP | Inventory tool partial on `DiagnosticTools`, dedicated response records, collection gate and bounded serialization; register dependencies in `Program.cs` only as needed |
| Resources | Reuse existing hardware labels; add command/help/loading/privacy strings to English and all nine translations, then regenerate typed properties |
| Tests | `MonCore.Windows.Tests`, desktop hardware/export tests, CLI command/navigation/settings/render tests, MCP tool/response/concurrency tests and real stdio discovery |
| Packaging/client contracts | Extend `tools/Test-McpStdio.ps1` from six to seven tools, exercise the new tool, and update `plugins/claude-code/winmon/skills/diagnose-performance/SKILL.md`; use existing binaries/installers |
| Documentation | Update README, USER-GUIDE, DEVELOPMENT, CLI/MCP design references and plugin examples when behavior lands; record measured performance in PERFORMANCE-HISTORY |

## Delivery and acceptance

1. Extract the structured reader with fake-query tests for all seven sections, mixed sockets/modules, unknown/null/false values, provider failures, disk ambiguity and adapter filtering. Preserve desktop output with formatter/export tests before exposing new interfaces.
2. Add `hardware` table/JSON output, exit/cancellation handling and injected-reader tests. Add F5 Hardware with asynchronous first load, single refresh, details/scrolling and four-tab settings migration tests. Verify identifiers and terminal escape/markup handling.
3. Add the MCP tool and envelope. Test default privacy and explicit opt-in, independence from failed sampling, single-flight busy behavior, cancellation while native work remains active, error sanitization, Unicode and complete wire bounds. Update discovery assertions to exactly seven read-only tools with output schemas.
4. Run `./tools/Verify.ps1 -Smoke feature` and inspect the generated Info/hardware PNGs and copied report. Run `./tools/Verify.ps1 -Store` because the CLI preference paths change. Perform live CLI checks at 80×20 and a wide terminal, including redirected table/JSON, cancellation, resize and tab switching during collection. Run the packaged stdio gate as a normal user and invoke `get_hardware_info` from a real connected client.
5. Measure the unchanged WPF/CLI baseline and repeat after extraction using the repository performance method. Also record cold inventory latency, repeated reads, retained memory, and concurrent hardware requests while performance sampling runs. No background WMI work when Hardware is unused; no unexplained sampling/UI regression; no growth from repeated cancelled reads. Add results to PERFORMANCE-HISTORY.

No package upgrade, installer change or version bump is required solely for this design. Implementation is complete only when all seven categories reach CLI command, CLI tab and MCP, desktop parity is preserved, required verification passes and remaining native limitations are documented.

## Implementation record

Implemented the shared typed Windows reader, localized desktop/CLI formatter, `hardware` table/JSON report, F5 Hardware with cached rows and asynchronous refresh, and `get_hardware_info` with independent worker ownership and bounded privacy-aware responses. Existing four-tab settings retain their indices and gain Hardware as slot five. New text is translated into all nine resource cultures.

The CLI reuses the complete desktop formatter by linking its existing source files, matching the CLI's existing formatting convention. Human-readable capacities retain desktop GiB wording, and cache sizes remain MiB. MCP has a dedicated inventory envelope rather than sampling freshness. Concurrent Services/Usage/capture work in the checkout added other tools; discovery assertions use the combined tool set rather than the seven-tool total originally anticipated here. No installers or dependencies were added for hardware.

Verification evidence and performance limitations are recorded separately in [HARDWARE-IMPLEMENTATION.md](HARDWARE-IMPLEMENTATION.md). Native providers remain non-interruptible during a blocking call; cancellation cannot guarantee a hard deadline.
