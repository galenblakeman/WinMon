# WinMon for Claude Code

This optional plugin connects Claude Code to the local, read-only WinMon MCP server and adds a performance-diagnostics skill. Both WinMon MCP downloads include this folder as `claude-code-plugin` beside `WinMon.Mcp.exe`. The plugin requires Windows x64 and a compatible Claude Code installation.

Set the server's absolute path before launching Claude, then select this folder:

```powershell
$env:WINMON_MCP_EXE = 'C:\Path\To\WinMon.Mcp.exe'
claude --plugin-dir 'C:\Path\To\claude-code-plugin'
```

For future shells, configure `WINMON_MCP_EXE` through Windows user environment settings and restart the terminal. Do not enable a duplicate direct WinMon MCP registration while using the plugin. Ask Claude for a system snapshot or a short performance sample. Diagnostic results describe local observations; client approvals and WinMon privacy defaults still apply.
