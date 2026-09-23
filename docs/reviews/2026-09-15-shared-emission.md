# Shared output emission and base-class review

FlowNode and FlowTransformNode already share processing ownership through
FlowProcessingLifetime. FlowSource has separate production and full completion:
downstream feedback can still use terminal controls after production drains.
FlowNode permits zero or many emissions and final flushing; FlowTransformNode
uses native TransformBlock ordering for concurrent one-result processing.

Merging these public bases immediately would require execution-mode switches or
factory/initialization hooks. This review does not treat those extra concepts as
a simplification. A smaller, concrete duplication was removed first: required
output acceptance now belongs to FlowOutput itself, through an internal method.
Source and processing wrappers share acceptance, cancellation and fault handling.
FlowSource.ThrowOutputUnavailableAsync and FlowProcessingLifetime.EmitAsync are
removed. No new class, public helper or component configuration is introduced.

The same output SendAsync path applies backpressure. A faulted output rethrows its
original completion failure; an ordinarily closed output retains the existing
source/node exception message. Additional-output null checks remain asynchronous.
Source start/stop phases, output ownership, tracing and transform ordering are
unchanged. Three public bases still remain; the common-base proof is pending.

The source-declaration baseline changes because it also records public members
of internal classes and async implementation modifiers. This change removes an
internal lifetime helper; it does not break the externally callable API.

Evidence is in artifacts/shared-emission-20260915/. No commit, push, publication,
external service run or new package consumer qualification was performed.

Final qualification: all 2,375 tests passed in seven reports, with no removed or
added test cases. Release build has zero warnings/errors; baseline and whitespace
checks passed. The initial run's baseline mismatch is retained separately; final
counts refer only to qualified-tests/ and verification.json.
