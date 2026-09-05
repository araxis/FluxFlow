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

## Sample Input Preflight

Runtime descriptors expose the destination materializer's declared input shape.
For a selected descriptor and sample value:

```csharp
var result = descriptor.ValidateInputShape("Input", sample);
if (result is null)
{
    // No shape was declared; this sample has not been checked.
}
else if (!result.IsValid)
{
    Console.WriteLine(result.Error);
}
```

This check runs without component activation and reports top-level kind or
required-property mismatches. Success is structural only. Runtime materialization
and domain validation still belong to the destination component. Unknown output
shapes do not make a canonical graph link invalid. See
[Flow Data Contracts](20-flow-data-contracts.md#discoverable-input-shapes).

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

Code-first `ApplicationRuntime.Errors` remains an aggregate observation surface
for fluent graphs, but it is not a canonical component port. Older Engine
numeric error, event, diagnostic, state, and runtime-build models were removed
in Engine version 3. Migrate persisted definitions explicitly and route normal
business failures from standardized Output values.

Next: [Runtime States](08-runtime-states.md)
