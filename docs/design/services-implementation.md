# Expandable Services view

Option 2: services and their observed worker processes share an expandable table beneath the existing resource chart.

| Requirement | Status |
| --- | --- |
| Discover local Win32 services without elevation, including stopped services | Verified |
| Track ancestry by PID plus creation time; stop at other service roots | Verified |
| Sum CPU, private bytes and read/write I/O; preserve unavailable values | Verified |
| Show shared services under one host total without duplicate attribution | Verified |
| Process-style Top N toolbar, flexible search, status filter, worker toggle, sorting, expandable children and CPU history | Verified |
| Selected service/process summary, View process, Start/Stop/Restart | Implemented |
| Disable actions in historical views; retain up to two minutes of service history | Verified |
| Localized labels, theme resources and horizontal scrolling at narrow sizes | Verified |
| Unit tests, build, existing suite and native visual smoke | Verified |

Worker attribution is inferred from observed ancestry, not a Windows ownership guarantee. Track established relationships while workers remain alive, even if an intermediate parent exits. Do not carry workers across a host restart. Driver services are outside this process-based view.

Service actions target the SCM service name, never terminate a shared host or recursively stop dependent services. Check the current host lifetime before control; request elevation only after access is denied. Confirmation is required for Stop/Restart.

SCM controls address the named service. The final identity/status checks reject ordinary stale selections, but the SCM API cannot atomically compare an expected process lifetime and stop only that lifetime. Closing WinMon while an accepted action is underway is deferred until the bounded operation finishes, protecting the Stop/Start sequence from monitor cancellation.

Service history uses monotonic elapsed time, a 121-frame cap, and a separate 120,000-observation budget (process plus service observations). The newest frame is kept even if it alone exceeds that budget; native process collection already has a 20,000-process cap.

Baseline: 502 existing tests passed from the pre-edit isolated build. The sandboxed testhost connection timed out; rerunning the immutable built tests outside the sandbox passed.

Delegation: primary agent owns discovery, aggregation, UI, actions, integration and visual validation. A test agent owns only `tests/WinMon.Tests/ServiceTreeTests.cs` for independent ancestry/aggregation/history regression coverage. A translation agent owns only the nine satellite resource files' new service strings.

## Validation

- Existing baseline: 502 tests passed. Latest full NUnit suite: 533 tests passed, including service aggregation/history, five-second ranking, themed dialogs and Top N filtering coverage.
- Release build: succeeded with zero warnings and zero errors, using isolated `.artifacts/services-release/` output.
- Final Release `--services-smoke-output .artifacts/services-final`: exited successfully. Live discovery found 309 services and mapped 129 service hosts, including five shared hosts. Counts are specific to the capture machine and moment.
- Inspected native-composed dark, light, 836px narrow and historical screenshots. Historical buttons and close-during-action deferral were asserted by the smoke mode. `docs/example/example_services.png` is an illustrative capture of the implemented UI.
- No live services were started, stopped or restarted. The service-control/UAC execution paths remain unverified against a disposable installed service; doing so requires an appropriate test service and administrator desktop session. Unit and visual checks cover the action state guards, not successful elevated SCM mutation.

October 23 header update: the toolbar now follows Processes with Top 5/10/15/20/50 (default 20), services-by ranking, flexible search with clear button, status filter and Workers toggle. Filtering precedes the host-row limit; expanded processes and shared-service children do not consume it. CPU and I/O use the shared process five-second window rules; memory remains latest and sparklines retain interval data. All new labels are translated in the nine supported satellites. The isolated Release build in `.artifacts/service-header-release/` passed with zero warnings/errors. Native smoke in `.artifacts/service-header-smoke/` passed wide/narrow/600px wrapped/German toolbar bounds and overlap checks, plus clear-button query/focus behavior. Captures were visually inspected and the illustrative gallery image updated.

October 23 action-bar update: History now follows the service name with headers synchronized to the row viewport. The selected-service footer uses a theme accent edge, bold identity, status badge, separate metadata line, an inspection/control divider, a restrained Stop outline, and a stronger neutral Restart outline. High contrast uses system colors. The bar can place actions below identity when space is constrained. Action eligibility and confirmation behavior are unchanged. Fully isolated Release output in `.artifacts/service-action-release/` built without warnings/errors; all 533 tests passed from `.artifacts/service-action-validation/`. Smoke output `.artifacts/service-action-smoke-v4/` passed header and action-bar visibility/overlap checks; dark/light/narrow/translated captures were inspected. The gallery uses the native-composed `services-dark.png`; explicitly named content fallbacks were only used for layout QA where foreground activation was denied.

Native API references (Windows desktop APIs; app targets .NET 10 WPF, SDK 10.0.402):
- https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-enumservicesstatusexw
- https://learn.microsoft.com/en-us/windows/win32/api/winsvc/ns-winsvc-service_status_process
- https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-controlservice

Parent PID is read from the existing x64 native process snapshot layout at offset 0x58. This layout is version-sensitive, like the existing sampler; validate against live process ancestry.
