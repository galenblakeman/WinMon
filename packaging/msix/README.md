# MSIX / Microsoft Store

This is an additional release channel. `tools/New-Release.ps1` continues to produce the existing Inno Setup installer, portable ZIP and GitHub update manifests.

## Build

Use PowerShell 7 on Windows, the SDK pinned by `global.json`, and the Windows SDK's x64 `MakeAppx.exe`. The script discovers MakeAppx under `Windows Kits/10/bin`, or accepts `-MakeAppxPath` (including a copy from Microsoft's `Microsoft.Windows.SDK.BuildTools` NuGet package).

The Store identity from Partner Center is saved in `StoreIdentity.psd1`. From the repository root, build the Store package with:

```powershell
./tools/New-MsixPackage.ps1
```

The saved values are package metadata, not signing credentials. If the Store identity changes, copy `Package/Identity/Name`, `Package/Identity/Publisher`, and `Package/Properties/PublisherDisplayName` exactly from Partner Center's Product identity page, and use a reserved app name for `DisplayName`. Command-line arguments can override the saved values, for example for a local development package:

```powershell
./tools/New-MsixPackage.ps1 -PackageName 'WinMon.Local' -Publisher 'CN=WinMon.Local'
```

The script publishes a self-contained x64 directory with `StoreBuild=true` and `PublishSingleFile=false`, generates package logos from the existing WinMon PNG, fills the manifest, and invokes MakeAppx with validation enabled. `/l` allows localized resources. No separately installed .NET runtime is required. `portable.flag` is excluded; preferences use the existing local application-data path. Verify packaged storage and migration on a test account.

Version comes from `Directory.Build.props` with a fourth component of zero, checked against the executable manifest. Outputs are the **unsigned** `artifacts/msix/<version>/WinMon-<version>-x64.msix` and `SHA256SUMS.txt`. Existing output directories are refused; use `-OutputDirectory` for another build. Payload/build evidence remains in `artifacts/msix-build/<run>`. `-DisableNuGetAudit` disables auditing for this publish only when the audit endpoint is unavailable.

## Update channels

`-p:StoreBuild=true` disables automatic checks, Settings update controls, network checks, downloads, helper handoff/execution, and startup handling of GitHub update state. Microsoft Store services this build. **Start WinMon when I sign in** uses the manifest's `windows.startupTask` extension (TaskId `WinMonStartup`, off by default) instead of the `Run` value, because a package's HKCU writes go to a private copy of the registry that Explorer never reads. The Store build targets `net10.0-windows10.0.17763.0` for the Windows Runtime `StartupTask` API, which adds about 23 MB to the package; see [Store sign-in startup](../../docs/design/STORE-STARTUP.md). Preferences retain the user's automatic-check choice for an ordinary installer/portable build.

Normal builds omit `StoreBuild` and retain the GitHub updater. Keep channel build outputs separate, as the packaging scripts do. An MSIX distributed outside the Store also disables the GitHub updater; use MSIX servicing (for example App Installer) for that channel.

## Validate

```powershell
dotnet test tests/WinMon.Tests/WinMon.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false --artifacts-path artifacts/validation/standard
dotnet test tests/WinMon.Tests/WinMon.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -p:StoreBuild=true --filter FullyQualifiedName~StoreBuildTests --artifacts-path artifacts/validation/store
```

Store tests verify rejection before network/file/process work. Existing updater tests deliberately exercise the enabled channel, so run the complete suite in the ordinary configuration. Add `-p:NuGetAudit=false` to an individual validation command only if needed for an audit endpoint outage.

For installation tests, sign the package with a development certificate matching `Publisher`, trust it on a disposable test machine, then use `Add-AppxPackage -Path <signed.msix>`. Do not distribute certificate private keys. Test Start menu launch, monitoring, tray, global shortcuts, startup registration, settings save/reload, upgrade and uninstall. With Developer Mode on, `Add-AppxPackage -Register <payload>\AppxManifest.xml` registers the unsigned staged payload from `artifacts/msix-build/<run>/payload` for quick checks; `Remove-AppxPackage` removes it. Read the startup task's state through `StartupTask.State` from inside the package (`Invoke-CommandInDesktopPackage`); its registry mirror is not kept current. Test coexistence/migration with the installer: both channels share the single-instance mutex and cannot run side by side. MakeAppx and unpackaged smoke validation do not prove packaged runtime behavior.

## Submit

### Automatic updates from GitHub releases

The `Submit Microsoft Store update` workflow (`.github/workflows/store-release.yml`) runs when a **stable GitHub release is published**, including publishing a previously created draft. Tags alone, draft creation, release edits and prereleases do not submit updates. The workflow builds the published tag with the SDK in `global.json`; that tag must contain the workflow's scripts and its name must exactly match `v<Version>` from `Directory.Build.props`. Merge these files onto the default branch before publishing a new release. Existing releases are not submitted retroactively.

