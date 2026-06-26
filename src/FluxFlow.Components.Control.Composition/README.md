# FluxFlow.Components.Control.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone control
nodes from `FluxFlow.Components.Control`.

This package does not choose an expression language, scan assemblies, or resolve
CLR types from strings. Hosts register closed control node types explicitly and
provide keyed `IFlowExpressionEngine` services.

## Registration

```csharp
services.AddKeyedSingleton<IFlowExpressionEngine>("default", expressionEngine);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterFilter<OrderMessage>()
        .RegisterWhen<OrderMessage>());
```

Use custom node type names when a host needs more than one input shape:

```csharp
registry
    .RegisterFilter<OrderMessage>("flow.filter.order")
    .RegisterWhen<HttpResponseOutput>("flow.when.http-response");
```

## Node Types

| Type | Node | Required resource | Ports |
|------|------|-------------------|-------|
| `flow.filter` | `FilterNode<TInput>` | `engine` | `Input`, `Output` |
| `flow.when` | `WhenNode<TInput>` | `engine` | `Input`, `WhenTrue`, `WhenFalse`, `Output` |

`contextFactory` is an optional keyed `IFlowMapContextFactory<TInput>` resource
for custom expression variables. `clock` is an optional keyed `TimeProvider`
resource for deterministic diagnostics.

For `flow.when`, `Output` is an alias for the node's primary `WhenTrue` stream.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "important": {
              "type": "flow.filter",
              "resources": {
                "engine": "default"
              },
              "configuration": {
                "expression": "input.Priority == \"High\"",
                "expressionName": "important-orders",
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

`ControlExpressionOptions.InputType` remains diagnostic metadata. The actual
composition port type comes from the closed generic registration selected by the
host.
Invalid `ControlExpressionOptions`, such as a missing expression, blank
`inputType`, or non-positive `boundedCapacity`, fail during composition build
and surface as factory diagnostics when build failures are configured as
diagnostics.

## Design Metadata

`ControlComponentDesignMetadataProvider` exposes neutral Designer metadata for
the `flow.filter` and `flow.when` composition nodes. Hosts can add it to a
`ComponentDesignMetadataCatalog` to populate palettes, editors, validation
views, or generated documentation.

The provider describes editable options, host-owned resources, and ports.
`engine` is required; `contextFactory` and `clock` are optional. Resource
metadata is descriptive only, so hosts still own keyed service registration,
selection, lifetime, and disposal.
