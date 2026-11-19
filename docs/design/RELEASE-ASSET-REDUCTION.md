# Reduce release assets

Keep the six desktop, CLI and MCP downloads and all eight updater metadata files. Move the WinGet ZIP to Actions-only distribution and bundle the Claude Code plugin with MCP instead of publishing a standalone plugin ZIP. Put a direct download table above generated release changes.

## Landed

`tools/New-Release.ps1` stages the versioned plugin under `claude-code-plugin` before compiling the MCP installer and copies the same folder into the MCP portable ZIP. `packaging/WinMon.Mcp.iss` installs and owns these files. Client registration and `WINMON_MCP_EXE` setup remain explicit.

The release workflow retains and validates WinGet material in Actions artifacts, excludes it from GitHub uploads, and prepends `packaging/RELEASE-NOTES.md` with the repository/version substituted. Public checksums exclude the WinGet ZIP. There are 15 uploaded assets instead of 17; GitHub source archives are additional.

Release notes use the GitHub CLI's [file input and generated-notes support](https://cli.github.com/manual/gh_release_create), keeping the download table above generated changes.

`Test-ReleaseSigning.ps1` exercises signed payload hashes, RSA signatures, legacy alias equality, bundled plugin contents/version, the workflow's asset selection and all six download links using fake build/signing/publication boundaries. `Test-McpInstaller.ps1` additionally checks installed plugin contents/version and removal. `Test-McpPackage.ps1 -PluginArchive` accepts the MCP ZIP as well as historical standalone plugin archives.

The implementation follows the proposed reduction. Existing release assets and updater contracts are unchanged. Local unsigned MCP publication and Inno Setup 6.7.3 compilation passed, followed by `Test-McpInstaller.ps1` installation, plugin version/content checks, MCP stdio discovery/snapshot, repair, downgrade rejection and owned-file removal. Signing-boundary tests passed with simulated Azure/build boundaries; live signing and publication remain separate checks.

`Verify.ps1` passed with 2,001 tests executed and no failures; expected platform, signed-bundle and explicit measurement exclusions remained. `Test-McpPackage.ps1 -ValidatePlugin -SkipSdkIntegration` passed against the MCP ZIP with isolated Codex/Claude Code configurations, including Claude plugin validation and a connected plugin server from a path containing spaces. The plugin includes its own setup README; the validator's optional marketplace-icon warning remains outside this distribution change.
