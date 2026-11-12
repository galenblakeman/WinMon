# CLI distribution

The bundling/update design below is historical. [Separate installers](SEPARATE-INSTALLERS.md) supersedes it: CLI now has its own installer and ZIP, updates manually, and shares no desktop payload ownership. See [migration](../INSTALLER-MIGRATION.md) and [current implementation acceptance](SEPARATE-INSTALLERS-IMPLEMENTATION.md).

Ship the desktop and terminal apps under one release version. Build the CLI once for Windows x64 as a self-contained single executable; sign it and embed the same signed optional network-service setup bundle as the desktop app. Reuse those exact bytes in the desktop installer, desktop portable ZIP and CLI-only ZIP. The CLI retains its separate LocalAppData settings and explicit service-install command.

The installer includes both apps and offers an unchecked per-user PATH task. Uninstall removes a PATH entry only when this installation added it. Microsoft Store distribution remains desktop-only. There is no CLI-specific installer, updater, version schedule or WinGet listing.

Portable desktop updates validate both executables from the signed archive. They replace the CLI before the desktop, preserve both originals until desktop readiness, and restore the CLI if replacement or startup fails. New CLI installations are removed on rollback. Locked files cancel replacement; the updater does not terminate CLI sessions. Windows can replace a running CLI's executable while that session continues using its loaded version; restart the CLI to use the update. Installed updates use Setup and validate both installed versions afterward.

## Requirements

| Requirement | Status | Evidence |
| --- | --- | --- |
| Same CLI binary in all three desktop-channel downloads | Verified | Local New-Release.ps1 run produced the installer and both ZIPs; archive entries, CLI hashes, version and extracted JSON snapshot all passed |
| CLI-only service setup uses the signed bundle | Implemented | CLI publish receives NetworkServiceBundleDirectory in signed releases; signing-boundary tests pass, live signed setup acceptance remains external |
| Optional PATH integration with owned cleanup | Implemented | WinMon.iss and Test-Installer.ps1 |
| Portable pair replacement and rollback | Verified | UpdateClient, UpdateRuntime and updater tests cover pair commit, failed startup, locked files, interrupted recovery and legacy archives |
| CLI settings regression resolved | Verified | Bounded atomic-replacement retry under the settings mutex; temporary and persistent file-lock regressions; both full CLI suites pass |
| Desktop and Store verification | Verified | Verify.ps1: 1,617 desktop and 1,568 Store tests pass; both app/benchmark builds have zero warnings/errors |
| Signed release and clean-profile install acceptance | Blocked | Requires release signing and a clean Windows profile; the installed WinMon process was left running |

## Upgrade compatibility

Previously released portable updaters extract only WinMon.exe. Their first automatic update to this implementation upgrades the desktop alone; extract the full new portable ZIP once to obtain the CLI. Subsequent updates run the new helper and service both executables. Installed copies receive the CLI through Setup on their first upgrade. Old desktop-only portable archives remain supported only when the destination has no CLI to leave stale.

## Validation

The original settings failure was reproduced in full desktop verification and local release packaging. Captured tracing showed UnauthorizedAccessException in File.Move while overwriting the settings file. Retrying only that operation preserves the merged preferences and atomic-save boundary. The retry remains bounded and persistent failures retain the original file and report the existing warning. A separate unchanged network-setup replacement test failed once in the initial full run and passed its focused rerun; its code was not changed.

Installer environment notification uses Inno Setup 6.7.3's built-in [ChangesEnvironment](https://jrsoftware.org/is6help/topic_setup_changesenvironment.htm) directive.

Final desktop evidence: `artifacts/verify/20261110-145640-545-desktop`. Store evidence: `artifacts/verify/20261110-145837-282-store`. The earlier feature smoke produced 125 PNGs under `artifacts/verify/20261110-144739-473-desktop/feature`; update/footer and Settings renders were inspected. Three explicit fixture/elevated tests are intentionally not executed by normal verification. The separate unchanged network-setup test also passes in both final full suites.

A native Windows check ran an isolated published CLI snapshot, replaced its executable while it was running, and verified that the existing process completed a valid JSON report normally. Evidence: `artifacts/cli-distribution/native-session-6b4d2f4516634bd6a90849799c8bebc8`. This checks running-image behavior; the complete parent/helper native update harness remains a clean-environment acceptance step.

Final unsigned package build: `artifacts/packaging/5c557b4f9f1240bf8c6bc60254293348`. All 1,617 release-pipeline tests passed, Inno Setup compiled successfully, and generated artifacts are under `artifacts/cli-distribution/release`. ZIP contents, SHA256SUMS, identical CLI bytes between ZIPs, CLI version and an extracted CLI JSON snapshot passed; evidence is `artifacts/cli-distribution/package-evidence.json`. The local installer is 77,053,969 bytes, desktop ZIP 107,151,623 bytes and CLI ZIP 34,362,495 bytes. These unsigned packages deliberately omit update manifests and the signed network setup bundle; they are not official release assets. `Test-ReleaseSigning.ps1` separately passed its signing-order, signed CLI payload, checksum, real RSA manifest, failure-gate and unsigned-build checks with mocked Azure/build boundaries.

## Result

Distribution and pair servicing landed in the existing release and update paths. The settings save now tolerates brief replacement locks. Store CLI packaging and a separate CLI package-manager identity remain deferred as planned. Native checking refined the running-session behavior: Windows can replace a running image, so an existing CLI session may finish on its previous version. Signing and clean-profile install/update acceptance remain external release gates; no publication is part of this change.
