# Definitions And Links

`ApplicationDefinition` is the executable contract that the engine builds into a
runtime graph.

## Application Shape

```csharp
public sealed record ApplicationDefinition
{
    public Dictionary<string, NodeDefinition> Resources { get; init; }
    public Dictionary<string, WorkflowDefinition> Workflows { get; init; }
}
```

- `Resources` are shared nodes that can be referenced by workflows.
- `Workflows` are named graphs of runtime nodes.
- `WorkflowDefinition.Nodes` maps node names to `NodeDefinition`.

## Node Shape

`NodeDefinition` contains:

- `Type`: the registered node type.
- `Configuration`: node-specific options as JSON values.
- `Phase`: startup ordering. Lower values start first.
- `When`: optional default condition for links declared on that node.
- port entries: JSON extension-data entries where the property name is the
  target input port name.

Example:

```json
{
  "type": "sample.order-sink",
  "configuration": {
    "category": "priority"
  },
  "Input": {
    "from": "review.Output",
    "when": "input.Priority == true"
  }
}
```

## Link Forms

Short form:

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

Multiple source links to one input:

```json
{
  "Input": [
    { "from": "fast.Output" },
    { "from": "slow.Output" }
  ]
}
```

## Address Rules

Inside a workflow, short addresses use `node.port`:

```text
review.Output
```

Fully-qualified addresses use `scope.node.port`:

```text
main.review.Output
```

Resource addresses use the `$resources` scope:

```text
$resources.shared.Output
```

## Conditional Links

The default condition expression exposes the current output item as both
`input` and `value`.

```json
{
  "from": "review.Output",
  "when": "input.Priority == true"
}
```

If no `when` expression is set, the link receives every item from the source
output.

## JSON Options

Use `ApplicationDefinitionJson.CreateSerializerOptions()` when serializing or
deserializing engine definitions:

```csharp
var options = ApplicationDefinitionJson.CreateSerializerOptions();
var definition = JsonSerializer.Deserialize<ApplicationDefinition>(json, options);
```

Next: [Node Authoring](03-node-authoring.md).
