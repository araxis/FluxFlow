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

Definition DTO collection properties copy assigned dictionaries and lists with
ordinal key comparison. A host can still intentionally edit the model before
validation/build, but caller-owned collections used during construction cannot
mutate the definition later. Workflow, node, configuration, and resource
dictionary keys are trimmed when assigned or built fluently; duplicate keys
after trimming are rejected at the composition boundary.
Node and port references trim assigned workflow/node/port segments and reject
empty dotted segments when parsed from fluent or configuration link strings.
Node definition types, node registration types, and composition port metadata
names are trimmed at the public boundary so incidental configuration or
registration whitespace does not create unknown node types or duplicate-looking
ports. Composition port metadata rejects null or blank port names and null
message types at the registration boundary. Node registrations also reject null
port metadata entries before validation/build. `CompositionPortMetadata` also
supports deconstruction for callers that prefer tuple-style reads.
If mutable DTO collections are hand-built with null workflow, node, link, or
link endpoint entries, validation reports `InvalidDefinition` diagnostics
instead of throwing while walking the model.

`ComposedNode` disposal always attempts both the node disposal path and the
optional descriptor cleanup hook. If both fail, the failures are reported
together so cleanup diagnostics do not hide an adapter-owned resource leak.
If a build is canceled after nodes or links have been allocated, the runtime
builder disposes the partially built graph before rethrowing cancellation.

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
