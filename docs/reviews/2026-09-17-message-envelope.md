# Message generation and lineage

Component authors now create an initial envelope with FlowMessage.Create(value)
and generate a related output with input.WithValue(result). Both operations have
typed overloads constrained to FlowValue. A generated output receives a new
MessageId and timestamp, retains TraceId/CorrelationId and inherited headers,
and uses the input MessageId as CausationId. Passing the same message through a
Gate still preserves its identity. CallFunction can explicitly retain the inner
reply's causation while restoring the outer exchange correlation.

Removed public operations: With, CreateError, WithError, Restore, RestoreError,
ConvertValue and ConvertError, including their generic variants. Removed internal
forwarding layers: CreateInitial, CreateDerived and Rehydrate. JSON deserialization
and runtime enrichment use one internal validated constructor; typed readers use
an internal typed-envelope constructor. Request/reply and event enrichment no
longer need separate value/error reconstruction branches. Read helpers remain in
FlowMessageExtensions. No public compatibility shim, context class or factory layer
was added.

## Error semantics and compatibility

FlowError is now the error discriminator: Create(error) and WithValue(error)
produce errors, including when statically typed as FlowValue. The previous ability
to carry the exact same FlowError instance as an ordinary success depending on the
factory is intentionally replaced. To process an error document as ordinary data,
use JsonFlowValue(error.Json) or an appropriate business FlowValue wrapper.
FlowMessage<FlowError>.Value exposes the actual error; reading an error through an
incompatible success type still throws. Typed receive APIs can still return error
envelopes without invoking a business handler.

The existing JSON shape and stored value/error flags remain readable and writable.
An ordinary error-shaped JSON document remains JsonFlowValue on untyped read.
Projecting such a success document specifically to FlowError now classifies it as
an error, consistent with the new rule. Headers remain immutable string/string;
structured headers and output capture settings were not implemented.

This is a breaking preview API change: callers must migrate and rebuild. Component
examples and all current source callers were migrated. Test-only TestMessages
fixtures construct persisted identities and expected typed projections; they are
not shipped. The Nodes test assembly can inspect the internal constructor, like
the existing Core and Engine test assemblies. Public API baseline updated.

## Size and validation

- FlowMessage.cs: 316 to 90 lines.
- FlowMessageOfT.cs: 45 to 21 lines.
- FlowMessageExtensions.cs: 76 to 41 lines.
- Combined: 437 to 152 lines; converter also falls from 178 to 161 lines.
- Release solution build: zero warnings/errors.
- Full regression: 2,415 passed in nine TRX reports, no failures/skips.
- Three added lineage/error-recovery/lazy-payload tests run on both frameworks.
- Existing request/reply, nested calls, tracing, JSON, errors and fanout tests pass.
- Fresh Core-only and Routing package consumers pass .NET 8 and .NET 10. Core
  consumer additionally checks generated output identity, input cause, retained
  correlation, payload identity without JSON materialization, and error round-trip.

The initial regression exposed the old ordinary-FlowError assertion; the test now
checks both unified error classification and ordinary error documents carried as
JsonFlowValue. No tests were removed. Evidence: artifacts/message-envelope-20260917/.
No commit, push or publication.
