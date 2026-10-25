# Releasing WinMon

The scripts require Windows x64, PowerShell 7.4 or later, the .NET SDK pinned in `global.json`, and Inno Setup 6.7.3. Official release packaging signs and timestamps the application and installer through Azure Artifact Signing. Its pinned PowerShell module also requires the .NET 8 runtime. Inno Setup's commercial licensing guidance applies to use of its compiler; see its [license information](https://jrsoftware.org/isorder.php).

## Code signing policy

See the [Code signing policy](../docs/CODE_SIGNING.md) for Azure OIDC, signing permissions, and the `release` environment secrets. Include a policy link and an accurate signing-status statement in release notes. Authenticode and the RSA update-manifest signature are separate requirements; both must verify before publication.

## Build locally

From the repository root:

```powershell
./tools/Install-InnoSetup.ps1
./tools/New-Release.ps1 -ExpectedTag v3.4.0 -CodeSign -UpdateSigningKeyPath C:\secure\winmon-update-private.pem
```

The compiler bootstrap downloads the pinned vendor release, verifies its SHA-256 and Authenticode signature, and installs it per-user under `artifacts/toolchain`. It also registers the compiler's own uninstaller with Windows. An existing compiler can instead be supplied with `-InnoCompiler path/to/ISCC.exe`; use version 6.7.3.

The release script checks the product/application manifest versions, runs the complete Release test suite, publishes the self-contained Windows x64 executable into isolated build output, compiles the installer, and generates:

- `WinMon-<version>-win-x64.zip`: executable, MIT license, portable instructions and `portable.flag`. Extract into a writable folder; preserve `settings.json` when upgrading.
- `WinMon-<version>-Setup-x64.exe`: per-user installer, normally targeting `%LOCALAPPDATA%\Programs\WinMon`. It adds a Start menu entry, an optional desktop shortcut, and an Apps & Features uninstall entry. It requires WinMon to be closed. Silent installations do not launch the app.
- `WinMon-<version>-winget.zip`: three WinGet schema 1.12.0 manifests under the community repository's directory structure. The installer hash is calculated from the final signed installer.
- `update-manifest.json` and `update-manifest.sig`: emitted only when `-UpdateSigningKeyPath` is supplied. The first is a compact signed record of the portable ZIP and installer name, size and SHA-256; the second is its base64 RSA SHA-256 PKCS#1 v1.5 detached signature.
- `SHA256SUMS.txt`: checksums for all three assets above.

Assets are placed in `artifacts/releases/<version>`. Existing output is refused to prevent accidental replacement of a published version. For another local build, specify a fresh `-OutputDirectory`. Build output and TRX results remain under the printed `artifacts/packaging/<run>` directory. These are repeatable build steps, not a guarantee of byte-identical binaries.

The installer supports x64 Windows starting at build 14393; runtime compatibility does not establish Microsoft support for an out-of-support Windows edition. Windows ARM64 and x86 are not included in this release. No separate .NET installation is required.

## MSIX / Microsoft Store

Build the additional Store channel with `tools/New-MsixPackage.ps1`. See [MSIX packaging and validation](msix/README.md) for identity inputs, prerequisites, local testing and submission. Store builds disable the GitHub updater; installer and portable builds retain it.

## Validate the installer

Use a clean Windows user profile or disposable VM/CI runner:

```powershell
./tools/Test-Installer.ps1 -InstallerPath ./artifacts/releases/1.0.0/WinMon-1.0.0-Setup-x64.exe
```

This refuses to run if a WinMon installer registration, startup entry or app shortcut already exists. It checks the running-app mutex, silent installation, uninstall metadata, shortcut target, the installed executable's native smoke run, same-version repair, portable preference preservation, and cleanup of app-owned startup/hotkey integration. Logs and smoke evidence remain under `artifacts/installer-validation`. This repair test does not replace a real older-to-newer version upgrade test before subsequent releases.

Uninstall preserves preferences. It removes the startup registry value and the app's optional root-level launch-hotkey shortcut only when they target the executable being uninstalled. The installer's `Programs\WinMon\WinMon.lnk` is separate from the app-managed `Programs\WinMon.lnk`, so installing does not turn on launch hotkeys.

## GitHub Releases

The `Package release` workflow can be run manually to build and validate artifacts without creating a release. Pushing a `v<version>` tag runs the same checks and creates a **draft** GitHub release with the assets and generated release notes. The tag must match `Directory.Build.props` and `app.manifest`. Every run requires the Azure OIDC secrets and `WINMON_UPDATE_SIGNING_KEY_BASE64` in the `release` environment and produces Authenticode-signed artifacts plus a signed update manifest. The workflow uses the SDK from `global.json`, the pinned compiler, and WinGet 1.29.380 on Windows Server 2025. It requires GitHub Actions and a `GITHUB_TOKEN` allowed to write repository contents. See [update publishing](../docs/UPDATING.md) for key provisioning and rotation.

For each release:

1. Set `Version` in `Directory.Build.props` and its matching four-part `assemblyIdentity version` in `src/WinMon/app.manifest`. Update the README version.
2. Review and commit the intended source and packaging changes. Test the installation and upgrade on the supported Windows versions you intend to advertise.
3. Create and push the matching tag (for example `v1.0.0`). This is a separate publishing action; the local scripts never commit, tag or push.
4. Review the workflow results, draft release notes, assets, checksums and signing status. Publish the draft when ready.

Once a release is public, keep its assets unchanged: WinGet URLs and hashes refer to those exact bytes. Ship corrections under a new version. If adding signatures later, sign the application before packaging and the installer before generating WinGet manifests/checksums.

## WinGet submission

Generating manifests does not publish a package to WinGet. First publish the GitHub release so the versioned installer URL is publicly downloadable. Then extract `WinMon-<version>-winget.zip` and validate its version directory:

```powershell
winget validate --manifest ./manifests/g/GalenBlakeman/WinMon/1.0.0
```

Copy that directory into a fork of [`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs) and submit a pull request. Follow Microsoft's [submission instructions](https://learn.microsoft.com/windows/package-manager/package/repository) and [validation requirements](https://learn.microsoft.com/windows/package-manager/package/winget-validation). Local schema validation does not prove acceptance or a working public download URL. Recheck the downloaded release asset's SHA-256 before submission.

After Microsoft accepts the package, users can install and upgrade it with:

```powershell
winget install --id GalenBlakeman.WinMon --exact --source winget
winget upgrade --id GalenBlakeman.WinMon --exact --source winget
```

Every new version requires updated manifests and a new submission. The workflow prepares them but does not automatically submit a PR or publish a release.
