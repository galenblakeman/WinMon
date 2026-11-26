# MonCore accounting corrections

The November 26, 2026 audit identified four accounting defects: a new flow blocked all connection rates; executable replacement mixed old and new activity in portable grouped rankings; missing system CPU discarded independent process usage; and overflow executables were counted again every tick.

Flow lifetime state now owns its own rate baseline and counter-reset boundary. Shared interruption handling remains in place. Established flows keep their rates when another flow is warming up.

Portable grouped rankings key accumulation by process lifetime and executable identity, using the supplied identity comparer. Earlier executable activity remains visible, while current memory and counts belong only to the newest executable. Individual process rankings continue to describe the lifetime, and the Windows ranking scratch path remains intact.

Usage accepts independent process inventories and counts available CPU, I/O and GPU measurements. UsageHistory separately records incomplete CPU coverage, so a usable inventory does not imply a complete period. Windows baselines and gaps remain excluded.

Usage lifetimes remember the actual executable key separately from their aggregate entry. The additive nullable UsageLifetimeState.ExecutableKey property preserves this identity through JSON snapshots without changing the existing constructor or Windows restart binary format. Older snapshots use the recorded app key when possible; an unknown overflow key establishes a baseline without counting another lifetime.

Landed in ProcessNetworkSession, ProcessRanking, UsageLedger and UsageHistory, with regressions in ProcessNetworkSessionTests and PortableAccountingRegressionTests. The implementation keeps executable splitting specific to portable grouped rankings; no new identity policy or persistence format version was needed.
