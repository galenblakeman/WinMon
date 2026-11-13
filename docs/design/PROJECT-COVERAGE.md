# Project coverage

The target is at least 80% line coverage independently for every production assembly in `src` and the experimental network probe. Use meaningful assertions for behavior and failure paths. Keep production behavior and security boundaries intact.

## Measurement

`tools/Measure-Coverage.ps1` wraps `Verify.ps1` with the installed `dotnet-coverage` collector. Its optional `-Smoke` runs all UI smoke modes; `-Store` measures the Store build separately. Cobertura output is saved under `artifacts/coverage/<timestamp>-<variant>`. Existing reports can be checked with `-Report`.

Exclude generated code and dependency/test assemblies. Include handwritten diagnostics, native adapters, startup code and linked production source in each assembly that compiles it. A missing assembly fails the gate. Do not combine Desktop and Store percentages or count tests from a failed run as successful verification.

## Tests

- Socket workload tests check all existing TCP/UDP scenarios, byte-count contracts, invalid commands and refusal to overwrite an existing report.
- A separate explicit non-elevated probe test checks the startup-failure report and verifies the ETW session is absent after exit. Pure capture-count tests separate protocol, IP family and direction.
- CLI and MCP use a shared updater fixture with a product-specific override. It checks signed product assets, corrupted/oversized/stalled downloads, cancellation, portable downloads and the Store restriction without launching an installer.
- Setup compiles the existing package signature and log ACL fixtures against its linked copies. Additional tests check named-pipe requester identity and service permission descriptors.
- Package tests reject malformed, oversized and unsigned metadata, signed invalid JSON, and invalid asset sizes/hash encodings. Policy tests cover on-disk normalization and rejection; setup entry-point tests reject privileged operations from the test host.
- Collector manager tests check cancelled subscriptions/startup, suspend/resume, disposal and releasing a lease after shutdown.
- Service-host tests check startup outside the protected installation fails closed, command-line invocations are rejected, and an unstarted service handles power/shutdown notifications.
- `SetupHost` supplies per-run service, registration, publisher and logging boundaries. Production defaults retain the fixed installation and real validation; elevated fixtures use protected temporary folders with simulated service/registry operations. They exercise install/repair, rollback, revocation, removal, acknowledgement and lock cancellation using real extraction, inventory, file locks, atomic replacement and ACLs.
- Handshake fixtures use real named pipes and requester identity checks for valid/invalid tokens, successful/rejected commits, disconnected clients and failed transactions. The entry point still opens its own verified package and uses the default host.
- Explicit signed-bundle tests check manifest/hash/publisher verification, asset locking and handle release after failure. `WINMON_NETWORK_SETUP_TEST_PACKAGE` must identify a complete signed bundle matching the referenced product version. Read-only service/registration inspection and protected temporary log-folder tests cover the native boundaries without changing the installation.
- The coverage gate has synthetic passing, below-threshold, missing-module, duplicate-module and invalid-rate checks.

## Native limits

The normal verifier disables explicit tests. Kernel ETW collection and protected-owner file tests require an explicitly selected elevated run. These tests do not prove installation or rollback of a signed service. No live service installation, removal or policy mutation is part of this coverage work.

## What landed

The repeatable per-project coverage gate and the tests above are implemented. Setup gained internal per-run boundaries rather than changing its security rules or exposing test options through the executable. The named-pipe helper accepts an internal package factory for fixture packages; production continues to call `OpenOwnPackage` before connecting. No mutable global overrides or executable options for overriding installation roots or bypassing publisher verification were added.

On November 12, 2026, the fresh combined Desktop and native report passed the 80% gate for every measured project:

| Project | Line coverage |
|---|---:|
| MonCore | 97.20% |
| MonCore.Windows | 85.46% |
| WinMon | 81.08% |
| WinMon.Cli | 82.77% |
| WinMon.Mcp | 85.54% |
| WinMon.NetworkProbe | 93.09% |
| WinMon.NetworkService | 81.68% |
| WinMon.NetworkSetup | 80.76% |

Evidence is in `artifacts/coverage-goal/final-combined.cobertura.xml` and `final-report/index.html`. The reports combine only the final production source: ordinary Desktop tests, ten successful desktop smoke modes, eighteen elevated setup fixtures, one elevated collector fixture and the successful standalone probe experiment. Desktop verification passed 1,935 tests; Store verification separately passed 1,869. The collector rate-sum assertions allow floating-point round-off while retaining exact byte-total assertions; a deterministic 1,024-flow case verifies that distinction. The verifier also checks NUnit's exact fixture-level explicit-skip reasons.

The initial final-run verifier rejected the adapter's `OneTimeSetUp:` skip prefix; after correcting and regression-testing that accounting, both complete verification commands passed. Its smoke assertions had all passed and its coverage used the same unchanged production source. The known one-DIP Gauges smoke failure, reproduced with and without instrumentation, was not included among the ten passing modes and remains unresolved. Representative fresh light and dark PNGs were inspected. The [performance record](../PERFORMANCE-HISTORY.md) records the unchanged UI binaries and isolated benchmark.

Native coverage requires an elevated environment and a signed release fixture; percentages do not replace native installation acceptance. Actual install/uninstall/SCM rollback on a disposable Windows installation remains outside these simulated transaction checks. An additional NativeAOT publish attempt could not finish because the local Visual Studio installation lacks the C++ linker workload; managed builds and tests passed, but NativeAOT publishing of the changed helper remains unverified.

### Global hotkey follow-up (November 12, 2026)

`GlobalHotkey` had zero covered lines in the combined report. Three integration tests in `GlobalHotkeyTests` now cover registration/replacement/disable, conflict handling, availability-probe cleanup, native window-message filtering and disposal. They use hidden test windows, reserve only unused Ctrl+Alt+Shift+F13–F24 combinations, and release their registrations before closing the windows. Desktop and Store suites run sequentially to avoid sharing those combinations across test processes.

The focused report at `artifacts/hotkey-coverage/focused.cobertura.xml` measures **30/30 lines (100%) and 87.5% of branches**, up from 0% line coverage. The updated combined report is at `artifacts/hotkey-coverage/report/index.html`; WinMon's line coverage rose from 81.08% to 81.27%. Full verification passed 1,938 Desktop tests and 1,872 Store tests. Tests deliver synthetic `WM_HOTKEY` messages to their own windows; physical keyboard input is not exercised. No production code changed.
