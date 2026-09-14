# FluxFlow.Components.Projections.Composition

Optional runtime registration and definition for event.project.

Canonical FlowMessage input is materialized as a projection event inside the
component. Output carries snapshots or errors; Events carries diagnostics. Filter,
rate, emission, preview and runtime options remain flat. The optional clock is
host-owned, and ordinary link conditions select output outcomes.

## DI Registration

This adapter registers direct node factories through `IServiceCollection`:

```csharp
services.AddProjections();
```

The resulting type-to-factory dictionary is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first definition

`AddEventProjection` retain typed options and named port handles. They embed the
same callbacks used by DI in ApplicationSetup.ComponentFactories. Build the setup
with application services or pass it to AddFluxFlowUpdates. Repeating the family
DI registration is optional: matching delegates are idempotent and conflicts fail.
Links validate the actual ports after node construction. Factories remain in process
and cannot be exported as JSON; the flat JSON configuration shape is unchanged.

The former ProjectionsComponents contract container is removed. Standalone node
construction remains available. Shared host resources are not disposed by components.
