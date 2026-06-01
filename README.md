# FluxFlow.Engine

[![Package Version](https://img.shields.io/nuget/vpre/FluxFlow.Engine?label=package)](https://www.nuget.org/packages/FluxFlow.Engine)
[![Package Downloads](https://img.shields.io/nuget/dt/FluxFlow.Engine?label=downloads)](https://www.nuget.org/packages/FluxFlow.Engine)

**A protocol-neutral, dataflow-based workflow engine for .NET.**

Define graphs of typed processing nodes in JSON, build them into live
[TPL Dataflow](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/dataflow-task-parallel-library)
networks, and drive them through a phase-ordered lifecycle — without any
protocol-specific dependencies.

```
┌─────────────────────────────────────────────────────────────────────┐
│  ApplicationDefinition (JSON or code)                               │
│  ┌─────────────┐  ┌────────────────────────────────────────────┐   │
│  │  resources  │  │  workflows.main                             │   │
│  │  ──────────  │  │  ──────────────────────────────────────────│   │
│  │  shared     │  │  source ──→ filter ──→ mapper ──→ sink      │   │
│  └─────────────┘  └────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
         │
         ▼  ApplicationRuntimeBuilder.Build()
┌─────────────────────────────────────────────────────────────────────┐
│  ApplicationRuntime (live TPL Dataflow graph)                       │
│  StateChanges  ──  ISourceBlock<ApplicationStateChanged>            │
│  Events        ──  ISourceBlock<FlowEvent>                          │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Features

| Feature | Description |
|---------|-------------|
| **Typed ports** | `InputPort<T>` / `OutputPort<T>`; type mismatches are caught at build time |
| **Reliable fan-out** | Runtime-owned output delivery sends each item to every linked input |
| **Conditional links** | Link-level `when` expressions route each output item to matching inputs |
| **Phase-ordered startup** | Nodes declare a `phase` integer; the runtime starts lower phases first |
| **Completion propagation** | Entry nodes drive completion through the whole graph |
| **State streams** | `ApplicationRuntime.StateChanges` and `Workflow.StateChanges` are `ISourceBlock<T>` |
| **Event stream** | `ApplicationRuntime.Events` aggregates `FlowEvent` from every `IFlowEventSource` node |
| **Diagnostics stream** | `FlowApplicationHost.Diagnostics` and `ApplicationRuntime.Diagnostics` aggregate node health, status, and metric diagnostics |
| **Expression mapping** | Built-in expression engines behind `IFlowExpressionEngine` |
| **Node authoring helpers** | Base classes and a fluent node builder reduce factory and port boilerplate |
| **Package authoring helpers** | `IFlowNodeModule` and `FlowNodeRegistration` group component families explicitly |
| **JSON definitions** | Full round-trip via `ApplicationDefinitionJson.CreateSerializerOptions()` |
| **Host lifecycle** | `FlowApplicationHost` owns build → start → stop → dispose |

---

## Quick start

### 1. Implement a node

```csharp
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;

public sealed class NumberSource : SourceFlowNode<int>
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext ctx)
    {
        var node = new NumberSource();
        return ctx.CreateNode(node)
            .Output("Output", node.OutputBlock)
            .Build();
    }

    public override async Task StartAsync(CancellationToken ct = default)
    {
        for (var i = 0; i < 10; i++)
            await SendOutputAsync(i, ct);

        CompleteOutput();
    }
}
```

Use `SinkFlowNode<T>`, `TransformFlowNode<TInput,TOutput>`, `MapFlowNode<TInput,TOutput>`,
and `EventFlowNodeBase` when those shapes fit. Direct `IFlowNode` implementations
still work when a node needs full control.

Nodes derived from `FlowNodeBase` can also emit operational diagnostics:

```csharp
TryEmitDiagnostic(
    "demo.node.ready",
    FlowDiagnosticLevel.Information,
    "Node is ready.");
```

The runtime labels each diagnostic with the node address, id, phase, and type.
Use diagnostics for health, status, counters, and live monitoring data. Use
`FlowEvent` for workflow/domain activity.

Subscribe to `FlowApplicationHost.Diagnostics` before `StartAsync` when startup
diagnostics matter:

```csharp
var diagnostics = new BufferBlock<RuntimeFlowDiagnostic>();
host.Diagnostics.LinkTo(diagnostics, new DataflowLinkOptions { PropagateCompletion = true });

var result = await host.StartAsync();
```

### 2. Define the graph in JSON

```json
{
  "workflows": {
    "main": {
      "source": { "type": "demo.numbers" },
      "even": {
        "type": "demo.printer",
        "Input": { "from": "source.Output", "when": "input % 2 == 0" }
      },
      "odd": {
        "type": "demo.printer",
        "Input": { "from": "source.Output", "when": "input % 2 != 0" }
      }
    }
  }
}
```

The default link condition engine exposes the current output item as `input`
and `value`. If no `when` expression is set, the link receives every item.

### 3. Register, build, run

```csharp
var registry = new RuntimeNodeFactoryRegistry();
registry.Register(new NodeType("demo.numbers"), NumberSource.Create);
registry.Register(new NodeType("demo.printer"),  PrinterNode.Create);

var json       = File.ReadAllText("flow.json");
var opts       = ApplicationDefinitionJson.CreateSerializerOptions();
var definition = JsonSerializer.Deserialize<ApplicationDefinition>(json, opts)!;

var host = FlowApplicationHost.Create(definition, registry);

var result = await host.StartAsync();
if (!result.IsSuccess)
    foreach (var e in result.Errors) Console.WriteLine(e.Message);

await host.StopAsync();
await host.DisposeAsync();
```

---

## Documentation

The package README is the short entrypoint. The focused docs set starts at
[docs/README.md](docs/README.md) and covers getting started, definitions, node
authoring, package authoring, hosting, observability, workspace projection, and
validation, error handling, runtime states, JSON conversion, and expression
mapping, package versioning, component composition, and storage host adapters.

---

## Samples

See [samples/FluxFlow.SampleApp](samples/FluxFlow.SampleApp) for a small
consumer-style console app. It keeps app-specific workspace metadata outside the
engine, projects executable resources and workflows into `ApplicationDefinition`,
registers typed components explicitly, and runs conditional links.

See [samples/FluxFlow.MappingControlSample](samples/FluxFlow.MappingControlSample)
for a broker-free component composition sample. It keeps source and sink nodes in
the host, then composes `flow.mapper`, `flow.filter`, `flow.when`, and
`flow.assert` from reusable component packages.

See [samples/FluxFlow.MqttCompositionSample](samples/FluxFlow.MqttCompositionSample)
for an MQTT composition sample backed by an in-memory host adapter. It composes
`mqtt.subscribe`, mapping/control nodes, and `mqtt.publish` without requiring a
live broker.

See [samples/FluxFlow.SessionsCompositionSample](samples/FluxFlow.SessionsCompositionSample)
for a session recording and replay sample. It keeps storage in the host, records
messages through `session.recorder`, then replays them through `session.replay`.

See [samples/FluxFlow.StateCompositionSample](samples/FluxFlow.StateCompositionSample)
for a timer, mapper, state reducer, and counter composition sample. It keeps
host-specific expressions and sinks outside the reusable packages.

See [samples/FluxFlow.StorageCompositionSample](samples/FluxFlow.StorageCompositionSample)
for a logical storage composition sample. It keeps the concrete store in the
host while composing `storage.put`, `storage.get`, and `storage.delete`.

See [samples/FluxFlow.ComponentPackageTemplate](samples/FluxFlow.ComponentPackageTemplate)
for a copyable component package shape with contracts, options, diagnostics,
module registration, and tests.

---

## Component Packages

Reusable components live outside `FluxFlow.Engine` and are released separately.

| Package | Nodes | Purpose |
|---------|-------|---------|
| `FluxFlow.Components.Mqtt` | `mqtt.publish`, `mqtt.subscribe` | Adapter-backed MQTT publish and subscribe nodes. |
| `FluxFlow.Components.Mapping` | `flow.mapper` | Pluggable expression mapping with generic or typed ports. |
| `FluxFlow.Components.Control` | `flow.filter`, `flow.when`, `flow.assert` | Pluggable expression-driven filtering, routing, and assertions. |
| `FluxFlow.Components.Validation` | `json.schema-validator` | JSON schema validation with result, valid, and invalid routing. |
| `FluxFlow.Components.FileSystem` | `file.write`, `file.read`, `file.watch`, `directory.enumerate` | File system operations with package-owned path safety. |
| `FluxFlow.Components.Observability` | `flow.logger`, `flow.metrics`, `flow.counter` | Neutral observer nodes for structured entries, metrics, and counters. |
| `FluxFlow.Components.Timers` | `timer.interval`, `timer.schedule`, `timer.delay`, `timer.throttle`, `timer.debounce` | Interval, cron schedule, delay, rate-limit, and quiet-period timing nodes. |
| `FluxFlow.Components.Sessions` | `session.recorder`, `session.replay` | Host-store-backed session recording and replay. |
| `FluxFlow.Components.State` | `state.reducer` | Per-key state updates through host-provided expression engines. |
| `FluxFlow.Components.Storage` | `storage.put`, `storage.get`, `storage.delete` | Host-store-backed logical record storage. |

---

## Building

```sh
dotnet build FluxFlow.sln
dotnet test  FluxFlow.sln
```

---

## License

FluxFlow.Engine is licensed under the MIT License.

---

## Extension boundary

FluxFlow.Engine does not ship transport, storage, web, or designer components.
Applications add those behaviors by:

1. Implementing `IFlowNode` for each external capability
2. Registering node factories with `RuntimeNodeFactoryRegistry`
3. Composing flows as JSON that FluxFlow.Engine builds and runs

Component packages should own their own options, validation, event names, tests,
and documentation. They can expose an `IFlowNodeModule` or a registry extension
that registers one module.

Applications are free to keep richer workspace files with dashboards, tests,
connections, or UI state. Project only the executable resources and workflows
into `ApplicationDefinition` before building the engine runtime.
