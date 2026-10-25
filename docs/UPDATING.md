# Updating WinMon

WinMon checks the public GitHub Releases feed after startup and periodically while it runs when automatic update checks are enabled. A release becomes eligible only when it contains `update-manifest.json` and `update-manifest.sig`. WinMon verifies the RSA SHA-256 PKCS#1 v1.5 signature with the public key compiled into the app, then verifies the selected download's file name, size and SHA-256 against that signed manifest. Drafts, prereleases, malformed releases and unsigned manifests are ignored.

Selecting **Update Now** first checks GitHub again, so it installs the newest release rather than the one the footer first offered; an unchanged release costs one `304 Not Modified` response, and if that check fails the offer already verified is used. It then downloads to `%LOCALAPPDATA%\WinMon\Updates`, preserves settings, exits WinMon normally, and starts a small updater mode. A portable install replaces only `WinMon.exe`; `portable.flag`, `settings.json`, the license and other files are preserved. An installed copy runs the matching per-user Inno Setup installer so Windows uninstall metadata and shortcuts remain valid. The update helper retains the previous portable executable until the replacement has launched successfully, and records enough state to recover on the next launch after interruption.

## Publishing an eligible release

The update signing key is distinct from Authenticode code signing. Keep the RSA private key outside the repository. Provision the matching public key once, review and commit it in the application before issuing an update-enabled release.

### One-time key setup

Use PowerShell 7.4 or later. Generate a permanent RSA key pair in a private folder outside the checkout; run this once and retain a secure backup of the private key:

```powershell
$signingDirectory = Join-Path $env:USERPROFILE '.winmon-signing'
New-Item -ItemType Directory -Path $signingDirectory -ErrorAction Stop | Out-Null
$signingKey = [Security.Cryptography.RSA]::Create(3072)
try {
    $privatePem = $signingKey.ExportPkcs8PrivateKeyPem()
    [IO.File]::WriteAllText((Join-Path $signingDirectory 'winmon.update-private.pem'), $privatePem)
    [IO.File]::WriteAllText((Join-Path $signingDirectory 'winmon.update-public.pem'), $signingKey.ExportSubjectPublicKeyInfoPem())
    [IO.File]::WriteAllText((Join-Path $signingDirectory 'github-secret.txt'), [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($privatePem)))
}
finally { $signingKey.Dispose() }
```

The script refuses an existing folder to avoid overwriting a signing identity. `github-secret.txt` contains private key material too. In **GitHub Settings → Environments → release → Environment secrets**, create **WINMON_UPDATE_SIGNING_KEY_BASE64** using that file's entire content. Keep both private files out of Git and release uploads. Then provision the public key from the repository root:

```powershell
./tools/Set-UpdatePublicKey.ps1 -PublicKeyPath (Join-Path $env:USERPROFILE '.winmon-signing/winmon.update-public.pem')
```

Review and commit **only** `src/WinMon/Updates/update-public-key.pem` with the updater code. This initial updater-enabled build must be distributed manually to copies that do not yet have the updater or its trust key. Subsequent signed releases can be offered through the footer. Authenticode signing is configured separately through [Azure Artifact Signing](CODE_SIGNING.md).

### Building a release

```powershell
./tools/New-Release.ps1 -UpdateSigningKeyPath (Join-Path $env:USERPROFILE '.winmon-signing/winmon.update-private.pem')
```

`Set-UpdatePublicKey.ps1` accepts an RSA PEM public key of at least 2048 bits and writes the canonical SubjectPublicKeyInfo PEM file at `src/WinMon/Updates/update-public-key.pem`. That file is intentionally committed: it is public trust material compiled into the app, never a secret. A release can only be verified by apps already carrying its public key. Key rotation therefore needs a bridge release signed by the old key that carries the new key and understands both keys; do not replace the key in an update-enabled release without that bridge.

`New-Release.ps1 -UpdateSigningKeyPath C:\secure\winmon-update-private.pem` first requires that the private RSA key (at least 2048 bits) matches the committed `update-public-key.pem`; it then emits `update-manifest.json` and its base64 detached `update-manifest.sig`. The manifest contains only the exact versioned x64 portable ZIP and installer names, each no larger than 512 MiB. Without that parameter the package is intentionally marked in the build log as ineligible for in-app updating; this is suitable for local testing only.

Every GitHub Actions release-packaging run, including manual validation runs, requires the `release` environment secret `WINMON_UPDATE_SIGNING_KEY_BASE64` containing the base64-encoded PKCS#8 RSA private-key PEM. The workflow writes it to the runner's temporary directory only, passes it to the packaging script, and fails if the secret is absent. Do not put the private key in workflow files, release assets or secrets with a broader scope than the release workflow.

WinMon 1.0.3 is the first release configured with the permanent update trust key. Install it manually on older copies that do not carry that public key. Back up the corresponding private key securely: losing it prevents issuing updates trusted by those installations. Never replace it without the bridge-release rotation described above.

## Recovery and verification

The helper waits for the original process to exit and blocks competing normal launches while applying an update. Preferences are saved before shutdown is committed. The restarted app must produce a valid monitoring sample within 60 seconds. A failed portable startup restores the backup and relaunches the previous executable. Installed updates retain the installer's error log and restart the executable left by Setup; they do not promise automatic installer rollback. Windows itself is never restarted by the updater.

Transactions and error logs remain under `%LOCALAPPDATA%\WinMon\Updates`. After an interrupted portable replacement, launching a healthy replacement completes its pending journal; if the helper is still running, it owns readiness and rollback. Large successful downloads/backups are cleaned up after success or on a later launch. A power failure that leaves an executable unable to start may require manually restoring the `.WinMon-<token>.backup.exe` from the application directory or reinstalling. Recovery cannot run when no executable can start.

Run the isolated end-to-end harness from PowerShell 7.4+ on Windows:

```powershell
./tools/Test-Updater.ps1 -IncludeRollback -IncludeInstaller
```

It refuses existing WinMon processes/installations, publishes fixtures with temporary keys, checks cancellation and signature tampering, performs a real portable replacement/restart, verifies failed-startup rollback, and upgrades/uninstalls an isolated per-user installer fixture. It preserves JSON evidence under `artifacts/updater-validation` and removes its temporary private key. It exercises local assets and helper processes; it does not publish or validate a live GitHub release.
