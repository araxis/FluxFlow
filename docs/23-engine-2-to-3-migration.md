# Engine 2 To 3 Migration

This page describes the historical Engine 2-to-3 model transition. On current
Engine 6.x, use `AddFluxFlow(...)`, resolve `FluxFlowApplication`, and access
stable ports through `application.Ports`; the former Hosting APIs are obsolete.

Engine version 3 removes the duplicate Engine application/runtime model and
uses the canonical Composition application, revision host, component catalog,
stable ports, and standalone node contracts.

## Package Changes

Update the affected runtime package:

```text
FluxFlow.Engine 3.0.0
FluxFlow.Composition 3.0.0
FluxFlow.Composition.Hosting 3.0.0
```

Add `FluxFlow.Nodes` directly when authoring standalone nodes and
`FluxFlow.Mapping` directly when implementing a host expression engine.

## API Replacements

| Removed Engine 2 surface | Canonical replacement |
|---|---|
| `FluxFlow.Engine.Definitions.ApplicationDefinition` | `FluxFlow.Composition.Model.ApplicationDefinition` |
| `ApplicationDefinitionJson` / validator | Composition serializer, normalizer, link compiler, and revision planner |
| `NodeDefinition` / `NodeName` / `PortAddress` | `ComponentDefinition` / object-key identity / `ApplicationAddress` |
| Engine node base classes | `FluxFlow.Nodes.FlowNode<TIn,TOut>` and `FlowSource<T>` |
| `RuntimeNodeFactoryRegistry` | DI-registered `ComponentDescriptor` values and `ComponentCatalog` |
| `ApplicationRuntimeBuilder` | internal Engine runtime assembly behind `FluxFlowApplication` |
| `FlowApplicationHost` | `AddFluxFlow` and `FluxFlowApplication` |
| Engine state/error/diagnostic streams | revision status, normal result data, component Events, and system signals |

`FluxFlow.Fluent` remains the code-first graph option and has no Engine
dependency.

## Document Migration

Old Engine documents nested components under `Nodes`, nested options under
`Configuration`, and represented port links as extension properties:

```json
{
  "Resources": {},
  "Workflows": {
    "orders": {
      "Nodes": {
        "source": {
          "Type": "sample.source",
          "Configuration": { "count": 3 }
        },
        "priority": {
          "Type": "sample.sink",
          "When": "input.Priority == true",
          "Input": "source.Output"
        }
      }
    }
  }
}
```

Convert compatible input once:

```csharp
using FluxFlow.Engine.Migration;
using FluxFlow.Composition.Model;

ApplicationDefinition definition =
    new LegacyEngineApplicationDefinitionMigrator().Migrate(legacyJson);

var canonicalJson = ApplicationDefinitionJson.Serialize(definition);
```

The canonical result is flat and uses exact link object names:

```json
{
  "Resources": {},
  "Workflows": {
    "orders": {
      "source": {
        "Type": "sample.source",
        "count": 3
      },
      "priority": {
        "Type": "sample.sink",
        "Input": {
          "Port": "source.Output",
          "Condition": "input.Priority == true"
        }
      }
    }
  }
}
```

The migrator also accepts UTF-8 JSON. It rejects non-empty executable
`Resources`, non-default `Phase`, resource-node links, and property collisions
because those require explicit host decisions.

## Resource Nodes

Old executable resource nodes are not converted into hidden components.
Describe the resource under canonical `Resources`, then materialize it through
an `IApplicationResourceRegistrar` as an exact keyed service. Credentials,
clients, stores, clocks, and connection lifecycle remain host or adapter owned.

## Processing Phase

Do not translate numeric `Phase` into raw Dataflow settings. Select or register
a semantic `processing.profile` resource and reference it through the canonical
component `Processing` property. Components reject unsupported concurrency
before activation.

## Host Migration

Old hosting:

```csharp
var registry = new RuntimeNodeFactoryRegistry();
await using var host = FlowApplicationHost.Create(definition, registry, expressionEngine);
var build = host.Build();
await host.StartBuiltAsync();
```

Canonical hosting:

```csharp
var services = new ServiceCollection();
services.AddSingleton<IFlowExpressionEngine>(expressionEngine);
services
    .AddFluxFlow(definition)
    .AddMappingComponents()
    .AddMyComponents();

await using var provider = services.BuildServiceProvider();
var application = provider.GetRequiredService<FluxFlowApplication>();
var result = await application.StartAsync();
```

Use `application.Ports` for direct stable-port interaction after the first
revision becomes active.

## Checklist

1. Convert and persist each old document once.
2. Replace executable resource nodes with host-owned keyed services.
3. Replace numeric phases with semantic processing profiles.
4. Move custom nodes to `FluxFlow.Nodes` and register descriptors explicitly
   through an `IServiceCollection` family extension.
5. Replace the old host/builder with canonical revision hosting and assembler.
6. Route normal failures from Output result values; observe diagnostics through
   component Events and system signals.
7. Verify conditions, fan-in, fan-out, cross-workflow addresses, revision
   rollback, cleanup, and direct-port access.

No compatibility shim recreates the removed Engine 2 runtime.
