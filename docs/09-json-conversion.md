# JSON Conversion

The default JSON path is composition JSON. Hosts normally load
`FluxFlow:Composition` from `IConfiguration` with
`CompositionConfigurationLoader`, or serialize `CompositionDefinition` directly
with `CompositionDefinitionJson.CreateSerializerOptions()`.

## Appsettings Composition

`CompositionConfigurationLoader` reads `FluxFlow:Composition` by default:

```csharp
var definition = new CompositionConfigurationLoader().Load(configuration);
```

An appsettings-style composition section uses explicit `nodes` and `links`:

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

Missing `FluxFlow:Composition` returns an empty `CompositionDefinition`.
Malformed reference JSON, invalid scalar conversion, or invalid configuration
shape throws `CompositionConfigurationException`.

## Direct Composition JSON

Use `CompositionDefinitionJson.CreateSerializerOptions()` when serializing or
deserializing composition DTOs directly:

```csharp
var options = CompositionDefinitionJson.CreateSerializerOptions();

var definition = JsonSerializer.Deserialize<CompositionDefinition>(json, options);
var text = JsonSerializer.Serialize(definition, options);
```

Use these options for:

| Type | JSON form |
|------|-----------|
| `CompositionDefinition` | object with `workflows` |
| `WorkflowDefinition` | object with `nodes` and `links` |
| `NodeDefinition` | object with `type`, `configuration`, and `resources` |
| `LinkDefinition` | object with `from` and `to` |
| `NodeReference` | `node` or `workflow.node` string, or object |
| `PortReference` | `node.port` or `workflow.node.port` string, or object |

The serializer uses web-style JSON options, indented output, and numeric reading
from strings. `NodeReference` and `PortReference` write back as compact strings.

## Definition Shape

`CompositionDefinition` contains only standalone composition data:

- `workflows`: named workflow graphs

`WorkflowDefinition` contains:

- `nodes`: named node definitions
- `links`: explicit source/target port references

`NodeDefinition` contains:

- `type`: registered composition node type
- `configuration`: node/package options as JSON values
- `resources`: local resource slots mapped to host-owned resource keys

Example node:

```json
{
  "type": "http.client",
  "configuration": {
    "method": "POST"
  },
  "resources": {
    "client": "orders-api"
  }
}
```

Composition does not create resources. It records resource names; the host or
adapter DI layer owns actual clients, stores, clocks, secrets, connections, and
expression engines.

## Node Configuration

Node configuration is stored as `Dictionary<string, JsonElement>`. Composition
does not interpret those values; the adapter factory that owns the node type
binds and validates them.

```csharp
private static T ReadRequired<T>(NodeDefinition definition, string name)
{
    if (!definition.Configuration.TryGetValue(name, out var value))
        throw new InvalidOperationException($"Missing required option '{name}'.");

    return value.Deserialize<T>(CompositionDefinitionJson.CreateSerializerOptions())
        ?? throw new InvalidOperationException($"Option '{name}' was empty.");
}
```

Keep package option validation near the package node or composition adapter.
Keep app-specific workspace validation in the app before building the
composition definition.

## Link JSON

Composition links use explicit objects:

```json
{ "from": "source.Output", "to": "sink.Input" }
```

Multiple source links to one input are separate array entries:

```json
[
  { "from": "primary.Output", "to": "sink.Input" },
  { "from": "fallback.Output", "to": "sink.Input" }
]
```

Inline link predicates are not part of composition JSON in v1. Use routing
nodes such as `flow.filter`, `flow.when`, or `flow.switch` for conditional
behavior.

## Reference Rules

Inside a workflow, short port references use:

```text
node.port
```

Cross-workflow references use:

```text
workflow.node.port
```

Direct `NodeReference` JSON accepts `node` or `workflow.node`.
Direct `PortReference` JSON accepts `node.port` or `workflow.node.port`.

Object forms are also accepted:

```json
{ "workflow": "main", "node": "source", "port": "Output" }
```

References are trimmed and cannot contain empty segments. Port references must
contain either two or three segments.

## Workspace Files

Applications may have richer workspace files than `CompositionDefinition`. Keep
those files in app-owned models and project only executable sections into
composition:

```csharp
var workspace = JsonSerializer.Deserialize<AppWorkspace>(json, appOptions)!;
var definition = workspace.ToCompositionDefinition();
var validation = new CompositionValidator().Validate(definition, registry);
```

This lets app validation handle UI layout, resource catalog entries, secrets,
designer metadata, and product-specific rules before composition builds the
standalone-node graph.

## Troubleshooting

| Symptom | Check |
|---------|-------|
| composition loads empty | Confirm the section name is `FluxFlow:Composition`, or pass the exact section name. |
| node type is missing | Put the type under `nodes:{name}:type`. |
| node option is missing | Put package options under `configuration`, not beside links. |
| resource is not resolved | Put resource slot mappings under `resources`, then register matching keyed services in the host. |
| link conversion fails | Use `{ "from": "node.Output", "to": "node.Input" }` objects in the workflow `links` array. |
| reference conversion fails | Use `node.port` or `workflow.node.port`; avoid empty segments. |

## Optional Engine JSON

`FluxFlow.Engine` still uses `ApplicationDefinitionJson.CreateSerializerOptions()`
for the older `ApplicationDefinition` runtime:

```csharp
var options = ApplicationDefinitionJson.CreateSerializerOptions();

var definition = JsonSerializer.Deserialize<ApplicationDefinition>(json, options);
var text = JsonSerializer.Serialize(definition, options);
```

Engine JSON differs from composition JSON:

- workflow JSON is a direct node map, not an object with `nodes` and `links`
- input-port links are stored as extension properties on each node
- `resources` are shared engine nodes under the `$resources` scope
- `phase` and `when` belong to engine node definitions
- link JSON can use strings, objects, or arrays on input-port properties

Use this JSON shape only when a host intentionally chooses the optional engine
runtime.

Next: [Expression Mapping](10-expression-mapping.md)
