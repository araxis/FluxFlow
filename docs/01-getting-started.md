# Getting Started

The smallest useful FluxFlow path is instance-first:

1. Add `FluxFlow.Core` and the component packages you need.
2. Construct components directly and add their instances to application workflows.
3. Connect their canonical `FlowMessage` ports with `LinkTo` / `LinkFrom`.
4. Add optional authoring or hosting packages only when needed.

Ordinary hosting uses `services.AddFluxFlow(application)` with an already built
Core `FlowApplication`. It starts and drains the graph without a revision source,
snapshot or component creator. The caller retains graph disposal ownership.
See the Engine package's direct graph hosting example.

The definition-based examples below currently use the explicit
`AddFluxFlowUpdates(...)` opt-in. This is the renamed definition/configuration
hosting API; its revision runtime is still being migrated to the Core executor.

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

Compiled C# is an independent first-class source. Complete component/resource
contracts carry their executable behavior into the built definition, and typed
handles remove port/address strings from normal application code:

```csharp
var builder = new ApplicationDefinitionBuilder()
    .AddWorkflow("Main", out var main);

main
    .AddComponent("First", SampleComponents.Uppercase, out var first)
    .AddComponent("Second", SampleComponents.Uppercase, out var second);

first.Output.LinkTo(second.Input);
var definition = builder.Build();

services.AddFluxFlowUpdates(definition);
```

Do not repeat the contracts through service registration. See
[End-To-End Code-First Simplification](41-end-to-end-code-first-simplification.md).

## Register And Host

There is no assembly scanning. A JSON/configuration host registers the
application and each component/resource family explicitly in one service
collection:

```csharp
using FluxFlow.Engine;

services
    .AddFluxFlowUpdates(configuration)
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

var output = await application.Ports.ReceiveAsync<FlowValue>(
    "Main.Map.Output",
    TimeSpan.FromSeconds(10));
```

For the compiled-C# definition above, keep the handles instead:

```csharp
var output = application.Ports.ReceiveAsync(second.Output);
var sent = await application.Ports.SendAsync(
    first.Input,
    FlowMessage.Create("hello"));
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
