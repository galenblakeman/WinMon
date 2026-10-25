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

Upload the generated `.msix` to Partner Center. This script does not generate `.msixupload` crash-symbol archives, sign, install, or submit packages. Microsoft signs the Store package after certification; direct MSIX distribution requires a trusted package signature separately from the executable signature.

Run Windows App Certification Kit, complete listing/screenshots/privacy policy/age ratings/pricing/markets, and explain `runFullTrust` for the desktop resource monitor when requested. The MSIX manifest requires Windows 10 version 1809 (build 17763) or later for Store MSIX distribution; the installer and portable channel are unchanged. Test OS compatibility before advertising it. Local validation does not guarantee certification. See [MSIX supported platforms](https://learn.microsoft.com/windows/msix/supported-platforms).

References: [desktop packaging](https://learn.microsoft.com/windows/msix/desktop/desktop-to-uwp-manual-conversion), [MakeAppx](https://learn.microsoft.com/windows/msix/package/create-app-package-with-makeappx-tool), [Store package requirements](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-package-requirements).
