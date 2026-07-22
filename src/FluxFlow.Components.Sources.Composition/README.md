# FluxFlow.Components.Sources.Composition

Composition registration and Designer metadata for canonical generated and
sequence `FlowValue` sources. Both node types have no input, one normal
`Output`, lifecycle `Events`, and no universal `Errors` port.

This package does not scan assemblies, resolve CLR types from strings, own
clock lifetime, add polling, persist output, or depend on Engine.

Existing definitions using `source.generated` remain supported as a hidden
alias; new definitions and Designer palettes use `source.items`.

## Canonical Registration

```csharp
services.AddKeyedSingleton<TimeProvider>(
    "Resources.System.Clock",
    timeProvider);

registry
    .RegisterGeneratedSource()
    .RegisterSequenceSource();
```

| Type | Node | Output | Optional resource |
|------|------|--------|-------------------|
| `source.items` | `FlowValueGeneratedSourceNode` | `FlowValue` | `clock` |
| `source.sequence` | `FlowValueSequenceSourceNode` | `FlowValue` | `clock` |

The composition runtime starts and stops both through `IFlowSource`. Invalid
options fail node activation. Missing generated `items` creates an empty source.

## Flat Definition

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
one JSON value directly or an array for multiple values. Ordinary JSON strings,
numbers, booleans, nulls, arrays, and objects are decoded once into immutable
`FlowValue` data at activation.

The sample output links are valid when the target inputs also accept
`FlowValue`. Exact payload types remain part of static link validation; the
runtime does not insert implicit mappers.

## Host-Owned Clock

`clock` is optional and resolves an exact keyed `TimeProvider` address. Without
it, the node uses `TimeProvider.System`. The host owns the selected service,
its lifetime, and disposal.

## Typed Compatibility

Code-authored hosts can retain released typed contracts explicitly:

```csharp
registry
    .RegisterGeneratedSource<OrderMessage>("source.items.order")
    .RegisterSequenceItemSource("source.sequence.item");
```

The generic registration emits `TOutput`; the sequence-item registration emits
`SourceSequenceItem`. Use distinct node type names when typed and canonical
registrations share a registry. Typed nodes retain their released error ports
and behavior.

## Design Metadata

Hosts should compose this provider through `ComponentDesignMetadataCatalog`.
The canonical catalog adds the traced `Events` output and an optional semantic
`processing` profile picker, and omits legacy `name`, `boundedCapacity`,
`maxDegreeOfParallelism`, and `ensureOrdered` options from normal editing.
Default execution requires no processing profile; raw provider metadata retains
released declarations for compatibility.


`SourcesComponentDesignMetadataProvider` describes canonical fixed `FlowValue`
outputs, option sections/editor hints, generated JSON items, and the optional
host-owned clock picker. The generic-only `outputType` diagnostic option is
explicitly omitted from canonical metadata.

The metadata is descriptive. Hosts own palettes, inspectors, validation UI,
resource selection, activation, persistence, and runtime status display.
