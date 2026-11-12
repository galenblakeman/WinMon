# Separate desktop, CLI and MCP installers

Status: implemented; release acceptance remains open in the [implementation and acceptance checklist](SEPARATE-INSTALLERS-IMPLEMENTATION.md). Decisions recorded November 11, 2026.

## Decision and scope

Ship three independent per-user installers: WinMon desktop, WinMon CLI and WinMon MCP. The primary goal is smaller desktop downloads and installations. CLI and MCP must install, run, update and uninstall without desktop WinMon. Keep a single product version and release pipeline initially; separate version schedules are outside this change.

Desktop retains its signed in-app updater. CLI and MCP updates are user initiated. The subsequent [on-demand update design](TOOL-UPDATES.md) adds explicit `update check` and `update install` commands; neither tool checks or installs updates in the background.

Keep product download links on the download page, with no cross-product download offers in the installers. MCP setup has two additional, unchecked options to register the installed server with Codex and Claude Code. These options configure already-installed clients; they do not download clients or install the Claude plugin.

This design supersedes the bundling decisions in [CLI distribution](CLI-DISTRIBUTION.md). [MCP setup](../MCP.md) and the [migration guide](../INSTALLER-MIGRATION.md) describe the split distribution.

## Implementation baseline and migration constraints

The inspected source uses product version 4.1.0, .NET 10 projects, SDK 10.0.402 with latest-patch roll-forward, and Inno Setup 6.7.3.

- `packaging/WinMon.iss` installs all three executables in one directory under one desktop AppId. Its optional CLI PATH task records ownership in `HKCU\Software\WinMon\CliPathDirectory`.
- `tools/New-Release.ps1` publishes and signs each executable, bundles all three in the desktop portable ZIP, emits a CLI-only ZIP, and builds one installer. There is no MCP-only executable ZIP.
- `UpdateClient` requires companion archive entries when those files already exist. `UpdateRuntime` validates both companions after an installed update and handles portable companion replacement, rollback and recovery.
- `UpdateManifest` accepts exact desktop asset names and schema version 1. `UpdateClient` discovers the latest GitHub release and retrieves `update-manifest.json` and its signature from that version.
- CLI settings already live at `%LOCALAPPDATA%\WinMon\cli-settings.json`, independently of its executable directory. MCP registrations normally contain an absolute executable path; the Claude plugin instead uses `WINMON_MCP_EXE`.

Consequently, an older automatic updater cannot be assumed to accept desktop-only payloads. Publishing one intermediate release does not force users who skip releases through that version. Moving files also does not migrate client registrations or old installer ownership.

## Products and ownership

| Product | Default directory under `%LOCALAPPDATA%\Programs` | Owned executable | Update mechanism |
| --- | --- | --- | --- |
| WinMon | `WinMon` | `WinMon.exe` | Existing signed desktop updater and manual installer |
| WinMon CLI | `WinMon CLI` | `WinMon.Cli.exe` | Manual installer or portable replacement |
| WinMon MCP | `WinMon MCP` | `WinMon.Mcp.exe` | Manual installer or portable replacement |

Retain desktop's existing AppId and directory for in-place upgrades. Allocate fixed, distinct AppIds for CLI and MCP, each with its own Apps & Features entry, repair and uninstaller. Allocate the actual GUIDs during implementation and keep them stable thereafter.

Each installer is per-user and self-contained for Windows x64, matching the current distribution. Installing a product does not require the other executables or a separate .NET installation. Preserve existing supported platform declarations pending native acceptance; this change does not expand platform support.

Reject a target directory shared with another product or an existing bundled desktop installation, including equivalent/reparse-resolved paths. Validate the registered product destination on upgrades too. Never place standalone files under the desktop installer's existing ownership. Do not silently downgrade an installed product.

Desktop owns its shortcuts, startup integration, settings and updater. CLI owns its unchecked PATH task and a distinct product-specific PATH ownership record; preserve the command name `WinMon.Cli`. MCP needs neither PATH nor startup registration: clients launch it over stdio using the absolute installed path. Do not launch MCP as an interactive app after installation.

