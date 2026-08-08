# JSON Conversion

The canonical vNext JSON path is
`FluxFlow.Composition.Model.ApplicationDefinition` with exactly two
case-sensitive root objects: `Resources` and `Workflows`.

## Canonical JSON

```json
{
  "Resources": {
    "Storage": {
      "Primary": {
        "Type": "sample.store",
        "Path": "data"
      }
    }
  },
  "Workflows": {
    "Orders": {
      "Source": {
        "Type": "source.sequence",
        "Count": 3
      },
      "Sink": {
        "Type": "storage.put",
        "Store": "Resources.Storage.Primary",
        "Input": "Source.Output"
      }
    }
  }
}
```

Use `ApplicationDefinitionJson` for strict parsing and deterministic writing:

```csharp
using FluxFlow.Composition.Model;

var definition = ApplicationDefinitionJson.Deserialize(json);
var canonical = ApplicationDefinitionJson.Serialize(definition);
```

The reader rejects missing or extra root sections, incorrect casing, duplicate
properties, non-object workflow/resource maps, missing `Type` values, dotted
names, reserved names, and legacy component wrappers. The writer emits root
sections in fixed order, sorts all named maps and object properties ordinally,
and preserves array order.

`ApplicationDefinition` carries component and resource option values as owned
`JsonElement` copies. Composition does not infer CLR option types or insert
mappers at this boundary.

A definition built with complete C# `ComponentContract` values may also own
executable runtime descriptors in memory. Those descriptors, factories,
selectors, handles, and delegates are deliberately omitted by the canonical
writer. Deserializing that portable projection produces a normal JSON
definition with no definition-owned descriptors, so its host must register the
required component contracts or families explicitly. This separation keeps
JSON suitable for configuration, persistence, hot reload, and Designer output
without restricting compiled C# authoring to serializable behavior.

## Configuration Loading

Engine's `ConfigurationApplicationDefinitionSource` reads either an
`IConfiguration` root or an explicitly selected host section:

```csharp
using FluxFlow.Engine;

var fromRoot = await new ConfigurationApplicationDefinitionSource(configuration)
    .LoadAsync();

var fromSection = await new ConfigurationApplicationDefinitionSource(
        configuration,
        "Application")
    .LoadAsync();
```

The strict canonical JSON parser remains authoritative after configuration is
projected to JSON. Configuration providers flatten source formats, so they
cannot preserve duplicate properties or every distinction among empty arrays,
empty objects, null, and scalar text. Use `ApplicationDefinitionJson` directly
when the original JSON shape must be verified exactly.

## Canonical Addresses

Resource references use `Resources.Group.Resource`. Absolute workflow ports
use `Workflow.Component.Port`; local workflow references use `Component.Port`
and require the current workflow name.

```csharp
using FluxFlow.Composition.Addressing;

var resource = ApplicationAddress.Parse("Resources.Storage.Primary");
var output = ApplicationAddress.Parse("Orders.Source.Output");
var input = ApplicationAddress.ResolvePort("Sink.Input", "Orders");
```

Address parsing is ordinal and case-sensitive. `System.Events.Output` and
`System.Diagnostics.Output` are the only accepted `System` addresses.

## Planned Link JSON

The canonical model preserves port-property values in their direct form:

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

A single link stays a string or object; arrays represent multiple links.
Component port metadata infers direction, normalizes input-side and output-side
declarations, and compiles conditions before activation.

## Legacy Document Migration

Normal startup does not load legacy JSON, and no in-process legacy parser is
shipped. Convert an existing `FluxFlow:Composition` section externally and
persist the canonical result before deployment.

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

An external converter must flatten node options and keyed resource references,
convert separate links, and stop for unknown, ambiguous, or lossy input. Review
the generated document, then load it through `ApplicationDefinitionJson` so
missing sections and malformed references fail at the canonical boundary.

## Legacy Engine JSON

Engine consumes this same canonical model and owns no second serializer or
legacy converter. Convert older Engine Workflows/Nodes documents externally,
persist the canonical result, and then use only `ApplicationDefinitionJson`
from `FluxFlow.Composition.Model`.

Next: [Expression Mapping](10-expression-mapping.md)
