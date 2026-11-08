# Network monitoring service foundation

Implements the service/IPC boundary of [optional per-process network monitoring](../../docs/design/PER-PROCESS-NETWORK.md). Optional Settings enablement, signed provisioning code and Connections presentation are integrated. Signed installation and actual LocalSystem/cross-account acceptance remain unverified; see the implementation checklist.

## Build and verification

The service targets .NET 10 on Windows, using TraceEvent 3.1.23 and `System.ServiceProcess.ServiceController` 10.0.14. It references MonCore.Windows for the protocol, not WinMon/WPF. MonCore contains only portable snapshot records and quality flags.

```powershell
./tools/Verify.ps1
./tools/Verify.ps1 -Store
```

Both commands now build the service and run `WinMon.NetworkService.Tests`. The Store configuration checks compatibility of the shared source; it does not package or enable the service. Tests use random local pipe names and install nothing. Run outside a restricted sandbox if the test host or native pipe operations are denied.

The explicit native ETW test needs an approved elevated Windows terminal and the already-built prototype workload:

```powershell
dotnet build tools/WinMon.NetworkProbe/WinMon.NetworkProbe.csproj -c Release -m:1 -p:UseSharedCompilation=false --artifacts-path artifacts/network-probe
dotnet test tests/WinMon.NetworkService.Tests/WinMon.NetworkService.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false --artifacts-path artifacts/network-service
# Run this selection in an elevated terminal:
dotnet test tests/WinMon.NetworkService.Tests/WinMon.NetworkService.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false --artifacts-path artifacts/network-service --no-build --filter 'FullyQualifiedName=WinMon.Tests.NativeNetworkCollectorTests.Collector_AccountsForControlledWorkerAndStopsItsSession'
```

The native test starts a bounded controlled worker, tests TCP/UDP and IPv4/IPv6 existing/short-lived/load cases, checks exact cumulative bytes, loss, rate recovery and trace cleanup, and writes a uniquely named report under `artifacts/network-service`. It exercises the actual collector in an elevated test host, not a registered LocalSystem service. Do not interpret this as signed installation or cross-account SCM acceptance.

## Service host and authorization policy

`Program` accepts no commands and requires LocalSystem. `ServiceBase` registers only `WinMonNetworkMonitor`. Startup requires the executable and dependencies in `%ProgramFiles%\WinMon\NetworkMonitor`, with an administrator-protected `allowed-users.json` containing a nonempty array of explicitly provisioned account SIDs (maximum 64). Missing, malformed, reparse-point or untrusted-writable installation/policy state fails closed. Policy is loaded at service start; elevated provisioning/revocation must restart it. Startup checks are defense in depth; the future signed installer must establish protection before any executable or dependency is loaded.

There is deliberately no command-line SID, console service mode, install command, self-update RPC or developer privilege bypass. A normal application launch still installs nothing. Do not register a service from this writable checkout. Future setup must publish/sign a self-contained x64 service into the fixed protected directory, register an own-process LocalSystem demand-start service with a quoted executable path, and grant provisioned users query-configuration, query-status and start rights only. Stop/configure/delete and binary/policy write rights remain administrative.

The first authenticated subscription starts one collector shared by all clients. Leases belong to pipe connections; explicit unsubscribe, broken pipes, timeouts and shutdown release them. After the last lease, tracing stops after a two-second reconnect grace period. The demand-started service listener remains idle until SCM stop/reboot. Keeping the listener avoids a last-disconnect/new-subscription shutdown race; automatic service exit is optional in the design and is not implemented here.

Power notifications asynchronously suspend/dispose collection and resume with a fresh collector generation. Stop/shutdown captures owned state once and cancels/drains the listener and collector. Unexpected collector failure yields unavailable snapshots rather than old live values. A hard process kill can still bypass managed ETW cleanup; orphan recovery remains a deployment/crash-test gate, not an automatic prefix-based stop of other sessions.

## Authenticated local IPC

The fixed endpoint is `WinMonNetworkMonitor.v1`. Native pipe creation rejects remote clients. Its protected DACL grants LocalSystem server rights and provisioned SIDs data rights while denying their pipe-instance creation right. A first-instance listener claims the name; a replacement listener is created before handing the connected instance to a handler, keeping ownership through reconnects. Tests grant their own hosting SID server rights; the production entry point requires LocalSystem.

The client uses .NET's explicit `PipeAccessRights` constructor with read/write data rights and identification-level QoS. It checks the registered SCM service before starting it: own-process type, LocalSystem account and the exact quoted protected executable command. It then verifies the connected pipe's server PID against the running SCM process, retains a limited-query process handle, checks process lifetime/exit state, and rechecks SCM. Token identity must be LocalSystem when queryable; normal-user token-query denial falls back to the already-validated SCM configuration. No public client API accepts an arbitrary service/pipe name or skips verification.

The server obtains the actual Windows user SID through checked pipe impersonation and compares it to the immutable allowlist. A request's claimed SID is never authorization. Snapshot rows are filtered by owner SID in the ledger; no other users' process identities or byte counters are returned. Global loss/coverage quality can conservatively affect all clients.

Protocol version 1 is length-prefixed JSON with status, subscribe, snapshot and unsubscribe operations. Requests are bounded to 4 KiB; responses to 2 MiB and 8,192 process rows. At most eight connections are served, with one pending listener anchoring the name. Requests/writes time out after ten seconds; control operations are capped at 16 per second per connection and snapshots at one per second. Invalid versions, commands, frames or authorization close that connection and release its lease. Client requests are serialized; interrupted exchanges close the pipe instead of reusing a partial frame. Disposal is bounded and idempotent.