Create a GitHub environment named **`store`** under **Settings → Environments**. For unattended submission, do not enable required reviewers on that environment; if you restrict deployment branches/tags, allow the release tags (`v*`). Configure these **environment variables** using **Partner Center → WinMon → Product management → Product identity**:

| GitHub variable | Partner Center value |
| --- | --- |
| `STORE_PACKAGE_NAME` | Package/Identity/Name, exactly as shown |
| `STORE_PUBLISHER` | Package/Identity/Publisher, including `CN=` and the full distinguished name |
| `STORE_PUBLISHER_DISPLAY_NAME` | Package/Properties/PublisherDisplayName |
| `STORE_DISPLAY_NAME` | The existing reserved product display name used in the package |

The workflow pins the existing Store product ID to **`9P6GPPWHZX15`**. The package identity name is a different value; do not use the product ID or package family name as `STORE_PACKAGE_NAME`.

Associate the Partner Center account with its Microsoft Entra tenant. Under **Account settings → User management/Users**, add an Entra application for release automation and assign the **Manager** role as required by Microsoft's submission API setup. Merely creating an Azure app registration does not grant Partner Center access. Copy its tenant/client IDs and create a client secret. Set these **secrets** in the GitHub `store` environment:

| GitHub secret | Value |
| --- | --- |
| `STORE_TENANT_ID` | Entra directory (tenant) ID, a GUID |
| `STORE_CLIENT_ID` | Automation application's application (client) ID, a GUID |
| `STORE_CLIENT_SECRET` | Client secret **value**, not its secret ID; rotate before expiry |

These are Partner Center API credentials. The Store job does not require Azure subscription IDs, Artifact Signing credentials, an MSIX signing certificate, or the GitHub updater's RSA key. It uploads an unsigned MSIX for Store signing.

For each stable release, increase the product version and matching executable manifest version above the existing Store x64 package version. Publish the GitHub release normally. The workflow runs standard tests, Store updater-isolation tests, offline submission tests and MakeAppx validation, and preserves the MSIX/checksums as an Actions artifact before contacting Partner Center.

`tools/Submit-StoreUpdate.ps1` uses Microsoft's **v1.0 Store submission API** directly. It verifies the package's tag/version and Store identity, refuses an existing pending submission or an equal/newer published x64 version, then clones the last published submission. It marks existing x64 packages for replacement, retains other supported architectures and the existing listing/screenshots/pricing/certification notes, uploads only the new MSIX in a ZIP, and commits with `targetPublishMode=Immediate` (publish after certification). Bundle-based or unknown architecture configurations stop for manual review.

The Actions summary records the submission ID. Success means the commit was accepted into processing (or a later stage); **it does not mean certification passed or the update is already live**. Monitor subsequent certification/publication in Partner Center. The workflow polls for acceptance for up to 20 minutes and serializes Store jobs. GitHub concurrency is not a release queue: avoid publishing multiple releases while an earlier Store submission is pending.

If upload/commit/status fails or times out, inspect the recorded submission before rerunning. The script never deletes an existing submission or retries mutations automatically. Resolve any pending draft/failed submission first; for an API-created draft, continue through the API or delete it deliberately before retrying. Do not edit an API-created draft in the Partner Center UI and then expect API updates to keep working. A rerun after the version is already published intentionally fails its version check.

Offline verification: `pwsh -NoProfile -File tools/Test-StoreSubmission.ps1`. These tests fake HTTP boundaries and do not contact Microsoft. Workflow checks do not install the MSIX or run Windows App Certification Kit; retain the packaged runtime/upgrade validation below before a release.

References: [API credentials and Manager role](https://learn.microsoft.com/windows/uwp/monetize/create-and-manage-submissions-using-windows-store-services), [submission creation, ZIP upload and commit](https://learn.microsoft.com/windows/uwp/monetize/manage-app-submissions), [app identity and pending submission](https://learn.microsoft.com/windows/uwp/monetize/get-an-app).

### Manual submission

Upload the generated `.msix` to Partner Center. This script does not generate `.msixupload` crash-symbol archives, sign, install, or submit packages. Microsoft signs the Store package after certification; direct MSIX distribution requires a trusted package signature separately from the executable signature.

Run Windows App Certification Kit, complete listing/screenshots/privacy policy/age ratings/pricing/markets, and explain `runFullTrust` for the desktop resource monitor when requested. The MSIX manifest requires Windows 10 version 1809 (build 17763) or later for Store MSIX distribution; the installer and portable channel are unchanged. Test OS compatibility before advertising it. Local validation does not guarantee certification. See [MSIX supported platforms](https://learn.microsoft.com/windows/msix/supported-platforms).

References: [desktop packaging](https://learn.microsoft.com/windows/msix/desktop/desktop-to-uwp-manual-conversion), [MakeAppx](https://learn.microsoft.com/windows/msix/package/create-app-package-with-makeappx-tool), [Store package requirements](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-package-requirements).
