# Hosting And Observability

`FlowApplicationHost` owns the lifecycle of one engine runtime.

## Build And Start

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterSampleOrderComponents(store);

await using var host = FlowApplicationHost.Create(definition, registry);

var build = host.Build();
if (!build.IsSuccess)
{
    foreach (var error in build.Errors)
        Console.Error.WriteLine(error.Message);

    return;
}

var start = await host.StartBuiltAsync();
```

Use `StartAsync()` when the app does not need to inspect the built runtime before
startup. Use `Build()` plus `StartBuiltAsync()` when the app needs to attach
subscribers first.

## Runtime Streams

`ApplicationRuntime` exposes:

- `StateChanges`: application state transitions.
- `Events`: aggregated `FlowEvent` records from event source nodes.
- `Diagnostics`: enriched node diagnostics.

`Workflow` exposes state changes for each workflow.

## Subscribe Before Startup

When startup diagnostics or events matter, build first, attach subscribers, then
start the built runtime:

```csharp
var build = host.Build();
if (!build.IsSuccess || host.Runtime is null)
    return;

var events = new BufferBlock<FlowEvent>(
    new DataflowBlockOptions { BoundedCapacity = 128 });

host.Runtime.Events.LinkTo(
    events,
    new DataflowLinkOptions { PropagateCompletion = true });

await host.StartBuiltAsync();
```

`FlowApplicationHost.Diagnostics` can also be linked before calling
`StartAsync()`. Host diagnostics aggregate runtime diagnostics after a runtime is
built.

## Stop And Dispose

```csharp
await host.StopAsync();
await host.DisposeAsync();
```

`StopAsync()` completes entry nodes and waits for graph completion. Dispose
releases runtime links, output pumps, collectors, and nodes.

## Failure Surface

Build failures return `FlowApplicationHostBuildResult` errors.

Startup failures return host build errors and dispose the failed runtime
best-effort.

Node processing failures can be reported through node error streams. Diagnostic
records are for status and monitoring; event records are for workflow activity.

Next: [Workspace Projection](06-workspace-projection.md).
