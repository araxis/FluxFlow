# Workspace Projection

Applications often need editor layout, dashboards, tests, deployment settings,
and other data that is not executable workflow configuration. Keep that state
in an app-owned workspace or sidecar and project only executable resources and
workflows into `FluxFlow.Json.Model.ApplicationDefinition`.

The canonical executable JSON root remains exactly:

```json
{
  "Resources": {},
  "Workflows": {}
}
```

Do not add `Composition`, `Nodes`, `Links`, layout, or product-specific wrapper
sections to that document.

## Projection Boundary

The workspace-to-application projection should:

- create immutable `ResourceDefinition`, `WorkflowDefinition`, and
  `ComponentDefinition` values
- use workflow and component object keys as canonical identities
- keep component settings, resource references, and port links flat
- retain only executable resource definitions under `Resources`
- leave UI layout, display names, saved views, and tests in app-owned storage
- avoid creating services or opening external resources

```csharp
using FluxFlow.Json.Model;

internal static ApplicationDefinition ToApplicationDefinition(
    Workspace workspace)
    => new(
        workspace.Resources.Select(ProjectResource),
        workspace.Workflows.Select(ProjectWorkflow));
```

After projection, compile links against the constructed component instances before starting execution:

```csharp
// instances contains constructed ComponentInstances keyed by workflow.component.
var definition = ToApplicationDefinition(workspace);
var compilation = new ApplicationLinkCompiler(instances).Compile(definition);
```

An unknown component type remains a validation error. Neither the projection
boundary nor Designer persistence rewrites aliases; persist canonical type names
before loading the workspace.

## Validation Layers

1. Workspace validation owns UI state, deployment settings, scenario rules,
   and product-specific requirements.
2. Canonical model and link validation own names, resource shape, registered
   component types, addresses, port existence, link cardinality, exact payload
   types, conditions, and cycles.
3. Runtime preparation owns component options, host-owned resources,
   processing-profile support, and actual port binding.

Setup failures reject a revision without taking down the surrounding host. An
active prior revision remains active.

## Resource Boundary

Canonical resources describe reusable configuration or host-owned state:

```json
{
  "Resources": {
    "Storage": {
      "Primary": {
        "Type": "host.storage-store",
        "Path": "data"
      }
    }
  },
  "Workflows": {
    "Orders": {
      "Save": {
        "Type": "storage.put",
        "Store": "Resources.Storage.Primary"
      }
    }
  }
}
```

The host maps `Resources.Storage.Primary` to a keyed service and owns its
lifetime. Definitions contain no live clients, stores, secret values, or DI
providers.

## Activation

Register the projected canonical definition with the revision host:

```csharp
services
    .AddFluxFlowUpdates(definition)
    .AddMyComponents();
```

Do not convert the canonical model back to the retired workflows/nodes/links
shape or to older Engine definition DTOs. Convert old input externally toward
the canonical model, never away from it.

Next: [Validation And Errors](07-validation-and-errors.md)
