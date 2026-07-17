# Definitions And Links

The canonical vNext definition is
`FluxFlow.Composition.Model.ApplicationDefinition`. It is an immutable
application document with exactly two case-sensitive root objects:
`Resources` and `Workflows`.

## Canonical Shape

```json
{
  "Resources": {
    "Messaging": {
      "Broker1": {
        "Type": "sample.broker",
        "Host": "localhost"
      },
      "Client1": {
        "Type": "sample.client",
        "Broker": "Resources.Messaging.Broker1"
      }
    }
  },
  "Workflows": {
    "Orders": {
      "Source": {
        "Type": "sample.source",
        "Count": 3
      },
      "Sink": {
        "Type": "sample.sink",
        "Input": "Source.Output"
      }
    }
  }
}
```

The document rules are deliberately narrow:

- both root sections are required and no other root property is allowed
- workflows and resource groups are objects keyed by exact names
- workflow objects contain components directly
- resource groups omit `Type`; resource leaves require a string `Type`
- components require a string `Type`
- component and resource settings are direct properties
- `Configuration`, per-component `Resources`, `Nodes`, and `Links` wrappers are
  not part of the canonical shape
- names use ordinal, case-sensitive comparison and cannot contain dots or
  surrounding whitespace
- `Resources` and `System` are reserved workflow names; `Type` is reserved in
  resource maps

The model copies caller-owned collections into immutable ordinal dictionaries
and clones retained `JsonElement` values. Mutating an input dictionary or
disposing its source `JsonDocument` cannot change a built definition.

## Model Types

```csharp
using FluxFlow.Composition.Model;

var application = new ApplicationDefinition(
    resources:
    [
        new("Messaging", new ResourceGroupDefinition(
        [
            new("Broker1", new ResourceInstanceDefinition("sample.broker"))
        ]))
    ],
    workflows:
    [
        new("Orders", new WorkflowDefinition(
        [
            new("Source", new ComponentDefinition("sample.source"))
        ]))
    ]);
```

`ResourceDefinition` is a closed resource shape with
`ResourceGroupDefinition` and `ResourceInstanceDefinition` variants. Groups
hold child resources; instances hold `Type` and flat properties.

## JSON And Configuration

`ApplicationDefinitionJson` is the authoritative strict reader and
deterministic writer:

```csharp
var definition = ApplicationDefinitionJson.Deserialize(json);
var canonicalJson = ApplicationDefinitionJson.Serialize(definition);
```

Writing always emits `Resources` before `Workflows`, sorts resource, workflow,
component, and property names ordinally, and recursively sorts nested JSON
object properties. Array order remains unchanged. Duplicate JSON properties
are rejected, including duplicates inside retained option values.

`ApplicationDefinitionConfigurationLoader` can load the canonical model from
an `IConfiguration` root or an explicitly named host section:

```csharp
var rootDefinition = new ApplicationDefinitionConfigurationLoader()
    .Load(configuration);

var hostedDefinition = new ApplicationDefinitionConfigurationLoader()
    .Load(configuration, "Application");
```

Configuration providers flatten JSON and cannot retain every lexical detail.
Use `ApplicationDefinitionJson` when exact JSON shape and duplicate-property
detection are required at the source boundary.

## Address Rules

`FluxFlow.Composition.Addressing.ApplicationAddress` is the shared ordinal,
case-sensitive address value.

| Target | Form | Example |
|---|---|---|
| Nested resource | `Resources.Group.Resource` | `Resources.Messaging.Client1` |
| Absolute workflow port | `Workflow.Component.Port` | `Orders.Source.Output` |
| Local workflow port | `Component.Port` | `Source.Output` |
| System events | reserved absolute address | `System.Events.Output` |
| System diagnostics | reserved absolute address | `System.Diagnostics.Output` |

Local references require a workflow context:

```csharp
var input = ApplicationAddress.ResolvePort("Sink.Input", "Orders");
var output = ApplicationAddress.Parse("Orders.Source.Output");
var resource = ApplicationAddress.Parse("Resources.Messaging.Client1");
```

Addresses reject blank segments, surrounding whitespace, ambiguous resource
references used as ports, and unrecognized `System` paths. Equality and hashing
are ordinal, so `Orders.Source.Output` and `orders.Source.Output` are distinct.

## Planned Link Properties

Port properties may retain the agreed link-shaped JSON while link compilation
is developed in the next milestone:

```json
{
  "Type": "sample.sink",
  "Input": [
    "Source.Output",
    {
      "Port": "Other.Source.Output",
      "Condition": "value != null"
    }
  ]
}
```

The canonical model only preserves these properties today. It does not yet
infer port direction, normalize links, compile conditions, or build a runtime
from them. The next Composition milestone will add those behaviors using node
port metadata and this same address contract.

## Legacy Runtime Definition

The existing executable Composition runtime still accepts
`CompositionDefinition`, `WorkflowDefinition`, `NodeDefinition`, and
`LinkDefinition` in the `FluxFlow.Composition` namespace. Its fluent builder
and `CompositionConfigurationLoader` continue to use the earlier
`workflows`/`nodes`/`links` shape during migration:

```csharp
var definition = CompositionDefinitionBuilder
    .Create()
    .Workflow("main", workflow => workflow
        .Node("source", "source.sequence")
        .Node("sink", "storage.put")
        .Link("source.Output", "sink.Input"))
    .Build();
```

Do not project new persisted application documents into this legacy shape by
default. A later bounded milestone will bind the canonical model to runtime
registrations and provide migration guidance before legacy declarations are
removed.

## Optional Engine Definition

`FluxFlow.Engine` also retains its older executable `ApplicationDefinition`.
It is not the canonical persistence or addressing model and will be removed in
the next appropriate Engine major after the Composition model is proven and a
legacy reader exists.

Next: [Node Authoring](03-node-authoring.md).
