# Extending the engine

This guide shows how to build production-quality node types for your application.

---

## Node implementation checklist

Before shipping a custom node type, verify:

- [ ] `Id` is `FlowNodeId.New()` — unique per instance
- [ ] `Completion` is properly set — resolves when all work is done, including after `Complete()`
- [ ] `Complete()` signals no more input (does NOT block)
- [ ] `Fault(Exception)` faults the completion and any internal blocks
- [ ] `Errors` is always non-null and posts processing errors (never throws from message handlers)
- [ ] `StartAsync` is cancellation-aware
- [ ] `IDisposable` / `IAsyncDisposable` releases resources (not `Complete()`)
- [ ] `OutputPort<T>` is declared for every data-out, `InputPort<T>` for every data-in
- [ ] Static `Create(RuntimeNodeFactoryContext)` factory reads phase and passes it to `RuntimeNode.Create`

---

## Pattern 1 — Source node (no inputs, one output)

Emits a stream of messages. Does not consume anything.

```csharp
public sealed class FileLineSource : IFlowNode, IFlowEventSource, IAsyncDisposable
{
    private readonly BufferBlock<string> _output = new(
        new DataflowBlockOptions { BoundedCapacity = 1000 });
    private readonly BroadcastBlock<FlowEvent> _events = new(e => e);
    private readonly TaskCompletionSource _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _path;

    public FlowNodeId Id { get; } = FlowNodeId.New();
    public Task Completion => _done.Task;
    public ISourceBlock<FlowError> Errors { get; } = new BufferBlock<FlowError>();
    public ISourceBlock<FlowEvent> Events => _events;

    public static RuntimeNode Create(RuntimeNodeFactoryContext ctx)
    {
        var path = ctx.Definition.Configuration["path"].GetString()!;
        var node = new FileLineSource(path);
        return RuntimeNode.Create(
            ctx.Address,
            node,
            outputs: [
                new OutputPort<string>(
                    new PortAddress(ctx.Address.Scope, ctx.Address.Node, new PortName("Lines")),
                    node._output)
            ],
            phase: ctx.Definition.Phase);
    }

    public FileLineSource(string path) => _path = path;

    public async Task StartAsync(CancellationToken ct = default)
    {
        try
        {
            await foreach (var line in File.ReadLinesAsync(_path).WithCancellation(ct))
            {
                await _output.SendAsync(line, ct);
                _events.Post(new FlowEvent
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Type      = "file.line.read",
                    Source    = _path
                });
            }
            _output.Complete();
            _done.SetResult();
        }
        catch (OperationCanceledException) { _done.TrySetCanceled(ct); }
        catch (Exception ex)
        {
            _output.Fault(ex);
            _done.TrySetException(ex);
        }
    }

    public void Complete() => _output.Complete();
    public void Fault(Exception ex) => _output.Fault(ex);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

---

## Pattern 2 — Transform node (one input, one output)

Receives messages, transforms, and forwards. Completion propagates from input → block → output.

```csharp
public sealed class UpperCaseTransform : IFlowNode
{
    private readonly TransformBlock<string, string> _block =
        new(line => line.ToUpperInvariant(),
            new ExecutionDataflowBlockOptions { BoundedCapacity = 500 });
    private readonly TaskCompletionSource _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FlowNodeId Id { get; } = FlowNodeId.New();
    public Task Completion => _done.Task;
    public ISourceBlock<FlowError> Errors { get; } = new BufferBlock<FlowError>();

    public static RuntimeNode Create(RuntimeNodeFactoryContext ctx)
    {
        var node = new UpperCaseTransform();
        var addr = ctx.Address;
        return RuntimeNode.Create(
            addr,
            node,
            inputs: [
                new InputPort<string>(
                    new PortAddress(addr.Scope, addr.Node, new PortName("Input")),
                    node._block)
            ],
            outputs: [
                new OutputPort<string>(
                    new PortAddress(addr.Scope, addr.Node, new PortName("Output")),
                    node._block)
            ],
            phase: ctx.Definition.Phase);
    }

    public UpperCaseTransform()
    {
        _block.Completion.ContinueWith(t =>
        {
            if (t.IsFaulted) _done.TrySetException(t.Exception!.InnerException!);
            else             _done.TrySetResult();
        }, TaskScheduler.Default);
    }

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void Complete() => _block.Complete();
    public void Fault(Exception ex) => _block.Fault(ex);
}
```

---

## Pattern 3 — Sink node (one input, no outputs)

Consumes messages. Has no outputs.

```csharp
public sealed class ConsoleSink : IFlowNode
{
    private readonly ActionBlock<string> _action;
    private readonly TaskCompletionSource _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FlowNodeId Id { get; } = FlowNodeId.New();
    public Task Completion => _done.Task;
    public ISourceBlock<FlowError> Errors { get; } = new BufferBlock<FlowError>();

    public static RuntimeNode Create(RuntimeNodeFactoryContext ctx)
    {
        var prefix = ctx.Definition.Configuration.TryGetValue("prefix", out var p)
            ? p.GetString() ?? "" : "";
        var node = new ConsoleSink(prefix);
        return RuntimeNode.Create(
            ctx.Address,
            node,
            inputs: [
                new InputPort<string>(
                    new PortAddress(ctx.Address.Scope, ctx.Address.Node, new PortName("Input")),
                    node._action)
            ],
            phase: ctx.Definition.Phase);
    }