Preserve user settings, captures and exports on uninstall. Keep CLI settings at their existing location. MCP installer ownership records are separate from client configuration, record only integration metadata, and contain no client credentials. Removing one product cannot remove another product's files or integrations.

## Release assets and download page

Build and sign each executable once per release and reuse those bytes in its installer and ZIP. Proposed executable-distribution assets are:

| Product | Installer | Portable archive |
| --- | --- | --- |
| Desktop | `WinMon-<version>-Setup-x64.exe` | `WinMon-<version>-win-x64.zip` |
| CLI | `WinMon-Cli-<version>-Setup-x64.exe` | Existing `WinMon-Cli-<version>-win-x64.zip` |
| MCP | `WinMon-Mcp-<version>-Setup-x64.exe` | New `WinMon-Mcp-<version>-win-x64.zip` |

Each archive contains only its product executable, license and relevant instructions; desktop also retains `portable.flag`. Remove companion executables from desktop installer, portable ZIP and desktop update staging. Existing Claude plugin ZIP remains separate and points to the independently installed MCP executable.

The download page presents Desktop, CLI and MCP separately, with installer and portable choices, manual-update instructions for the tools, and a prominent first-split-release migration notice. Use the repository README release/download section as the initial owning surface unless an external download page is identified during implementation. No website project is required for this change.

Sign all official executables and installers through the existing policy; include all assets in checksums. Retain desktop's WinGet identity and update its payload expectations. Separate CLI/MCP WinGet listings and automatic submissions are deferred; do not advertise package-manager installation until those listings exist. Store remains desktop-only.

## Desktop update compatibility

Choose an explicit manual cutover rather than continuing large compatibility bundles:

1. Starting with the first split release, publish desktop update metadata as `desktop-update-manifest.json` and `desktop-update-manifest.sig`. New desktop builds use those names with the existing signature, hash, size, version and origin checks. Schema version 1 and desktop asset names can remain unchanged because their meaning is now desktop-only.
2. Do not publish the old `update-manifest.json` / `.sig` names on split releases. An old updater must reject the release before downloading or executing a payload. Verify this against representative historical helpers. Older published assets remain immutable.
3. Users on bundled builds manually download the new desktop installer, or replace the desktop portable executable from the new ZIP. Release notes and the download page explain that this first upgrade is manual. Existing old builds may display their generic unconfigured-update error; do not promise that they show new migration text.
4. After cutover, desktop's in-app updates replace and validate desktop alone. CLI/MCP files left in an old desktop folder are untouched. A running independent CLI or MCP must not block desktop updates.

This deliberately sacrifices automatic upgrades from old builds to obtain smaller downloads without perpetually shipping bundled compatibility assets. It is a proposed implementation tradeoff, not a claim of existing compatibility. If uninterrupted automatic cutover becomes mandatory, revisit this decision before implementation rather than pointing legacy metadata at incompatible payloads.

Remove companion handling from new transactions, but retain narrowly scoped recovery for existing journals with `CliExisted` / `McpExisted` and their backups. Resolve old in-progress transactions before starting a new desktop-only one; do not sweep backups or treat an interrupted bundle update as committed merely because desktop starts. Preserve transaction authentication, readiness, rollback and staging-path validation.

## Migration of installed bundled copies

Migration is explicit and nondestructive. Desktop setup neither installs the tools nor edits agent clients on the user's behalf.

