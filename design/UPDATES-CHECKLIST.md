# In-app updates

Approved flow: detect a newer GitHub release, offer an Update Now footer, download and verify while monitoring continues, exit normally, replace through a separate helper, and restart the same executable path.

## Requirements and evidence

| Requirement | Status | Evidence / remaining validation |
| --- | --- | --- |
| Preserve existing behavior and unrelated work | Verified | Working tree clean before implementation; baseline 369 NUnit tests passed outside sandbox. |
| Stable GitHub release discovery, numeric versions, bounded metadata, caching | Verified | Fake HTTP responses exercise the GitHub contract, stable eligibility and ETag caching; live public release remains a publishing check. |
| Signed manifest and size/hash verification before execution | Verified | Real temporary RSA keys, signature/hash rejection tests and process-level tamper rejection. |
| Footer, notes, download progress, cancel, session dismissal | Verified | Release build and native dark/light footer + progress renders inspected. Diagnostic fixtures do not contact GitHub or click a live update. |
| Settings manual check and automatic check preference | Verified | Preference round trips, native Settings render and existing language/save/cancel diagnostics passed; all nine translated languages have matching keys. |
| Normal preference-preserving exit and acknowledged helper handoff | Verified | Real parent/helper Ready/Commit/Cancel protocol and preserved portable preferences in the integration harness. |
| Portable replacement, backup, restart readiness, rollback | Verified | Real 1.0.2 to 1.0.3 replacement/restart; failed-startup fixture restored original hash/version and relaunched. |
| Installed copy uses matching per-user installer and installation path | Verified | Actual Inno 6.7.3 older-to-newer install: executable and HKCU DisplayVersion verified; fixture uninstalled. |
| Interrupted update journal and next-launch completion | Verified | Concurrent journal-write regression, pending-state finalization, version mismatch and active-helper exclusion tests. Physical power loss was not simulated. |
| Release signing tooling and CI configuration | Verified | Scripts parse; temporary-key signed manifests verified. GitHub workflow execution requires maintainer configuration. |
| Permanent release signing identity and live publication | Blocked | User explicitly chose to configure signing. Follow docs/UPDATING.md for the permanent public key and matching GitHub Actions secret. No production key, commit, tag, push or release was created. |
| Privacy and publishing documentation | Verified | Reviewed docs/PRIVACY.md, docs/UPDATING.md, README.md and packaging/RELEASING.md. |

## Validation notes

The initial baseline restore failed because the NuGet vulnerability feed was inaccessible. Local validation uses `-p:NuGetAudit=false`; this does not change repository audit configuration. The sandbox testhost then timed out; the same baseline binaries passed outside the sandbox. First affected Release build: zero warnings/errors. First updater-focused run: 21 tests passed.

No commit, tag, push, or public release is part of this implementation.

Final full NUnit run: **400 passed**, including 31 updater cases. Native feature smoke passed, including live language changes and unclipped Settings save buttons. A resource-write newline regression and outdated diagnostic Settings casts were corrected before these final runs. Test keys were temporary and removed.

Final process harness evidence: `artifacts/updater-validation/6d18707d1dab40eab5a2db62391093ae/evidence.json` (cancel, tampered signature, rollback, portable success, installer success). UI renders: `artifacts/update-smoke/screenshots-final/`. Installer rollback is not promised; installer failures retain logs and restart the executable left by Setup. Production signing and a live GitHub release were not validated.

An unrelated concurrent edit to docs/DEVELOPMENT.md was left intact.
