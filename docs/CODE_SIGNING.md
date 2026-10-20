# Code signing policy

WinMon's current releases are unsigned. This policy prepares the project for a SignPath Foundation application; it does not indicate acceptance or an active signing service.

## Responsibilities

[Galen Blakeman](https://github.com/galenblakeman) is the sole maintainer, committer, reviewer, and intended signing approver. Contributions from other people must be reviewed by the maintainer before merging. Each signing request must receive manual approval from the maintainer.

All contributors with repository write access or signing roles must use MFA for GitHub and, when provisioned, SignPath. GitHub MFA was confirmed by the maintainer on October 20, 2026. SignPath MFA must be enabled when the account is created.

## Signing scope

Signing will cover WinMon release artifacts built from this repository through the approved CI integration. Product metadata must identify WinMon and use the release's product version consistently. Upstream libraries must not be individually signed as WinMon code.

WinMon currently ships as a self-contained .NET executable, an Inno Setup installer, and a portable ZIP. The treatment of the bundled runtime and dependencies must be agreed with SignPath during onboarding. Signed output must be verified before publication; installer hashes, portable archives, and WinGet manifests must be generated from the final signed files. Existing public release assets must not be replaced with newly signed bytes.

The maintainer must review the source revision, build results, artifact contents, and requested signing scope before approving each request. Changes to build scripts and workflow configuration require the same review as application code.

## Provider credit after activation

Once the service is approved and active, the signing credit will be:

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

SignPath Foundation would be the certificate publisher. This credit is conditional until onboarding is complete. Every download/release page must link to this Code signing policy and accurately describe whether its assets are signed.

## Privacy

See the [privacy policy](PRIVACY.md) for local data, clipboard use, diagnostic output, and user-requested browser navigation.
