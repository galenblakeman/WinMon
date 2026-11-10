# Code signing

WinMon's release workflow uses Azure Artifact Signing with the Public Trust profile `WinMonPublic` in `galenwinmonsigning` (East US), with publisher Galen Blakeman. Recorded validation for version 1.0.3 reports valid Authenticode signatures and Microsoft timestamps for both `WinMon.exe` and `WinMon-1.0.3-Setup-x64.exe`. Each release must complete Azure authentication, signing, signature verification, and packaging validation; workflow configuration alone is not signature evidence. Existing unsigned assets remain unchanged.

To inspect a downloaded executable or installer, open **Properties → Digital Signatures** and check the signer and signature details. PowerShell can also report the signature status:

```powershell
Get-AuthenticodeSignature -LiteralPath .\WinMon.exe | Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate
```

Use the downloaded installer's path instead when checking the installer. Check for a valid signature, publisher Galen Blakeman, and a timestamp.

## GitHub configuration

The `release` GitHub environment must exist and contain:

| Secret | Value |
| --- | --- |
| `AZURE_CLIENT_ID` | Application (client) ID of `WinMon-GitHub-Signing` |
| `AZURE_TENANT_ID` | Directory (tenant) ID of that application |
| `AZURE_SUBSCRIPTION_ID` | Subscription containing the signing account |
| `WINMON_UPDATE_SIGNING_KEY_BASE64` | Base64-encoded permanent RSA PKCS#8 update private-key PEM |

The application's federated credential must trust the repository's OIDC token for environment `release`. The release job declares that environment and requests `id-token: write`. Its Azure login action is pinned to an immutable commit. No Azure client secret or user password is needed. Assign the application the **Artifact Signing Certificate Profile Signer** role on the signing account or individual profile.

For this repository's current immutable-ID subject format, the configured subject is:

```text
repo:galenblakeman@4369241/WinMon@1426202531:environment:release
```

Issuer: `https://token.actions.githubusercontent.com`; audience: `api://AzureADTokenExchange`. Keep these aligned if the repository or OIDC settings change. Restrict the GitHub environment's allowed deployment branches/tags to refs approved for signing. The maintainer reviews changes and draft releases before publication; environment protection rules can add a separate approval gate if desired.

## Packaging and verification

GitHub installs Microsoft's `ArtifactSigning` PowerShell module at version **0.1.20**, supplies its required .NET 8 runtime alongside WinMon's pinned .NET 10 SDK, and always passes `-CodeSign` to `tools/New-Release.ps1`. Using the module inside packaging preserves this order:

1. Run Release tests, publish the service and standalone NativeAOT setup helper, sign and verify both, then create their RSA-signed fixed bundle. Publish the self-contained desktop and CLI applications with that bundle embedded in both.
2. Sign and timestamp `WinMon.exe` and `WinMon.Cli.exe`, then verify each executable's Authenticode status, publisher (`Galen Blakeman`), and timestamp presence.
3. Build the Inno Setup installer containing both signed executables, then sign, timestamp, and verify the installer.
4. Create the portable ZIP, WinGet manifests, RSA update manifest, and checksums from the final bytes.
5. Validate installer lifecycle/startup and WinGet manifests, then create a draft release for a version tag.

Signing uses SHA-256 and RFC 3161 timestamping at `http://timestamp.acs.microsoft.com`. Authentication uses only the Azure CLI session established by GitHub OIDC. Provider errors, invalid signatures, unexpected publishers, and absent timestamps fail packaging. The Windows signature check establishes trust on the build machine; it does not guarantee immediate SmartScreen acceptance on every customer's machine.

Desktop, CLI and installer signature evidence is written under `artifacts/packaging/<run>/` as `application-signature.json`, `cli-signature.json` and `installer-signature.json`, and uploaded with validation evidence. It contains final hashes and certificate details. Upstream dependencies are not individually re-signed as WinMon code. The generated Inno uninstaller is not separately signed by this integration.

The RSA update-manifest signature remains separate from Authenticode. Every workflow run requires `WINMON_UPDATE_SIGNING_KEY_BASE64` from the `release` environment; Azure signing does not replace it. WinMon 1.0.3 introduces the permanent embedded update public key. Earlier copies without that trust key need a manual update to establish it. See [Updating WinMon](UPDATING.md).

