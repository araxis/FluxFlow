# Workspace Projection

Applications often need richer files than the engine needs. A workspace may
include UI state, dashboards, tests, connection settings, or app-specific
metadata. Keep that model in the application and project only executable
resources and workflows into `ApplicationDefinition`.

## App Workspace

The sample app uses this shape:

```csharp
internal sealed record SampleWorkspaceDefinition
{
    public required string Name { get; init; }
    public Dictionary<string, NodeDefinition> Resources { get; init; } = [];
    public Dictionary<string, WorkflowDefinition> Workflows { get; init; } = [];
    public Dictionary<string, SampleViewDefinition> Views { get; init; } = [];
    public Dictionary<string, SampleCheckDefinition> Checks { get; init; } = [];

    public ApplicationDefinition ToEngineDefinition()
        => new()
        {
            Resources = new Dictionary<string, NodeDefinition>(Resources, StringComparer.Ordinal),
            Workflows = new Dictionary<string, WorkflowDefinition>(Workflows, StringComparer.Ordinal)
        };
}
```

`Views` and `Checks` stay outside the engine. The engine receives only
`Resources` and `Workflows`.

## Validation Layers

Use two validation layers:

1. App validation for workspace sections, UI metadata, scenario definitions,
   external connection requirements, and package-specific options.
2. Engine validation for executable definition shape and links.

This keeps the engine protocol-neutral while still allowing applications to have
strict domain rules.

## Configuration

The engine host can load an `ApplicationDefinition` from configuration, but
applications with richer workspace files usually own their own loader:

```csharp
var workspace = LoadWorkspace(path);
var definition = workspace.ToEngineDefinition();
var host = FlowApplicationHost.Create(definition, registry);
```

## Projection Checklist

- Keep app-only sections out of `ApplicationDefinition`.
- Keep app-specific validation before runtime build.
- Keep package-specific option parsing near the package nodes.
- Keep persisted node type and port names stable.
- Project by copying dictionaries when the app may keep using its workspace
  object.
- Build the runtime only from the projected executable definition.

## Why This Matters

This boundary lets each app choose its own file format and UI concepts without
forcing those concepts into the engine package. The same engine runtime can then
be reused by apps with very different workspace models.
