# Getting Started

The smallest useful FluxFlow path is standalone-node-first:

1. Add `FluxFlow.Nodes` and the component packages you need.
2. Build or reuse standalone nodes with `FlowMessage<T>` ports.
3. Add `FluxFlow.Composition` for the canonical application model, explicit
   component registration, addresses, and links.
4. Add `FluxFlow.Composition.Hosting` plus `FluxFlow.Engine` when the host needs
   revision lifecycle and directly addressable runtime ports.

## Install

```sh
dotnet add package FluxFlow.Nodes
dotnet add package FluxFlow.Composition
dotnet add package FluxFlow.Composition.Hosting
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
resource references, and input/output link declarations are flat. A string is
one link; an array is several links; an object adds a condition.

## Register And Host

There is no assembly scanning. Register package factories explicitly:

```csharp
services
    .AddFluxFlowApplication(configuration)
    .UseRuntimeAssembler(runtime => runtime.RegisterNodes(registry => registry
        .RegisterGeneratedSource()
        .RegisterMapper()));
```

The host normalizes compatibility aliases before validation and activation.
New saves use canonical type names.

After activation, send, receive, or observe by canonical address:

```csharp
var ports = provider.GetRequiredService<IApplicationRuntimeAccess>()
    .GetRequiredPorts();

var output = ApplicationAddress.Parse("Main.Map.Output");
var events = ApplicationAddress.Parse("Main.Map.Events");
```

Expected failures are ordinary `Output` values, normally `FlowResult<T>`.
`Events` carries traced component diagnostics. `Completion` faults only for an
unrecoverable component or lifecycle failure.

## Samples

Run the in-memory composition sample:

```sh
dotnet run --project samples/FluxFlow.CompositionSample/FluxFlow.CompositionSample.csproj
```

Run the MQTT composition sample for host-owned keyed resources:

```sh
dotnet run --project samples/FluxFlow.MqttCompositionSample/FluxFlow.MqttCompositionSample.csproj
```

Obsolete `CompositionDefinition` builders and the older runtime host remain for
existing applications but are not the starting point for new persisted
definitions.

Next: [Definitions And Links](02-definitions-and-links.md).
