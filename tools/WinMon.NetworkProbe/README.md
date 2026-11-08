# ETW network prototype

This is the first increment of [optional per-process network monitoring](../../docs/design/PER-PROCESS-NETWORK.md). It is a standalone development tool, not the Windows service or a shipping WinMon feature. It installs nothing and changes no WinMon preferences. Its TraceEvent 3.1.23 dependency is experimental and isolated from all shipping projects.

## Build and test

Run from the repository root, with the SDK pinned in `global.json`:

```powershell
dotnet build tools/WinMon.NetworkProbe/WinMon.NetworkProbe.csproj -c Release -m:1 -p:UseSharedCompilation=false --artifacts-path artifacts/network-probe
dotnet test tests/WinMon.NetworkProbe.Tests/WinMon.NetworkProbe.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false --artifacts-path artifacts/network-probe-tests
```

If the test host cannot connect in a restricted sandbox, run the tests outside it. During an audit-endpoint outage, `-p:NuGetAudit=false` can be applied to an individual development command; this does not validate dependency vulnerability status and is not a project setting.

## Run the controlled native experiment

Use an elevated PowerShell on an approved Windows test machine. The user authorized this development box for the recorded experiment. The tool does not request elevation itself; a normal user receives an ETW permission error in its report.

```powershell
./artifacts/network-probe/bin/WinMon.NetworkProbe/release/WinMon.NetworkProbe.exe --output artifacts/network-probe/native-result.json
$LASTEXITCODE
```

Choose a new report path for each run; existing reports are never overwritten. The tool has a 90-second cancellation budget plus bounded ETW cleanup. It creates a uniquely named `WinMon-NetworkProbe-<pid>-<guid>` system logger with `NoRestartOnCreate`, so it does not replace another trace session. Normal completion, Ctrl+C and handled failures dispose that session and terminate the controlled worker. It records whether the session was removed. It does not create ETL files or retain packet contents or endpoints.

The worker binds loopback sockets and is the only process for which byte rows are retained. The collector receives system-wide network metadata to measure callback volume, then discards unrelated events. This tool has no production cross-user authorization or IPC. Do not distribute it as the service.

The suite takes about 45 seconds on the measured development box:

1. Create existing IPv4/IPv6 TCP connections before tracing starts.
2. Measure three seconds of idle collector cost without tracing and then with tracing.
3. Send 16 MiB on each existing TCP connection.
4. Send 4 KiB on each of 32 short-lived TCP connections per IP family.
5. Send/receive 1,024 UDP datagrams of 1,024 bytes per family.
6. Exercise three seconds each of sustained TCP and high-event-rate UDP traffic per family.
7. Exit the worker, drain events and stop the trace.

Both socket ends belong to the worker, so each one-way payload is sent once and received once under that PID. Each scenario flushes/drains until its known byte targets arrive or ten seconds elapse. Exit code 0 requires all scenarios, exact controlled send/receive payload comparisons, all eight direction/protocol/family payload PID checks, no lost events, no rejected positive-byte worker events and session cleanup. IPv6 unavailable, a mismatch, loss, cancellation or permission failure yields exit code 1 and a report. Invalid arguments or an existing output file yield exit code 2. `CapturePassed` describes only this suite, not release readiness.

Zero-byte events are counted separately and contribute no bytes. Do not classify them as missing traffic. Only send/receive events contribute bytes; TCP copy/retransmission events are not added a second time. The pinned TraceEvent parser fixes up `ProcessID` from the payload before dispatch; the tool independently compares the raw payload PID for the first event of each of the eight categories. Version-zero network events use older layouts and are explicitly rejected/count as unsupported in this prototype. See the [3.1.23 kernel parser source](https://github.com/microsoft/perfview/blob/v3.1.23/src/TraceEvent/Parsers/KernelTraceEventParser.cs).

## Reading the JSON

Each phase contains known payload bytes, observed bytes, ETW event loss and collector process CPU milliseconds, allocated bytes, private bytes and wall-clock duration. The final snapshot contains cumulative worker rows identified by PID and creation FILETIME, event counts, sampled PID mismatches and cleanup state. Report metadata includes OS, runtime, library version and actual token elevation.

Phase durations include workload execution and the time required to drain its events. Do not divide phase bytes by that duration and present it as link throughput. CPU excludes workload execution and Windows kernel/provider overhead. Allocations use the runtime's process-wide approximate total and can be reported in batches. The initial TCP phase includes JIT/parser warmup. These measurements do not establish production service performance budgets.

Tests cover the bounded prototype ledger's protocol/direction separation, PID reuse, stale lifecycle events, out-of-order seeding, capacity and pruning. The prototype seeds a known worker's real creation FILETIME and observes its exit; it does not yet reconcile arbitrary process starts/ownership. Delayed events after an exit are safely rejected and reported, not reassigned. Tombstone eviction needs a quality flag before production reuse.

## Abnormal termination and remaining experiments

An OS/process hard kill can bypass managed cleanup. Query `logman query -ets` in an elevated terminal, verify the exact orphan name against the probe's console/report, and stop only that session with `logman stop <exact-session-name> -ets`. Never stop the shared NT Kernel Logger or another application's session. If the process is still running, let its cancellation/cleanup finish before attempting manual removal.

Still required before a production collector is accepted: external-link traffic, VPN/proxy/loopback attribution differences, retransmission accounting, multiple unrelated processes and process churn, older supported Windows builds, forced consumer stalls/event loss, crash recovery and ownership boundaries. Successful loopback byte equality on this OS is evidence for this workload, not a Windows-wide accounting guarantee.

Results and the service/UI/packaging checklist are in [the implementation record](../../docs/design/PER-PROCESS-NETWORK-IMPLEMENTATION.md). Service installation and Windows settings are not part of this prototype.
