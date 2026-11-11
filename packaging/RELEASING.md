# Releasing WinMon

The scripts require Windows x64, PowerShell 7.4 or later, the .NET SDK pinned in `global.json`, Inno Setup 6.7.3, and a NativeAOT-capable Visual Studio Desktop C++ workload with the Windows SDK. Official release packaging signs and timestamps the application, service, setup helper and installer through Azure Artifact Signing. Its pinned PowerShell module also requires the .NET 8 runtime. Inno Setup's commercial licensing guidance applies to use of its compiler; see its [license information](https://jrsoftware.org/isorder.php).

## Code signing policy

See the [Code signing policy](../docs/CODE_SIGNING.md) for Azure OIDC, signing permissions, and the `release` environment secrets. Include a policy link and an accurate signing-status statement in release notes. Authenticode and the RSA update-manifest signature are separate requirements; both must verify before publication.

## Build locally

From the repository root:

```powershell
./tools/Install-InnoSetup.ps1
./tools/New-Release.ps1 -ExpectedTag v4.0.1 -CodeSign -UpdateSigningKeyPath C:\secure\winmon-update-private.pem
```

The compiler bootstrap downloads the pinned vendor release, verifies its SHA-256 and Authenticode signature, and installs it per-user under `artifacts/toolchain`. It also registers the compiler's own uninstaller with Windows. An existing compiler can instead be supplied with `-InnoCompiler path/to/ISCC.exe`; use version 6.7.3.

The release script checks the product/application manifest versions, runs the complete Release test suite, publishes the self-contained Windows x64 desktop, CLI and MCP executables into isolated build output, signs all three for official releases, compiles the installer, and generates:

- `WinMon-<version>-win-x64.zip`: desktop, CLI and MCP executables, MIT license, portable instructions and `portable.flag`. Extract into a writable folder; preserve `settings.json` when upgrading.
- `WinMon-Cli-<version>-win-x64.zip`: `WinMon.Cli.exe`, MIT license and terminal instructions. It has no desktop settings or updater.
- `WinMon-<version>-Setup-x64.exe`: per-user installer, normally targeting `%LOCALAPPDATA%\Programs\WinMon`. It installs all three executables, adds a Start menu entry, an optional desktop shortcut, an unchecked user-PATH task for `WinMon.Cli`, and an Apps & Features uninstall entry. It requires WinMon to be closed. Silent installations do not launch the app.
- `WinMon-Claude-Code-<version>.zip`: standalone plugin manifest, MCP configuration and performance-diagnostics skill. Extract to a stable folder and configure `WINMON_MCP_EXE`; see [MCP setup](../docs/MCP.md). Packaging does not register agent clients automatically.
- `WinMon-<version>-winget.zip`: three WinGet schema 1.12.0 manifests under the community repository's directory structure. The installer hash is calculated from the final signed installer.
- `update-manifest.json` and `update-manifest.sig`: emitted only when `-UpdateSigningKeyPath` is supplied. The first is a compact signed record of the portable ZIP and installer name, size and SHA-256; the second is its base64 RSA SHA-256 PKCS#1 v1.5 detached signature.
- `SHA256SUMS.txt`: checksums for every release asset above.

Assets are placed in `artifacts/releases/<version>`. Existing output is refused to prevent accidental replacement of a published version. For another local build, specify a fresh `-OutputDirectory`. Build output and TRX results remain under the printed `artifacts/packaging/<run>` directory. These are repeatable build steps, not a guarantee of byte-identical binaries.

The installer supports x64 Windows starting at build 14393; runtime compatibility does not establish Microsoft support for an out-of-support Windows edition. Windows ARM64 and x86 are not included in this release. No separate .NET installation is required.

## Optional service payload

Signed desktop releases first run `tools/New-NetworkServicePackage.ps1`: publish the self-contained service and NativeAOT helper, sign both through Artifact Signing, and sign a bounded version/runtime/protocol/hash/size manifest with the existing RSA release key. The four assets are embedded into both the desktop and CLI executables before they are signed. Portable updates replace both executables and retain both originals until desktop startup succeeds; see [CLI distribution](../docs/design/CLI-DISTRIBUTION.md) for recovery and older-portable migration. `-CodeSign` requires the matching RSA private key. Unsigned local builds omit the bundle and cannot provision a service. Neither path installs a service during packaging. Signed installers expose an unchecked `networkservice` task that runs `WinMon.exe --install-network-service` as the original installer user after file installation and enables that user's monitoring preference only on success. The task is not preselected again on upgrades. Unsigned installers omit it. Cancellation or failure leaves WinMon installed and reports that Settings → Network can retry; silent installers log the failure without a dialog. The service retains its independent removal lifecycle. A signed/protected installation acceptance run remains mandatory before release; unit tests and fake signing-boundary tests do not establish UAC, SCM or alternate-account behavior. Microsoft Store builds reject a supplied network setup bundle.