    public ConsoleSink(string prefix)
    {
        _action = new ActionBlock<string>(
            line => Console.WriteLine($"{prefix}{line}"),
            new ExecutionDataflowBlockOptions { BoundedCapacity = 100 });

        _action.Completion.ContinueWith(t =>
        {
            if (t.IsFaulted) _done.TrySetException(t.Exception!.InnerException!);
            else             _done.TrySetResult();
        }, TaskScheduler.Default);
    }

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void Complete() => _action.Complete();
    public void Fault(Exception ex) => _action.Fault(ex);
}
```

---

## Pattern 4 — Resource node (shared, referenced by workflow nodes)

A resource is a node under `"resources"` in the definition. Workflow nodes look it up
via `RuntimeNodeFactoryContext.GetResource(name)`.

```csharp
// Resource: a shared connection handle
public sealed class DatabaseResource : IFlowNode, IDisposable
{
    private readonly TaskCompletionSource _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FlowNodeId Id { get; } = FlowNodeId.New();
    public Task Completion => _done.Task;
    public ISourceBlock<FlowError> Errors { get; } = new BufferBlock<FlowError>();

    public DatabaseConnection Connection { get; private set; } = null!;
    private readonly string _connectionString;

    public static RuntimeNode Create(RuntimeNodeFactoryContext ctx)
    {
        var cs   = ctx.Definition.Configuration["connectionString"].GetString()!;
        var node = new DatabaseResource(cs);
        // Resources typically have no ports
        return RuntimeNode.Create(ctx.Address, node, phase: ctx.Definition.Phase);
    }

    public DatabaseResource(string connectionString) => _connectionString = connectionString;

    public async Task StartAsync(CancellationToken ct = default)
    {
        Connection = await DatabaseConnection.OpenAsync(_connectionString, ct);
    }

    public void Complete() => _done.TrySetResult();
    public void Fault(Exception ex) => _done.TrySetException(ex);

    public void Dispose()
    {
        Connection?.Dispose();
        _done.TrySetResult();
    }
}
```

```csharp
// Workflow node that uses the resource
public sealed class QueryNode : IFlowNode
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext ctx)
    {
        // Access the shared resource declared under "resources"
        var dbNode = ctx.GetResource(new NodeName("db"));
        var db     = (DatabaseResource)dbNode.Node;

        var node   = new QueryNode(db.Connection);
        // ... declare ports ...
        return RuntimeNode.Create(ctx.Address, node, phase: ctx.Definition.Phase);
    }
    ...
}
```

```json
{
  "resources": {
    "db": { "type": "myapp.database", "phase": 0, "configuration": { "connectionString": "..." } }
  },
  "workflows": {
    "main": {
      "query": { "type": "myapp.query", "phase": 1 }
    }
  }
}
```

---

## Pattern 5 — Fan-out: one output → multiple inputs

Nothing special is required. `OutputPort<T>` internally uses `BroadcastBlock<T>`,
so you can link one output to many inputs in JSON:

```json
{
  "workflows": {
    "main": {
      "source": { "type": "demo.source" },
      "metrics": { "type": "demo.metrics", "Input": "source.Output" },
      "logger":  { "type": "demo.logger",  "Input": "source.Output" }
    }
  }
}
```

Both `metrics.Input` and `logger.Input` receive every message from `source.Output`.

---

## Pattern 6 — Fan-in: multiple outputs → one input

Link multiple sources to the same input port:

```json
"aggregator": {
  "type": "demo.aggregator",
  "Input": ["source1.Output", "source2.Output"]
}
```

The builder installs an `InputCompletionLink` that completes the input only after
**all** sources have completed.

---

## Registering node types

```csharp
var registry = new RuntimeNodeFactoryRegistry();

registry
    .Register(new NodeType("myapp.database"), DatabaseResource.Create)
    .Register(new NodeType("myapp.query"),    QueryNode.Create)
    .Register(new NodeType("demo.source"),    DemoSource.Create)
    .Register(new NodeType("demo.printer"),   DemoPrinter.Create);
```

Trying to register the same `NodeType` twice throws `InvalidOperationException`.

---

## Error reporting in nodes

Always post errors, never throw from the message handler:

```csharp
_transform = new TransformBlock<Input, Output>(async input =>
{
    try
    {
        return await ProcessAsync(input);
    }
    catch (Exception ex)
    {
        await ((BufferBlock<FlowError>)Errors).SendAsync(new FlowError
        {
            NodeId    = Id,
            Code      = FlowErrorCodes.ProcessingFailed,
            Message   = $"Failed to process message: {ex.Message}",
            Exception = ex,
            Context   = input.ToString()
        });

        // Return a sentinel or re-use the default — the transform must return something
        return Input.Null;
    }
});
```

---

## Testing custom nodes

Test nodes directly — no full runtime required:

```csharp
[Fact]
public async Task UpperCaseTransform_ConvertsAllInput()
{
    var node  = new UpperCaseTransform();
    var block = ((InputPort<string>)
        UpperCaseTransform.Create(FakeContext("wf", "node")).Inputs[0]).Target;

    // Feed data
    block.Post("hello");
    block.Post("world");
    block.Complete();

    // Collect output
    var results = new List<string>();
    var output = UpperCaseTransform.Create(FakeContext("wf", "node"))
        .Outputs.OfType<OutputPort<string>>().First().Source;

    await foreach (var item in output.ReadAllAsync())
        results.Add(item);

    results.ShouldBe(["HELLO", "WORLD"]);
}
```
