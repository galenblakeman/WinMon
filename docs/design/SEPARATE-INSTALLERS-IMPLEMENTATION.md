# Separate installers implementation checklist

Work started November 11, 2026. Design: [Separate installers](SEPARATE-INSTALLERS.md).

| Requirement | Status | Evidence / remaining acceptance |
| --- | --- | --- |
| Independent desktop, CLI and MCP installers and ZIPs | Verified | Final real unsigned release pipeline, three native installer compiles, product-only archive/hash checks and signing-boundary tests |
| Separate identities, collision/downgrade protection | Verified | All three scripts compile; native CLI/MCP collision, downgrade, repair and uninstall pass |
| Registered-path overlap and reparse protection | Blocked | Guards implemented and compiled; disposable-profile junction/overlap acceptance remains open |
| Desktop-only signed update metadata and transactions | Verified | 112 focused updater tests; full feature-only desktop/Store suites; signing-boundary tests |
| Nondestructive migration and owned CLI PATH cleanup | Blocked | Implemented; owned PATH lifecycle verified; actual historical bundled migration still requires a disposable profile |
| Optional user-scoped Codex/Claude registration | Verified | Final MCP suite: 40 tests in desktop and Store, including exact ownership, malformed configuration and bounded execution |
| Registration conflict, failure, repair and uninstall ownership | Verified | Isolated tests preserve existing/edited entries; native default-off MCP lifecycle passes after fixing unsupported uninstall launch API |
| Native selected registration task acceptance | Blocked | Tasks implemented/compiled and use the tested helper; no developer client settings changed; clean-profile opt-in/failure UI acceptance remains open |
| Shared service independence and mixed-version protection | Blocked | Existing service lifecycle retained; signed desktop-free CLI and mixed-version native acceptance remains external |
| User/developer/release documentation and translations | Verified | Migration guide and download docs updated; final resource tests: 38 passed; all ten resource files and designer regenerated |
| Full desktop and Store verification of the feature | Verified | Frozen committed baseline plus this change: desktop 11 steps passed including smoke; Store 10 steps passed; final MCP/resource deltas retested |
| Full live-checkout verification | Blocked | Concurrent capture/network edits caused unrelated compile errors and a capture-report assertion failure; preserved unchanged |
| Published MCP and client acceptance | Verified | Source-free executable in path with spaces, real Codex/Claude registrations in isolated homes, Claude/plugin health checks and stdio tool invocation |
| Historical desktop installer migration and lifecycle | Blocked | Requires disposable Windows profile; developer's desktop/MCP processes remain running and untouched |
| Signed size comparison and clean-machine service/runtime acceptance | Blocked | Requires signed baseline/new outputs, disposable Windows profile/VM and release signing credentials |

## Baseline

SDK selected by global.json: 10.0.404 (10.0.402 with latest-patch roll-forward).
Pre-existing working-tree edits affect network-service recovery and its documentation; preserve those edits.
Focused verification is logged to `artifacts/separate-installers-baseline.log` and `artifacts/separate-installers-baseline-native.log`. Both solution and benchmark builds succeeded without warnings. Sandbox testhosts failed to connect; the native rerun overlapped the updater implementation and exposed obsolete bundle assertions, so it is not a clean pre-edit functional baseline. MCP's existing 24 tests passed natively. Updater's initial isolated build lacked restore assets, then its sandbox testhost timed out; no clean pre-edit updater test result is claimed.

Available size references (bytes): the downloaded 4.1.0 desktop installer has a valid Authenticode signature and is 158,057,376 bytes; its desktop ZIP is 190,888,129 bytes. These older published assets are historical context, not a matching three-product build. The existing unsigned `artifacts/mcp-release-final` three-product build has a 101,831,189-byte installer and 141,922,027-byte ZIP. Its uncompressed executable payload is desktop 176,206,248 + CLI 77,015,236 + MCP 78,942,329 bytes. A matching signed before/after comparison remains blocked on release signing.

## Validation evidence

Concurrent tasks modified capture and network sources during verification. The live Store run built cleanly but failed `CaptureReportsTests.NetworkReport_UsesPeakTrafficAndPreservesFallbackDisclaimer`. During the live desktop run, the solution built but its later benchmark build encountered changing `ProcessNetworkSession.Frame` arguments. A live publish also encountered incomplete `CaptureReportText` edits. These are outside this change and were not repaired or reverted.

