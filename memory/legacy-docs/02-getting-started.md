# Getting started

This guide builds a complete, runnable pipeline from scratch:
`NumberSource → DoubleFilter → PrinterSink`

---

## Prerequisites

- .NET 10 SDK
- `FluxFlow.Engine` package (or project reference to `src/FluxFlow.Engine`)

---

## Step 1 — Reference the library

```xml
<PackageReference Include="FluxFlow.Engine" Version="1.0.0" />
```

---

## Step 2 — Implement node types

Every node implements `IFlowNode` (in `FluxFlow.Engine.Components`).
`IFlowNode` extends `IDataflowBlock`, so it inherits `Completion`, `Complete()`,
and `Fault(Exception)` from TPL Dataflow.

### Source node — emits integers

```csharp
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Core;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using System.Threading.Tasks.Dataflow;

public sealed class NumberSource : IFlowNode
{
    private readonly BufferBlock<int> _output = new();
    private readonly TaskCompletionSource _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _count;

    // IFlowNode ----------------------------------------------------------
    public FlowNodeId Id { get; } = FlowNodeId.New();
    public Task Completion => _done.Task;
    public ISourceBlock<FlowError> Errors { get; } = new BufferBlock<FlowError>();

    // Factory ------------------------------------------------------------
    public static RuntimeNode Create(RuntimeNodeFactoryContext ctx)
    {
        // Read count from the node's "configuration" JSON block
        var count = ctx.Definition.Configuration.TryGetValue("count", out var v)
            ? v.GetInt32() : 10;

        var node = new NumberSource(count);
        return RuntimeNode.Create(
            ctx.Address,
            node,
            outputs: [
                new OutputPort<int>(
                    new PortAddress(ctx.Address.Scope, ctx.Address.Node, new PortName("Output")),
                    node._output)
            ],
            phase: ctx.Definition.Phase);
    }

    private NumberSource(int count) => _count = count;

    // Start / complete ---------------------------------------------------
    public async Task StartAsync(CancellationToken ct = default)
    {
        for (var i = 0; i < _count; i++)
            await _output.SendAsync(i, ct);
        _output.Complete();
        _done.SetResult();
    }

    public void Complete() => _output.Complete();
    public void Fault(Exception ex) => _done.TrySetException(ex);
}
```

### Transform node — passes only even numbers

```csharp
public sealed class EvenFilter : IFlowNode
{
    private readonly BufferBlock<int> _input = new();
    private readonly BufferBlock<int> _output = new();
    private readonly TaskCompletionSource _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TransformManyBlock<int, int> _transform;

    public FlowNodeId Id { get; } = FlowNodeId.New();
    public Task Completion => _done.Task;
    public ISourceBlock<FlowError> Errors { get; } = new BufferBlock<FlowError>();

    public static RuntimeNode Create(RuntimeNodeFactoryContext ctx)
    {
        var node = new EvenFilter();
        return RuntimeNode.Create(
            ctx.Address,
            node,
            inputs: [
                new InputPort<int>(
                    new PortAddress(ctx.Address.Scope, ctx.Address.Node, new PortName("Input")),
                    node._transform)
            ],
            outputs: [
                new OutputPort<int>(
                    new PortAddress(ctx.Address.Scope, ctx.Address.Node, new PortName("Output")),
                    node._output)
            ]);
    }

    public EvenFilter()
    {
        _transform = new TransformManyBlock<int, int>(
            n => n % 2 == 0 ? [n] : Array.Empty<int>());

        _transform.LinkTo(_output, new DataflowLinkOptions { PropagateCompletion = true });
        _output.Completion.ContinueWith(_ => _done.SetResult(), TaskScheduler.Default);
    }

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void Complete() => _transform.Complete();
    public void Fault(Exception ex) => _transform.Fault(ex);
}
```

### Sink node — prints to console

