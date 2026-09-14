# Validation And Failure Semantics

FluxFlow keeps definition, revision, normal-result, diagnostic-event, and fatal
completion failures separate. The surrounding application host must remain
available when a workflow component rejects data or a revision cannot activate.

## Setup Layers

| Layer | Surface | Meaning |
|-------|---------|---------|
| Parse | `ApplicationDefinitionJson` / configuration loader | Invalid canonical document shape or values. |
| Compile | `ApplicationLinkCompiler` | Unknown types/ports, addresses, cardinality, exact type, condition, and cycle diagnostics. |
| Plan | Engine revision planning | Changed resources/workflows, dependency impact, and invalid resource graphs. |
| Prepare/activate | `FluxFlowApplication` | Resource, component factory, descriptor, link activation, and revision failures. |

Caller cancellation remains cancellation. A source-load failure leaves the host
`Degraded` when no revision is active. A rejected update keeps the prior active
revision running.

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
