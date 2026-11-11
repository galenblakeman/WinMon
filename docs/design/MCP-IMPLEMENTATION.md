# MCP implementation checklist

Tracking acceptance for [the proposal](MCP-SERVER.md). Statuses describe evidence, not intent.
An explicitly waived check is omitted by user decision and is not represented as verified.

| Requirement | Status | Evidence or next check |
|---|---|---|
| Existing solution/unit baseline | Verified | Existing projects passed outside sandbox; restricted testhost could not connect |
| Stable pinned official SDK and stdio host | Verified | ModelContextProtocol 2.3.0; tagged official tools/transports documentation and real SDK client |
| One serialized MonitorSession, warm-up, freshness, bounded publication and shutdown | Verified | Lifecycle, cancellation and delayed-observation freshness tests pass |
| Five explicit read-only tools and validated inputs | Verified | DiagnosticToolsTests; real discovery of exactly five read-only handlers |
| Versioned DTOs, units, quality, errors and total response bounds | Verified | Serialization, discovered output schemas, Unicode list bounds and complete trend-result bounds pass |
| Lifetime-safe identities and PID reuse | Verified | Details, connections and controlled-clock sampling tests |
| Weighted trends, coverage, cancellation and concurrency limits | Verified | PerformanceAggregateTests; controlled-clock integration, four-slot cancellation isolation |
| Privacy defaults, endpoint opt-in, no mutation | Verified | Field allowlist and endpoint tests; discovered tools have read-only annotations |
| Real stdio initialization/discovery/invocation and integrity | Verified | StdioTests; source-free native JSON-RPC session |
| Solution and normal verification integration | Verified | WinMon.sln and explicit Verify.ps1 test list |
| Claude plugin, skill and environment discovery | Verified | Strict Claude 2.1.318 validation; isolated plugin:winmon:winmon connected health |
| Publish/signing integration, portable and plugin archives | Verified | Signing boundary tests including manifest versioning; unsigned Inno 6.7.3 build, archive contents and SHA256SUMS; native installer gate below |
| Published executable and actual client acceptance | Verified | External path with spaces, invalid child DOTNET_ROOT; SDK session; isolated Codex 0.166.0 add/list and Claude direct/plugin connection health |
| Installed-server protocol check before and after repair | Verified | Final installer run invokes Test-McpStdio successfully before and after repair |
| Clean-profile installer lifecycle | Verified | User uninstalled WinMon; final-package install, repair, MCP launch and uninstall passed on this devbox |
| Execution on a machine without a separate .NET runtime | Waived by user | On November 11, 2026, the user explicitly requested skipping this test and completing the goal. Runtime-free-machine behavior remains unverified |
| User/developer documentation and official setup references | Verified | README, DEVELOPMENT, docs/MCP.md, release guide and performance history reconciled with actual evidence |
| Full verification and affected smoke/Store gates | Verified | Full desktop/Store and feature smoke passed; final packaging suite passed 1,649 tests, including 24 MCP tests; final installer smoke passed |
| Performance baseline and bounded server load measurements | Verified | WPF baseline/repeat, seven CLI snapshots per build, final four-request profile and two retained heaps; results in PERFORMANCE-HISTORY |
| Final requirement-by-requirement audit and landed/departures | Verified | Current source and evidence audited; all remaining required implementation and acceptance work is complete after the explicit runtime-free-machine test waiver |

## Verification evidence

