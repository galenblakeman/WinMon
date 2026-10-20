# Multiple disk volumes

Selected layout: a six-metric strip for two monitored volumes. The superseded mockup has been removed.

Scope: extend the existing .NET 10 WPF monitor to one or two configured logical volumes. Default primary is the Windows system volume; the optional second selector defaults to None. Preserve all other resource and process behavior. Use existing controls, themes, PDH APIs and the current sampling loop.

Requirements checklist:

- Verified: persisted, validated primary and optional second drive letters; duplicate volumes rejected in Settings and normalized out of stored preferences.
- Verified: independent primary and secondary PDH queries and rate baselines; initial readings, gaps, missing volumes and configuration changes do not fabricate zero readings or stop other resources.
- Verified: six-column layout only when a second volume is configured; 720-DIP minimum width with two volumes and the original 600-DIP minimum otherwise.
- Verified: orange primary solid trace, gold secondary dashed trace, per-drive active time and decimal MB/s, independent remembered visibility.
- Verified: unavailable second volume retains its selection and shows a dash with Unavailable; available idle volumes correctly show zero.
- Verified: historical samples retain both volume names and values; each trace breaks when its source volume changes.
- Verified: native Settings Save flow for a selected second drive and None; Release publish; full suite of 129 tests. Final dark/light screenshot review passed after the capture-timing correction. Native published smoke exited 0.

Implementation boundaries: MonitorPreferences/PreferenceStore and SettingsWindow; ResourceSample and WindowsResourceSampler; MainWindow and CpuGraph; isolated diagnostics; NUnit tests. Existing legacy visibility bits are unchanged; the second disk uses a separate preference and chart bit 32. No package or framework changes.

Verification evidence is under artifacts/multiple-disks-capture-verified. Initial baseline test execution aborted when the sandboxed VSTest process could not connect to testhost; the updated full suite passed outside the sandbox (129 tests). Restore required a local command-line NuGetAudit=false because nuget.org vulnerability metadata was unreachable; this does not change project configuration. Actual removable-device unplug/replug and actual mixed-monitor DPI transitions remain manual checks.

Validation completed:

- Release publish succeeded with zero warnings/errors. Executable: src/WinMon/bin/Release/net10.0-windows/win-x64/publish/WinMon.exe.
- Full NUnit suite: 129 passed, zero failed/skipped. Final command used dotnet test tests/WinMon.Tests/WinMon.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false outside the sandbox.
- Published native smoke exited 0; disks.json records readable C: and D: measurements plus an unavailable A: while C: remained readable. The Settings dialog Save action returned the selected second volume and None in separate checks.
- Independent toggles, restored single-drive layout, retained historical names/readings, pause/resume, hidden/minimized recording, expiry and shutdown checks passed.
- Dark/light and 100/150/200 percent bitmap renders inspected. Comparison: artifacts/multiple-disks-capture-verified/comparison.png. Render fixture values are illustrative, separate from native measurement evidence.
- git diff --check passed. No commit or push was performed.
