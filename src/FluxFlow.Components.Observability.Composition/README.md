# FluxFlow.Components.Observability.Composition

Optional JSON registrations and Designer metadata for Counter, Logger, and
Metrics.

Configuration-driven nodes use their `JsonElement` specializations and emit
typed snapshots or log entries on one Output plus Events. Errors are in-band.
Expression engines, context factories, selectors, attributes, and clocks are
host-owned keyed resources.

Metadata provides filtering, logging, metric, attribute, diagnostic, type, and
runtime hints. Resource key patterns support Designer pickers without changing
resource ownership or requiring Engine.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit ObservabilityComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddObservability();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.
