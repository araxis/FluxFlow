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
| **Node authoring helpers** | Base classes and a fluent node builder reduce factory and port boilerplate |
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

The package README is the current public entrypoint. The full docs set is being
rebuilt around the standalone package boundary; see [docs/README.md](docs/README.md)
for status.

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