Wire snapshots contain raw collector-generation cumulative totals. The app applies client-session baselines through ProcessNetworkSession in MonitorSession and passes the resulting frame to Connections. Other subscribers must retain generation/quality/window metadata rather than treating this API as an ordinary arrival-time throughput counter.

## Delayed events, identities and bounded state

The collector enables only kernel network and process metadata on a uniquely named system logger. Typed callbacks use TraceEvent's payload-corrected PID, copy compact values to a bounded channel, and perform no process/token I/O. Zero-byte events do not count as missing traffic. Unsupported payload versions and queue rejection cause explicit data gaps.

The compact queue holds at most 262,144 events (about 6 MiB of event structs), sized for burst delivery from the requested 16 MiB ETW buffer budget. The original 65,536-item queue lost traffic during native UDP stress and was rejected. Snapshot `EventsLost` describes native ETW loss; `DroppedEvents` separately counts service queue rejection. Neither is silently converted to idle throughput.

Ownership probes read the creation FILETIME and token user SID from the same process handle. Cached ownership is trusted only through a successful native verification timestamp. Reconciliation checks creation time cheaply and resolves a new token when a PID changes. At capacity, existing live identities are preserved and new ones are rejected with incomplete coverage. Retained closed lifetimes accept delayed events within their original bounds; strict reuse boundaries remain ambiguous and are discarded. Unknown ownership never becomes another user's traffic.

Rates use one-second event-time buckets over a five-second window ending three seconds behind the current whole second. Initial rates are null until that complete window exists. Every rate is provisional: the holdback is not a delivery guarantee. Late events update the bucket where they occurred, not the arrival interval; older retained events update cumulative bytes without creating a current spike. Retention is 30 seconds, with at most 8,192 lifetimes/tombstones and bounded buckets.

Detected native loss, dropped batches and ambiguous known traffic suppress rate windows until a fresh window follows the gap; incomplete totals remain flagged for the generation. Coverage uncertainty from an unrelated protected or already-exited PID marks the snapshot incomplete without continuously suppressing known processes' valid windows. Requests cannot force a collector flush and there is no known-byte synchronization in production.

Event timestamps use `TimeStampRelativeMSec` mapped once into the Stopwatch clock domain; snapshots and arrivals use that monotonic clock. The original UTC calibration is frozen. A periodic drift check fails collection unavailable if wall time diverges by more than a second, rather than applying a changed wall clock to process identities. Reconnect/resume creates a new generation; old baselines must not bridge it. Unavailable snapshots now carry a bounded first-failure code distinguishing the clock guard, ETW consumer/stop, event consumer, startup and shutdown. The code contains no process or traffic metadata and is retained only in memory; it is diagnostic evidence, not an automatic restart request.

## Optional signed setup

`WinMon.NetworkSetup` implements provisioning and independent Windows Installed apps removal; Settings enables collection only on Save after authorized subscription/health succeeds. Startup/re-enable use a compatible authorized service without UAC. Disabling/closing releases only that client's subscription; explicit repair/removal can elevate and removal affects all users. Signed desktop releases embed the helper, archive and RSA-signed fixed manifest in WinMon.exe. The helper is NativeAOT, runs elevated, authenticates the original pipe-server process token SID, checks fixed protected paths, deploys under Program Files, grants provisioned users only query configuration/status/start (`0x15`), and restores pre-commit deployment failures. No staged managed dependency is loaded by the elevated helper. The protected owned-file inventory prevents deletion of unrelated files. Removal through the installed helper can finish deleting its locked image at reboot (3010).

CI run 42429999830 signed corrected revision f455bc5 using the existing release credentials. Approved native installation, normal-user production client authentication/snapshots, Settings disable/re-enable, enabled app restart, signed repair and removal passed on the development box. The service process grants provisioned SIDs only PROCESS_QUERY_LIMITED_INFORMATION (0x1000), allowing held-process identity verification without memory/control rights; its protected DACL is applied before listening and fails closed. The test installation was removed and the original debug app restored. An intermittent post-repair unavailable state remains unexplained, so sustained recovery is not accepted. No unsigned setup fallback is allowed. Unsigned elevated collector tests and native pipe tests remain available without installing a service. The checklist records the remaining native gates and transaction limitations, including OS failures after removal commit and unverified machine-crash recovery.

## Remaining acceptance

## Elevated account revocation

An administrator may revoke a provisioned account only with the installed helper and a canonical SID:

```powershell
& "$env:ProgramFiles\WinMon\NetworkMonitor\WinMon.NetworkSetup.exe" --revoke-user S-1-5-...
```

The helper validates the owned service, protected installation, uninstall registration and current policy before mutation. Revoking one of several accounts stops the service, atomically replaces the protected policy, reapplies the exact service ACL, and restores the original policy/ACL/running state on failure. Revoking the final account uses the existing all-users removal path; it can return `3010` if Windows defers deletion of the running helper image until reboot.

## Remaining acceptance

The [implementation record](../../docs/design/PER-PROCESS-NETWORK-IMPLEMENTATION.md) contains actual commands/results and the remaining checklist. Still required: installed-upgrade/rollback matrix, sustained recovery, reboot acceptance, real second-account isolation, remote access rejection attempts, suspend/resume on hardware, hard-kill orphan recovery, controlled forced-loss recovery, external-link/VPN/proxy/retransmission behavior, older supported Windows versions, installed app integration and final service performance budgets.

References: [named-pipe security and access rights](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights), [pipe impersonation](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-impersonatenamedpipeclient), [service access rights](https://learn.microsoft.com/en-us/windows/win32/services/service-security-and-access-rights), and [.NET 10 pipe client source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeClientStream.Windows.cs).
