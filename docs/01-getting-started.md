# Getting Started

The smallest useful FluxFlow path is standalone-node-first:

1. Add `FluxFlow.Nodes`.
2. Build nodes as normal C# objects over `FlowNode` or `FlowSource`.
3. Link nodes directly, or add `FluxFlow.Composition` for fluent/config-based composition.

## Install

```sh
dotnet add package FluxFlow.Nodes
dotnet add package FluxFlow.Composition
dotnet add package FluxFlow.Composition.Hosting
```

Add component packages only when your host needs those nodes or adapters.

## Run The Standalone Composition Sample

From the repository root:

```sh
dotnet run --project samples/FluxFlow.CompositionSample/FluxFlow.CompositionSample.csproj
```

Expected output:

```text
ALPHA
BETA
```

The sample builds a pure in-memory workflow:

```text
source.Output -> upper.Input -> upper.Output -> sink.Input
```

Run the MQTT-shaped composition sample when you want to see keyed adapter
resources in the hosted composition path:

```sh
dotnet run --project samples/FluxFlow.MqttCompositionSample/FluxFlow.MqttCompositionSample.csproj
```

That sample uses an in-memory `IMqttTriggerSource` and `IMqttPublisher` with
the same `mqtt.trigger` and `mqtt.publish` factories a real adapter package
would use.

## Composition Flow

Every composition host follows the same core steps:

1. Register node type strings with explicit factories.
2. Build a `CompositionDefinition` from fluent C# or `IConfiguration`.
3. Validate and build with `CompositionRuntimeBuilder`.
4. Start, observe, stop, and dispose the `CompositionRuntime`.

```csharp
var registry = new CompositionNodeRegistry()
    .Register(
        "sample.uppercase",
        _ =>
        {
            var node = new UppercaseNode();
            return ValueTask.FromResult(ComposedNode.Create(
                node,
                inputs: [CompositionPorts.Input<string>("Input", node.Input)],
                outputs: [CompositionPorts.Output<string>("Output", node.Output)]));
        },
        inputs: [CompositionPorts.Metadata<string>("Input")],
        outputs: [CompositionPorts.Metadata<string>("Output")]);

var definition = CompositionDefinitionBuilder
    .Create()
    .Workflow("main", workflow => workflow
        .Node("upper", "sample.uppercase"))
    .Build();

var result = await new CompositionRuntimeBuilder(registry).BuildAsync(definition);
```

## Engine Path

`FluxFlow.Engine` is still available for hosts that need the older
`ApplicationDefinition` runtime, conditional links, and engine lifecycle:

```sh
dotnet run --project samples/FluxFlow.SampleApp/FluxFlow.SampleApp.csproj
```

Keep app screens, dashboards, resource registration, protocol clients, stores,
and secrets outside reusable component nodes. Composition records resource names;
the host or adapter DI layer owns the actual resources.

When the host should own build/start/stop, register the optional hosting bridge:

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterMyNodes());
```

Node factories can then resolve named resources from adapter-owned keyed DI
services with `context.GetRequiredResource<T>("resourceSlot")`.

Next: [Definitions And Links](02-definitions-and-links.md).
