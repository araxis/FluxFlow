# FluxFlow

FluxFlow is a standalone-node-first workflow toolkit for .NET.

The default architecture is:

1. Build reusable nodes over `FluxFlow.Nodes`.
2. Compose those nodes directly with TPL Dataflow, fluent C#, or configuration.
3. Keep resources such as clients, stores, secrets, and protocol adapters owned by the host or adapter package.

`FluxFlow.Engine` remains available as an optional advanced executable runtime
for hosts that need its older `ApplicationDefinition` model, conditional links,
and engine lifecycle. It is no longer the required path for normal component
packages.

## Main Packages

| Package | Purpose |
|---------|---------|
| `FluxFlow.Nodes` | Minimal standalone node kit: `FlowNode`, `FlowSource`, `FlowMessage`, `FlowError`, and `FlowEvent`. |
| `FluxFlow.Composition` | Optional composition layer for fluent C# and `IConfiguration` JSON. It links standalone nodes directly and does not reference `FluxFlow.Engine`. |
| `FluxFlow.Composition.Hosting` | Optional DI/host bridge that builds and starts a composition runtime and resolves adapter-owned keyed resources. |
| `FluxFlow.Engine` | Optional legacy/advanced runtime for `ApplicationDefinition`-based execution. |

Component packages should expose normal standalone nodes first. Composition
factory registration, engine modules, design metadata, and host-specific DI
helpers are optional adapters around those nodes.

## Standalone Node Example

```csharp
public sealed class UppercaseNode : FlowNode<string, string>
{
    protected override Task ProcessAsync(FlowMessage<string> message)
    {
        Emit(message.With(message.Payload.ToUpperInvariant()));
        return Task.CompletedTask;
    }
}
```

Nodes are plain Dataflow processors. Construct them, link their ports, send
`FlowMessage<T>` values, and await completion.

## Composition Example

`FluxFlow.Composition` adds DTOs, explicit factory registration, validation,
and runtime lifecycle around standalone nodes:

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
                outputs: [CompositionPorts.Output<string>("Output", node.Output)],
                events: node.Events,
                errors: node.Errors));
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

There is no reflection, assembly scanning, or engine dependency in this path.

`FluxFlow.Composition.Hosting` can own the host lifecycle around the same model:

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterMyNodes());
```

Adapter packages still own concrete resources and register them in DI, usually
as named keyed services.

## Samples

Run the standalone composition sample:

```sh
dotnet run --project samples/FluxFlow.CompositionSample/FluxFlow.CompositionSample.csproj
```

Run the MQTT composition sample with in-memory adapter resources:

```sh
dotnet run --project samples/FluxFlow.MqttCompositionSample/FluxFlow.MqttCompositionSample.csproj
```

Run the HTTP trigger sample:

```sh
dotnet run --project samples/FluxFlow.HttpTriggerSample/FluxFlow.HttpTriggerSample.csproj
```

Run the engine sample when you need the advanced engine runtime:

```sh
dotnet run --project samples/FluxFlow.SampleApp/FluxFlow.SampleApp.csproj
```

Use `samples/FluxFlow.ComponentPackageTemplate` as the copyable shape for new
component packages.

## Building

```sh
dotnet build FluxFlow.sln
dotnet test FluxFlow.sln
```

## License

FluxFlow is licensed under the MIT License.
