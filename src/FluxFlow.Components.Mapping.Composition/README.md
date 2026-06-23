# FluxFlow.Components.Mapping.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone mapper
node from `FluxFlow.Components.Mapping`.

This package does not choose an expression language, scan assemblies, or resolve
CLR types from strings. Hosts register closed mapper node types explicitly and
provide keyed `IFlowExpressionEngine` services.

## Registration

```csharp
services.AddKeyedSingleton<IFlowExpressionEngine>("default", expressionEngine);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterMapper<InputMessage, OutputMessage>());
```

Use custom node type names when a host needs more than one mapper shape:

```csharp
registry
    .RegisterMapper<HttpResponseOutput, MqttPublishRequest>("flow.mapper.http-to-mqtt")
    .RegisterMapper<MqttReceivedMessage, HttpRequestInput>("flow.mapper.mqtt-to-http");
```

## Node Types

| Type | Node | Required resource | Ports |
|------|------|-------------------|-------|
| `flow.mapper` | `FlowMapperNode<TInput,TOutput>` | `engine` | `Input`, `Output`, `Failed` |

`contextFactory` is an optional keyed `IMappingContextFactory` resource for
custom expression variables. `clock` is an optional keyed `TimeProvider` resource
for deterministic diagnostics.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "map": {
              "type": "flow.mapper",
              "resources": {
                "engine": "default"
              },
              "configuration": {
                "expression": "input",
                "expressionName": "copy",
                "inputType": "app.input",
                "outputType": "app.output",
                "boundedCapacity": 128
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

`MapperOptions.InputType`, `OutputType`, and `targetType` remain diagnostic
metadata. The actual composition port types come from the closed generic
registration selected by the host.

## Design Metadata

`MappingComponentDesignMetadataProvider` exposes neutral Designer metadata for
the `flow.mapper` composition node. Hosts can add it to a
`ComponentDesignMetadataCatalog` to populate palettes, editors, validation
views, or generated documentation.

The provider describes editable options, host-owned resources, and ports. The
`engine` resource is required; `contextFactory` and `clock` are optional.
Resource metadata is descriptive only, so hosts still own registration,
selection, lifetime, and disposal of those keyed services.
