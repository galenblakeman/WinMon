# Network collector corrections

November 26, 2026. Scope: `WinMon.NetworkService.EtwNetworkCollector` on .NET 10, TraceEvent 3.2.8.

## Ownership boundary

If lifecycle events are missed, a failed creation probe does not prove that the previous owner held the PID until the probe time. A currently observed successor also does not rule out an intervening process from another account. Extending the old lifetime to either timestamp could expose delayed counters and endpoints under the wrong account.

`EndInferredLifetime` closes the old lifetime at the earlier of the inferred boundary and its last successful native verification. The excluded interval carries `AmbiguousLifetime` coverage. This can omit uncertain traffic rather than assigning it to an account without evidence. An explicit process-stop event keeps its observed boundary, preserving ordinary delayed-event attribution.

## Queue progress

`ConsumeAsync` processes one batch of at most 4,096 events before checking the reconciliation deadline and returning to its cancellation check. Producers no longer need to let the queue empty before those checks can run. The existing bounded queue, lock scope and reusable timer remain.

## Verification

`CollectorRegressionTests` adds three inferred-exit cases, an explicit-stop control, a nonempty-queue reconciliation case and a batch-boundary cancellation case. Before the fix, five failed and the control passed; after the fix all six passed. The affected service suite also passed: 382 executed, zero failures, with its two explicit native/benchmark cases excluded. TRX reports are under `artifacts/network-collector-fixes/`.

Only scoped validation was run, following the user's request for minimal tests. Full desktop/Store verification, live ETW, installed-service and second-account acceptance were not run. Synthetic timing evidence is recorded in `docs/PERFORMANCE-HISTORY.md`.

The successful service command was:

```powershell
dotnet test tests/WinMon.NetworkService.Tests/WinMon.NetworkService.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false --artifacts-path artifacts/network-collector-fixes/changed --logger trx --results-directory artifacts/network-collector-fixes/service-results -- NUnit.ExplicitMode=None
```

The focused six-case run used the same options with `--filter FullyQualifiedName~CollectorRegressionTests` and results in `green-results`. Both commands exited 0, and their TRX counters and regression-case outcomes were parsed successfully. The service build completed without warnings or errors. The service-suite command had already started when the user narrowed verification; no broader suite was launched afterward.

## What landed

The collector changes and regression tests landed in source, with updated characterization expectations for inferred lifetimes. The fix also bounds detected-reuse inference, since the same uncertain interval can hide intermediate PID owners. No protocol, installation or UI change was needed; this is the only extension beyond the two audit findings.
