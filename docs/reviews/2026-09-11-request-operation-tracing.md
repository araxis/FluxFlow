# Request operation tracing checkpoint

The canonical request exchange now emits one optional `flow.request` activity from
accepted registration through reply/admission cleanup. It reuses the existing Core
activity source and scope helper. There is no additional request engine, registry,
public API, exporter or component-construction abstraction.

The actual caller supplies the parent when available; immutable message creation
context supplies the fallback and causal link. Only the existing exchange envelope
receives the new carrier. Original message and payload ownership remain unchanged.
Disabled tracing preserves the existing header reference. Distinct exchanges retain
independent spans and reply correlations even under one trace.

Received replies, deadline expiry, cancellation and failure have distinct outcomes.
Timeout/failure records error status and exception type, not exception content.
A returned error envelope is still a received reply. Rejected requests do not start
spans. Sampling/start/stop callback exceptions cannot replace the business outcome.
The contract is documented in docs/processing-trace-context.md.

## Qualification

The fresh nonincremental Release build has zero warnings/errors. All 4,467 selected
local tests pass (79 reports, zero failed/skipped). Discovery finds 4,677 cases,
including 46 new request-tracing cases across the two supported frameworks; 1,482
qualification inputs remain stable. The final focused run passed 64 cases.
Detailed requirement-to-test mappings and assertion/pseudo-mutation review are in
.testagent/request-tracing-review.md. Raw command/TRX evidence is under
artifacts/core-refactoring/request-tracing, with verification.json authoritative.

This is library-side instrumentation; SDK/exporter selection remains host-owned,
consistent with the standard [instrumentation guidance](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs).
The telemetry skill was applied to the operation boundary, not to install host
collection infrastructure inside Core.

## Remaining acceptance

The local run excludes external/container/acceptance and database integration suites.
It does not qualify all custom/protocol callers, source/fan-in/transport/durable or
revision boundaries, discovery-callback failures, synchronous callback latency or
enabled-observation overhead. Previous listener-free measurements predate this Core
change and are not current-tree performance acceptance. The local package candidate
also predates these changes and must be refreshed after final qualification.

The full simplification goal remains active. No publishing or external consumer
coordination was performed.
