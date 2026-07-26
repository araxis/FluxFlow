# FluxFlow.Composition

Canonical application definitions and component contracts for
`FluxFlow.Nodes`.

Use this package for the flat canonical application document, shared address
model, explicit component registry, link compilation, runtime-facing component
contracts, and explicit migration of retired Composition documents. Component
packages remain free of `FluxFlow.Engine`.

## Boundary

The canonical boundary owns:

- immutable application, workflow, component, and resource definitions
- strict deterministic JSON with exactly `Resources` and `Workflows`
- nested resource namespaces and flat component/resource settings
- one ordinal, case-sensitive application address value
- canonical input/output-side link parsing and absolute normalization
- compile-once expression conditions and static link diagnostics
- complete-definition revision changes and transitive resource dependency
  planning
- shared revision lifecycle event contracts
- deterministic component and resource alias normalization with migration
  diagnostics
- traced addressable component events and semantic processing profiles
- direct root or named-section `IConfiguration` loading

The executable component boundary also owns:

- explicit component type to factory registration
- reflection-free typed port metadata dispatch for executable hosts
- canonical factory context option binding and keyed resource resolution
- code-first runtime lifecycle ownership for already-linked descriptors
- runtime start, stop, completion, event aggregation, and disposal

This package does not itself activate canonical links or provide stable runtime
ports. `FluxFlow.Engine.Hosting` can assemble these contracts into the optional
Engine port runtime. Composition also does not own broker clients, stores,
secrets, resource registration, file watching, YAML, live reload, assembly
scanning, reflection discovery, or engine projection.

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
`ApplicationDefinitionNormalizer` rewrites registered component aliases and
known resource aliases before validation, revision comparison, Designer
projection, or activation. It returns structured migration diagnostics and is
idempotent.

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

Component registrations declare signal inputs with
`CompositionPorts.SignalMetadata(...)`, while factories expose the matching
`IFlowSignalTarget` through `CompositionPorts.SignalInput(...)`.
`CompositionNodeFactoryContext` accepts one canonical flat
`ComponentDefinition`. `ComponentName` is the workflow object key and defaults
an absent `Name` option for runtime option types that still expose it. Canonical
resource properties such as `Client` resolve through the same keyed-service
methods.

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
data-link cycles. Links into explicitly registered signal ports are bounded
feedback relations and do not create data-cycle edges; a message port remains
part of cycle validation regardless of its name. Valid compiled links are the
input to the stable-port runtime in `FluxFlow.Engine`.

## Output, Events, And Completion

Canonical components use one failure model:

- `Output` carries ordinary success and expected failure values, normally as
  `FlowResult<T>`.
- `Events` carries `FlowMessage<CompositionComponentEvent>` for lifecycle,
  diagnostics, input/output observations, warnings, and metrics. Its address is
  `Workflow.Component.Events` and it may be linked, conditioned, mapped,
  observed, or received like any other output.
- `Completion` faults only for unrecoverable implementation, infrastructure,
  or lifecycle failures.

No universal `Errors` output is added. Component events do not also flow
through `System.Events.Output`; that Engine-owned stream is reserved for
application and revision events, avoiding duplicate emission.

Event forwarding is bounded and fault-isolated. It preserves available
correlation and wraps the component event in the normal `FlowMessage<T>` trace
envelope without turning an event-forwarding failure into a component fault.

## Processing Profiles

Canonical JSON may reference an optional reusable `processing.profile`
resource through one flat `Processing` property:

```json
{
  "Resources": {
    "Processing": {
      "ParallelOrdered": {
        "Type": "processing.profile",
        "Mode": "Parallel",
        "Order": "Preserve",
        "Buffer": "Standard"
      }
    }
  },
  "Workflows": {
    "Orders": {
      "Send": {
        "Type": "http.request",
        "Processing": "Resources.Processing.ParallelOrdered"
      }
    }
  }
}
```

No profile is required for current defaults. The default mapper translates
semantic modes into technical node options; hosts can replace
`ICompositionProcessingProfileMapper` through DI. A registration declares its
supported processing capabilities, and unsupported parallel/order combinations
fail before its factory executes. Standalone C# option properties remain
available for direct-code and compatibility use.

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

## Legacy Document Migration

Version 3 removes the retired Composition DTO, builder, validator, loader, and
runtime-builder families. Runtime loading is canonical-only. When an existing
document still uses `workflows` / `nodes` / `links`, convert it explicitly:

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
using FluxFlow.Composition.Migration;

var definition = new LegacyCompositionDefinitionMigrator()
    .Migrate(configuration);

var canonicalJson = ApplicationDefinitionJson.Serialize(definition);
```

Migration flattens each legacy node's `Configuration` and `Resources` into one
canonical component object and moves separate links onto target input
properties. It rejects collisions, unknown or lossy shapes, missing endpoints,
and existing properties that conflict with migrated links. The returned model
contains no concrete resources because legacy resource slots were host-owned
keyed-service references. Persist the returned canonical definition before
normal loading and activation.

`ComposedNode` disposal always attempts node disposal and its optional cleanup
hook. `CompositionRuntime` similarly attempts every owned node, graph link, and
diagnostic link, aggregates cleanup failures, and leaves runtime completion
faults separately observable.

Use `FluxFlow.Composition.Hosting` with `FluxFlow.Engine.Hosting` when DI should
assemble and run a canonical application with revision lifecycle.

## Sample

Run the pure in-memory sample:

```sh
dotnet run --project samples/FluxFlow.CompositionSample/FluxFlow.CompositionSample.csproj
```
