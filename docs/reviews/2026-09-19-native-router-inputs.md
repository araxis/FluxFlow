# Native router inputs — 2026-09-19

FlowRouter previously received an application-bound FlowOutputEndpoint even though
execution only needed its native source, address, prepared links and discard policy.
It now receives those explicitly and retains no endpoint/application/workflow reference.
The fault callback remains the narrow channel for reporting a runtime failure.

Application's existing router dictionary is indexed by full output address using
ordinal comparison. Producer-completion dependencies carry that same address rather
than an endpoint object. Dependency ordering derives the component from the output
address, as it already did for target addresses.

No production class was added or deleted. Routing selection, bounded fan-out,
fallback, failure drain, source attachment and lifecycle algorithms remain unchanged.
The constructor has more explicit arguments; the benefit is a narrower dependency,
not fewer lines or a new component authoring model. Internal FlowOutputEndpoint still
stores route declarations, registration metadata and validation behavior. Its physical
removal is not complete and must not be described as complete.

Validation:

- Release solution build: zero warnings/errors.
- Full regression: 2,545 passed, zero failed/skipped, 29 TRX reports.
- Source declaration baseline updated: the scanner includes public constructors on
  internal classes. No exported API changed in this step.
- Fresh Core package `0.0.0-local.20260919213842` passed its package-only consumer on
  net8.0/net10.0, including native source identity, delivery/recovery, document and
  delegate links, and revision rollback.
- Routing/Configuration package consumers were not repeated; their source tests
  participated in the full regression. Previous package evidence remains historical.

Evidence and before-edit copies of both changed production files are in
`artifacts/native-router-inputs/`. No commit, push or publication occurred.

Next: simplify remaining workflow output storage and composition validation, retaining
output-local links and existing delivery/completion guarantees without a replacement
manager or parallel application-wide link table.
