# FluxFlow.Composition

Optional standalone-first composition for `FluxFlow.Nodes`.

Use this package when a host wants to build a workflow from fluent C# or
`Microsoft.Extensions.Configuration` JSON while keeping component packages free
of `FluxFlow.Engine`.

## Boundary

`FluxFlow.Composition` owns:

- composition DTOs: workflows, nodes, links, and port references
- explicit node type to factory registration
- fluent C# definition building
- `IConfiguration` loading
- structural validation
- direct typed Dataflow linking
- runtime start, stop, completion, event/error aggregation, and disposal

It does not own broker clients, stores, secrets, resource registration, file
watching, YAML, live reload, assembly scanning, reflection discovery, or engine
projection.

## Fluent Composition

```csharp
var registry = new CompositionNodeRegistry()
    .Register(
        "sample.source",
        context =>
        {
            var options = context.BindConfiguration<SourceOptions>();
            var node = new StringSourceNode(options.Messages);
            return ValueTask.FromResult(ComposedNode.Create(
                node,
                outputs: [CompositionPorts.Output<string>("Output", node.Output)],
                events: node.Events,
                errors: node.Errors));
        },
        outputs: [CompositionPorts.Metadata<string>("Output")]);

var definition = CompositionDefinitionBuilder
    .Create()
    .Workflow("main", workflow => workflow
        .Node("source", "sample.source", node => node.Configure("messages", new[] { "alpha" }))
        .Node("sink", "sample.sink")
        .Link("source.Output", "sink.Input"))
    .Build();

var result = await new CompositionRuntimeBuilder(registry).BuildAsync(definition, services);
if (!result.Succeeded)
{
    foreach (var diagnostic in result.Diagnostics)
        Console.Error.WriteLine(diagnostic.Message);
}

await using var runtime = result.Runtime!;
await runtime.StartAsync();
await runtime.Completion;
```

## Configuration Shape

The default loader reads `FluxFlow:Composition`:

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "source": {
              "type": "sample.source",
              "configuration": {
                "messages": [ "alpha", "beta" ]
              },
              "resources": {
                "store": "primary-store"
              }
            },
            "sink": {
              "type": "sample.sink"
            }
          },
          "links": [
            { "from": "source.Output", "to": "sink.Input" }
          ]
        }
      }
    }
  }
}
```

```csharp
var definition = new CompositionConfigurationLoader().Load(configuration);
```

Resources are named references only. The host or adapter DI layer still owns
the concrete resource registration and lifetime.

Use `FluxFlow.Composition.Hosting` when DI should build and start the runtime
and node factories should resolve those resource references from keyed services.

## Sample

Run the pure in-memory sample:

```sh
dotnet run --project samples/FluxFlow.CompositionSample/FluxFlow.CompositionSample.csproj
```
