# FluxFlow.Composition

Canonical application definitions and standalone-first composition for
`FluxFlow.Nodes`.

Use this package for the vNext flat application document and shared address
model. The package also retains the current fluent/config runtime DTOs while
runtime binding migrates to the canonical model. Component packages remain
free of `FluxFlow.Engine`.

## Boundary

The vNext boundary owns:

- immutable application, workflow, component, and resource definitions
- strict deterministic JSON with exactly `Resources` and `Workflows`
- nested resource namespaces and flat component/resource settings
- one ordinal, case-sensitive application address value
- canonical input/output-side link parsing and absolute normalization
- compile-once expression conditions and static link diagnostics
- complete-definition revision changes and transitive resource dependency
  planning
- shared revision lifecycle event contracts
- direct root or named-section `IConfiguration` loading

The current runtime compatibility boundary also owns:

- composition DTOs: workflows, nodes, links, and port references
- explicit node type to factory registration
- fluent C# definition building
- `IConfiguration` loading
- structural validation
- direct typed Dataflow linking
- runtime start, stop, completion, event/error aggregation, and disposal

It does not yet activate canonical links or provide stable runtime ports. It
also does not own broker clients, stores, secrets, resource registration, file
watching, YAML, live reload, assembly scanning, reflection discovery, or
engine projection.

## Canonical Definition

The canonical document has exactly two case-sensitive root objects. Resource
groups omit `Type`; resource leaves and workflow components require it.

```json
{
  "Resources": {
    "Messaging": {
      "Broker1": {
        "Type": "sample.broker",
        "Host": "localhost"
      },
      "Client1": {
        "Type": "sample.client",
        "Broker": "Resources.Messaging.Broker1"
      }
    }
  },
  "Workflows": {
    "Orders": {
      "Source": {
        "Type": "sample.source"
      },
      "Sink": {
        "Type": "sample.sink",
        "Input": "Source.Output"
      }
    }
  }
}
```

```csharp
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Model;

var definition = ApplicationDefinitionJson.Deserialize(json);
var absolute = ApplicationAddress.ResolvePort("Sink.Input", "Orders");
var fromConfiguration = new ApplicationDefinitionConfigurationLoader()
    .Load(configuration, "Application");
```

Names and addresses are exact and case-sensitive. Component addresses use
`Workflow.Component`; workflow port addresses use `Workflow.Component.Port`;
local port references use `Component.Port`; resources use
`Resources.Group.Resource`. Parsing a two-segment absolute address produces a
component address, while `ResolvePort("Component.Port", currentWorkflow)` keeps
the same local-port behavior. `System.Events.Output` and
`System.Diagnostics.Output` are reserved system addresses. The canonical
serializer sorts names and nested object properties for deterministic output.

Link properties may be declared on exactly one endpoint. Direction is inferred
from the registered component port metadata:

```json
{
  "Type": "sample.source",
  "Output": [
    "Sink.Input",
    {
      "Port": "Audit.Writer.Input",
      "Condition": "value != null"
    }
  ]
}
```

```csharp
using FluxFlow.Composition.Links;

var compilation = new ApplicationLinkCompiler(registry, expressionEngine)
    .Compile(definition);

if (!compilation.IsValid)
{
    foreach (var diagnostic in compilation.Diagnostics)
        Console.Error.WriteLine(diagnostic.Message);
}
```

Strings, objects with exact `Port` and optional `Condition` names, and mixed
arrays are supported. Every successful link has absolute source and target
addresses and records whether it was declared input-side or output-side.
Payload types must match exactly for message ports; links never insert a
mapper. A payload-independent signal input accepts any source message type and
preserves the source envelope identity. Multiple links are allowed by default.
Register a port with
`CompositionPortLinkCardinality.Single` when it permits only one claim.

Node registrations declare signal inputs with
`CompositionPorts.SignalMetadata(...)`, while factories expose the matching
`IFlowSignalTarget` through `CompositionPorts.SignalInput(...)`. The standalone
runtime forwards source envelopes to signal targets without taking ownership
of target completion. `CompositionNodeFactoryContext` accepts either the
legacy node DTO or a canonical flat `ComponentDefinition`; canonical resource
properties such as `Client` resolve through the same keyed-service methods.

Conditions use `FluxFlow.Mapping.IFlowExpressionEngine` and compile once for
each distinct expression during one compiler invocation. `IsMatch(...)`
evaluates a compiled condition; `TryMatch(...)` turns an evaluation exception
into a rejected link plus an exception value so a runtime can report that link
failure and continue evaluating siblings.

`System.Events.Output` and `System.Diagnostics.Output` are Engine-owned.
Hosts compiling a link from either stream provide
`ApplicationSystemOutputMetadata` with its payload type; missing metadata or
an incompatible target is a static diagnostic. Composition remains free of an
Engine reference.

Compilation rejects malformed declarations, unknown component types, missing
ports, duplicate endpoint pairs (including a link declared on both sides),
single-link claim conflicts, incompatible types, invalid expressions, and
component cycles. Valid compiled links are the input to the stable-port
runtime in `FluxFlow.Engine`; the legacy runtime below is unchanged.

## Revision Planning

`ApplicationRevisionPlanner` compares the current and next complete canonical
definitions. It reports added, updated, and removed resources and workflows,
then expands changed resources through transitive resource dependents and the
workflows that reference them. Missing resource references and resource cycles
reject the plan before a host prepares replacements.

```csharp
using FluxFlow.Composition.Revisions;

var plan = new ApplicationRevisionPlanner().Plan(current, next);
if (!plan.IsValid)
{
    foreach (var diagnostic in plan.Diagnostics)
        Console.Error.WriteLine(diagnostic.Message);
}
```

`ApplicationRevisionEvent` and `IApplicationRevisionEventSink` are the shared,
Engine-independent lifecycle boundary for `Proposed`, `Accepted`, `Rejected`,
`Activated`, `Draining`, and `Disposed` phases. Composition computes the plan;
it does not create providers, start candidates, switch routing, or drain old
revisions.

## Legacy Runtime Definition

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
Multiple outputs may target the same input. Data links do not independently
complete that shared input; the runtime completes it only after every upstream
output succeeds, or faults it when the first upstream faults.
Runtime disposal attempts every node, graph link, and diagnostic link even when
earlier cleanup fails, then reports cleanup failures together in an
`AggregateException`. Runtime `Completion` remains the separate node-failure
observation path.

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

## Legacy Configuration Shape

The existing runtime loader reads `FluxFlow:Composition`:

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
the concrete resource registration and lifetime. Node factories resolve those
references with the `CompositionNodeFactoryContext` instance methods
`GetRequiredResourceKey`, `GetRequiredResource<TResource>`, and
`GetResource<TResource>` over the keyed services the host registered.

Use `FluxFlow.Composition.Hosting` when DI should build and start the runtime
with host lifecycle.

## Sample

Run the pure in-memory sample:

```sh
dotnet run --project samples/FluxFlow.CompositionSample/FluxFlow.CompositionSample.csproj
```
