# FluxFlow.Components.Routing.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone routing
nodes from `FluxFlow.Components.Routing`.

This package does not choose an expression language, scan assemblies, resolve
CLR types from strings, or create selector resources. Hosts register closed
routing node types explicitly and provide keyed selector delegates where a node
needs routing logic.

## Registration

```csharp
services.AddKeyedSingleton<Func<OrderMessage, string?>>(
    "order-route",
    order => order.Kind);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterSwitch<OrderMessage>()
        .RegisterFork<OrderMessage>()
        .RegisterMerge<OrderMessage>()
        .RegisterWindow<OrderMessage>()
        .RegisterCorrelation<OrderMessage>()
        .RegisterJoin<RequestMessage, ResponseMessage>());
```

Use custom node type names when a host needs more than one input shape:

```csharp
registry
    .RegisterSwitch<OrderMessage>("flow.switch.order")
    .RegisterJoin<RequestMessage, ResponseMessage>("flow.join.requests");
```

## Node Types

| Type | Node | Required resources | Ports |
|------|------|--------------------|-------|
| `flow.switch` | `FlowSwitchNode<TInput>` | `routeKeySelector` | `Input`, `Output`, `Matched`, `Default`, optional `Routed`, configured route-output ports |
| `flow.fork` | `FlowForkNode<TInput>` | none | `Input`, `Output`, configured output ports |
| `flow.merge` | `FlowMergeNode<TInput>` | none | `Input`, `Output` |
| `flow.window` | `FlowWindowNode<TInput>` | none | `Input`, `Output` |
| `flow.correlation` | `FlowCorrelationNode<TInput>` | `keySelector`, `sideSelector` | `Input`, `Output`, `Matched`, `Timeouts` |
| `flow.join` | `FlowJoinNode<TLeft,TRight>` | `leftKeySelector`, `rightKeySelector` | `Left`, `Right`, `Output`, `Timeouts` |

`clock` is an optional keyed `TimeProvider` resource for deterministic timing
and diagnostics. Selector delegates are required keyed resources for switch,
correlation, and join nodes. Option fields such as `Engine`, `InputType`,
`LeftInputType`, and `RightInputType` remain diagnostic/config metadata; the CLR
port types come from the closed generic registration selected by the host.

`flow.switch` and `flow.fork` expose output ports from node configuration at
composition build time. Static registry metadata is intentionally limited to
their input ports so config-defined output names are validated after their
factories bind options.

## Design Metadata

`RoutingComponentDesignMetadataProvider` exposes neutral Designer metadata for
the six routing composition nodes. Hosts can add it to a
`ComponentDesignMetadataCatalog` to populate palettes, editors, validation
views, or generated documentation.

The provider describes node options, built-in ports, and host-owned resource
hints. Selector delegates such as `routeKeySelector`, `keySelector`,
`sideSelector`, `leftKeySelector`, and `rightKeySelector` are exposed as
required resources for the nodes that need them. The optional `clock` resource
is exposed separately from editable node options. Switch `routeOutputs` and fork
`outputs` are represented as configuration options because those dynamic ports
are exposed after the composition factory binds node options.
The metadata is authored through the shared validated Designer metadata builder
while preserving the same public metadata contracts consumed by hosts.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "route": {
              "type": "flow.switch",
              "resources": {
                "routeKeySelector": "order-route"
              },
              "configuration": {
                "routes": [ "priority", "standard" ],
                "routeOutputs": {
                  "priority": "Priority"
                },
                "emitRouteEnvelope": true,
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

Dynamic output names for switch and fork composition must be simple identifiers
and cannot collide with built-in composition ports.

Invalid routing options, such as blank `inputType`, non-positive
`boundedCapacity`, invalid window boundaries, or invalid correlation limits,
fail during composition build and surface as factory diagnostics when build
failures are configured as diagnostics.
