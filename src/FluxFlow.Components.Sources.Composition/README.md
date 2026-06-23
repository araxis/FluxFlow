# FluxFlow.Components.Sources.Composition

Optional `FluxFlow.Composition` registration helpers for standalone generated
and sequence source nodes from `FluxFlow.Components.Sources`.

This package does not scan assemblies, resolve CLR types from strings, add hot
reload behavior, or resolve generated items from resources. Hosts register
closed generated source output types explicitly and provide optional keyed
`TimeProvider` services.

## Registration

```csharp
services.AddKeyedSingleton<TimeProvider>("fixed", timeProvider);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterGeneratedSource<OrderMessage>()
        .RegisterSequenceSource());
```

Use custom node type names when a host needs more than one generated output
shape:

```csharp
registry
    .RegisterGeneratedSource<OrderMessage>("source.generated.order")
    .RegisterGeneratedSource<HttpMessage>("source.generated.http");
```

## Node Types

| Type | Node | Optional resource | Ports |
|------|------|-------------------|-------|
| `source.generated` | `GeneratedSourceNode<TOutput>` | `clock` | `Output` |
| `source.sequence` | `SequenceSourceNode` | `clock` | `Output` |

The composition runtime starts both sources through the normal `IFlowSource`
lifecycle. `source.generated` deserializes inline `items` from node
configuration into the closed generic output type registered by the host.

## Design Metadata

`SourcesComponentDesignMetadataProvider` exposes neutral Designer metadata for
the generated and sequence source composition nodes. Hosts can add it to a
`ComponentDesignMetadataCatalog` to populate palettes, editors, validation
views, or generated documentation.

The provider describes node options and ports only. Inline generated `items`
are node configuration and are exposed as JSON metadata. The optional `clock`
resource remains a host-owned composition resource and is not exposed as an
editable node option.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "orders": {
              "type": "source.generated",
              "resources": {
                "clock": "fixed"
              },
              "configuration": {
                "name": "orders",
                "outputType": "app.order",
                "items": [
                  { "id": "A-100", "total": 125 },
                  { "id": "A-101", "total": 250 }
                ],
                "boundedCapacity": 128
              }
            },
            "numbers": {
              "type": "source.sequence",
              "configuration": {
                "name": "numbers",
                "start": 10,
                "step": 5,
                "count": 3
              }
            }
          },
          "links": []
        }
      }
    }
  }
}
```

Missing generated `items` bind as an empty source. `GeneratedSourceOptions.OutputType`
remains diagnostic metadata; the actual output port type comes from the closed
generic registration selected by the host.