## MSIX / Microsoft Store

Build the additional Store channel with `tools/New-MsixPackage.ps1`. See [MSIX packaging and validation](msix/README.md) for identity inputs, prerequisites, local testing and submission. Store builds disable the GitHub updater; installer and portable builds retain it.

## Validate the installer

Use a clean Windows user profile or disposable VM/CI runner:

```powershell
./tools/Test-Installer.ps1 -InstallerPath ./artifacts/releases/4.0.1/WinMon-4.0.1-Setup-x64.exe
```

This refuses to run if a WinMon installer registration, startup entry or app shortcut already exists. It checks the running-app mutex, silent installation, uninstall metadata, shortcut target, the installed executable's native smoke run, same-version repair, portable preference preservation, and cleanup of app-owned startup/hotkey integration. It also verifies MCP executable identity and invokes `Test-McpStdio.ps1` before and after repair to test the installed server's initialization, read-only tool/schema discovery, snapshot and graceful shutdown. The MCP check requires PowerShell 7.4 but no SDK or agent client. Logs and smoke evidence remain under `artifacts/installer-validation`. This repair test does not replace a real older-to-newer version upgrade test before subsequent releases.

Uninstall preserves preferences. It removes the startup registry value, the app's optional root-level launch-hotkey shortcut, and an installer-owned PATH entry only when they target the executable or installation being uninstalled. The installer's `Programs\WinMon\WinMon.lnk` is separate from the app-managed `Programs\WinMon.lnk`, so installing does not turn on launch hotkeys.

## GitHub Releases

The `Package release` workflow can be run manually to build and validate artifacts without creating a release. Pushing a `v<version>` tag runs the same checks and creates a **draft** GitHub release with the assets and generated release notes. The tag must match `Directory.Build.props` and `app.manifest`. Every run requires the Azure OIDC secrets and `WINMON_UPDATE_SIGNING_KEY_BASE64` in the `release` environment and produces Authenticode-signed artifacts plus a signed update manifest. The workflow uses the SDK from `global.json`, the pinned compiler, and WinGet 1.29.380 on Windows Server 2025. It requires GitHub Actions and a `GITHUB_TOKEN` allowed to write repository contents. See [update publishing](../docs/UPDATING.md) for key provisioning and rotation.

For each release:

1. Set `Version` in `Directory.Build.props` and its matching four-part `assemblyIdentity version` in `src/WinMon/app.manifest`. Update the README version.
2. Review and commit the intended source and packaging changes. Test the installation and upgrade on the supported Windows versions you intend to advertise.
3. Create and push the matching tag (for example `v4.0.1`). This is a separate publishing action; the local scripts never commit, tag or push.
4. Review the workflow results, draft release notes, assets, checksums and signing status. Publish the draft when ready.

Once a release is public, keep its assets unchanged: WinGet URLs and hashes refer to those exact bytes. Ship corrections under a new version. If adding signatures later, sign the application before packaging and the installer before generating WinGet manifests/checksums.

## Service validation before release

For service-only native acceptance before a release, the manual `Build signed network service test bundle` workflow builds and signs the helper/service with the existing release environment and uploads Actions artifacts. It creates no release or tag and installs nothing. The isolated validation branch bootstrapped this workflow with a branch-restricted push trigger; run 42426234294 built and signed the bundle successfully, and local package verification passed. Native installation acceptance remains separate. Use an approved reviewed ref and verify the downloaded bundle before installing it. See [code signing](../docs/CODE_SIGNING.md#service-validation-without-a-release). Unsigned local packages omit provisioning and cannot validate the first-enable installation path.

## WinGet submission

Generating manifests does not publish a package to WinGet. First publish the GitHub release so the versioned installer URL is publicly downloadable. Then extract `WinMon-<version>-winget.zip` and validate its version directory:

```powershell
winget validate --manifest ./manifests/g/GalenBlakeman/WinMon/4.0.1
```

Copy that directory into a fork of [`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs) and submit a pull request. Follow Microsoft's [submission instructions](https://learn.microsoft.com/windows/package-manager/package/repository) and [validation requirements](https://learn.microsoft.com/windows/package-manager/package/winget-validation). Local schema validation does not prove acceptance or a working public download URL. Recheck the downloaded release asset's SHA-256 before submission.

After Microsoft accepts the package, users can install and upgrade it with:

```powershell
winget install --id GalenBlakeman.WinMon --exact --source winget
winget upgrade --id GalenBlakeman.WinMon --exact --source winget
```

Every new version requires updated manifests and a new submission. The workflow prepares them but does not automatically submit a PR or publish a release.
