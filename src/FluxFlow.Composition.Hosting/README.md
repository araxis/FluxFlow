# FluxFlow.Composition.Hosting

Optional hosting bridge for `FluxFlow.Composition`.

Use this package when a .NET host wants DI/configuration to own composition
startup while keeping concrete resources in adapter packages.

## Boundary

This package owns:

- registering a single composition runtime with `IServiceCollection`
- loading a `CompositionDefinition` from an object or `IConfiguration`
- building the runtime through `CompositionRuntimeBuilder`
- starting and stopping the runtime through `IHostedService`
- exposing build diagnostics through `ICompositionRuntimeHost`
- resolving named node resources from keyed DI services

It does not own resource creation policies. Adapter packages still own concrete
clients, stores, reconnect behavior, secrets, hosted client lifetime, and
adapter-specific options.

## Registration

```csharp
services.AddKeyedSingleton<IMessageStore>("primary", new InMemoryMessageStore());

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.Register(
        "sample.sink",
        context =>
        {
            var store = context.GetRequiredResource<IMessageStore>("store");
            var node = new StoreSinkNode(store);
            return ValueTask.FromResult(ComposedNode.Create(
                node,
                inputs: [CompositionPorts.Input<string>("Input", node.Input)]));
        },
        inputs: [CompositionPorts.Metadata<string>("Input")]));
```

Configuration records the resource reference by name:

```json
{
  "workflows": {
    "main": {
      "nodes": {
        "sink": {
          "type": "sample.sink",
          "resources": {
            "store": "primary"
          }
        }
      }
    }
  }
}
```

The node factory asks for the local resource slot (`store`), and hosting
resolves the keyed service named `primary`.
Resource slot names passed to the factory helpers and configured keyed service
references are trimmed before lookup, so incidental surrounding whitespace does
not change which host-owned service is resolved.

## Runtime Access

```csharp
var host = services.GetRequiredService<ICompositionRuntimeHost>();

foreach (var diagnostic in host.Diagnostics)
{
    Console.Error.WriteLine(diagnostic.Message);
}
```

By default the hosted service builds and starts the runtime with the host and
throws `CompositionHostingException` if the composition cannot be built.
Hosted and manual start/stop calls are idempotent at the hosting boundary: a
runtime that is already started is not started again, and a runtime that has
already been stopped is not completed or started again.

If you already have the exact section, call `AddFluxFlowCompositionSection(...)`.
