# Native zero/one/many processing

The existing FlowTransformNode now uses TransformManyBlock rather than
TransformBlock. Components return zero, one or many messages using the platform's
ordered parallel processing. Gate returns an empty result when closed and the
original message when open. Open/Close remain ordinary command inputs, and port
registration remains outside the component.

The protected override changes from Task<FlowMessage> to
Task<IEnumerable<FlowMessage>>. Single results become [message]. The payload-only
helper remains, preserves lineage and produces a single result. CallFunction and
all current overrides, including the standalone package consumer, are migrated.

Finite result sequences are materialized inside processing. This catches iterator
exceptions and preserves processing context during lazy message creation. Failure
produces one error message with no partial batch. This is not an unbounded streaming
iterator API: batch memory grows with result count. Input and output remain bounded
according to native execution options and output capacity. The existing reliable
fanout is retained; native source links alone do not duplicate data to every target.

Four new tests per target framework cover zero/one/many fanout and identity,
lazy-enumeration failure/recovery, parallel batch ordering, and command-input error
draining/recovery. Existing tests cover cancellation, explicit faults, blocked
delivery, request/reply, tracing and Gate lifecycle.

Verification: Release build has zero warnings/errors; all 2,436 tests pass in nine
TRX reports. Fresh Core and Routing package consumers pass .NET 8 and .NET 10.
Evidence: artifacts/transform-many-20260918/. Public API baseline updated.

This improves ordinary component authoring but does not yet reduce the number of
bases. FlowNode and FlowControlInputs remain for existing explicit emission,
multi-output, flush and staged terminal-control behavior. The transform base gained
ordinary-input support; that overlap must be evaluated in the next bounded step,
not presented as completed consolidation. FlowTracing was not changed.