1. Install desired standalone CLI and/or MCP products into their own directories, while the old desktop installation is still present. Do not copy old executable bytes into the new installation.
2. Verify CLI snapshot/watch and MCP protocol startup using the new paths. Preserve `%LOCALAPPDATA%\WinMon\cli-settings.json` unchanged.
3. For CLI, optionally enable the new PATH task. Remove an old desktop PATH entry only when the old ownership record matches that exact registered desktop directory and the user explicitly selects legacy PATH cleanup. Leave user-managed entries alone. Store the new CLI's PATH ownership separately so desktop uninstall cannot erase it. Until cleanup, an older PATH entry can resolve the old CLI first; migration instructions include checking `Get-Command WinMon.Cli` in a fresh terminal and updating scripts with hardcoded paths.
4. For MCP, update each direct client registration or `WINMON_MCP_EXE` to the new path, restart affected agent sessions, and confirm discovery and a snapshot. MCP installer registration options can add an absent entry; an existing legacy entry requires explicit replacement as described below. Do not attempt to infer every project/client configuration.
5. Upgrade desktop manually. Keep bundled CLI/MCP files in the desktop folder during this upgrade; the new desktop no longer updates them. Warn in migration documentation that those copies are legacy and receive no further updates.
6. Once replacement paths are verified, users can remove the old bundled copies through the old desktop ownership lifecycle or documented manual cleanup of the two known files after closing them. Do not recursively delete a desktop directory or automatically remove binaries based only on their names. Desktop uninstall may still remove legacy bundled files recorded by the old installer; it must never reach standalone product directories.

Steps 1–4 may follow the desktop upgrade because the upgrade preserves old tools, but must precede uninstalling the legacy desktop if their old paths are still in use. Do not run the old desktop uninstaller as a prerequisite for an in-place desktop upgrade. Test actual Inno uninstall-log behavior across this transition rather than assuming omitted `[Files]` entries are removed or disowned.

Silent desktop upgrades also preserve legacy tools and record the migration condition in the installer log. They do not silently change PATH, client registrations, service state or tool installations.

## Migration of portable copies

Extract each new archive into a separate writable product directory. Preserve desktop `settings.json`, `portable.flag` and existing user data; preserve CLI's LocalAppData settings. Update scripts, PATH and agent registrations explicitly. Never extract a standalone tool into a desktop-installed directory.

A manual desktop-only executable replacement may leave old companion files beside it. New desktop updates ignore them; remove them only after verifying the replacement tools and registrations. Keep the old directory available until migration succeeds. Portable MCP still supports manual registration; installer tasks do not manage arbitrary portable copies.

## MCP installer registration options

Show independent unchecked tasks:

- **Register WinMon MCP with Codex for this user**
- **Register WinMon MCP with Claude Code for this user**

Explain that registration lets the selected client launch this read-only server and receive local diagnostic data. Preserve current privacy defaults: no command lines or executable paths in tool results, and no network endpoints without explicit request. Registration must not change client approvals, trust policy or endpoint-disclosure defaults.

Use the installing user's existing client CLI and supported commands rather than editing TOML/JSON with string replacement. Run as the original unelevated user, after committing and verifying MCP files, with explicit absolute paths and safely passed arguments. Resolve installed command launchers in that user's context, including Windows npm shims; do not assume only a native `.exe` exists or search the current working directory. Do not modify custom client homes or project configuration implicitly. Detect missing/unsupported clients and offer manual instructions rather than installing them.

The command baseline inspected for this proposal is Codex CLI 0.166.0 and Claude Code 2.1.318:

```powershell
codex mcp add winmon -- "<absolute installed WinMon.Mcp.exe path>"
claude mcp add --scope user --transport stdio winmon -- "<absolute installed WinMon.Mcp.exe path>"
```

Codex's [official MCP command documentation](https://learn.chatgpt.com/docs/developer-commands#codex-mcp) describes entries in the user's Codex configuration. Claude's [MCP documentation](https://code.claude.com/docs/en/mcp) describes stdio registration and scopes; explicit `--scope user` avoids its default local scope. Recheck installed versions and capabilities at implementation time; supported client discovery and registration inspection are acceptance requirements, not universal compatibility guarantees.

### Existing registrations, failure and repair

Before each selected registration, inspect the existing `winmon` entry in the intended user configuration. Do not interpret any inspection failure as absence.

| Existing state | Required behavior |
| --- | --- |
| Absent | Add and verify the entry; record installer ownership only after success. |
| Already targets this installed executable with equivalent arguments | Leave unchanged; retain previously recorded ownership, but do not claim a manually created entry. |
| Targets a legacy bundled path or another server | Display old/new targets and require separate explicit consent to replace. In silent mode preserve it and report a conflict. |
| Cannot inspect reliably, or policy disallows changes | Skip registration, report the reason and provide manual recovery instructions. |

