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

## Code-first authoring

`ObservabilityComponents` exposes `Counter`, `Logger`, and `Metrics` typed contracts. The retained `AddX` methods use those same contracts, and every handle exposes `Events`. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from these contracts retains its executable descriptors.
Normal code-first hosting therefore calls only `AddFluxFlow(definition)` and
does not repeat the family registration above. Use that service registration
for JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contracts.
