# Diagnostic file logging

Always collect application errors, startup milestones and network-service connectivity changes locally. Logging is automatic, has bounded memory and disk usage, and never uploads data. This design targets the existing WPF/.NET 10 app and .NET 10 Windows service.

## Locations and access

- App: `%PROGRAMDATA%\WinMon\Logs\App\<Windows SID>` when securely provisioned and writable.
- App fallback: `%LOCALAPPDATA%\WinMon\Logs` when the shared location is unavailable. This also covers portable and Store launches without an installed service.
- Service: `%PROGRAMDATA%\WinMon\Logs\Service`. A privileged process never falls back into a user's writable folder.
- Elevated app/helper launches: `%PROGRAMDATA%\WinMon\Logs\Elevated\<Windows SID>`, protected for administrators/System, using the same 10 MiB active app-set limit. They never write to ordinary user-controlled files or use LocalAppData fallback.

Network-service setup creates protected shared ancestors and a private app directory for the requesting account. Administrators/System control the ancestors and service directory. Only the account, administrators and System can write its app directory. Provisioned accounts may read service logs. Reject untrusted owners, unexpected writers and reparse directories instead of repairing an arbitrary pre-existing tree. Failure to provision logs must not block service installation.

Each active app log set retains five files of at most 2 MiB, and the service retains another five: **20 MiB for one account's active app set plus the shared service set**, as agreed. Additional accounts have independent app sets. Historical files in an inactive fallback location are not migrated or deleted automatically. The zero-byte writer lease is outside the payload budget. Files persist across launches and service removal; rotation removes the oldest archive. Provisioned service-log read access remains after monitoring authorization is revoked; logs contain no monitored account traffic.

## Events and privacy

UTF-8 text uses UTC timestamps, severity, App/Service component, a random launch/session identifier, a code-owned event template and safe values. Existing `Trace` error/warning calls feed the same listener, including portable MonCore callers without adding a file dependency to MonCore.

New events cover startup version/runtime/Windows version and Store status, preference loading/default recovery, window readiness, sampling start/first healthy sample, normal exit, network state changes, initial connection failures, reconnect attempts and outage duration, setup start/result/failure phase, service startup/listener failure, collector startup/failure, power transitions and shutdown.

String argument values are omitted rather than heuristically redacted. Exception details retain type, HRESULT, native Win32 code when available, inner-exception chains (up to eight), and method stacks (up to 24 frames). Messages, source file paths and line numbers are excluded. Numeric, Boolean, version and enum arguments are retained. Do not add user-controlled values to templates. Direct `Trace.Write` payloads are omitted. No process names, executable paths, usernames, endpoints, command lines, setup tokens, raw IPC messages or monitored samples are intentionally recorded. The SID exists only in the protected folder name.

Identical warning/error templates emit immediately once, then at most a repeat summary per minute. A different template is still recorded immediately. Connectivity recovery records failed-attempt count and elapsed outage time. An unhandled UI/domain exception receives a best-effort final record; logging does not mark it handled or change crash policy. Unobserved task exceptions are recorded without changing their observation policy. These hooks cannot guarantee capture of native crashes, process kills or power loss, nor detect every exception intentionally swallowed by existing code.

## Writer and failure handling

Use a small `TraceListener` in MonCore.Windows, shared by app and service. No package or logging framework is added. A 512-entry bounded channel feeds one background writer, with a one-second flush cadence. Each entry is limited to 4,000 characters (under 16 KiB). Duplicate tracking is limited to 128 templates. A per-folder lease prevents competing processes from rotating the same files.

Queue overflow drops entries and later emits a count. Storage failures retain the queue's bound, attempt reopening the same destination and report lost entries after recovery. Initial app destination selection falls back to LocalAppData; it does not switch directories mid-session. If neither app destination works, monitoring continues without file logging. Shutdown drains best-effort with a two-second wait; a stalled disk cannot force the UI to wait indefinitely. The worker closes its handles when disk operations return. Logging changes no existing retry, cancellation, collection or shutdown ownership.

## Info action

Add **Open logs folder** to Info → About, using the existing button style and translated resources in all supported cultures. Open the actual active app folder. If logging did not start or Explorer cannot open the folder, show a localized notice. Service logs remain in the sibling Service directory beneath the shared Logs root.

## Verification checklist

- Verified: rolling writer, bounded queue, privacy filtering, duplicate summaries and shutdown hooks.
- Verified by focused tests: per-user ACL validation, protected-directory rejection and LocalAppData fallback. ProgramData provisioning is implemented; signed installed acceptance remains manual.
- Verified: startup/preference integration and connection failure/recovery events. A normal app launch also produced preference/window/first-healthy-sample and successful network connection events in the fallback file. Service/collector/setup events build and pass their existing tests; live installed service logging acceptance remains manual.
- Verified: translated Info action/layout, all resource cultures and user/developer/privacy documentation. Standard and Store verification pass; feature smoke produced 113 renders and Info/About PNGs were inspected.
- Verified: focused NUnit tests for rotation, newest-entry retention, private argument filtering, method stack preservation, duplicate summaries, concurrent producers/overflow, writer exclusion/release, fallback preference/failure and unexpected directory writers.
- Verified: four alternating isolated live-frame benchmark pairs, matching pixels and 608,512 UI B/frame in both modes, with results recorded in PERFORMANCE-HISTORY. Median paired timing difference was +0.012 ms/frame; timing remains inconclusive because run-to-run variation was large. A quiet-machine timing gate remains open.
- Blocked on manual acceptance: a signed installed-service upgrade, real LocalSystem logging and access from a second Windows account, native sleep/resume and disk-full/slow-volume hardware behavior.

## References

- [.NET 10 TraceListener](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.tracelistener?view=net-10.0)
- [WPF .NET 10 DispatcherUnhandledException](https://learn.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-10.0)
- [.NET 10 WindowsPrincipal.IsInRole and UAC](https://learn.microsoft.com/en-us/dotnet/api/system.security.principal.windowsprincipal.isinrole?view=net-10.0)

## Result

Implemented in `MonCore.Windows/Diagnostics`, `App`, `PreferenceStore`, the network controller/setup broker, network service and setup helper, and `HardwareInfoWindow`. The agreed 20 MiB budget applies to one active app set and the service set, rather than every account and historical fallback folder on the machine. Exception messages are deliberately omitted; method stacks and codes provide diagnostic context without saving arbitrary sensitive text. No log viewer, uploads, debug mode or new preferences were added.
