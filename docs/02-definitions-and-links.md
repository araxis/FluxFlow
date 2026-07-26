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

## Canonical Link Compilation

Port properties use the registered port name and may declare one link, or an
array of links, on either endpoint:

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

The compiler accepts a string, an object with exact `Port` and optional
`Condition` property names, or a mixed array of those forms. An empty array
means no links. A link must appear on only one endpoint.

```csharp
using FluxFlow.Composition.Links;

var catalog = provider.GetRequiredService<ComponentCatalog>();
var result = new ApplicationLinkCompiler(catalog, expressionEngine)
    .Compile(definition);

if (!result.IsValid)
{
    foreach (var diagnostic in result.Diagnostics)
        Console.Error.WriteLine(diagnostic);
}
```

`ComponentCatalog` descriptor metadata determines whether a property is an
input or output. The compiler converts local references to absolute addresses,
preserves `ApplicationLinkDeclarationSide`, and sorts successful links by
source and target. Ordinary component settings that do not match a registered
port remain settings and are ignored by the link compiler.

Validation rejects malformed declarations, unknown component types, missing
components or ports, exact payload-type mismatches, duplicate endpoint pairs,
explicit single-link claim conflicts, condition compilation failures, and
data-link cycles. Multiple upstreams to one input and multiple targets from one
output remain valid by default. Use `CompositionPortLinkCardinality.Single`
only for a port whose contract is exclusive.

Cycle validation is port-aware. A link targeting metadata registered with
`CompositionPortKind.Signal` is a bounded feedback relation and is excluded
from the unbounded data-cycle graph. This permits relations such as
`Receive.Output -> Handle.Input` and `Handle.Output -> Receive.Ack`. Merely
naming an ordinary message port `Ack`, `Nak`, or `Cancel` does not make it a
signal, so data cycles cannot bypass validation through port naming. Local and
fully qualified addresses use the same classification.

Each distinct condition string is compiled once per compiler invocation using
`IFlowExpressionEngine`. A compiled link exposes `IsMatch(...)` and
`TryMatch(...)`; the latter returns a captured evaluation exception so the
future runtime can reject only that link for that message and continue with
sibling links.

Reserved system streams require host-supplied
`ApplicationSystemOutputMetadata`. That keeps system payload contracts in the
Engine while allowing Composition to perform the same exact type check without
depending on Engine. `FluxFlow.Engine.Hosting` activates successful compiled
links through the stable-port runtime.

## Legacy Document Migration

The version 3 Composition package no longer loads or executes the earlier
`workflows` / `nodes` / `links` model. Convert an existing document at an
explicit migration boundary:

```csharp
using FluxFlow.Composition.Migration;

var definition = new LegacyCompositionDefinitionMigrator().Migrate(legacyJson);
var canonicalJson = ApplicationDefinitionJson.Serialize(definition);
```

The migrator flattens legacy configuration and resource slots into component
properties and converts separate links into canonical port properties. It
rejects collisions and shapes that cannot be converted without loss. Normal
loading, validation, persistence, and activation remain canonical-only.

## Legacy Engine Documents

Engine version 3 no longer exposes a second executable definition model. Use
`LegacyEngineApplicationDefinitionMigrator` for compatible old
Workflows/Nodes JSON, persist the returned canonical definition, and activate
it through the standard runtime assembler. The migrator rejects executable
resource nodes, non-default phases, and flat-property collisions that require
an explicit host decision.

Next: [Node Authoring](03-node-authoring.md).
