# On-demand CLI and MCP updates

Status: implemented and verified, except the actual signed upgrade acceptance below. This extends the separate-installer design's initial manual-only update decision at the user's request.

Both executables accept `update check` and `update install`. Commands run only when requested; neither tool polls in the background. MCP processes these arguments before starting its stdio host, so update output never enters the MCP transport and no mutation tool is exposed to agents.

One shared source implementation is linked into CLI and MCP, following the existing linked-source pattern. It reuses the desktop manifest verifier with an explicit allowlisted product identity. Each signed manifest binds exactly that product's installer and portable ZIP, version, Windows x64 runtime, byte sizes and SHA-256 values. Official releases emit `desktop-update-manifest.*`, `cli-update-manifest.*` and `mcp-update-manifest.*` under the existing RSA trust root. There is no new trust key or dependency.

`update check` reports whether a newer stable GitHub release exists without installing anything. `update install` verifies signed metadata, downloads the appropriate product payload with bounded HTTPS reads and checks the complete file before use. Registered installed copies launch their own installer interactively, then the requesting tool exits; the installer handles files in use and preserves product settings and registrations. No agent client, other tool or shared network service is terminated or updated automatically.

The initial scope is installed-copy updates. A portable copy downloads its signed, verified product ZIP and reports its path with instructions to close sessions and extract it over the portable copy. It is never silently converted to an installed copy or overwritten while running. In-place portable replacement is a separate extension if requested.

Store builds reject GitHub update operations. Missing trust keys, unsigned/incomplete releases, invalid signatures, wrong product/runtime/version, oversized or corrupt payloads, unexpected redirect origins and cancellation must fail before launching an installer. Desktop automatic update behavior and legacy journal recovery remain unchanged.

## Requirements and validation

| Requirement | Status | Verification |
| --- | --- | --- |
| CLI/MCP terminal command integration; MCP transport remains read-only | Verified | CLI command-routing tests, 7 MCP routing tests, stdio discovery regression and published help |
| Shared bounded downloader, product identity and signed manifest checks | Verified | Signature/product mismatch, corrupted payload, chunked overrun, stall deadline and cancellation regressions |
| Independent installer launch and portable ZIP guidance | Verified | Injected launch success/failure tests and actual verified portable download fixture |
| Product-specific release metadata and translations | Verified | Signing-boundary script verifies all three manifest signatures/hashes; resource tests pass with all ten cultures |
| Desktop and Store verification | Verified | Both complete Verify.ps1 variants passed on the live checkout; 232 smoke renders reviewed |
| Actual signed upgrade to a newer release | Blocked | Requires a published newer signed tool release and disposable acceptance profile |

## Landed and departures

Implemented linked `ToolUpdateCommand`, product-aware manifest verification, CLI/MCP routing, key/resource embedding, Store rejection and per-product signed release metadata. Installed updates start the ordinary installer and return; a successful command means setup started, not that the interactive upgrade finished. Portable installs intentionally remain a verified-download/manual-extraction flow, with no conversion to an installed product.

Verification on the current checkout (SDK 10.0.404):

- `artifacts/tool-updates-desktop.log`: `Verify.ps1 -Smoke full`, 11 steps passed, zero build warnings/errors. All test projects passed with normal explicit/elevation skips; the 232 renders were reviewed on six contact sheets.
- `artifacts/tool-updates-store.log`: `Verify.ps1 -Store`, 10 steps passed, including Store update-policy rejection before HTTP. Positive GitHub-update fixtures are intentionally skipped in this variant.
- `artifacts/tool-updates-focused.log`: final CLI routing/updater tests, 13 passed and one Store-only assertion skipped. Includes a correctly signed wrong-product manifest served through the requested product's endpoint.
- `artifacts/tool-updates-signing.log`: fake signing/build boundary tests verify desktop/CLI/MCP metadata names, selected assets, hashes and real RSA signatures without Azure access.
- `artifacts/tool-updates-publish.log`, `tool-updates-cli-help.log` and `tool-updates-mcp-help.log`: both final single-file executables published and showed the expected command help.
- `artifacts/tool-updates-cli-live-check.log` and `tool-updates-mcp-live-check.log`: both published executables checked the public stable feed, reported 4.1.0 up to date, and exited 0. No installer was launched or client configuration changed.

The actual new-version signed upgrade remains blocked on a newer signed release and disposable acceptance environment. Neither tool performs background checks, exposes MCP mutation tools, forces sessions closed, or self-replaces portable executables. No sampling/hot-path changes were made, so no sampling benchmark result is claimed.
