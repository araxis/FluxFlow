# FluxFlow.Components.Validation.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone JSON
schema validator node from `FluxFlow.Components.Validation`.

This package does not scan assemblies, resolve CLR types from strings, watch
schema files, or own schema resources. Hosts register closed validator node
types explicitly and provide any optional keyed selector or clock services.

## Registration

```csharp
services.AddKeyedSingleton<IJsonSchemaValueSelector<OrderMessage>>(
    "payload",
    new OrderPayloadSelector());

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry =>
        registry.RegisterJsonSchemaValidator<OrderMessage>());
```

Use custom node type names when a host needs more than one input shape:

```csharp
registry
    .RegisterJsonSchemaValidator<OrderMessage>("json.schema-validator.order")
    .RegisterJsonSchemaValidator<HttpMessage>("json.schema-validator.http");
```

## Node Types

| Type | Node | Optional resources | Ports |
|------|------|--------------------|-------|
| `json.schema-validator` | `JsonSchemaValidatorNode<TInput>` | `selector`, `clock` | `Input`, `Output`, `Valid`, `Invalid` |

`Output` emits `JsonSchemaValidationResult<TInput>`. `Valid` and `Invalid`
emit the original `TInput` message with the input correlation id.

`selector` is an optional keyed `IJsonSchemaValueSelector<TInput>` resource for
selecting the value to validate. `clock` is an optional keyed `TimeProvider`
resource for deterministic result and diagnostic timestamps.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "validate-order": {
              "type": "json.schema-validator",
              "resources": {
                "selector": "payload",
                "clock": "fixed"
              },
              "configuration": {
                "schema": {
                  "type": "object",
                  "required": [ "id" ],
                  "properties": {
                    "id": { "type": "string" }
                  }
                },
                "schemaId": "orders",
                "valueSelector": "payload",
                "inputType": "app.order",
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

`schemaPath` is also supported and is read during composition build. The node
does not perform file I/O or schema compilation in its message pump.

`JsonSchemaValidatorOptions.InputType` remains diagnostic metadata. The actual
composition port type comes from the closed generic registration selected by the
host.

## Design Metadata

`ValidationComponentDesignMetadataProvider` exposes neutral Designer metadata for
the `json.schema-validator` composition node. Hosts can add it to a
`ComponentDesignMetadataCatalog` to populate palettes, editors, validation
views, or generated documentation.

The provider describes node options and ports only. Runtime resources such as
`selector` and `clock` remain host-owned composition resources.
