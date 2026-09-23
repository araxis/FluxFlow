# Validation And Failure Semantics

Ordinary composition validates the configuration and registered links before startup.
Configuration registrars reject malformed sections and link syntax with their location.
Build resolves ports and compiles conditions. FlowApplication composes native factories and links through
BuildApplicationAsync. There is no revision planning or update activation stage.

If Build fails, partial processing is stopped/drained and the host provider remains
responsible for disposal. Cancellation remains cancellation. Fix the configuration and
construct a new host/provider; failed Build does not restore a previous live application.

## Input Validation

Components own deserialization and domain validation. Read FlowValue.Json using
local System.Text.Json metadata, or reuse a compatible typed FlowValue directly.
No framework materializer interface or result wrapper is required. Runtime
processing failures retain their FlowError and original diagnostic exception.


## Runtime Channels

Canonical components follow one model:

| Channel | Contract | Use |
|---------|----------|-----|
| `Output` | `FlowMessage<T>` | A typed value or `FlowError` that workflow logic may handle. |
| `Events` | `FlowMessage` with a non-null canonical `FlowValue` | Lifecycle, diagnostics, observations, warnings, and metrics. |
| `Completion` | `Task` | Unrecoverable implementation, infrastructure, or lifecycle failure. |

There is no new universal `Errors` port. A validation rejection, HTTP failure,
storage miss, or protocol command failure that the workflow can inspect is a
normal result value. Links may condition on it:

```json
{
  "Type": "session.record",
  "Input": {
    "Port": "Validate.Output",
    "Condition": "payload.isError = true"
  }
}
```

Use a mapper when a downstream component requires a different result shape.
Do not fault component completion for expected per-message outcomes.

## Addressable Events

Every canonical registration exposes `Events` at
`Workflow.Component.Events`. Component events are bounded, fault-isolated,
correlated where source information exists, and carried in the normal traced
message envelope. They can feed logging, metrics, mapping, conditional links,
another workflow, or direct observation.

`System.Events.Output` is the reliable canonical stream for Engine application and
revision transitions. `System.Diagnostics.Output` is the best-effort canonical
diagnostic stream. `application.Ports.Activity` mirrors both and
aggregates component event outputs into one canonical, dynamically filterable
stream without coupling workflow execution to observers.

## Completion

Completion faults represent a condition the component cannot safely continue
through, such as a broken implementation invariant, unrecoverable external
infrastructure required for lifecycle, or a failed start/stop sequence. The
runtime observes those faults, coordinates shared-input completion, and still
attempts complete cleanup. Cleanup failures are aggregated without duplicating
the already observable runtime completion fault.

## Revision Example

```csharp
var application = services.GetRequiredService<FluxFlowApplication>();
var result = await application.ReloadAsync("deployment-43");

foreach (var diagnostic in result.Diagnostics)
{
    logger.LogWarning(
        "{Stage} {Code}: {Message}",
        diagnostic.Stage,
        diagnostic.Error.Code,
        diagnostic.Error.Message);
}
```

Semantically identical canonical definitions return an unchanged update without
preparing another runtime candidate. Obsolete aliases are rejected before
candidate preparation.

## Compatibility Boundary

Code-first graphs execute through `FlowApplication` in Core; there is no separate
composition runtime or aggregate `Errors` property. Observe native component
event sources, or `FluxFlowApplication.Events` when using the optional revision
host. Observation feeds are not business-error routes. Older numeric error,
event, diagnostic, state, and runtime-build models are removed. Migrate persisted
definitions explicitly and route normal business failures from standardized
Output values.

Next: [Runtime States](08-runtime-states.md)
