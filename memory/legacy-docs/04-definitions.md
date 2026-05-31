# Definitions

Namespace: `FluxFlow.Engine.Definitions`

---

## ApplicationDefinition

The top-level JSON model. All four dictionaries are optional (default to empty).

```csharp
public sealed record ApplicationDefinition
{
    public Dictionary<string, NodeDefinition>     Resources  { get; init; }
    public Dictionary<string, WorkflowDefinition> Workflows  { get; init; }
    public Dictionary<string, DashboardDefinition> Dashboards { get; init; }
    public Dictionary<string, ScenarioDefinition> Tests      { get; init; }
}
```

JSON representation:

```json
{
  "resources": {
    "broker": { "type": "mqtt.connection", "configuration": { "host": "localhost" } }
  },
  "workflows": {
    "main": { ... }
  },
  "tests": {
    "smoke": { ... }
  }
}
```

---

## WorkflowDefinition

A flat dictionary of node names to `NodeDefinition` values.
The JSON converter serializes a workflow directly as its node map:

```json
{
  "source": {
    "type": "demo.numbers",
    "configuration": { "count": 100 }
  },
  "printer": {
    "type": "demo.printer",
    "Input": "source.Output"
  }
}
```

```csharp
public sealed record WorkflowDefinition
{
    public Dictionary<string, NodeDefinition> Nodes { get; init; }
}
```

---

## NodeDefinition

Describes a single node.

```csharp
public sealed record NodeDefinition
{
    public required NodeType                    Type          { get; init; }
    public Dictionary<string, JsonElement>      Configuration { get; init; }
    public string?                              When          { get; init; }
    public int                                  Phase         { get; init; } = 0;

    // Port links — any JSON property not named "type", "configuration", or "when"
    // is treated as a port-link declaration and stored here via [JsonExtensionData]
    public Dictionary<string, JsonElement>      Ports         { get; init; }
}
```

### Port link formats

A port link declaration lives as an extra JSON property whose name is the **target port name**
and whose value is the **source port address**:

```json
// Single link — string shorthand
"Input": "source.Output"

// Single link — object form (allows conditional routing via "when")
"Input": { "from": "source.Output", "when": "topic.StartsWith(\"sensors/\")" }

// Multiple links to the same input — array
"Input": ["source1.Output", "source2.Output"]

// Mixed array
"Input": [
  "source1.Output",
  { "from": "source2.Output", "when": "qos > 0" }
]
```

### Port address format

`"scope.nodeName.PortName"` — three segments separated by `.`

- **scope** is either a workflow name or the literal string `"resources"`
- **nodeName** is the node key within that scope
- **PortName** is case-sensitive and must match what the factory declared on `RuntimeNode`

Within a workflow, the scope segment may be omitted:
`"source.Output"` is equivalent to `"<currentWorkflow>.source.Output"`.

To reference a resource: `"resources.broker.Connection"`.

### Phase ordering

```json
{
  "broker": { "type": "mqtt.connection", "phase": 0 },
  "trigger": { "type": "mqtt.trigger", "phase": 1, "Connection": "resources.broker.Connection" }
}
```

The runtime calls `StartAsync` on all phase-0 nodes before any phase-1 node.
Resources and workflow nodes share the same phase space.

### The `when` field

`when` carries a filter expression passed to the link at build time.
The built-in builder does **not** evaluate `when`; it stores it on the `LinkDefinition`
so custom `OutputPort` subclasses or post-processing steps can apply it.

---

## NodeType

```csharp
public readonly record struct NodeType(string Value);
```

A non-empty string key that maps to a registered factory.
Examples: `"mqtt.trigger"`, `"demo.printer"`, `"file.writer"`.

---

## NodeName, PortName, WorkflowName

Typed wrappers around strings to prevent accidental swaps at call sites.

```csharp
public readonly record struct NodeName(string Value);
public readonly record struct PortName(string Value);
public readonly record struct WorkflowName(string Value);
```

---

## PortAddress

Fully-qualified address of a port: `scope + node + port`.

```csharp
public readonly record struct PortAddress(string Scope, NodeName Node, PortName Port)
{
    // Parse "scope.node.port" or "node.port" (scope inferred from context)
    public static PortAddress Parse(string value) { ... }
    public override string ToString() => $"{Scope}.{Node.Value}.{Port.Value}";
}
```

---

## LinkDefinition

```csharp
public sealed record LinkDefinition
{
    public required PortAddress From { get; init; }
    public string?              When { get; init; }
}
```

---

## WellKnownScopes

```csharp
public static class WellKnownScopes
{
    public const string Resources = "resources";
}
```

Use `WellKnownScopes.Resources` when constructing `PortAddress` for resource ports.

---

## JSON serialization

Always use `ApplicationDefinitionJson.CreateSerializerOptions()`:

```csharp
var options = ApplicationDefinitionJson.CreateSerializerOptions();

// Deserialize
var definition = JsonSerializer.Deserialize<ApplicationDefinition>(json, options)!;

// Serialize
var json = JsonSerializer.Serialize(definition, options);
```

The options register converters for `NodeType`, `PortName`, `NodeName`, `PortAddress`,
`LinkDefinition`, `WorkflowDefinition`, and `DashboardGridTrackDefinition`.
Without them, deserialization fails.

---

## Validation

`ApplicationDefinitionValidator` is called automatically by `ApplicationRuntimeBuilder`.
It checks:

| Rule | Error code |
|------|-----------|
| At least one workflow | `EmptyDefinition` |
| Each workflow has at least one node | `EmptyWorkflow` |
| Node names are non-empty | `EmptyNodeName` |
| Node types are non-empty | `EmptyNodeType` |
| Link source nodes exist | `MissingSourceNode` |
| Link target ports are non-empty | `EmptyTargetPort` |
| Duplicate links on the same target port | `DuplicateLink` |
| Scenario step types are registered | `UnknownScenarioStepType` |
| Scenario step connection resources exist | `MissingScenarioStepResource` |

`ApplicationDefinitionValidationResult.IsValid` is `false` if any error is present.

You can run validation manually before building:

```csharp
var validator = new ApplicationDefinitionValidator();
var result = validator.Validate(definition);
if (!result.IsValid)
    foreach (var e in result.Errors)
        Console.WriteLine($"{e.Code}: {e.Message}");
```

---

## Complete JSON example

```json
{
  "resources": {
    "broker": {
      "type": "mqtt.connection",
      "phase": 0,
      "configuration": {
        "host": "mqtt.example.com",
        "port": 1883,
        "clientId": "my-app"
      }
    }
  },
  "workflows": {
    "main": {
      "trigger": {
        "type": "mqtt.trigger",
        "phase": 1,
        "configuration": {
          "subscriptions": ["sensors/#"]
        },
        "Connection": "resources.broker.Connection"
      },
      "filter": {
        "type": "mqtt.message-filter",
        "configuration": {
          "expression": "qos >= 1"
        },
        "Input": "trigger.Output"
      },
      "metrics": {
        "type": "mqtt.metrics",
        "Input": "filter.Output"
      }
    }
  },
  "tests": {
    "smoke": {
      "steps": {
        "publish": {
          "type": "mqtt.publish",
          "configuration": {
            "connection": "broker",
            "topic": "sensors/temp",
            "payload": "{ \"value\": 42 }",
            "qos": 1
          }
        },
        "expect": {
          "type": "expect.event",
          "configuration": {
            "type": "mqtt.message.received",
            "topic": "sensors/temp",
            "timeoutSeconds": 5
          }
        }
      }
    }
  }
}
```
