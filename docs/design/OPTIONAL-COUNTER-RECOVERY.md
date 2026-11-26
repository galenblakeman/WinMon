# Optional counter recovery

Optional Windows measurements must not discard otherwise valid resource frames. A temporary GPU shared-memory read failure must leave later observations able to recover. Service startup tests must execute the loop used by the real clients.

## Implementation

- `MemoryPressureCounter.Read` constructs an unavailable reading after failed collection. This runs the thermal-zone list initializer; a zero-initialized struct would leave the list null. The thermal reader can consume the failure observation, and rate collection requires a fresh baseline on recovery.
- `WindowsGpuMemory.Attach` retains the shared counter after a formatting exception. An internal, instance-owned counter interface and capacity delegate allow tests to prove that dedicated memory survives, the next shared read succeeds on the same query, and required-counter failures still dispose and reopen it. Native counter acquisition and disposal remain in the Windows adapter.
- `ProtectedServiceIdentity` owns the single startup loop and a native implementation of `IServiceStartupScope`. Both network overloads forward there, and the sensor client uses it directly. The controlled startup fixture runs for both endpoints, preserving production error messages, cancellation order, 50 ms polling and the ten-second deadline.

## Delivery

Landed in `src/MonCore.Windows/Monitoring` with regression checks in `TemperatureCollectorTests`, `GpuMemoryTests` and `ServiceStartupCharacterizationTests`. Existing public entry points and wire formats are unchanged. No new dependency was added. Performance and verification evidence are recorded under `artifacts/audit-fixes` and in the performance history.

There is no feature expansion or departure from the recovery behavior above. Native signed service installation and physical GPU fault injection remain separate acceptance work; the regression tests inject failures without changing installed services.
