# Runtime Lifecycle

The default runtime path is `CompositionRuntime`. It does not expose a state enum.
Its lifecycle is observable through build diagnostics, host method results,
`Completion`, `Events`, and `Errors`.

## Composition Host Lifecycle

`FluxFlow.Composition.Hosting` wraps composition build/start/stop for .NET hosts:

| Surface | Meaning |
|---------|---------|
| `ICompositionRuntimeHost.Runtime` | The currently built runtime, or `null` when build failed or has not run. |
| `ICompositionRuntimeHost.Diagnostics` | Last build diagnostics. |
| `ICompositionRuntimeHost.Completion` | Current runtime completion task, or a completed task when no runtime exists. |
| `BuildAsync()` | Loads the definition, validates it, creates nodes, links the graph, and stores diagnostics. |
| `StartRuntimeAsync()` | Builds if needed and starts source nodes. |
| `StopRuntimeAsync()` | Completes entry nodes and waits for graph completion. |

`CompositionBuildResult.Succeeded` is true only when a runtime exists and there
are no diagnostics:

```csharp
var host = services.GetRequiredService<ICompositionRuntimeHost>();

var build = await host.BuildAsync();
if (!build.Succeeded || build.Runtime is null)
{
    foreach (var diagnostic in build.Diagnostics)
        Console.Error.WriteLine(diagnostic.Message);

    return;
}

var runtime = build.Runtime;
```

By default, the hosted service builds and starts the runtime with the .NET host.
Use `StartRuntimeWithHost = false` when the application needs to attach
subscribers after build and before source startup.

## Build Phase

`CompositionRuntimeBuilder.BuildAsync(...)` performs build work in this order:

1. Validate the `CompositionDefinition` against the `CompositionNodeRegistry`.
2. Invoke each registered factory with a `CompositionNodeFactoryContext`.
3. Validate that each produced descriptor exposes the expected ports.
4. Link output ports to input ports.
5. Return `CompositionBuildResult.Success(runtime)` or diagnostics.

If a factory fails, returns `null`, exposes the wrong ports, or a link cannot be
created, the builder cleans up created nodes and returns diagnostics instead of a
partial runtime.

## Start Phase

`CompositionRuntime.StartAsync()` starts every composed node that implements
`IFlowSource`:

```csharp
await runtime.StartAsync(cancellationToken);
```

Source nodes own their own startup rules. A file watcher, timer source, replay
source, or generated source starts here. Transform and sink nodes are already
ready after construction because their input queues and output streams exist as
standalone node ports.

When using `ICompositionRuntimeHost`, call:

```csharp
await host.StartRuntimeAsync(cancellationToken);
```

`StartRuntimeAsync()` builds first when no runtime has been built yet.

## Observability Streams

`CompositionRuntime` aggregates node observability:

| Stream | Source |
|--------|--------|
| `Events` | Linked from node descriptor event streams. |
| `Errors` | Linked from node descriptor error streams. |
| `Completion` | Completes when all node completion tasks complete. |

Subscribe before startup when source startup events or early errors matter:

```csharp
var build = await host.BuildAsync();
if (!build.Succeeded || build.Runtime is null)
    return;

var events = new BufferBlock<FlowEvent>(
    new DataflowBlockOptions { BoundedCapacity = 128 });
var errors = new BufferBlock<FlowError>(
    new DataflowBlockOptions { BoundedCapacity = 128 });

build.Runtime.Events.LinkTo(
    events,
    new DataflowLinkOptions { PropagateCompletion = true });
build.Runtime.Errors.LinkTo(
    errors,
    new DataflowLinkOptions { PropagateCompletion = true });

await host.StartRuntimeAsync();
```

Events describe workflow activity. Errors describe per-message or node failures
where the node can surface an error stream. Build diagnostics remain separate and
belong to `CompositionBuildResult.Diagnostics` and `ICompositionRuntimeHost.Diagnostics`.

## Stop Phase

`CompositionRuntime.StopAsync()` completes entry nodes and waits for all node
completion tasks:

```csharp
await runtime.StopAsync(cancellationToken);
```

Entry nodes are nodes with no incoming composition links. If every node has an
incoming link, the runtime completes all nodes. This keeps ordinary source-first
graphs simple while still letting cyclic or manually fed graphs stop.

`ICompositionRuntimeHost.StopRuntimeAsync()` wraps this behavior and applies
`CompositionHostingOptions.StopTimeout` when it is greater than zero.

## Disposal

`CompositionRuntime.DisposeAsync()` disposes composed nodes in reverse order,
disposes graph links and diagnostic subscriptions, and then observes completion.
Dispose keeps attempting teardown even if one node dispose path throws. Runtime
completion remains the observable failure path.

`CompositionRuntimeHost.DisposeAsync()` disposes the current runtime and prevents
later build/start calls on that host instance.

## Hosting Options

`CompositionHostingOptions` controls the hosted boundary:

| Option | Default | Meaning |
|--------|---------|---------|
| `StartRuntimeWithHost` | `true` | Build and start with the .NET host. |
| `StopRuntimeWithHost` | `true` | Stop the runtime during hosted service stop. |
| `ThrowOnBuildFailure` | `true` | Throw `CompositionHostingException` when build fails. |
| `StopTimeout` | 30 seconds | Maximum stop wait when stopping through the host. |

Use `ThrowOnBuildFailure = false` when an application wants to inspect
diagnostics without failing host startup.

## Dashboard Pattern

For application dashboards:

1. Show whether `ICompositionRuntimeHost.Runtime` exists.
2. Show the latest build diagnostics from `ICompositionRuntimeHost.Diagnostics`.
3. Track `Completion` as running, completed, canceled, or faulted.
4. Link `Events` and `Errors` before startup for live activity rows.
5. Treat start/stop method failures as host orchestration failures.
6. Keep app-specific UI states outside the composition runtime.

Composition gives hosts the lifecycle signals; the application decides how those
signals map to UI states, health checks, logs, or operator actions.

## Optional Engine States

`FluxFlow.Engine` still exposes the older state enum model for hosts that
intentionally use `ApplicationDefinition`:

- `FlowApplicationHostState`
- `ApplicationState`
- `WorkflowState`
- `ApplicationStateChanged`
- `WorkflowStateChanged`

Use those APIs only on the optional engine runtime path. Composition-first hosts
should observe build diagnostics, events, errors, completion, and host method
results instead.

Next: [JSON Conversion](09-json-conversion.md)
