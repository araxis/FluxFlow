# Getting Started

The smallest useful FluxFlow path is standalone-node-first:

1. Add `FluxFlow.Nodes` and the component packages you need.
2. Build or reuse standalone nodes with `FlowMessage<T>` ports.
3. Add `FluxFlow.Composition` for the canonical application model, explicit
   component registration, addresses, resources, and links.
4. Add `FluxFlow.Engine` when the host needs lifecycle, revision replacement,
   or directly addressable runtime ports.

## Install

```sh
dotnet add package FluxFlow.Nodes
dotnet add package FluxFlow.Composition
dotnet add package FluxFlow.Engine
```

## Canonical Application

The executable JSON root has exactly `Resources` and `Workflows`:

```json
{
  "Resources": {},
  "Workflows": {
    "Main": {
      "Source": {
        "Type": "source.items",
        "Items": ["alpha", "beta"],
        "Output": "Map.Input"
      },
      "Map": {
        "Type": "data.map",
        "Expression": "$uppercase(payload)"
      }
    }
  }
}
```

Workflow and component objects are keyed by exact names. Component settings,
resource references, and input/output links are flat. A string is one link, an
array is several links, and an object adds a condition.

## Register And Host

There is no assembly scanning. Register the application and each component
family explicitly in one service collection:

```csharp
using FluxFlow.Engine;

services
    .AddFluxFlow(configuration)
    .AddSources()
    .AddMapping();

var application = provider.GetRequiredService<FluxFlowApplication>();
```

The standard hosted service starts and stops that same singleton. For explicit
control, set `StartWithHost = false` and call:

```csharp
var start = await application.StartAsync();
if (start.IsRejected)
{
    foreach (var diagnostic in start.Diagnostics)
        Console.Error.WriteLine(diagnostic.Error.Code);
}
```

After activation, send, receive, observe, or request/reply by canonical address:

```csharp
var sent = await application.Ports.SendAsync(
    "Main.Map.Input",
    FlowMessage.Create(input));

var output = await application.Ports.ReceiveAsync<JsonElement>(
    "Main.Map.Output",
    TimeSpan.FromSeconds(10));
```

Each component family contributes immutable `ComponentDescriptor` instances.
DI builds one `ComponentCatalog`, which validation, link compilation,
Designer metadata, and Engine activation share. Composition adapters register
revision resources through `IApplicationResourceRegistrar` and standard keyed
DI.

Expected failures are ordinary value-or-error `FlowMessage<T>` values on
`Output`; inspect `IsError` and `Error` when a failure branch is needed.
`Events` carries traced component diagnostics. `Completion` faults only for an
unrecoverable component or lifecycle failure.

Convert an old workflows/nodes/links document with an external, one-time tool,
persist its canonical result, and load it through the same application path.
The shipped runtime has no legacy parser or migration service.

Next: [Definitions And Links](02-definitions-and-links.md).
