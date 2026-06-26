# Workspace Projection

Applications often need richer files than a runnable workflow graph needs. A
workspace may include UI state, dashboards, tests, connection settings, layout
metadata, saved resource aliases, or app-specific validation rules. Keep that
model in the application and project only executable workflows into
`CompositionDefinition`.

## App Workspace

An application workspace can wrap composition with app-owned sections:

```csharp
internal sealed record WorkspaceDefinition
{
    public required string Name { get; init; }
    public Dictionary<string, WorkflowDefinition> Workflows { get; init; } = [];
    public Dictionary<string, WorkspaceResourceDefinition> Resources { get; init; } = [];
    public Dictionary<string, WorkspaceViewDefinition> Views { get; init; } = [];
    public Dictionary<string, WorkspaceCheckDefinition> Checks { get; init; } = [];

    public CompositionDefinition ToCompositionDefinition()
        => new()
        {
            Workflows = new Dictionary<string, WorkflowDefinition>(
                Workflows,
                StringComparer.Ordinal)
        };
}
```

`Views`, `Checks`, and app resource catalogs stay outside composition. The
composition layer receives workflows with node definitions, resource slot names,
and links. Host DI still owns the concrete clients, stores, clocks, expression
engines, secrets, and disposal policy.

## Projection Boundary

The workspace-to-composition projection should be a boring copy step:

- copy workflow dictionaries into a new `CompositionDefinition`
- keep app-only sections out of node configuration
- keep resource catalog entries in the app or host layer
- keep persisted node type and port names stable
- avoid runtime service creation during projection

The result is an executable definition that can be validated, built, and linked
without knowing about editor state, dashboards, or environment-specific resource
setup.

## Validation Layers

Use three validation layers:

1. App validation for workspace sections, UI metadata, scenario definitions,
   external connection requirements, and environment rules.
2. Composition validation for workflow structure, known node types, duplicate
   links, missing ports, and static port type mismatches.
3. Factory/build validation for node options and required host-owned resources.

This keeps app concerns out of component packages while still allowing strict
domain rules before any runtime is started.

## Configuration

Apps that only need workflow JSON can load composition directly:

```csharp
var definition = new CompositionConfigurationLoader().Load(configuration);
```

Apps with richer workspace files should own their workspace loader and project
to composition explicitly:

```csharp
var workspace = LoadWorkspace(path);
var definition = workspace.ToCompositionDefinition();

var registry = new CompositionNodeRegistry()
    .RegisterMyNodes();

var build = await new CompositionRuntimeBuilder(registry, services)
    .BuildAsync(definition);
```

Hosted apps can project before registering a static definition source:

```csharp
services
    .AddFluxFlowComposition(new StaticCompositionDefinitionSource(definition))
    .RegisterNodes(registry => registry.RegisterMyNodes());
```

## Resource Catalogs

Workspace files may contain resource catalogs, but composition nodes should only
receive resource slot references:

```json
{
  "nodes": {
    "writer": {
      "type": "storage.put",
      "resources": {
        "store": "primary"
      }
    }
  }
}
```

The host maps `primary` to a keyed service. The workspace can store display
names, connection profiles, design-time warnings, and secret references, but the
composition DTO should not contain live clients, credentials, reconnect policy,
or backend-specific setup.

## Projection Checklist

- Keep app-only sections out of `CompositionDefinition`.
- Validate workspace-specific rules before composition validation.
- Keep package-specific option parsing near the package nodes or adapters.
- Keep concrete service registration in host or adapter-owned DI.
- Project by copying dictionaries when the app may keep using its workspace
  object.
- Build the runtime only from the projected composition definition.

## Optional Engine Projection

`FluxFlow.Engine` still uses `ApplicationDefinition` for hosts that
intentionally choose the older executable runtime:

```csharp
public ApplicationDefinition ToEngineDefinition()
    => new()
    {
        Resources = new Dictionary<string, NodeDefinition>(
            Resources,
            StringComparer.Ordinal),
        Workflows = new Dictionary<string, WorkflowDefinition>(
            Workflows,
            StringComparer.Ordinal)
    };
```

Use this path only when the host needs the engine-specific definition shape,
conditional links, or engine lifecycle APIs. Normal component packages and
workspace files should target standalone nodes and composition first.

## Why This Matters

This boundary lets each app choose its own file format and UI concepts without
forcing those concepts into component packages or the composition runtime. The
same standalone nodes can then be reused by code-first hosts, configuration-first
hosts, and richer design tools.

Next: [Validation And Errors](07-validation-and-errors.md)
