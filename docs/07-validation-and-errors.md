# Validation And Errors

FluxFlow has separate error layers. Keep them separate when you build an
application, then flatten them only when a user-facing view needs one list.

## Layers

| Layer | API | What it catches |
|-------|-----|-----------------|
| Definition validation | `CompositionValidator.Validate()` | malformed composition definitions, unknown node types, missing nodes, duplicate links, static port mismatches |
| Runtime build | `CompositionRuntimeBuilder.BuildAsync()` | factory failures, descriptor/registration mismatches, runtime port mismatches, link failures, cleanup failures |
| Host lifecycle | `ICompositionRuntimeHost.BuildAsync()` / `StartRuntimeAsync()` | configuration loading failures, build diagnostics, hosted start/stop ownership |
| Running nodes | `CompositionRuntime.Events`, `CompositionRuntime.Errors`, `CompositionRuntime.Completion` | processing errors, workflow events, node completion or faults |

Definition validation does not create nodes. Runtime build creates nodes and
wires ports, but does not start source nodes. Host start builds the runtime and
then starts it when configured to do so.

## Composition Definition Validation

Use `CompositionValidator` when an application wants to validate a
`CompositionDefinition` before building a runtime:

```csharp
var validator = new CompositionValidator();
var validation = validator.Validate(definition, registry);

if (!validation.IsValid)
{
    foreach (var diagnostic in validation.Diagnostics)
    {
        Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
    }
}
```

`CompositionDiagnostic` contains:

- `Code`: stable machine-readable reason.
- `Message`: human-readable explanation.
- `WorkflowName`: workflow scope when known.
- `NodeName`: node name when known.
- `Link`: the link involved in a link diagnostic.
- `Exception`: the original exception when one exists.

Validation diagnostic codes:

| Code | Meaning |
|------|---------|
| `EmptyDefinition` | The composition has no workflows. |
| `EmptyWorkflowName` | A workflow dictionary key is empty. |
| `EmptyWorkflow` | A workflow has no nodes. |
| `InvalidDefinition` | A mutable DTO collection contains a null workflow, node, link, or link endpoint. |
| `EmptyNodeName` | A node dictionary key is empty. |
| `EmptyNodeType` | A node has no `type`. |
| `UnknownNodeType` | No composition factory registration exists for the node type. |
| `MissingNode` | A link source or target node does not exist. |
| `MissingInputPort` | The target node registration does not expose the requested input port. |
| `MissingOutputPort` | The source node registration does not expose the requested output port. |
| `DuplicateLink` | The same source and target ports are linked more than once. |
| `PortTypeMismatch` | Source output and target input metadata expose different message types. |

## Runtime Build

Use `CompositionRuntimeBuilder` when the application already has a
`CompositionDefinition` and a `CompositionNodeRegistry` containing all node
factories:

```csharp
var builder = new CompositionRuntimeBuilder(registry);
var build = await builder.BuildAsync(definition, services);

if (!build.Succeeded)
{
    foreach (var diagnostic in build.Diagnostics)
    {
        Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
    }

    return;
}

await using var runtime = build.Runtime!;
```

`CompositionRuntimeBuilder` runs definition validation first. If validation
fails, the same diagnostics are returned in the build result and no node
instances are created.

Additional build diagnostic codes:

| Code | Meaning |
|------|---------|
| `FactoryFailed` | A composition factory threw, or returned no descriptor. |
| `DescriptorPortMismatch` | A factory descriptor did not match registered static port metadata. |
| `LinkFailed` | The output could not link to the input. |
| `CleanupFailed` | Build failed, then link or node cleanup also reported one or more failures. |
| `InvalidConfiguration` | A definition source or configuration loader could not produce a valid definition. |

When build fails after creating nodes, the builder attempts to dispose created
nodes and links before returning diagnostics.

## Host Build And Start

`FluxFlow.Composition.Hosting` wraps definition loading, runtime build, and
host-owned lifecycle:

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterMyNodes());

var host = services.GetRequiredService<ICompositionRuntimeHost>();
var build = await host.BuildAsync();

if (!build.Succeeded)
{
    foreach (var diagnostic in host.Diagnostics)
    {
        Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
    }
}
```

By default the hosted service throws `CompositionHostingException` when build
fails. The exception exposes the same `CompositionDiagnostic` list:

```csharp
catch (CompositionHostingException exception)
{
    foreach (var diagnostic in exception.Diagnostics)
        Console.WriteLine(diagnostic.Message);
}
```

Set `CompositionHostingOptions.ThrowOnBuildFailure = false` when diagnostics
should be collected without throwing. Set `StartRuntimeWithHost = false` when
the host should build but the application should start the runtime manually.

`CompositionConfigurationLoader` throws `CompositionConfigurationException` when
configuration cannot be converted into a `CompositionDefinition`.

## Runtime Streams

Build and host diagnostics describe setup and lifecycle failures. Running nodes
have their own streams:

| Stream | Use it for |
|--------|------------|
| `FlowError` through `CompositionRuntime.Errors` | Node processing errors produced by composed nodes. |
| `FlowEvent` through `CompositionRuntime.Events` | Workflow activity, diagnostics, counters, and domain events emitted by nodes. |
| `CompositionRuntime.Completion` | Completion or fault of the composed graph. |

Use setup diagnostics for validation screens and load failures. Use runtime
streams for logs, dashboards, monitoring, and live operational views.

## App Pattern

For richer applications, keep validation in passes:

1. Validate the app workspace: UI sections, connection settings, package
   options, resource names, scenario files, naming rules, and domain-specific
   requirements.
2. Load or build a `CompositionDefinition`.
3. Validate with `CompositionValidator`.
4. Build with `CompositionRuntimeBuilder` or `ICompositionRuntimeHost`.
5. Subscribe to `Events`, `Errors`, and `Completion` before starting when early
   source output matters.

This keeps app rules outside reusable components while still giving composition
a stable, structured diagnostic surface.

## Troubleshooting

| Symptom | Check |
|---------|-------|
| `EmptyDefinition` | The composition section is missing or has no workflows. |
| `UnknownNodeType` | Register the composition package that owns that node type before build. |
| `MissingNode` | A link points to a missing workflow/node pair. |
| `MissingInputPort` | The target node factory did not expose the named input. |
| `MissingOutputPort` | The source node factory did not expose the named output. |
| `PortTypeMismatch` | Make the source `OutputPort<T>` and target `InputPort<T>` use the same `T`, or add a mapper. |
| `FactoryFailed` | Check node options and required resource mappings. |
| `DescriptorPortMismatch` | Fix the adapter factory so descriptor ports match registration metadata. |
| `LinkFailed` | Check custom port implementations and message type compatibility. |
| `CleanupFailed` | Treat it as a secondary failure after the primary build error. |
| `CompositionHostingException` | Inspect `exception.Diagnostics` or `ICompositionRuntimeHost.Diagnostics`. |
| Runtime `FlowError` | Inspect the node error stream; later messages may still continue depending on the node. |

## Optional Engine Errors

`FluxFlow.Engine` has its own validation and build surface for hosts that still
use `ApplicationDefinition`:

- `ApplicationDefinitionValidator.Validate()`
- `ApplicationRuntimeBuilder.Build()`
- `FlowApplicationHost.Build()` / `StartAsync()`
- `ApplicationRuntimeBuildErrorCode`
- `ApplicationDefinitionValidationErrorCode`

Engine definitions support extension-property input links and inline `when`
conditions. Use the engine error model only when a host intentionally chooses
that older runtime path.

Next: [Runtime States](08-runtime-states.md)
