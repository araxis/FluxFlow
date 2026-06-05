# FluxFlow.Components.Sources

Reusable deterministic source components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `source.generated` | `Output`, `Errors` | Emits configured JSON items as a registered output type. |
| `source.sequence` | `Output`, `Errors` | Emits deterministic numeric sequence items. |

`source.generated` uses host-registered type aliases so applications can emit
plain objects, JSON values, or application-owned records without this package
knowing those models.

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterSourcesComponents(options => options
        .RegisterType<AppMessage>("app.message"));
```

Hosts that need deterministic timing can provide a clock:

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterSourcesComponents(options => options
        .UseClock(sourceClock));
```

Generated source configuration:

```json
{
  "type": "source.generated",
  "outputType": "app.message",
  "items": [
    { "id": "A-100", "value": "alpha" },
    { "id": "A-101", "value": "beta" }
  ],
  "boundedCapacity": 32
}
```

Sequence source configuration:

```json
{
  "type": "source.sequence",
  "name": "demo",
  "start": 10,
  "step": 5,
  "count": 3
}
```

Timing options are deliberately simple:

- `initialDelayMilliseconds`
- `intervalMilliseconds`
- `maxItems` and `loop` for generated lists

`UseClock(...)` controls delay scheduling and source timestamps. Without it,
sources use the system clock.

Generic replay is intentionally left out of this package for now. Use the
sessions package for stored session replay, and add a dedicated replay source
only when a second neutral replay shape is proven.

## Design Metadata

This package exposes a package-owned `IComponentDesignMetadataProvider` for its
node types. Hosts can compose it through `ComponentDesignMetadataCatalog` to
populate palettes, editors, validation views, and documentation without
duplicating package descriptors.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
