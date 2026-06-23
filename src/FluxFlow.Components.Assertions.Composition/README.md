# FluxFlow.Components.Assertions.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone
assertion node from `FluxFlow.Components.Assertions`.

This package does not choose an expression language, scan assemblies, or resolve
CLR types from strings. Hosts register closed assertion node types explicitly
and provide keyed `IFlowExpressionEngine` services.

## Registration

```csharp
services.AddKeyedSingleton<IFlowExpressionEngine>("default", expressionEngine);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry =>
        registry.RegisterAssertion<OrderMessage>());
```

Use custom node type names when a host needs more than one input shape:

```csharp
registry
    .RegisterAssertion<OrderMessage>("flow.assert.order")
    .RegisterAssertion<HttpResponseOutput>("flow.assert.http-response");
```

## Node Types

| Type | Node | Required resource | Ports |
|------|------|-------------------|-------|
| `flow.assert` | `FlowAssertionComponent<TInput>` | `engine` | `Input`, `Output`, `Passed`, `Failed` |

`Output` emits `FlowAssertionResult`. `Passed` and `Failed` emit the original
`TInput` message when the assertion options allow routed inputs.

`contextFactory` is an optional keyed `IFlowMapContextFactory<TInput>` resource
for custom expression variables. `clock` is an optional keyed `TimeProvider`
resource for deterministic result and diagnostic timestamps.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "score-check": {
              "type": "flow.assert",
              "resources": {
                "engine": "default"
              },
              "configuration": {
                "expression": "input.Score >= 10",
                "description": "score-check",
                "failureMessage": "Score too low.",
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

`AssertionOptions.InputType` remains diagnostic metadata. The actual composition
port type comes from the closed generic registration selected by the host.

## Design Metadata

`AssertionsComponentDesignMetadataProvider` exposes neutral Designer metadata for
the `flow.assert` composition node. Hosts can add it to a
`ComponentDesignMetadataCatalog` to populate palettes, editors, validation
views, or generated documentation.

The provider describes node options and ports only. Runtime resources such as
`engine`, `contextFactory`, and `clock` remain host-owned composition resources.
