# Internal diagnostic publication simplification

Runtime diagnostic producers now pass their existing FlowEvent directly to an
internal ApplicationRuntimeSignals overload. It creates the canonical FlowMessage
once and reuses a normalized measurement snapshot for instrumentation. The former
immediate ToFlowEvent decode is unnecessary on this path.

The external-message overload still performs full canonical decoding/validation.
Both paths share the original publication routine: event observer, stopped/bounded
admission, independent instrumentation channels, then activity publication. No new
runtime envelope, subscription mechanism, queue, service registration or public API
was introduced. Activity, request completion and internal system-delivery failures
use the typed path; rejection publication remains unchanged.

The snapshot matters: a caller-owned measurement dictionary can be mutated by the
event observer. Instrumentation must retain the same accepted values that were
encoded before the callback. Event names are normalized identically to the wire
representation. No per-branch cloning is introduced; observers and streams still
receive the same canonical message instance.

## Verification

- 50 focused observer tests pass, including twelve net new cases.
- Full non-incremental Release build: zero warnings and errors.
- Local regression: 4421 passed, zero failed/skipped, 79 reports.
- Solution discovery: 4631, up from 4619. Concurrent output joined two names on
  one line; the initial line-count aggregation failed at 4630. Raw prefix counting
  verifies the complete list. Original logs and the failed aggregation are retained.
- All 1481 qualification source inputs remained unchanged during the commands.
- Test behavior/mutation review: .testagent/typed-diagnostics-review.md.
- Evidence: artifacts/core-refactoring/typed-diagnostics/verification.json.

## Isolated allocation measurement

Eight fresh processes, four alternating-order repeats per framework, compare the
old canonical-message entry sequence and the typed entry on identical current
binaries. Each scenario uses 10000 warmups and 100000 measured calls. Both paths
publish the same activity or request event into a stopped runtime, deliberately
excluding asynchronous queue work. No listeners or logger are enabled.

All sixteen scenario pairs allocate less with the typed entry. Activity savings
are about 1120 bytes/call on net8 and 1017–1217 on net10. Request savings are about
1064 bytes/call on net8 and 1014–1139 on net10. Allocation variation is retained in
the raw results; no elapsed-time or workflow throughput claim is made.

The artifact-only probe initially had a net8 diagnostic-assembly reference conflict.
It was corrected by explicitly referencing the same existing runtime dependency;
the final probe build has zero warnings/errors. No packages were downloaded and
no project references or production dependency changes were needed.

Artifacts: typed-diagnostics/probe, measure.ps1, allocation/comparison.json and raw
per-process reports. Binary hashes are verified against the built Engine outputs.

## Remaining scope

Matched workflow effects are now verified separately in
docs/reviews/2026-09-11-diagnostic-workflow-measurement.md: all 48 runs pass and all
sixteen timed pairs improve allocation and throughput. This is not final framework
or release acceptance. The broader
telemetry, cancellation/revision, retained-state, capability and final-package
qualification gates remain open. The earlier local candidate predates this change
and must be refreshed after final qualification. Nothing has been published.
