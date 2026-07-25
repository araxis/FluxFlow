# Canonical vNext Migration

This guide records intentional next-major removals and their canonical
replacements. It is updated as each cleanup ledger entry is completed.

## Composition 2.x To 3.0

Composition 3.0 removes the parallel persisted definition and runtime path:

- `CompositionDefinition`, its workflow/node/link/reference DTOs, and JSON helper
- `CompositionDefinitionBuilder`
- `CompositionConfigurationLoader`
- `CompositionValidator` and its diagnostics
- `CompositionRuntimeBuilder` and `CompositionBuildResult`
- legacy definition sources and reload planner contracts
- node-oriented `CompositionNodeFactoryContext` members

Use `ApplicationDefinition`, canonical links, application revision hosting, and
component-oriented factory contexts. `CompositionRuntime` remains only as the
small lifecycle owner for already-created code-first or Engine descriptors.

### JSON

Before:

```json
{
  "workflows": {
    "Orders": {
      "nodes": {
        "Source": {
          "type": "source.items",
          "configuration": {
            "items": ["alpha", "beta"]
          }
        },
        "Sink": {
          "type": "sample.sink",
          "resources": {
            "store": "Resources.Storage.Primary"
          }
        }
      },
      "links": [
        { "from": "Source.Output", "to": "Sink.Input" }
      ]
    }
  }
}
```

After:

```json
{
  "Resources": {
    "Storage": {
      "Primary": {
        "Type": "storage.memory"
      }
    }
  },
  "Workflows": {
    "Orders": {
      "Source": {
        "Type": "source.items",
        "items": ["alpha", "beta"]
      },
      "Sink": {
        "Type": "sample.sink",
        "store": "Resources.Storage.Primary",
        "Input": "Source.Output"
      }
    }
  }
}
```

Legacy resource slots were references to host-owned keyed services; migration
does not invent resource definitions. Add canonical `Resources` entries and DI
registrations according to the host's ownership model.

### Explicit Conversion

```csharp
using FluxFlow.Composition.Migration;
using FluxFlow.Composition.Model;

var migrator = new LegacyCompositionDefinitionMigrator();
var definition = migrator.Migrate(legacyJson);
var canonicalJson = ApplicationDefinitionJson.Serialize(definition);
```

The migrator also accepts UTF-8 JSON or an `IConfiguration` root/section. It is
strict: unknown properties, option/resource collisions, missing link endpoints,
and link/property collisions fail rather than producing a lossy application.
Persist canonical JSON and use normal canonical loading thereafter.

## Composition.Hosting 2.x To 3.0

Hosting 3.0 removes:

- `AddFluxFlowComposition(...)` and its builder
- `ICompositionRuntimeHost` and `CompositionRuntimeHost`
- legacy hosted-service options and exception contracts
- static/configuration Composition definition sources
- obsolete factory-context resource extension methods

Before:

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterAppComponents());
```

After:

```csharp
services
    .AddFluxFlowApplication(canonicalConfiguration)
    .UseRuntimeAssembler(runtime => runtime.RegisterNodes(registry =>
        registry.RegisterAppComponents()));
```

The canonical host keeps one active complete application definition, prepares
candidate revisions transactionally, preserves the active revision after a
rejection, and drains/disposes replaced revisions. Adapter packages remain the
owners of concrete clients, stores, clocks, credentials, and other resources.

## Compatibility Report

These removals are intentional source and binary breaks against published
`FluxFlow.Composition` 2.7.0 and `FluxFlow.Composition.Hosting` 2.3.0. No shim
recreates the removed parallel architecture. Package validation should report
the corresponding removals until the 3.0 baselines are published; review those
reports as expected breaking-change evidence rather than suppressing them.
