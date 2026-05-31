# JSON Conversion

`ApplicationDefinitionJson.CreateSerializerOptions()` is the supported JSON
entrypoint for engine definitions.

```csharp
var options = ApplicationDefinitionJson.CreateSerializerOptions();

var definition = JsonSerializer.Deserialize<ApplicationDefinition>(json, options);
var text = JsonSerializer.Serialize(definition, options);
```

Use these options whenever you read or write `ApplicationDefinition`,
`WorkflowDefinition`, `NodeDefinition`, `NodeType`, `PortAddress`, or
`LinkDefinition` values.

## Options

`CreateSerializerOptions()` returns web-style JSON options with indented output
and engine converters for:

| Type | JSON form |
|------|-----------|
| `NodeType` | string |
| `NodeName` | string |
| `PortName` | string |
| `PortAddress` | `scope.node.port` string |
| `LinkDefinition` | fully-qualified string or object with fully-qualified `from` |
| `WorkflowDefinition` | direct node map |

The workflow converter is important: a workflow serializes as a direct map of
node names to node definitions. There is no `nodes` wrapper in engine JSON.

```json
{
  "workflows": {
    "main": {
      "source": { "type": "sample.source" },
      "sink": {
        "type": "sample.sink",
        "Input": "source.Output"
      }
    }
  }
}
```

## Definition Shape

`ApplicationDefinition` contains only executable engine data:

- `resources`: shared nodes referenced by workflows
- `workflows`: named workflow graphs

`NodeDefinition` contains:

- `type`: registered node type
- `configuration`: node/package options as JSON values
- `phase`: startup order, default `0`
- `when`: default condition for links declared on the node
- extension properties: target input port links

Example:

```json
{
  "type": "sample.sink",
  "configuration": {
    "category": "priority"
  },
  "phase": 10,
  "Input": {
    "from": "review.Output",
    "when": "input.Priority == true"
  }
}
```

Because input ports are stored as JSON extension data, avoid using port names
that collide with reserved node properties:

- `type`
- `configuration`
- `phase`
- `when`

## Node Configuration

Node configuration is stored as `Dictionary<string, JsonElement>`. The engine
does not interpret those values; the package that owns the node type reads and
validates them.

```csharp
private static T ReadRequired<T>(NodeDefinition definition, string name)
{
    if (!definition.Configuration.TryGetValue(name, out var value))
        throw new InvalidOperationException($"Missing required option '{name}'.");

    return value.Deserialize<T>()
        ?? throw new InvalidOperationException($"Option '{name}' was empty.");
}
```

Keep package-specific option validation near the package node or package module.
Keep app-specific workspace validation in the app before projecting to
`ApplicationDefinition`.

## Link JSON

Node input ports accept three link shapes.

Short string form:

```json
{ "Input": "source.Output" }
```

Object form:

```json
{
  "Input": {
    "from": "source.Output",
    "when": "input.Priority == true"
  }
}
```

Multiple links to the same input:

```json
{
  "Input": [
    { "from": "primary.Output" },
    { "from": "fallback.Output" }
  ]
}
```

For node port entries, `LinkJson.ParseMany()` accepts:

- string link values
- object link values
- arrays of string/object link values
- lower-case or upper-case `from` and `when`

Any other JSON value is invalid.

## Address Rules

Inside a workflow, short source addresses use:

```text
node.port
```

The current workflow name is added when the link is parsed.

Fully-qualified addresses use:

```text
scope.node.port
```

Resource addresses use the `$resources` scope:

```text
$resources.shared.Output
```

Addresses may include additional path segments after the port:

```text
main.source.Output.payload.id
```

The engine stores those extra segments in `PortAddress.SubPath`. Runtime port
wiring currently uses the node and port part of the address.

## Default Conditions

`NodeDefinition.When` is a default condition for links declared on that node.

```json
{
  "type": "sample.sink",
  "when": "input.Priority == true",
  "Input": "review.Output"
}
```

If a link object has its own `when`, the link value is used. If the link object
has `when: null`, the node default is used.

## Direct LinkDefinition JSON

Most application JSON stores links inside node port extension properties. Those
links are parsed with the current workflow name, so short `node.port` addresses
work.

If you serialize or deserialize `LinkDefinition` directly, use a fully-qualified
address:

```json
{ "from": "main.source.Output" }
```

Direct `LinkDefinition` conversion does not know the current workflow name.

## Workspace Files

Applications may have richer workspace files than the engine definition. Keep
those files in app-owned models and project only executable sections into
`ApplicationDefinition`.

Top-level properties that are not part of `ApplicationDefinition` are ignored
when deserializing directly into `ApplicationDefinition`, but relying on that as
a workspace loader makes app validation weaker. Prefer this shape:

```csharp
var workspace = JsonSerializer.Deserialize<AppWorkspace>(json, appOptions)!;
var definition = workspace.ToEngineDefinition();
var validation = new ApplicationDefinitionValidator().Validate(definition);
```

## Troubleshooting

| Symptom | Check |
|---------|-------|
| `Flow node type must be a string.` | `type` must be a JSON string. |
| `Flow link must be a string or object.` | The input port value must be a string, object, or array of those values. |
| `Port address cannot be empty.` | A `from` value is missing or empty. |
| short link fails in direct `LinkDefinition` conversion | Use `scope.node.port` because direct conversion has no workflow context. |
| node option is missing | Look under `configuration`, not beside the port links. |

Next: [Expression Mapping](10-expression-mapping.md)
