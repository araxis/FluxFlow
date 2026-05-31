# Core concepts

Namespace: `FluxFlow.Engine.Components` (and `FluxFlow.Engine.Core` for `FlowNodeId`)

---

## IFlowNode

The base contract for every processing node in a graph.

```csharp
// FluxFlow.Engine.Components
public interface IFlowNode : IDataflowBlock
{
    FlowNodeId Id { get; }
    ISourceBlock<FlowError> Errors { get; }
    Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

`IFlowNode` extends `IDataflowBlock` (from `System.Threading.Tasks.Dataflow`), so it
inherits three members from TPL:

| Member | Description |
|--------|-------------|
| `Task Completion` | Completes when the node has processed all pending work |
| `void Complete()` | Signals no more input; node drains and completes |
| `void Fault(Exception)` | Faults the node; `Completion` transitions to faulted |

### Implementing a node

Minimum requirements:

```csharp
public sealed class MyNode : IFlowNode
{
    private readonly TaskCompletionSource _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Required -----------------------------------------------------------------
    public FlowNodeId Id { get; } = FlowNodeId.New();
    public Task Completion => _done.Task;
    public ISourceBlock<FlowError> Errors { get; } = new BufferBlock<FlowError>();

    // StartAsync is called by ApplicationRuntime in phase order ----------------
    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

    // IDataflowBlock -----------------------------------------------------------
    public void Complete() => _done.TrySetResult();
    public void Fault(Exception ex) => _done.TrySetException(ex);
}
```

### Posting errors

When a node handles a message that fails processing, post a `FlowError` rather
than throwing:

```csharp
try
{
    var result = Process(message);
    await _output.SendAsync(result, ct);
}
catch (Exception ex)
{
    await ((BufferBlock<FlowError>)Errors).SendAsync(new FlowError
    {
        NodeId  = Id,
        Code    = FlowErrorCodes.ProcessingFailed,
        Message = $"Failed to process message: {ex.Message}",
        Exception = ex
    }, ct);
}
```

Posting to `Errors` leaves the node running so it can process subsequent messages.

---

## FlowNodeId

Lightweight typed wrapper around `Guid`. Every `IFlowNode` has one.

```csharp
// FluxFlow.Engine.Core
public readonly record struct FlowNodeId(Guid Value)
{
    public static FlowNodeId New()   => new(Guid.NewGuid());
    public static FlowNodeId Empty  => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
```

Use `FlowNodeId.New()` in the constructor so each node instance gets a unique ID.

---

## FlowError

Represents a recoverable processing failure surfaced on `IFlowNode.Errors`.

```csharp
// FluxFlow.Engine.Components
public sealed record FlowError
{
    public required FlowNodeId NodeId    { get; init; }
    public required int        Code      { get; init; }
    public required string     Message   { get; init; }
    public Exception?          Exception { get; init; }
    public DateTimeOffset      OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public string?             Context   { get; init; }
}
```

| Property | Description |
|----------|-------------|
| `NodeId` | The node that produced the error |
| `Code` | Numeric error code — use `FlowErrorCodes` constants or define your own |
| `Message` | Human-readable description |
| `Exception` | Original exception, if available |
| `OccurredAt` | UTC timestamp |
| `Context` | Optional free-text context (e.g. the topic or message that caused the error) |

### Built-in error codes (`FlowErrorCodes`)

| Constant | Value | When to use |
|----------|-------|-------------|
| `NodeFaulted` | 1000 | Node entered faulted state |
| `ProcessingFailed` | 2000 | Message processing threw an exception |
| `DynamicExpressionFailed` | 3000 | A DynamicExpresso or Jsonata expression evaluation failed |

---

## FlowEvent

An observation record emitted by nodes that implement `IFlowEventSource`.
Used by the scenario runner and the event journal.

```csharp
// FluxFlow.Engine.Components
public sealed record FlowEvent
{
    public required DateTimeOffset Timestamp       { get; init; }
    public required string         Type            { get; init; }
    public required string         Source          { get; init; }
    public FlowNodeId?             SourceNodeId    { get; init; }
    public string?                 Subject         { get; init; }
    public string?                 Status          { get; init; }
    public string?                 Topic           { get; init; }
    public int?                    PayloadBytes    { get; init; }
    public string?                 PayloadPreview  { get; init; }
    public IReadOnlyDictionary<string, string> Attributes { get; init; }
    // GetAttribute(name) — returns null if absent
}
```

### Built-in event type constants (`FlowEventTypes`)

| Constant | Value |
|----------|-------|
| `MqttMessageReceived` | `"mqtt.message.received"` |
| `MqttMessagePublished` | `"mqtt.message.published"` |
| `MqttMessageRecorded` | `"mqtt.message.recorded"` |
| `FileWritten` | `"file.written"` |
| `JsonSchemaValidated` | `"json.schema.validated"` |
| `AssertionEvaluated` | `"flow.assertion.evaluated"` |

You are free to define additional event type strings for your own node types.

### Emitting events

```csharp
public sealed class MyNode : IFlowNode, IFlowEventSource
{
    private readonly BroadcastBlock<FlowEvent> _events = new(e => e);
    public ISourceBlock<FlowEvent> Events => _events;

    private void EmitReceived(string topic, int bytes)
    {
        _events.Post(new FlowEvent
        {
            Timestamp    = DateTimeOffset.UtcNow,
            Type         = FlowEventTypes.MqttMessageReceived,
            Source       = Id.ToString(),
            SourceNodeId = Id,
            Topic        = topic,
            PayloadBytes = bytes
        });
    }
}
```

---

## IFlowEventSource

Optional interface for nodes that want to participate in the event journal.

```csharp
// FluxFlow.Engine.Components
public interface IFlowEventSource
{
    ISourceBlock<FlowEvent> Events { get; }
}
```

`ApplicationRuntime.Events` automatically aggregates every node that implements
`IFlowEventSource`. No registration is needed — the `FlowEventCollector` inspects
each `RuntimeNode.Node` during runtime construction.

---

## Key design rules

1. **Never throw from message processing** — post to `Errors` and continue.
2. **Implement `Complete()`/`Fault()` honestly** — they must eventually complete
   the `Completion` task so the runtime graph can drain.
3. **`StartAsync` for setup only** — connect to external services, subscribe to brokers,
   start background tasks. Do not block indefinitely inside `StartAsync`.
4. **Always honour `CancellationToken`** in `StartAsync`.
5. **Dispose resources in `IDisposable` / `IAsyncDisposable`**, not in `Complete()`.
