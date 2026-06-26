# Hosting And Observability

The default hosted path is `FluxFlow.Composition.Hosting`. It loads a
`CompositionDefinition`, builds a `CompositionRuntime`, starts source nodes, and
keeps concrete resources in host-owned DI.

## Build And Start

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterMyNodes());
```

The hosted service builds and starts the runtime with the .NET host by default.
Use `ICompositionRuntimeHost` when the application needs to inspect diagnostics,
attach subscribers, or manually control start and stop:

```csharp
var host = services.GetRequiredService<ICompositionRuntimeHost>();

var build = await host.BuildAsync();
if (!build.Succeeded)
{
    foreach (var diagnostic in build.Diagnostics)
        Console.Error.WriteLine(diagnostic.Message);

    return;
}

var runtime = build.Runtime!;
await host.StartRuntimeAsync();
```

Use `StartRuntimeWithHost = false` when startup should build only. Use
`BuildAsync()` plus `StartRuntimeAsync()` when the app needs to attach
subscribers first.

## Runtime Streams

`CompositionRuntime` exposes:

- `Events`: aggregated `FlowEvent` records from composed nodes.
- `Errors`: aggregated `FlowError` records from composed nodes.
- `Completion`: a task that completes when all composed nodes finish.

Each node still owns its typed input and output ports. Composition hosting only
aggregates observability streams and lifecycle.

## Subscribe Before Startup

When startup diagnostics, source events, or early errors matter, build first,
attach subscribers, then start the runtime:

```csharp
var host = services.GetRequiredService<ICompositionRuntimeHost>();
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

`ICompositionRuntimeHost.Diagnostics` contains build diagnostics. Runtime events
and errors flow through the built `CompositionRuntime`.

## Resource Resolution

Composition definitions name local resource slots. Adapter packages decide which
slots they need, and the host maps those slots to keyed services:

```json
{
  "workflows": {
    "main": {
      "nodes": {
        "writer": {
          "type": "storage.put",
          "resources": {
            "store": "primary"
          }
        }
      }
    }
  }
}
```

The node factory asks for the local slot:

```csharp
var store = context.GetRequiredResource<IStorageStore>("store");
```

The hosting bridge resolves the configured keyed service named `primary`.
Concrete clients, stores, reconnect policies, secrets, and disposal ownership
stay with the host or adapter package.

## Stop And Dispose

```csharp
await host.StopRuntimeAsync();
await ((IAsyncDisposable)host).DisposeAsync();
```

`StopRuntimeAsync()` completes entry nodes and waits for graph completion.
Dispose releases runtime links, diagnostic subscriptions, collectors, and nodes.
Hosted start/stop calls are idempotent at the hosting boundary.

## Failure Surface

Build failures return `CompositionDiagnostic` records. By default the hosted
service throws `CompositionHostingException` when the runtime cannot be built.
Set `ThrowOnBuildFailure = false` when diagnostics should be captured without
throwing.

Node processing failures can be reported through node error streams. Diagnostic
records are for build and status; event records are for workflow activity.

## Optional Engine Host

`FluxFlow.Engine` remains available for hosts that intentionally use the older
`ApplicationDefinition` runtime:

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterSampleOrderComponents(store);

await using var host = FlowApplicationHost.Create(definition, registry);
var build = host.Build();
```

Use this path when an application still needs the engine-specific executable
model, conditional links, or engine lifecycle APIs. Component packages should
still expose standalone nodes first and keep engine modules out of normal
component packages.

Next: [Workspace Projection](06-workspace-projection.md).