```csharp
public sealed class PrinterSink : IFlowNode
{
    private readonly ActionBlock<int> _action;
    private readonly TaskCompletionSource _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FlowNodeId Id { get; } = FlowNodeId.New();
    public Task Completion => _done.Task;
    public ISourceBlock<FlowError> Errors { get; } = new BufferBlock<FlowError>();

    public static RuntimeNode Create(RuntimeNodeFactoryContext ctx)
    {
        var node = new PrinterSink();
        return RuntimeNode.Create(
            ctx.Address,
            node,
            inputs: [
                new InputPort<int>(
                    new PortAddress(ctx.Address.Scope, ctx.Address.Node, new PortName("Input")),
                    node._action)
            ]);
    }

    public PrinterSink()
    {
        _action = new ActionBlock<int>(n => Console.WriteLine($"Received: {n}"));
        _action.Completion.ContinueWith(_ => _done.SetResult(), TaskScheduler.Default);
    }

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void Complete() => _action.Complete();
    public void Fault(Exception ex) => _action.Fault(ex);
}
```

---

## Step 3 — Write the JSON definition

```json
{
  "workflows": {
    "main": {
      "source": {
        "type": "demo.numbers",
        "configuration": { "count": 20 }
      },
      "filter": {
        "type": "demo.even-filter",
        "Input": "source.Output"
      },
      "printer": {
        "type": "demo.printer",
        "Input": "filter.Output"
      }
    }
  }
}
```

The `"Input": "source.Output"` line means:
> The `Input` port of `filter` receives from the `Output` port of `source`.

Port references inside a workflow use the format `"nodeName.PortName"`.
To reference a resource node use `"resources.nodeName.PortName"`.

---

## Step 4 — Register factories and run

```csharp
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using System.Text.Json;

// Register factories
var registry = new RuntimeNodeFactoryRegistry();
registry.Register(new NodeType("demo.numbers"),     NumberSource.Create);
registry.Register(new NodeType("demo.even-filter"), EvenFilter.Create);
registry.Register(new NodeType("demo.printer"),     PrinterSink.Create);

// Load definition
var opts       = ApplicationDefinitionJson.CreateSerializerOptions();
var definition = JsonSerializer.Deserialize<ApplicationDefinition>(
    File.ReadAllText("flow.json"), opts)!;

// Build and start
var host = FlowApplicationHost.Create(definition, registry);

var buildResult = await host.StartAsync();
if (!buildResult.IsSuccess)
{
    foreach (var error in buildResult.Errors)
        Console.Error.WriteLine($"[{error.Code}] {error.Message}");
    return;
}

// Wait for the graph to finish naturally
await host.Runtime!.Completion;
await host.DisposeAsync();
```

**Expected output:**

```
Received: 0
Received: 2
Received: 4
...
Received: 18
```

---

## Step 5 — Observe state changes

```csharp
var host = FlowApplicationHost.Create(definition, registry);

// Subscribe to state transitions before starting
var runtime = (await host.StartAsync()).IsSuccess
    ? host.Runtime!
    : throw new Exception("Build failed");

runtime.StateChanges.LinkTo(
    new ActionBlock<ApplicationStateChanged>(s =>
        Console.WriteLine($"[state] {s.Previous} → {s.Current}")));

await runtime.Completion;
```

---

## Step 6 — Load definition from appsettings.json

If your app uses `Microsoft.Extensions.Configuration`, you can load the definition
from `appsettings.json` via the built-in config loader:

```json
// appsettings.json
{
  "FluxMq": {
    "FlowApplication": {
      "workflows": {
        "main": {
          "source": { "type": "demo.numbers" },
          "printer": { "type": "demo.printer", "Input": "source.Output" }
        }
      }
    }
  }
}
```

```csharp
// Program.cs
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var host = FlowApplicationHost.Create(configuration, registry);
// section name defaults to "FluxMq:FlowApplication"
```

To use a custom section name:

```csharp
var loader = new FlowApplicationConfigurationLoader();
var definition = loader.Load(configuration, "MyApp:FlowGraph");
var host = FlowApplicationHost.Create(definition, registry);
```

---

## Next steps

- [Core concepts](03-core-concepts.md) — understand `IFlowNode`, `FlowError`, `FlowEvent`
- [Definitions](04-definitions.md) — full JSON format reference
- [Runtime](05-runtime.md) — ports, links, completion, phase ordering
- [Extending](09-extending.md) — patterns for real-world node types
