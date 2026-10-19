# Resource overlay — selected option 1

Date: 2026-10-19. User selected the first displayed resource mockup and authorized implementation.

Visual target: [selected mockup](resource-overlay-selected.png). Existing .NET 10 WPF application, SDK 10.0.402; no new package dependencies. Reuses collection lifecycle, monotonic timing, bounded history, tray/preferences and compact/expanded session.

## Requirements and status

| Requirement | Status | Evidence |
| --- | --- | --- |
| Four labeled metric readouts above one shared fixed-scale overlay graph | Verified | Native renders and combined reference comparison in design QA |
| Green CPU, blue memory, amber disk and purple network; independent hide/show | Verified | Diagnostic toggle assertions; preference mask tests |
| Physical memory used/total; system-volume disk activity and throughput | Verified | Native smoke returned memory and C: active-time/bytes-per-second values |
| Identified link utilization plus raw receive/send rates | Verified | Native smoke returned Ethernet link speed and both rates; deterministic normalization tests |
| CPU-ranked process table with memory and I/O | Verified | Native collection/render; identity and elapsed-rate tests |
| Coherent historical resource/process selection, expiry and return to live | Verified | Unit history tests and diagnostic assertions |
| Pause/resume and reset/gap handling | Verified | Native diagnostic assertions; counter reset and link-change tests |
| Light/dark and scaled rendering | Verified | Final render review passed; actual DPI and mixed-monitor checks remain manual |

## Measurement decisions

- CPU is percent of machine capacity; physical memory is `(total − available) / total` from GlobalMemoryStatusEx. Display GiB and process MiB explicitly.
- Disk is the Windows system volume, not a sum of drives. Language-neutral PDH LogicalDisk counters supply `% Idle Time` and `Disk Bytes/sec`; active time is the inverse of idle. Query handles are disposed on reset/shutdown. Counter failures leave disk unavailable and allow other resources to continue.
- Network is one explicitly identified, connected Ethernet/Wi-Fi adapter. Prefer a default-gateway interface, then faster link, and retain the selection while connected. Network percent uses the busier full-duplex direction relative to reported link speed. It does not measure Internet capacity. Adapter identity or speed changes invalidate the rate baseline.
- CPU, disk and network reset after pause/sleep/delay; unavailable values are null and are never converted to zero. Memory is instantaneous. Samples are captured sequentially in one pass, not atomically across providers.
- Process memory uses resident working set. Process read/write I/O includes file, network and device operations, so the mockup's “Disk I/O” column is accurately labeled **I/O MB/s** with an explanatory tooltip. PID plus creation time prevents reuse from borrowing previous I/O counters.
- Readouts stay live during historical inspection. The selected timestamp, resource summary, process values and hover details expose historical state explicitly. Series visibility persists without discarding recordings. High contrast uses different line patterns in addition to labels.
- Minimum window is 600 × 800 DIPs; initial compact size is 600 × 840. The wider investigation view retains the same metric colors and history. Existing smaller saved bounds are brought up to the minimum.

## Validation

- `dotnet test WinMon.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false`: 46 passed outside the sandbox. Initial sandbox test hosts/data collectors failed to connect; those aborted runs are inconclusive, not test failures.
- Release build: zero warnings/errors. Native diagnostic run outside the sandbox: exit 0; real CPU/memory/disk/network, toggles, view switching, historical selection/expiry and pause/resume passed. Sandbox disk counters were unavailable, so native verification was repeated outside it.
- Short native measurements and bitmap-scale captures are not sustained overhead, actual desktop DPI, mixed-monitor, tray/startup or distribution validation.
- Final artifacts and visual QA: see [design QA](../design-qa.md).

## API sources

Existing desktop application targets .NET 10 and uses supported Windows desktop APIs; no preview APIs or framework migration.

- [GlobalMemoryStatusEx](https://learn.microsoft.com/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex)
- [Language-neutral PDH counters](https://learn.microsoft.com/windows/win32/api/pdh/nf-pdh-pdhaddenglishcounterw)
- [PDH formatted values and two-sample rates](https://learn.microsoft.com/windows/win32/api/pdh/nf-pdh-pdhgetformattedcountervalue)
- [Process memory](https://learn.microsoft.com/windows/win32/api/psapi/nf-psapi-getprocessmemoryinfo)
- [Process I/O counters](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-getprocessiocounters)
- [.NET 10 network interfaces](https://learn.microsoft.com/dotnet/api/system.net.networkinformation.networkinterface?view=net-10.0)
