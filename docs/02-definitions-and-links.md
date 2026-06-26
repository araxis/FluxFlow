# Definitions And Links

The default definition model is `CompositionDefinition`. Fluent builders and
`IConfiguration` JSON both produce this DTO before validation, node creation, and
linking.

## Composition Shape

```csharp
public sealed record CompositionDefinition
{
    public Dictionary<string, WorkflowDefinition> Workflows { get; init; }
}

public sealed record WorkflowDefinition
{
    public Dictionary<string, NodeDefinition> Nodes { get; init; }
    public List<LinkDefinition> Links { get; init; }
}
```

- `Workflows` are named standalone-node graphs.
- `WorkflowDefinition.Nodes` maps node names to `NodeDefinition`.
- `WorkflowDefinition.Links` connects named output ports to named input ports.

## Node Shape

`NodeDefinition` contains:

- `Type`: the registered composition node type string.
- `Configuration`: node-specific options as JSON values.
- `Resources`: local resource slots mapped to host-owned keyed resources.

Example:

```json
{
  "type": "storage.put",
  "configuration": {
    "defaultCollection": "orders"
  },
  "resources": {
    "store": "primary"
  }
}
```

Composition records resource names only. The host or adapter package owns the
actual client, store, secret, clock, connection, or expression engine.

## Link Shape

Links are explicit `from` and `to` port references:

```json
{
  "from": "source.Output",
  "to": "sink.Input"
}
```

Appsettings-style composition configuration keeps links in the workflow:

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "source": {
              "type": "source.sequence",
              "configuration": {
                "count": 3
              }
            },
            "sink": {
              "type": "storage.put",
              "resources": {
                "store": "primary"
              }
            }
          },
          "links": [
            { "from": "source.Output", "to": "sink.Input" }
          ]
        }
      }
    }
  }
}
```

The fluent builder creates the same model:

```csharp
var definition = CompositionDefinitionBuilder
    .Create()
    .Workflow("main", workflow => workflow
        .Node("source", "source.sequence", node => node
            .Configure("count", 3))
        .Node("sink", "storage.put", node => node
            .Resource("store", "primary"))
        .Link("source.Output", "sink.Input"))
    .Build();
```

## Reference Rules

Inside a workflow, short port references use `node.port`:

```text
source.Output
```

Cross-workflow references use `workflow.node.port`:

```text
main.source.Output
```

Node references use the same rule without the port:

```text
source
main.source
```

References are trimmed and cannot contain empty segments. Port references must
use either two segments (`node.port`) or three segments (`workflow.node.port`).

## Validation And Build

`CompositionValidator` validates the definition against a
`CompositionNodeRegistry` before the runtime is linked. It catches:

- empty definitions, workflows, node names, and node types
- unknown node types
- missing source or target nodes
- missing input or output ports when registration metadata exposes them
- duplicate links
- port type mismatches when registration metadata exposes message types

`CompositionRuntimeBuilder` then creates node instances, validates descriptor
ports, links the graph, and cleans up created nodes if build fails.

## Configuration Loading

`CompositionConfigurationLoader` reads `FluxFlow:Composition` by default:

```csharp
var definition = new CompositionConfigurationLoader().Load(configuration);
```

Use `CompositionDefinitionJson.CreateSerializerOptions()` when serializing or
deserializing `CompositionDefinition`, `WorkflowDefinition`, `NodeDefinition`,
`LinkDefinition`, `NodeReference`, or `PortReference` values directly.

## Conditions And Routing

Composition links are structural connections. They do not own inline `when`
expressions in v1. Use normal standalone nodes for conditional behavior:

- `flow.filter` to drop rejected messages.
- `flow.when` to split true/false branches.
- `flow.switch` to route by a host-owned selector.
- `flow.mapper` to shape messages before routing.

This keeps link wiring simple and leaves expression engines, context factories,
and selectors as host-owned resources.

## Optional Engine Definition

`FluxFlow.Engine` still uses `ApplicationDefinition` for the older executable
runtime:

```csharp
public sealed record ApplicationDefinition
{
    public Dictionary<string, NodeDefinition> Resources { get; init; }
    public Dictionary<string, WorkflowDefinition> Workflows { get; init; }
}
```

Engine JSON stores input-port links as extension properties on the node and can
use inline `when` conditions. Use `ApplicationDefinitionJson` and the engine
validation/build APIs only when a host intentionally chooses that runtime path.

Next: [Node Authoring](03-node-authoring.md).