## Local use

Unsigned packaging remains available for local testing by omitting `-CodeSign`; it omits the privileged provisioning bundle. The unsigned ETW prototype, elevated collector test and native IPC tests exercise collection/transport without service installation. They do not validate the Settings enable/UAC/install path. That path requires the real signatures and existing RSA trust root; there is no unsigned setup switch.

To sign locally, install the pinned module, sign in with `az login` as an identity with profile signing access, and run:

```powershell
Install-Module ArtifactSigning -RequiredVersion 0.1.20 -Repository PSGallery -Scope CurrentUser
./tools/New-Release.ps1 -CodeSign -UpdateSigningKeyPath C:\secure\winmon-update-private.pem
```

Defaults are `https://eus.codesigning.azure.net/`, `galenwinmonsigning`, `WinMonPublic`, and expected publisher `Galen Blakeman`. Override with `-SigningEndpoint`, `-SigningAccountName`, `-CertificateProfileName`, and `-ExpectedPublisher`.

Run `./tools/Test-ReleaseSigning.ps1` to exercise packaging order, final hashes, updater RSA verification, and signing failure gates using simulated build/signing boundaries. It makes no Azure calls and does not prove live signing. Its private fixture key is written under ignored `artifacts/signing-validation/` and deleted once the signed fixture release completes; it must never be uploaded as a release asset.

## Service validation without a release

The manual-only `.github/workflows/network-service-validation.yml` runs service/setup boundary tests and builds a signed service bundle using the existing `release` environment credentials. It uploads Actions artifacts and test evidence, with repository contents permission limited to read. It creates no release, tag or installer and installs no service. Run it only from a reviewed ref allowed by the environment's deployment restrictions; do not relax those restrictions to make a test run pass.

The v3.5.1 [release run](https://github.com/galenblakeman/WinMon/actions/runs/42393608236) completed Azure OIDC login and signed packaging successfully with the current RSA trust root. This establishes that credentials worked in CI for that run, not that the new service bundle has been signed. Local Azure authorization and local NativeAOT toolchain failures do not establish that CI has the same failures; a completed service workflow and downloaded bundle verification are still required.

To register the new workflow before it is on the default branch, the isolated `codex/network-service-validation` branch temporarily added a bootstrap push trigger restricted to that branch and service-related paths. The initial cleanup was `ce5b48f`; the trigger was temporarily restored for diagnostics and the process-access correction, then removed in `f7afbf4`; a later diagnostics/revocation test cycle removed its temporary trigger in `44ac8d8`. Both the validation branch and production checkout are manual-only. [Validation run 42426234294](https://github.com/galenblakeman/WinMon/actions/runs/42426234294) succeeded from reviewed commit `7b1ad72`: NativeAOT publishing, service/helper signing and the signed manifest completed. The downloaded artifact passed the production verifier locally and both binaries have valid timestamped signatures from Galen Blakeman. Corrected signed revision `f455bc5`, run [42429999830](https://github.com/galenblakeman/WinMon/actions/runs/42429999830), subsequently passed approved native install/normal-user IPC/Settings reuse/repair/removal; remaining acceptance gaps are in the [feature checklist](design/PER-PROCESS-NETWORK-IMPLEMENTATION.md). No release, tag or service installation is part of this bootstrap.

The diagnostics/revocation candidate at `dbdd801` also completed signing in [run 42433461085](https://github.com/galenblakeman/WinMon/actions/runs/42433461085). Its downloaded artifact digest and production bundle/service-publisher checks passed locally; account-revocation and sustained native recovery acceptance remain separate gates.

## Responsibilities and privacy

[Galen Blakeman](https://github.com/galenblakeman) maintains the repository and Azure publisher identity. Use MFA for human GitHub and Azure access. Existing public release assets must not be replaced with newly signed bytes; publish a new version instead.

See Microsoft's [OIDC integration](https://github.com/Azure/artifact-signing-action/blob/main/docs/OIDC.md), [signing module](https://www.powershellgallery.com/packages/ArtifactSigning/0.1.20), and [role documentation](https://learn.microsoft.com/en-us/azure/artifact-signing/tutorial-assign-roles). See the [privacy policy](PRIVACY.md) for local data handling.
