# Runtime Lifecycle

The canonical hosted lifecycle is exposed by `IApplicationRevisionHost`:

| State | Meaning |
|-------|---------|
| `Empty` | No source load or revision attempt has completed. |
| `Starting` | The initial complete definition is loading. |
| `Running` | A valid application is active; rejected reloads preserve it. |
| `Degraded` | No valid application is active after a source or revision failure. |
| `Stopped` | The active candidate drained and disposed. |
| `Disposed` | The host itself is disposed. |

`StartApplicationAsync` loads the configured complete definition. `ReloadAsync`
loads another complete definition from the source, and `ApplyAsync` accepts one
directly. The host normalizes before planning. Activation publishes one
immutable current snapshot before draining the old candidate.

## Preparation

The standard runtime assembler:

1. Compiles canonical links.
2. Creates a resource-revision service snapshot, including processing profiles.
3. Invokes alias-aware component registrations with canonical
   `ComponentDefinition` factory contexts.
4. Validates descriptor ports, including the reserved `Events` output.
5. Creates workflow-revision views and a stable port revision.
6. Starts the candidate only after preparation succeeds.

Preparation failure disposes every allocated component, link, generation, and
provider snapshot. A prior active revision remains active.

## Runtime Observation

Use canonical stable addresses through `IApplicationRuntimeAccess`:

```csharp
var ports = provider.GetRequiredService<IApplicationRuntimeAccess>()
    .GetRequiredPorts();

var input = ApplicationAddress.Parse("Orders.Validate.Input");
var output = ApplicationAddress.Parse("Orders.Validate.Output");
var events = ApplicationAddress.Parse("Orders.Validate.Events");
```

Direct output observation is broadcast and does not steal workflow delivery.
Expected failures remain normal output values. Component `Events` provide
traced component diagnostics, while `System.Events.Output` provides application
and revision events. `System.Diagnostics.Output` is the best-effort Engine
diagnostic stream. Component completion faults represent unrecoverable failure.

## Stop And Disposal

Stopping drains the active candidate according to its lifecycle contract and
then disposes it. Disposal remains idempotent and attempts every cleanup step.
Multiple cleanup failures are aggregated. A cleanup error is not treated as a
normal workflow result, and an existing completion fault is not duplicated as
a disposal error.

## Host Pattern

For operational views:

1. Show `IApplicationRevisionHost.State` and the current revision snapshot.
2. Show source, normalization, planning, preparation, and activation results
   separately.
3. Observe component `Events` for component-level activity.
4. Observe system events and diagnostics for application/runtime activity.
5. Treat expected `FlowResult<T>` failures as workflow data.
6. Treat completion faults as incidents requiring host/operator attention.

## Obsolete Compatibility Runtime

`CompositionRuntime`, `ICompositionRuntimeHost`, and related
`CompositionDefinition` APIs remain available for existing applications. Their
aggregate `Events` and `Errors` streams are compatibility surfaces, not the
canonical per-component failure model. New applications should use
`AddFluxFlowApplication(...)`, the revision host, canonical addresses, and the
runtime assembler.

Next: [JSON Conversion](09-json-conversion.md)
