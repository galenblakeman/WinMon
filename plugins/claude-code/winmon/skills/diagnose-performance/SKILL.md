---
name: diagnose-performance
description: Diagnose local Windows performance with WinMon's read-only MCP tools.
---

# Diagnose performance

Start with `get_system_snapshot`. State unavailable or stale metrics rather than treating them as zero.

Use `list_processes` to identify candidates. When following one process, retain the returned PID and lifetime identity and pass both to `get_process_details` or `sample_performance`; a PID can be reused after a process exits.

Use `sample_performance` for a bounded trend. Explain its requested duration, valid coverage and gaps. The server samples once per second, so it can miss shorter peaks.

Use `get_network_connections` without endpoints first. Explain that `includeEndpoints: true` discloses local and remote addresses and ports, then obtain the user's agreement before requesting it. Do not request process command lines, credentials, private paths or network setup; the server is read-only.

Use `get_process_network_traffic` to find bandwidth-heavy processes when the optional service is already installed. It connects on demand without setup or elevation and returns no endpoints or paths. Rates use the delayed five-second service window; totals cover this server subscription across all adapters. Explain incomplete/unavailable coverage and retry warming-up results after a few seconds. Preserve lifetime identity when following a PID.
