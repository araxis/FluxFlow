# Materializer exception details: bounded review

Status: recommendation only; no runtime or public contract changes in this step.

Follow-up: observability step 21 implemented the first slice below (result, JSON
base materializer and State). This document retains the original review; see the
observability plan for validation and the remaining consumers.

## Confirmed gap

`FlowValueMaterializationResult<T>` contains a successful value or `FlowError`.
Its constructors are private; callers use `Success(value)` or `Failure(error)`.
The interface deliberately returns invalid-input errors instead of throwing them.

The JSON base materializer and primitive content materializer in
`src/Core/FluxFlow.Core/Data/FlowValueMaterializers.cs`, plus HTTP and MQTT publish
request materializers, catch conversion exceptions and retain only their message
in error details. The original stack and inner exceptions are lost at that point.

State, HTTP, MQTT publish/control, Serialization and Payloads consume these results.
Their rejection branches have the originating message and component, but cannot
recover the discarded exception. Reporting a newly constructed exception there
would not restore the original evidence.

This does not apply to every rejection. For example, the primitive string
materializer rejects a non-string with a FlowError directly, without an exception.
Such outcomes must remain exception-free. Existing explicit throws inside parsing
(such as HTTP's non-object guard) are caught exceptions; preserve them without
creating additional ones.

## Options

| Approach | Consequence |
| --- | --- |
| Optional exception on the existing local result | Preserves the original object and domain error with a small additive API change. Reporting remains at the node boundary. |
| Throw instead of returning a failed result | Changes the documented contract and callers' rejection handling; can lose module-owned codes and details. |
| Pass message, component or diagnostic callback into every materializer | Couples conversion to execution/observability and expands constructors or method signatures. |
| Report directly in the converter | Lacks the originating message/component context; risks duplicate reports if callers also report. |

Recommend the first option. No new result class, registry, deserializer service,
execution context or materializer interface method is needed.

## Proposed smallest change

Keep `Failure(FlowError)` unchanged and add an overload accepting the caught
exception. Expose it as an optional `Exception` property on the local result, with
`System.Text.Json.Serialization.JsonIgnore`. Success and ordinary rejection results
have no exception. Preserve object identity; do not wrap or reconstruct it.

This result is a local conversion return value, not the workflow envelope. Do not
add an exception property to FlowError, FlowValue or FlowMessage. JsonIgnore would
exclude the property under System.Text.Json; it is not a guarantee for arbitrary
serializers, and the whole materialization result is not a transport contract.

Materializers would attach the exception they already catch. The consuming node
would call the existing FlowDiagnostics hook once when that property is present,
then emit the same FlowError as before. Materializers themselves would not report.
Forwarded workflow errors would not retain or re-report that exception.

Keep existing optional/custom materializers working: callers of the old factory
remain valid and simply provide no diagnostic exception. Component-specific JSON
options, converters and parsing ownership remain unchanged. Source-generated
serialization is a separate performance step; this proposal adds no serialization
round trip between components.

## Next implementation checkpoint

Limit the first slice to the result, the existing JSON base materializer and State's
rejection branch. Validate old factory/custom-materializer compatibility, exact
exception identity and inner/stack details, unchanged workflow error serialization,
one report with tracing enabled/disabled, observer-failure containment, successful
continuation and no exception for an ordinary rejection. Build both target frameworks.

Then migrate content, HTTP and MQTT materializers and their consumers in separate
small steps. Do not claim the gap is closed until those consumers are covered.

Only source inspection was performed for this review; no tests/builds were rerun.
Deferred/lifecycle diagnostics, full runtime metrics/exporter integration, phase 3
host/SSE and the user's recorded Sessions concern remain outside this checkpoint.
