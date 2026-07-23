# FluxFlow.Components.State.Composition

Composition registration and Designer metadata for the canonical
`state.reduce` component. It consumes typed commands containing immutable
`FlowValue` data and emits one normal `FlowResult<T>` output.

Existing definitions using `state.reducer` remain supported as a hidden alias;
new definitions and Designer palettes use `state.reduce`.

This package does not choose an expression language, scan assemblies, resolve
CLR types from strings, or own expression-engine and clock resources.

## Registration

```csharp
services
    .AddFluxFlowApplication(configuration)
    .UseRuntimeAssembler(runtime => runtime
        .RegisterNodes(registry => registry.RegisterStateReducer())
        .ConfigureServices(context => context.Services
            .AddExternalFluxFlowResource<IFlowExpressionEngine>(
                ApplicationAddress.Parse("Resources.State.Engine"),
                expressionEngine)));
```

| Type | Required resources | Input | Output |
|------|--------------------|-------|--------|
| `state.reduce` | `engine` | `FlowValueStateReducerInput` | `FlowResult<FlowValueStateReducerResult>` |

`clock` is an optional keyed `TimeProvider`. The fixed descriptor exposes
Events and no universal Errors port. Expected operation failures remain normal
result data on Output.

## Flat Definition

```json
{
  "Resources": {
    "State": {
      "Engine": {
        "Type": "host.expression-engine"
      },
      "Clock": {
        "Type": "host.clock"
      }
    }
  },
  "Workflows": {
    "Orders": {
      "TrackState": {
        "Type": "state.reduce",
        "engine": "Resources.State.Engine",
        "clock": "Resources.State.Clock",
        "reducer": "increment-count",
        "keyExpression": "order-customer-key",
        "initialState": {
          "count": 0
        },
        "maxKeys": 1024,
        "Input": "Normalize.Output"
      }
    }
  }
}
```

Component settings, resource references, and port links are flat. Resource
addresses and component/port names are exact and case-sensitive. The factory
decodes ordinary JSON `initialState` values into immutable `FlowValue`; workflow
authors do not write the tagged canonical serialization format.

The `engine` property is the required resource reference used for DI selection;
it is not duplicated as diagnostic metadata. Missing resources and invalid
options fail activation with preparation diagnostics.

## Design Metadata

Hosts should compose this provider through `ComponentDesignMetadataCatalog`.
The canonical catalog adds the traced `Events` output and an optional semantic
`processing` profile picker, and omits legacy `name`, `boundedCapacity`,
`maxDegreeOfParallelism`, and `ensureOrdered` options from normal editing.
Default execution requires no processing profile; raw provider metadata retains
released declarations for compatibility.

`StateComponentDesignMetadataProvider` describes canonical command/result
ports, reducer option sections and editor hints, and host-owned expression-engine
and clock pickers using exact `Resources.{name}` addresses. The metadata is
descriptive only. Hosts own palette and inspector rendering, resource selection,
validation UI, activation, and persistence.

Composition `3.x` registers only the canonical fixed contract. Existing
`state.reducer` type names remain accepted as a hidden load-time alias and are
normalized to `state.reduce` when saved.
