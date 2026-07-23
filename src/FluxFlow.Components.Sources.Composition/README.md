# FluxFlow.Components.Sources.Composition

Optional `FluxFlow.Composition` registrations and Designer metadata for the
canonical generated and sequence sources. Both types have no input, one
`FlowValue` Output, lifecycle Events, and no universal Errors port.

This package does not scan assemblies, resolve CLR types from strings, own
clock lifetime, persist output, or depend on Engine. Existing definitions using
`source.generated` remain supported as an explicit migration alias; new
definitions and Designer palettes use `source.items`.

## Registration

```csharp
services.AddKeyedSingleton<TimeProvider>(
    "Resources.System.Clock",
    timeProvider);

var registry = new CompositionNodeRegistry()
    .RegisterGeneratedSource()
    .RegisterSequenceSource();
```

| Type | Node | Output | Optional resource |
|------|------|--------|-------------------|
| `source.items` | `GeneratedSourceNode` | `FlowValue` | `clock` |
| `source.sequence` | `SequenceSourceNode` | `FlowValue` | `clock` |

The runtime starts and stops both nodes through `IFlowSource`. Invalid options
reject candidate activation. Missing generated `items` creates an empty source.

## Flat Document

```json
{
  "Resources": {
    "System": {
      "Clock": {
        "Type": "host.clock"
      }
    }
  },
  "Workflows": {
    "Main": {
      "Orders": {
        "Type": "source.items",
        "clock": "Resources.System.Clock",
        "items": [
          { "id": "A-100", "total": 125 },
          { "id": "A-101", "total": 250 }
        ],
        "Output": "Normalize.Input"
      },
      "Numbers": {
        "Type": "source.sequence",
        "start": 10,
        "step": 5,
        "count": 3,
        "Output": ["Audit.Input", "Aggregate.Input"]
      }
    }
  }
}
```

Component settings, resource references, and links are flat. `items` accepts
one JSON value directly or an array for multiple values. Strings, numbers,
booleans, nulls, arrays, and objects are decoded once into immutable
`FlowValue` data during activation.

Links remain statically typed. The runtime does not insert implicit mappers.

## Host-Owned Clock

`clock` is optional and resolves an exact canonical resource address such as
`Resources.System.Clock`. Without it, nodes use `TimeProvider.System`. The host
owns the selected service, its lifetime, and disposal.

## Migration From 2.x

Only fixed canonical registrations remain. Remove
`RegisterGeneratedSource<TOutput>(nodeType)` and
`RegisterSequenceItemSource(nodeType)` calls. Convert typed source values to
`FlowValue` before constructing a source or at an explicit application
boundary. The hidden `source.generated` configuration alias remains available
for document migration; newly persisted definitions use `source.items`.

## Design Metadata

`SourcesComponentDesignMetadataProvider` describes fixed `FlowValue` outputs,
flat generated-item and sequence options, and the optional host-owned clock
picker using the `Resources.{name}` address pattern. Hosts own palettes,
inspectors, validation UI, persistence, activation, and runtime status.