Preserve existing registrations' extra settings. If replacement cannot preserve them through supported client commands, leave the entry unchanged and require manual migration. Capture only the affected entry for rollback; never restore a whole client configuration over unrelated concurrent edits. If replacement removes the old entry and adding the new one fails, restore the old entry when safe and report any recovery failure explicitly. Serialize installer-owned operations and recheck conflicts before mutation; unsupported client concurrency guarantees require conservative failure handling.

Use bounded execution with separate results per client. A timeout, missing client, conflict, policy denial or failed registration leaves MCP installed and the other client's successful registration intact. Show/log a partial-success outcome and exact manual next steps. Do not report a registration as successful solely from process exit: read it back and validate its target and scope. Protocol acceptance is tested separately; installation does not start an AI conversation or send diagnostic readings to a provider.

Task selections are not automatically replayed on upgrades. Preserve successful installer ownership records and registrations while replacing the binary at its stable path. Repair can explicitly retry a selected failed integration. Unchecking a registration task is not a request to unregister an existing entry. Existing sessions may retain the old server process; instruct users to restart them. If a running server prevents replacement, require it to be closed and allow retry; do not terminate agent clients automatically.

The Claude plugin remains an alternative registration route, not an additional server to enable by default. Explain duplicate-server risk when users already use it. The installer does not install the plugin, rewrite plugin configuration or set `WINMON_MCP_EXE` automatically; plugin users update that path explicitly.

### Uninstall and ownership

Track registrations created or explicitly replaced by this installer per client, user/configuration identity, entry name and exact effective server definition. After an explicit replacement, the new definition is installer-owned; retain only minimal rollback metadata during the operation, not client secrets in uninstall records.

On uninstall, remove an installer-owned entry only if it still matches the recorded definition and points to this installation. Preserve entries the user changed or created independently. Use the client's scoped remove command after inspection; never remove an arbitrary same-name entry. Do not resurrect a stale legacy registration on uninstall.

If the CLI is missing, inspection is ambiguous, or removal fails, preserve client configuration, continue file uninstall, and report the remaining entry with manual cleanup instructions. Document that a preserved entry may then point to a removed executable. Do not delete or rewrite entire client configuration files. Test actual user-scope inspection/removal support before promising automatic cleanup for a client version.

## Shared network service

Desktop and CLI continue using the existing signed, separately managed service and embedded setup bundle. CLI retains its explicit `network install` flow; no desktop executable may be required. Preserve desktop's unchecked network-service setup task. The new CLI installer initially installs only CLI; service provisioning remains its existing explicit command.

MCP retains its current read-only collection behavior and does not gain service installation or mutation tools. No product uninstall removes the shared service, revokes a user's authorization, or resets another product's preferences. Service removal remains explicit through its own lifecycle.

Manual tool updates permit mixed product versions even with coordinated releases. Validate actual service protocol compatibility, return the existing actionable incompatibility state, and prevent an older embedded bundle from downgrading a newer service. Do not claim arbitrary cross-version compatibility. Native signed service acceptance must cover desktop-free CLI provisioning and mixed-version clients.

## Implementation boundaries

| Area | Required change |
| --- | --- |
| `packaging/WinMon.iss` | Desktop-only payload; retain AppId; preserve legacy tool files and scoped legacy cleanup behavior. |
| New CLI/MCP installer definitions | Separate identities/directories, owned PATH or registration tasks, repair/uninstall, file-in-use handling. Reuse existing signing/build patterns without creating a generic installer framework. |
| `tools/New-Release.ps1`, release workflow | Three installers, three executable ZIPs, signing/checksums, separate desktop manifest names and payload checks. |
| Update client/runtime/manifest generation and tests | Desktop-only future transactions; legacy journal recovery; manual cutover and rejection of incompatible legacy metadata. |
| Installer/package acceptance scripts and fixtures | Per-product lifecycle checks, coexistence, migration, isolated client registration, real historical updater compatibility. Reuse `Test-McpStdio.ps1` and `Test-McpPackage.ps1`. |
| README download section, user guide, MCP setup, releasing/updating/development docs | Three downloads, manual tool updates, optional registration and migration instructions; revise old bundling claims when code lands. |
| String resources | New visible installer text and recovery instructions use existing resource-generation conventions and all nine translations. |