- Desktop full verification: `artifacts/verify/20261111-105911-694-desktop`, all steps passed including feature smoke. Relevant update-progress, dark update-footer and feature-settings PNGs inspected.
- Store full verification: `artifacts/verify/20261111-105651-532-store`, all steps passed. The preceding run hit a file-replacement failure in unchanged network-setup rollback code; its focused rerun and complete rerun passed.
- Existing explicit fixture-write and privileged ETW/ACL tests remain intentionally unexecuted by ordinary verification. The explicit performance test is run separately.
- Unsigned local release: `artifacts/mcp-release-local`; build/test evidence `artifacts/packaging/b8b89faade8f4adcb4d58b2eb1d9c89d`. Contains desktop installer, desktop portable ZIP with MCP, unchanged standalone CLI ZIP contract and separate plugin ZIP; final checksums verified. This is not a signed or published release.
- Signing boundary: `artifacts/signing-validation/b8adf6b484384c3f8c33777d052e4a1e`, checks passed with mocked signing/build boundaries, release-derived plugin manifest version and a real test RSA manifest.
- Final MCP regression suite: `dotnet test tests/WinMon.Mcp.Tests/WinMon.Mcp.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false --artifacts-path artifacts/mcp-final-tests --filter 'TestCategory!=Performance'`: 24/24 passed, including a real SDK subprocess session.
- Final Store-target MCP suite: same command with `-p:StoreBuild=true --artifacts-path artifacts/mcp-final-store-tests`: 24/24 passed.
- Later deterministic verification: `artifacts/verify/20261111-110339-631-desktop`; MCP unit tests passed, but unchanged `RevocationTransaction_RollsBackAfterRestartFailureAndStopsCachedServiceFirst` again hit a native file-replacement failure. No network-setup source was changed for this task. Earlier full desktop and Store runs passed; this intermittent failure is not represented as a clean final whole-solution run.
- Final published executable/client health: `artifacts/mcp-package-validation/53c72dd2d7f9409cab53cedc8a566d5a`. Final-current executable and actual release plugin archive were copied/extracted externally, launched from an external working directory, and passed direct JSON-RPC, SDK integration, strict plugin validation, isolated Codex registration and Claude direct/plugin connected-health checks. Raw outputs are retained. User client configurations are unchanged.
- Installed-server check integration: `Test-McpStdio.ps1` is shared by the package and installer gates, checks initialization, the five read-only tools/output schemas, a snapshot and graceful stdin-close shutdown, and needs neither an SDK nor agent client. Its standalone run passed (`artifacts/mcp-stdio-final`); the complete package/SDK/client/plugin gate passed again after extraction (`artifacts/mcp-package-validation/f32cea274286436dabf6704c1f254d33`). The installer invokes it before and after same-version repair.
- Earlier installer acceptance attempt: guarded refusal while WinMon was still installed. Sandbox attempt: `artifacts/mcp-sandbox/072115fd0f0e4ef9a4b3609ab04e935c`, no guest evidence. The subsequent user-uninstall and successful installer retries below supersede that profile blocker.
- November 11 retry after the user uninstalled WinMon: `Test-Installer.ps1` passed against the first local package (`artifacts/installer-validation/58bce005ad224e2d8bff7dd060cecde1`). The source was then rebuilt into `artifacts/mcp-release-final`; its complete packaging suite passed 1,649 tests with zero failures, including 24 MCP tests. Build evidence: `artifacts/packaging/178d3f97b35246d6b7495b88e6f07af1`; final artifact SHA256SUMS verified.
- Final-package installer acceptance passed: `artifacts/installer-validation/97f144012a5e41a4b6af98ace84c4112`. The running-app mutex guard, install metadata/MCP identity, app/CLI smoke, installed MCP session, same-version repair, repaired MCP session, preference preservation, PATH cleanup and uninstall assertions all passed. The update-progress and feature-settings PNGs were inspected. The test installation was uninstalled successfully.
- Runtime isolation control: the self-contained executable passed with invalid child DOTNET_ROOT and DOTNET_ROOT_X64 (`artifacts/mcp-runtime-root-positive`). A deliberately framework-dependent Debug publish also passed those settings (`artifacts/mcp-runtime-control`, `artifacts/mcp-runtime-root-negative`). Consequently these launch tests prove successful local execution, not operation without an installed runtime. A native-module-path assertion was also not usable on this host and was removed; no production code changed for that experiment.
- Runtime profiles: `artifacts/mcp-performance`, `artifacts/mcp-performance-capped`, `artifacts/mcp-performance-final`; final explicit profile passed in 3m53s and retained heap decreased from 49.8 to 44.2 MB. WPF and CLI evidence is listed in the performance history.

## Final audit limits

The final unsigned package now contains the final MCP source and passed all 1,649 packaging tests plus native installer lifecycle acceptance. Store-target MCP tests passed 24/24 separately. Earlier network-setup file-replacement failures remain recorded as intermittent history; the final complete packaging suite passed that unchanged test. Local unsigned packages are test evidence, not official signed/published releases. No tag, push, production signing-service invocation or publication was performed.

## User-approved acceptance exception

Native install/repair/uninstall and installed-server launch are verified. On November 11, 2026, the user explicitly waived the packaged-server test on a Windows machine without a separate .NET runtime, requested marking the goal complete, and authorized committing the implementation. That test was skipped, not passed; this devbox's installed SDK/runtime was not removed. Actual production Authenticode signing and publication remain separate release operations outside this implementation task.
