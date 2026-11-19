# Sampling under CPU load

Aligned gaps across resource charts can occur when the shared collection loop misses ticks. The chart breaks traces after 2.5 seconds; the CPU sampler resets interval baselines after five seconds. Those limits preserve honest coverage and must not be widened to conceal delayed collection.

## Approach

Run the existing synchronous frame source and compaction on one dedicated background worker, using a long-running task on the default scheduler. On Windows, set only that thread to [AboveNormal priority](https://learn.microsoft.com/en-us/dotnet/api/system.threading.threadpriority?view=net-10.0); keep process and UI priority unchanged. A failed priority change is logged and collection continues.

Use monotonic Stopwatch deadlines and the cancellation token's wait handle between captures. Waiting must not require a thread-pool timer or continuation. Account for capture duration, skip missed deadlines without accumulating catch-up captures, and wake immediately for cancellation. An in-progress synchronous native read still finishes before source disposal.

Preserve the existing queue, dispatcher delivery, failure recovery, retention and gap semantics. The portable MonCore leaf acquires no Windows-only reference or native import. Desktop, CLI and MCP reuse the same worker behavior.

## Verification

`CapturesStayOnOneDedicatedThreadWithElevatedPriority` checks successive captures stay on the same non-pool thread, Windows priority, recorded frames and disposal. `CancellationInterruptsTheWaitBetweenCaptures` checks cancellation wakes a minute-long wait. `SlowCaptureDoesNotAccumulateCatchUpTicks` checks a slow read does not produce a burst of overdue captures. Existing MonitorSession tests cover cancellation before startup and during capture, late-frame dropping, reset/recreation and delivery ordering. The full desktop smoke checks sampling through a blocked UI dispatcher.

Measure real WindowsFrameSource intervals under CPU saturation and controlled pool starvation, before and after; retain the raw probe and results under `artifacts/sampling-probe` and record results in PERFORMANCE-HISTORY.md. Synthetic UI refresh benchmarks disable collection and cannot measure this change.

Final verification: `./tools/Verify.ps1 -Smoke chart` passed all twelve steps, with 2,001 passing tests and zero failures, clean app/benchmark builds and twelve reviewed chart renders. It reused already-restored dependencies through a local CLI wrapper adding `--no-restore`; online NuGet vulnerability audit feeds were unavailable. The full native desktop smoke also completed with 100% reported CPU, including hidden/minimized recording and the UI-stall check (`artifacts/sampling-probe/smoke-drained`). One later full-smoke repetition exceeded its existing 90-second budget while rendering language settings; the final focused chart smoke passed. Ordinary verification excludes explicit, platform-inapplicable and signed/elevated environment-dependent cases as reported by Verify.ps1.

## What landed

The MCP bounded-performance consumer uses retained request-window observations regardless of their age when processed. It still requires a fresh initial observation and preserves gap, interval and process-lifetime checks. Controlled tests hold the consumer until ten intervals are queued: the old code counted only three and reported the first process absence at eight seconds; the corrected code retains all ten intervals and recognizes the first absence at one second. The tests are `DelayedMcpConsumerKeepsRecordedSystemSamples` and `DelayedMcpConsumerRecognizesRecordedProcessExit`.

The desktop smoke now yields through dispatcher ContextIdle before checking hidden/minimized history and UI-stall recovery. Capture drains are posted at Background priority, below the normal-priority async test continuation; inspecting history before draining them caused a false failure under load. The original assertions and waits remain intact, and hidden-window failures now report counts and visibility separately.

The shared MonitorSession owns the dedicated worker, Windows priority and cancellable monotonic schedule. No UI controls, dependencies or settings were added. Native collectors remain sequential: this change removes pool scheduling delays but cannot guarantee deadlines through a blocked driver, process suspension, higher-priority workloads or stop-the-world GC.
