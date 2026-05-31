# FluxFlow.Engine

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
| **BroadcastBlock fan-out** | Every `OutputPort<T>` wraps its source in a `BroadcastBlock<T>` automatically |
| **Phase-ordered startup** | Nodes declare a `phase` integer; the runtime starts lower phases first |
| **Completion propagation** | Entry nodes drive completion through the whole graph |
| **State streams** | `ApplicationRuntime.StateChanges` and `Workflow.StateChanges` are `ISourceBlock<T>` |
| **Event journal** | `ApplicationRuntime.Events` aggregates `FlowEvent` from every `IFlowEventSource` node |
| **Expression mapping** | Built-in DynamicExpresso (C#) and JSONata engines behind `IFlowExpressionEngine` |
| **Scenario testing** | Deterministic, step-by-step test runner driven by `FlowEvent` observations |
| **JSON definitions** | Full round-trip via `ApplicationDefinitionJson.CreateSerializerOptions()` |
| **Host lifecycle** | `FlowApplicationHost` owns build → start → stop → dispose |

---

## Quick start

### 1. Implement `IFlowNode`

```csharp
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Core;
using FluxFlow.Engine.Runtime;
using System.Threading.Tasks.Dataflow;

public sealed class NumberSource : IFlowNode, IFlowEventSource
{
    private readonly BufferBlock<int> _output = new();
    private readonly TaskCompletionSource _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly BroadcastBlock<FlowEvent> _events = new(e => e);

    public FlowNodeId Id { get; } = FlowNodeId.New();
    public Task Completion => _done.Task;
    public ISourceBlock<FlowError> Errors { get; } = new BufferBlock<FlowError>();
    public ISourceBlock<FlowEvent> Events => _events;

    public static RuntimeNode Create(RuntimeNodeFactoryContext ctx)
    {
        var node = new NumberSource();
        return RuntimeNode.Create(
            ctx.Address,
            node,
            outputs: [
                new OutputPort<int>(
                    ctx.Address.Port(new PortName("Output")),
                    node._output)
            ]);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        for (var i = 0; i < 10; i++)
            await _output.SendAsync(i, ct);
        _output.Complete();
        _done.SetResult();
    }

    public void Complete() => _output.Complete();
    public void Fault(Exception ex) => _done.TrySetException(ex);
}
```

### 2. Define the graph in JSON

```json
{
  "workflows": {
    "main": {
      "source": { "type": "demo.numbers" },
      "sink":   { "type": "demo.printer", "Input": "source.Output" }
    }
  }
}
```

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

| Document | Contents |
|----------|----------|
| [Architecture](docs/01-architecture.md) | Engine design, execution model, completion propagation |
| [Getting started](docs/02-getting-started.md) | Full walkthrough with runnable examples |
| [Core concepts](docs/03-core-concepts.md) | `IFlowNode`, `FlowError`, `FlowEvent`, `FlowNodeId` |
| [Definitions](docs/04-definitions.md) | JSON format, validation rules, serialization |
| [Runtime](docs/05-runtime.md) | Builder, ports, links, state streams |
| [Mapping](docs/06-mapping.md) | Expression engines, `IFlowMapper`, `IFlowPredicate` |
| [Scenarios](docs/07-scenarios.md) | Deterministic scenario testing |
| [Hosting](docs/08-hosting.md) | `FlowApplicationHost`, configuration loader |
| [Extending](docs/09-extending.md) | Writing and registering custom node types |
| [API reference](docs/10-api-reference.md) | Complete public surface, all namespaces |

---

## Building

```sh
dotnet build FluxFlow.sln
dotnet test  FluxFlow.sln
```

---

## Extension boundary

FluxFlow.Engine does not ship transport, storage, web, or designer components.
Applications add those behaviors by:

1. Implementing `IFlowNode` for each external capability
2. Registering node factories with `RuntimeNodeFactoryRegistry`
3. Composing flows as JSON that FluxFlow.Engine builds and runs

Component packages should own their own options, validation, event names, tests,
and documentation.