To obtain reproducible evidence, `artifacts/separate-installers-frozen` contains an export of HEAD with only this change's code, tests, packaging and installer resource additions applied. It excludes the unrelated network-service recovery, network-flow and capture work. Its source inventory and final integration-delta note are retained there. No worktree, commit, tag, publication or production agent-client registration was created.

- `artifacts/separate-installers-frozen-desktop.log`: `Verify.ps1 -Smoke full`, 11 steps passed. Solution and benchmark builds have zero warnings/errors. All eight test projects passed; normal explicit/elevation skips remain. The 232 smoke PNGs were reviewed on six contact sheets, including update-footer/progress layouts.
- `artifacts/separate-installers-frozen-store.log`: `Verify.ps1 -Store`, 10 steps passed with zero build warnings/errors.
- `artifacts/separate-installers-final-mcp.log` and `...-final-mcp-store.log`: final MCP source after timeout/configuration hardening, 40 tests passed in each variant. The explicit four-minute performance measurement is not part of this packaging change.
- `artifacts/separate-installers-final-resources.log`: 38 resource tests passed after the final cleanup-message translations.
- `artifacts/separate-installers-signing.log`: fake Azure/build boundary checks passed for all executable/installer signing order, product-only ZIP payloads, plugin archive, final hashes, RSA desktop metadata, error gates and unsigned local packaging.
- `artifacts/separate-installers-cli-lifecycle.log`: native install/snapshot/repair/uninstall, default-off PATH, owned PATH restoration, collision and downgrade rejection passed.
- `artifacts/separate-installers-mcp-lifecycle.log`: native install/stdio/repair/uninstall, collision and downgrade rejection passed. An earlier failed validation-only MCP installation was repaired with the corrected uninstaller and removed after checking its exact workspace registration path.
- `artifacts/separate-installers-client-acceptance.log`: published MCP executable initialized, discovered five tools, served a snapshot and shut down. Real Codex/Claude user registrations and Claude plugin validation/health passed with isolated temporary homes; the script skipped only redundant SDK integration already exercised by the MCP suite.
- `artifacts/separate-installers-final-release-retry.log`: final canonical unsigned `New-Release.ps1` run passed all eight suites, published the three self-contained executables and built three installers, three executable ZIPs, plugin/WinGet archives and checksums. Final outputs are under `artifacts/separate-installers-frozen/artifacts/separate-final-release`.
- `artifacts/separate-installers-desktop-migration-package.log` and `artifacts/separate-installers-migration-lifecycle.log`: final desktop legacy-PATH cleanup compiled and passed a unique-AppId/mutex fixture without icons or app launch. Uninstall removed exactly the legacy entry and preserved independent CLI ownership/PATH; the harness restored the original raw user PATH and registry test records. Final desktop installer, WinGet hash and checksums were refreshed afterward. The reusable `tools/Test-InstallerMigration.ps1` is included in release CI.

Native tests run outside the sandbox because sandbox testhosts failed to connect. Service ETW and protected-owner tests retain their explicit/elevation skips. Tests here do not establish operation on a Windows machine without .NET, older Windows compatibility, signed/UAC service installation, all product install/uninstall orders, or historical Inno uninstall-log migration. These remain pre-publication acceptance items rather than claimed passes.

A later canonical packaging rerun encountered an unchanged `RevocationTransaction_RollsBackAfterRestartFailureAndStopsCachedServiceFirst` file-replacement failure (rollback could not remove its temporary policy file). Its isolated rerun passed, as had the earlier desktop, Store and packaging runs. The service source was not modified to mask this transient failure. The final canonical retry passed and is recorded in `artifacts/separate-installers-final-release-retry.log`.

## Local size measurements

Unsigned local desktop payloads are approximately half the existing unsigned three-product baseline: installer 101,831,189 → 52,902,993 bytes; ZIP 141,922,027 → 73,107,255 bytes. This comparison is illustrative, not a matching signed production measurement. The separate local installers are approximately 26.7 MB (CLI) and 26.8 MB (MCP); final output sizes/checksums are in the local release artifacts. No runtime sampling or hot-path behavior changed, so no sampling benchmark result is claimed.
