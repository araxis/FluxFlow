# Validation And Failure Semantics

FluxFlow keeps definition, revision, normal-result, diagnostic-event, and fatal
completion failures separate. The surrounding application host must remain
available when a workflow component rejects data or a revision cannot activate.

## Setup Layers

| Layer | Surface | Meaning |
|-------|---------|---------|
| Parse | `ApplicationDefinitionJson` / configuration loader | Invalid canonical document shape or values. |
| Normalize | `ApplicationDefinitionNormalizer` | Canonical definition plus structured alias migration diagnostics. |
| Compile | `ApplicationLinkCompiler` | Unknown types/ports, addresses, cardinality, exact type, condition, and cycle diagnostics. |
| Plan | `ApplicationRevisionPlanner` | Changed resources/workflows, dependency impact, and invalid resource graphs. |
| Prepare/activate | `IApplicationRevisionHost` | Resource, component factory, descriptor, link activation, and revision failures. |

Caller cancellation remains cancellation. A source-load failure leaves the host
`Degraded` when no revision is active. A rejected update keeps the prior active
revision running.

## Runtime Channels

Canonical components follow one model:

| Channel | Contract | Use |
|---------|----------|-----|
| `Output` | Usually `FlowMessage<FlowResult<T>>` | Success and expected failure values that workflow logic may handle. |
| `Events` | `FlowMessage<CompositionComponentEvent>` | Lifecycle, diagnostics, observations, warnings, and metrics. |
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

`System.Events.Output` is separate. It carries Engine application and revision
events; component events are not duplicated into it. `System.Diagnostics.Output`
remains the Engine best-effort diagnostic stream.

## Completion

Completion faults represent a condition the component cannot safely continue
through, such as a broken implementation invariant, unrecoverable external
infrastructure required for lifecycle, or a failed start/stop sequence. The
runtime observes those faults, coordinates shared-input completion, and still
attempts complete cleanup. Cleanup failures are aggregated without duplicating
the already observable runtime completion fault.

## Revision Example

```csharp
var host = services.GetRequiredService<IApplicationRevisionHost>();
var result = await host.ReloadAsync("deployment-43");

foreach (var migration in result.Update?.NormalizationDiagnostics ?? [])
    logger.LogInformation("{Code}: {Message}", migration.Code, migration.Message);

if (result.Error is not null)
    logger.LogError("{Code}: {Message}", result.Error.Code, result.Error.Message);

foreach (var failure in result.Update?.Failures ?? [])
    logger.LogWarning("{Code}: {Message}", failure.Error.Code, failure.Error.Message);
```

Alias-only changes normalize to the active canonical definition and return an
unchanged update without preparing another runtime candidate.

## Legacy Compatibility

Obsolete `CompositionRuntime.Errors`, node `Errors` streams, typed compatibility
registrations, and older Engine error models remain available for existing
consumers. New canonical guidance should not project those surfaces as a
universal component contract.

Next: [Runtime States](08-runtime-states.md)
