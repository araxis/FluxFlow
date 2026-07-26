# FluxFlow

FluxFlow is a standalone-node-first workflow toolkit for .NET.

The default architecture is:

1. Build reusable nodes over `FluxFlow.Nodes`.
2. Register component factories explicitly with `FluxFlow.Composition`.
3. Load the canonical application document with exactly `Resources` and
   `Workflows`.
4. Activate it through `FluxFlow.Composition.Hosting` and the optional
   `FluxFlow.Engine` runtime assembler when addressable runtime ports are needed.
5. Keep resources such as clients, stores, secrets, and protocol adapters owned by the host or adapter package.

`FluxFlow.Engine` remains optional for component packages. Canonical hosts use
its runtime assembler for revisions, compiled links, stable direct ports, and
system signals without moving resource ownership into the engine.

## Main Packages

| Package | Purpose |
|---------|---------|
| `FluxFlow.Data` | Immutable transport-neutral `FlowValue`, `FlowContent`, and `FlowResult<T>` contracts. |
| `FluxFlow.Nodes` | Minimal standalone node kit: `FlowNode`, `FlowSource`, `FlowMessage`, `FlowError`, and `FlowEvent`. |
| `FluxFlow.Coordination` | Generic bounded pending exchanges with deterministic timeout, cancellation, and exact-once settlement. |
| `FluxFlow.Resilience` | Transport-neutral retry policy, schedules, state transitions, jitter, and direct-call execution. |
| `FluxFlow.Composition` | Canonical application definitions, aliases, addresses, links, component registrations, events, and processing profiles. |
| `FluxFlow.Composition.Hosting` | DI/host revision lifecycle for complete canonical definitions and immutable resource snapshots. |
| `FluxFlow.Engine` | Optional canonical runtime assembler with stable direct ports and system signals. |

Component packages should expose normal standalone nodes first. Composition
factory registration, design metadata, and host-specific DI helpers are optional
adapters around those nodes. Engine-specific integration is separate from the
normal component package shape.

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

`FluxFlow.Composition` adds strict canonical definitions, explicit factory
registration, normalization, validation, and link compilation around
standalone nodes:

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
                events: node.Events));
        },
        inputs: [CompositionPorts.Metadata<string>("Input")],
        outputs: [CompositionPorts.Metadata<string>("Output")]);

var definition = ApplicationDefinitionJson.Deserialize(json);
var normalized = new ApplicationDefinitionNormalizer(registry).Normalize(definition);
var links = new ApplicationLinkCompiler(registry).Compile(normalized.Definition);
```

There is no reflection, assembly scanning, or engine dependency in this path.

`FluxFlow.Composition.Hosting` and the standard assembler can own the lifecycle
around the same model:

```csharp
services
    .AddFluxFlowApplication(configuration)
    .UseRuntimeAssembler(runtime => runtime.RegisterNodes(registry =>
        registry.RegisterMyNodes()));
```

Adapter packages still own concrete resources and register them in DI, usually
as named keyed services.

Expected failures remain normal `Output` values, usually `FlowResult<T>`.
Every canonical component also exposes traced `Workflow.Component.Events`;
unrecoverable faults remain on `Completion`. Canonical JSON does not expose
Dataflow capacities or parallelism settings. An optional `processing.profile`
resource provides semantic `Mode`, `Order`, and `Buffer` settings when defaults
are not enough.

`FluxFlow.Components.Resilience` provides a standalone retry-controlled
operation node, and its optional Composition adapter registers `flow.retry`.
Ack, Nak, and Cancel are payload-independent signal inputs. Retry attempts keep
one workflow `TraceId`; an internal attempt key prevents late feedback from an
older attempt completing a newer one.

## Samples

Run the canonical application composition sample:

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

Run the advanced canonical host sample for workspace projection, conditional
links, and component Events fan-in:

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