No monitoring-layer refactor, new runtime dependency, background migration service, automatic client installation, independent version schedules or extra package-manager channel is needed.

## Acceptance and rollout

Before implementation, record signed installer, compressed ZIP and installed payload sizes for the bundled release. Compare equivalent signed outputs after the split; report desktop reductions and per-product sizes without inventing a target percentage. This is a packaging measurement, not a sampling performance change.

Required acceptance before publication:

1. Fresh install, repair, newer-version manual upgrade and uninstall for each product in a clean profile, including paths containing spaces and silent execution. CLI/MCP succeed without desktop or a separately installed .NET runtime on the acceptance machine.
2. All installation orders coexist; uninstall any product without changing remaining products, shared service, user data or client-owned entries. Reject colliding destinations and downgrades before modifying files.
3. CLI snapshot/watch and optional signed service provisioning succeed without desktop. MCP stdio initialization, discovery, snapshot and graceful shutdown succeed independently.
4. Both registration tasks default off, including silent installs. Test each alone and together, missing clients, npm launchers, existing identical/conflicting entries, explicit migration consent, denied writes, timeouts, repair, mixed results and uninstall ownership. Use isolated client homes/configurations; never exercise mutation tests against developer settings. Confirm real user-scoped visibility from a fresh client session; account for Claude project overrides and managed client policy.
5. From representative real bundled installers, install standalone tools, migrate PATH/direct/plugin references, upgrade desktop, then uninstall desktop. Verify new paths still work and legacy uninstall ownership cannot delete standalone files. Repeat with desktop upgraded first and with migration deferred.
6. Old updaters discover a split release and reject missing legacy metadata before replacement; manual cutover works. New updaters consume the new signed metadata, update desktop alone, and preserve the signature/hash/version/origin checks. Tampered payloads remain rejected.
7. Old prepared/in-progress update journals recover correctly, including companion backups and locks. New desktop-only updates do not touch legacy companion files or fail because independent tools are running. Verify both installed and portable paths.
8. Run `tools/Verify.ps1` and `tools/Verify.ps1 -Store` after implementation, plus affected smoke modes and visual review for any changed UI. Run installer lifecycle acceptance in disposable interactive Windows environments and native Services acceptance outside the restricted sandbox. Record client, compiler and Windows versions with results.

Publish all three products together only after these gates pass. Explain the one-time manual desktop cutover prominently in release notes. Do not replace existing public release assets, rotate update trust keys, or publish new WinGet identities as part of migration. If a release fails acceptance, fix it before publication; after publication, ship corrections under a new version.

## Landed and departures

Implemented three independent per-user installers and executable ZIPs, separate identities/directories, optional owned CLI PATH and legacy cleanup, and unchecked Codex/Claude Code registration tasks. Desktop-only updates publish/discover the new signed metadata names; legacy companion journal recovery remains. Existing bundled files are preserved during registered desktop upgrades. Download and migration documentation describes the one-time manual cutover and manual tool updates.

MCP registration uses installed native executables or PowerShell npm shims, exact definition inspection, bounded execution and hashed ownership. Existing conflicts always require manual migration rather than an interactive replacement path, preserving custom settings without attempting reconstruction through client CLIs. Custom client homes and command/batch-only launchers also require manual setup. Elevated uninstall deliberately leaves client registrations unchanged and reports cleanup instructions; normal per-user uninstall removes only unchanged installer-owned entries.

The first native MCP uninstall test exposed unsupported `ExecAsOriginalUser` use during uninstall, documented in [Inno Setup's function reference](https://jrsoftware.org/ishelp/topic_isxfunc_execasoriginaluser.htm). Cleanup now uses ordinary unelevated `Exec`, catches failures, and preserves configuration when elevated. Actual CLI/MCP lifecycle checks and isolated real-client protocol/plugin checks pass; signed service, historical installed-upgrade and no-runtime clean-machine acceptance remain separate. See the checklist for the frozen-source verification evidence and unrelated live-checkout failures.
