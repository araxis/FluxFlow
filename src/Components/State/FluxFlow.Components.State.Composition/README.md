# FluxFlow.Components.State.Composition

Optional runtime registration and definition for keyed JSON state reduction.

The JsonStateReducerNode receives canonical FlowMessage values and materializes
its reducer input locally. Output carries results or errors; Events is diagnostic.
Reducer/key expressions, initial state and key limits stay flat. The expression
engine is required and host-owned; the host-owned clock is optional.

## DI Registration

This adapter registers direct node factories through `IServiceCollection`:

```csharp
services.AddState();
```

The resulting type-to-factory dictionary is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first definition

`AddStateReducer` retain typed options and named port handles. They embed the
same callbacks used by DI in ApplicationSetup.ComponentFactories. Build the setup
with application services or pass it to AddFluxFlowUpdates. Repeating the family
DI registration is optional: matching delegates are idempotent and conflicts fail.
Links validate the actual ports after node construction. Factories remain in process
and cannot be exported as JSON; the flat JSON configuration shape is unchanged.

The former StateComponents contract container is removed. Standalone node
construction remains available. Shared host resources are not disposed by components.
