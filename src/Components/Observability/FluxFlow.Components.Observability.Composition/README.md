# FluxFlow.Components.Observability.Composition

Optional runtime registration and definition for Counter, Logger and Metrics.

Canonical FlowMessage ports carry values, snapshots, log entries and in-band errors.
JSON materialization and expression evaluation stay local to each component.
Expression engines, context factories, selectors and clocks remain host-owned
resources. Filtering, logging, metric and attribute settings stay flat.

## DI Registration

This adapter registers direct node factories through `IServiceCollection`:

```csharp
services.AddObservability();
```

The resulting type-to-factory dictionary is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first definition

`AddCounter`, `AddLogger`, `AddMetrics` retain typed options and named port handles. They embed the
same callbacks used by DI in ApplicationSetup.ComponentFactories. Build the setup
with application services or pass it to AddFluxFlowUpdates. Repeating the family
DI registration is optional: matching delegates are idempotent and conflicts fail.
Links validate the actual ports after node construction. Factories remain in process
and cannot be exported as JSON; the flat JSON configuration shape is unchanged.

The former ObservabilityComponents contract container is removed. Standalone node
construction remains available. Shared host resources are not disposed by components.
