# Hardware CLI and MCP implementation

Tracks [the design](HARDWARE-CLI-MCP.md). Implemented in the working tree on November 11, 2026; no release or installation was performed.

| Requirement | Status | Evidence / remaining work |
| --- | --- | --- |
| Typed shared Windows inventory for all seven categories | Verified | Shared query/native probe seams; deterministic reader tests and native JSON inventory |
| Partial availability, units, unknown values and safe issues | Verified | Missing sources, native failures, nullable counts, numeric-code retention, overflow, cancellation and truncation tests |
| Identifier omission and explicit opt-in | Verified | Reader/MCP privacy tests; native CLI default and explicit opt-in reports retained locally |
| Desktop localized display and copy parity | Verified | Typed formatter/export tests; all expanded hardware sections reviewed in feature smoke |
| CLI hardware command, table/JSON and exit codes | Verified | HardwareCommandTests and redirected native table/JSON execution |
| F5 Hardware, details, async load and manual refresh | Verified | Render tests at 80×20 and 140×40 without performance frames; live pseudo-terminal load, refresh, CPU details and quit |
| Four-to-five tab preference compatibility | Verified | Existing values preserved by normalization/merge/reset tests; both build variants pass |
| MCP read-only hardware tool independent of sampling | Verified | Dedicated envelope tests and real SDK client invocation against published executable |
| Single worker, cancellation and response bounds | Verified | Busy/cancelled-worker ownership, safe errors, Unicode, CPU-name and nested-list complete-wire bounds tests |
| All resource cultures and generated properties | Verified | English plus all nine cultures; generated properties and StringResourceTests |
| Packaged stdio/client and plugin integration | Verified | Published stdio hardware invocation; isolated Codex registration and Claude direct/plugin health checks |
| README, user/developer guide and design records | Verified | Usage, privacy, ownership, migration and implementation notes updated |
| Baseline and final build/tests | Verified | Final desktop: 1,766 tests; zero build warnings/errors. Initial baseline feed-access failure recorded below |
| Desktop feature smoke and PNG review | Verified | 125 renders; reviewed hardware expansions plus light/minimum Info renders |
| Store variant verification | Verified | Final Store: 1,709 tests; zero build warnings/errors |
| Performance baseline and repeated measurements | Verified | UI baseline/repeat, eight paired live workloads, seven paired CLI snapshots, 120 native MCP reads and concurrent sampling; see PERFORMANCE-HISTORY |

## Verification evidence

- `artifacts/hardware-verify-final.log`: `./tools/Verify.ps1 -Smoke feature`, 12 steps passed, no failures. Smoke output: `artifacts/verify/20261111-222702-565-desktop/feature`.
- `artifacts/hardware-verify-store-final.log`: `./tools/Verify.ps1 -Store`, 11 steps passed, no failures.
- `artifacts/hardware-mcp-stdio`: published server discovery and native hardware result; SDK StdioTests separately passed against `artifacts/hardware-mcp-publish/WinMon.Mcp.exe`.
- `artifacts/mcp-package-validation/ccfbf22934ff46c9bf3403d887a27fc7`: source-free executable path containing spaces, isolated Codex and Claude registration, Claude plugin validation and connection health. Existing user registrations were not changed.
- `artifacts/hardware-performance`: before/after UI measurements and `native-mcp.json` with 120 native reads plus concurrent performance sampling. Artifacts are local verification outputs, not committed assets.

Initial sandbox builds failed NU1900 when package vulnerability feeds were unreachable. Restoring the verification output directories outside the sandbox cleared the cached errors without disabling audit for final verification. Test hosts also stalled inside the sandbox; final runs completed outside it. An interactive CLI smoke briefly locked the build executable; closing that session allowed the final build to pass. Concurrent investigation work added other MCP tools and an explicit performance test; discovery/verification registration includes that combined work without removing it.

## Open limitations

Native WMI/driver calls cannot be forcibly cancelled in process. Cancellation stops a caller waiting and prevents later queries; ownership remains with the active worker until it exits. The implementation must not claim a hard collection deadline.

Native checks ran on this Windows/Hyper-V development environment, not a physical multi-socket machine or every storage/adapter provider. Live terminal load/refresh/details were exercised in a pseudo-terminal; minimum and wide layout checks are deterministic render tests, not a manual resize acceptance run. A long-duration cancellation/memory soak, signed installation and a newly installed server in the user's existing agent session were not exercised. The isolated published client checks do not install or replace the existing MCP server.
