# Validation And Errors

FluxFlow has three error layers. Keep them separate when you build an
application, then flatten them only when you need to show one list to a user.

## Layers

| Layer | API | What it catches |
|-------|-----|-----------------|
| Definition validation | `ApplicationDefinitionValidator.Validate()` | malformed executable definitions and invalid link references |
| Runtime build | `ApplicationRuntimeBuilder.Build()` | unregistered node types, factory failures, missing ports, type mismatches, link failures, cleanup failures |
| Host lifecycle | `FlowApplicationHost.Build()` / `StartAsync()` | configuration loading failures and node startup failures |

Definition validation is pure shape validation. It does not create nodes.
Runtime build creates nodes and wires ports, but does not start the graph.
Host start builds the runtime and then starts the nodes.

## Definition Validation

Use `ApplicationDefinitionValidator` when an application wants to validate a
projected definition before building a runtime:

```csharp
var validator = new ApplicationDefinitionValidator();
var validation = validator.Validate(definition);

if (!validation.IsValid)
{
    foreach (var error in validation.Errors)
    {
        Console.WriteLine($"{error.Code}: {error.Message}");
    }
}
```

`ApplicationDefinitionValidationError` contains:

- `Code`: stable machine-readable reason
- `Message`: human-readable explanation
- `WorkflowName`: workflow scope when known
- `NodeName`: target node when known
- `PortName`: target port when known

Validation error codes:

| Code | Meaning |
|------|---------|
| `EmptyDefinition` | The definition has no workflows. |
| `EmptyWorkflowName` | A workflow dictionary key is empty. |
| `EmptyWorkflow` | A workflow has no nodes. |
| `EmptyNodeName` | A node dictionary key is empty. |
| `EmptyResourceName` | A resource dictionary key is empty. |
| `EmptyNodeType` | A node has no `type`. |
| `InvalidLink` | A link value cannot be parsed as a valid link definition. |
| `MissingSourceNode` | A link references a missing resource, workflow, or node. |
| `EmptySourcePort` | A link source has no output port name. |
| `EmptyTargetPort` | A target port dictionary key is empty. |
| `DuplicateLink` | The same source, target, and condition are declared more than once. |

## Runtime Build

Use `ApplicationRuntimeBuilder` when the application already has a projected
`ApplicationDefinition` and a registry containing all node factories:

```csharp
var builder = new ApplicationRuntimeBuilder(registry);
var build = builder.Build(definition);

if (!build.IsSuccess)
{
    foreach (var error in build.Errors)
    {
        Console.WriteLine($"{error.Code}: {error.Message}");
    }

    return;
}

await using var runtime = build.Runtime!;
```

`ApplicationRuntimeBuildResult` always includes the validation result. If
definition validation fails, each validation error is also returned as a runtime
build error with code `ValidationFailed`.

Runtime build error codes:

| Code | Meaning |
|------|---------|
| `ValidationFailed` | Definition validation failed before node creation. |
| `UnknownNodeType` | No factory is registered for the node type. |
| `FactoryFailed` | A node factory threw while creating a runtime node. |
| `MissingInputPort` | The target node does not expose the requested input port. |
| `MissingOutputPort` | The source node does not expose the requested output port. |
| `PortTypeMismatch` | Source output and target input have different value types. |
| `LinkFailed` | The output could not link to the input. |
| `CleanupFailed` | Runtime build failed, then cleanup also reported one or more failures. |

`WorkflowName`, `NodeName`, and `PortName` point to the target side of the
problem when available. For source-output problems, the message names the source
node and output.

## Host Build And Start

`FlowApplicationHost` wraps runtime build and lifecycle ownership:

```csharp
var host = FlowApplicationHost.Create(definition, registry);
var result = await host.StartAsync();

if (!result.IsSuccess)
{
    foreach (var error in EnumerateHostErrors(result))
    {
        Console.WriteLine(error);
    }
}
```

Host-level errors are stored in `FlowApplicationHostBuildResult.Errors`.
The runtime build result is stored in `FlowApplicationHostBuildResult.RuntimeBuild`.
That means `result.Errors` can be empty while `result.IsSuccess` is false.

Flatten both layers when presenting errors:

```csharp
static IEnumerable<string> EnumerateHostErrors(FlowApplicationHostBuildResult result)
{
    foreach (var error in result.Errors)
        yield return $"{error.Code}: {error.Message}";

    if (result.RuntimeBuild is null)
        yield break;

    foreach (var error in result.RuntimeBuild.Errors)
        yield return $"{error.Code}: {error.Message}";
}
```

Host build error codes:

| Code | Meaning |
|------|---------|
| `InvalidConfiguration` | The host could not load an application definition from configuration. |
| `StartFailed` | Runtime build succeeded, but node startup failed. |

When a node throws during startup, the host records `StartFailed`, sets the
state to `Faulted`, stores the exception in `LastException`, and includes the
node address fields when available.

## Runtime Streams

Build and host errors describe setup and lifecycle failures. Running nodes have
their own streams:

| Stream | Use it for |
|--------|------------|
| `FlowError` | Node processing errors produced by an `IFlowNode`. |
| `FlowDiagnostic` / `RuntimeFlowDiagnostic` | Health, status, counters, and live monitoring data. |
| `FlowEvent` | Workflow or domain events emitted by event-source nodes. |
| `ApplicationStateChanged` / `WorkflowStateChanged` | Runtime and workflow state transitions. |

Use setup errors for validation screens and load failures. Use runtime streams
for logs, dashboards, monitoring, and live operational views.

## App Pattern

For richer applications, keep validation in two passes:

1. Validate the app workspace: UI sections, connection settings, package options,
   scenario files, naming rules, and any domain-specific requirements.
2. Project to `ApplicationDefinition` and run engine validation/build.

This keeps app rules outside the engine while still giving the engine a stable,
structured error surface.

## Troubleshooting

| Symptom | Check |
|---------|-------|
| `ValidationFailed` with `EmptyWorkflow` | The projected workflow has no executable nodes. |
| `ValidationFailed` with `MissingSourceNode` | A link points to a missing resource, workflow, or node name. |
| `UnknownNodeType` | Register the package/module that owns that node type before build. |
| `MissingInputPort` | The target node factory did not expose the named input. |
| `MissingOutputPort` | The source node factory did not expose the named output. |
| `PortTypeMismatch` | Make the source `OutputPort<T>` and target `InputPort<T>` use the same `T`. |
| `LinkFailed` | Check conditional-link support and any custom output port implementation. |
| `StartFailed` | Inspect `LastException`, node diagnostics, and startup hooks. |
| `CleanupFailed` | Treat it as a secondary failure after the primary build error. |

Next: [Runtime States](08-runtime-states.md)
