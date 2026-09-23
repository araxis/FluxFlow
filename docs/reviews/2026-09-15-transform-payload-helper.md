# Remove the payload-only node base

FlowValueTransformNode is deleted. It only adapted payload processing to the
existing FlowTransformNode runtime. Its job is now the protected overload
TransformAsync(FlowMessage, Func<FlowValue, CancellationToken, ValueTask<FlowValue>>).
There is no replacement node class or new execution engine.

A component inherits FlowTransformNode, overrides TransformAsync(message), and
can pass its ordinary payload method to that helper. Components that need the
envelope still implement the same override directly. Returning a null payload
is rejected by the existing message.With validation. The outer execution boundary
continues to convert errors to FlowError and forward incoming errors unchanged.
Native TransformBlock ordering, cancellation, diagnostics and lifetime are intact.

The prior payload-processing tests were migrated and renamed to
FlowTransformPayloadTests; lazy JSON tests also use the helper. No test cases were
removed. The standalone package consumer now implements its Increment component
with this helper, including local JSON conversion, errors and continued processing.

Breaking preview API: replace FlowValueTransformNode inheritance with
FlowTransformNode and the helper call. FlowNode/FlowTransformNode/FlowSource still
remain. This completes one hierarchy reduction, not the entire base-class plan.
Next review is the common ownership base for those remaining shapes; preserve
multi-output emission, ordered transformations and two-phase source shutdown.

Evidence: artifacts/value-transform-helper-20260915/. No commit, push or publication.

Qualification passed: 2,377 tests across seven reports, zero failed/skipped;
Release build zero warnings/errors. Fresh Core-only package consumers passed
net8.0/net10.0 with the migrated component. Public API baseline and whitespace
checks passed. See verification.json, tests/, build.log and consumer.log.
