# Runtime

Namespace: `FluxFlow.Engine.Runtime`

---

## ApplicationRuntimeBuilder

Turns an `ApplicationDefinition` into a live `ApplicationRuntime`.

```csharp
public sealed class ApplicationRuntimeBuilder
{
    public ApplicationRuntimeBuilder(
        RuntimeNodeFactoryRegistry factories,
        ApplicationDefinitionValidator? validator = null) { }

    public ApplicationRuntimeBuildResult Build(ApplicationDefinition definition) { }
}
```

### Build result

```csharp
public sealed record ApplicationRuntimeBuildResult
{
    public bool IsSuccess                              { get; }
    public ApplicationRuntime? Runtime                { get; }   // null on failure
    public IReadOnlyList<ApplicationRuntimeBuildError> Errors { get; }
}
```

Build errors have a structured `ApplicationRuntimeBuildErrorCode`:

| Code | Meaning |
|------|---------|
| `ValidationFailed` | Definition failed `ApplicationDefinitionValidator` |
| `UnknownNodeType` | No factory registered for a node type |
| `FactoryFailed` | Factory delegate threw an exception |
| `MissingInputPort` | Target node did not declare the named input port |
| `MissingOutputPort` | Source node did not declare the named output port |
| `PortTypeMismatch` | `OutputPort<T>` and `InputPort<T>` have incompatible `T` |
| `LinkFailed` | Dataflow `LinkTo` threw an exception |

---

## RuntimeNodeFactoryRegistry

Maps `NodeType` strings to factory delegates.

```csharp
public sealed class RuntimeNodeFactoryRegistry
{
    // Register with the full factory delegate
    public RuntimeNodeFactoryRegistry Register(NodeType type, RuntimeNodeFactory factory);

    // Convenience: factory receives (NodeAddress, NodeDefinition) — no context resource access
    public RuntimeNodeFactoryRegistry Register(
        NodeType type,
        Func<NodeAddress, NodeDefinition, RuntimeNode> factory);

    public bool TryGetFactory(NodeType type, out RuntimeNodeFactory factory);
}

// Delegate
public delegate RuntimeNode RuntimeNodeFactory(RuntimeNodeFactoryContext context);
```

**Fluent chaining:**

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .Register(new NodeType("demo.source"),  DemoSource.Create)
    .Register(new NodeType("demo.printer"), DemoPrinter.Create);
```

---

## RuntimeNodeFactoryContext

Passed to every factory. Gives the factory all it needs to construct a `RuntimeNode`.

```csharp
public sealed record RuntimeNodeFactoryContext(
    NodeName     Name,
    NodeDefinition Definition,
    string?      WorkflowName,
    IReadOnlyDictionary<NodeName, RuntimeNode> Resources)
{
    public bool   IsResource { get; }     // true when WorkflowName is null
    public NodeAddress Address { get; }  // Scope + Node computed from Name + WorkflowName

    // Look up a resource node by name (throws if not found)
    public RuntimeNode GetResource(NodeName resourceName);
}
```

**Reading configuration:**

```csharp
public static RuntimeNode Create(RuntimeNodeFactoryContext ctx)
{
    // ctx.Definition.Configuration is Dictionary<string, JsonElement>
    var host = ctx.Definition.Configuration["host"].GetString()!;
    var port = ctx.Definition.Configuration.TryGetValue("port", out var p) ? p.GetInt32() : 1883;

    // Access a shared resource declared under "resources"
    var brokerNode = ctx.GetResource(new NodeName("broker"));
    var broker = (BrokerResource)brokerNode.Node;

    var node = new MyTrigger(broker, host, port);
    return RuntimeNode.Create(ctx.Address, node, outputs: [...], phase: ctx.Definition.Phase);
}
```

---

## RuntimeNode

A wrapper that pairs an `IFlowNode` with its declared ports and phase.

```csharp
public sealed record RuntimeNode(
    NodeAddress             Address,
    IFlowNode               Node,
    IReadOnlyList<InputPort>  Inputs,
    IReadOnlyList<OutputPort> Outputs,
    int                     Phase = 0)
{
    public static RuntimeNode Create(
        NodeAddress address,
        IFlowNode   node,
        IEnumerable<InputPort>?  inputs  = null,
        IEnumerable<OutputPort>? outputs = null,
        int phase = 0);

    public InputPort?  FindInput(PortName port);
    public OutputPort? FindOutput(PortName port);
}
```

---

## InputPort\<T\>

Typed wrapper around an `ITargetBlock<T>`.

```csharp
public sealed class InputPort<T>(PortAddress address, ITargetBlock<T> target) : InputPort
{
    public ITargetBlock<T> Target     { get; }
    public override Task   Completion { get; }   // = Target.Completion
    public override void   Complete() { }
    public override void   Fault(Exception e) { }
}
```

**Creating an input:**

```csharp
var actionBlock = new ActionBlock<string>(msg => Process(msg));
var inputPort   = new InputPort<string>(
    new PortAddress("main", new NodeName("sink"), new PortName("Input")),
    actionBlock);
```

---

## OutputPort\<T\>

Typed wrapper that wraps an `ISourceBlock<T>` in a `BroadcastBlock<T>`.

```csharp
public sealed class OutputPort<T> : OutputPort
{
    public ISourceBlock<T> Source         { get; }  // = the BroadcastBlock
    public override bool   DrainWhenUnlinked { get; }

    public OutputPort(
        PortAddress      address,
        ISourceBlock<T>  source,
        bool             drainWhenUnlinked = true);
}
```

- The internal `BroadcastBlock<T>` fans out to as many linked inputs as needed.
- `DrainWhenUnlinked` defaults to `true` for all types except `FlowError`.
  When `true` and the port has no links, the builder silently drains it to
  `DataflowBlock.NullTarget<T>()` so completion can propagate.
- `FlowError` ports have `DrainWhenUnlinked = false` by default — unobserved
  errors are *not* silently discarded.

**Creating an output:**

```csharp
var buffer     = new BufferBlock<int>();
var outputPort = new OutputPort<int>(
    new PortAddress("main", new NodeName("source"), new PortName("Output")),
    buffer);
```

---

## ApplicationRuntime

The live graph. Owns all `Resources` and `Workflows`.

```csharp
public sealed class ApplicationRuntime : IAsyncDisposable, IDisposable
{
    public IReadOnlyList<RuntimeNode> Resources { get; }
    public IReadOnlyList<Workflow>    Workflows { get; }
    public IEnumerable<RuntimeNode>   Nodes    { get; }  // Resources + all workflow nodes

    public ApplicationState                         State        { get; }
    public ISourceBlock<ApplicationStateChanged>    StateChanges { get; }
    public ISourceBlock<FlowEvent>                  Events       { get; }
    public Task                                     Completion   { get; }

    public Task   StartAsync(CancellationToken ct = default);
    public void   Complete();
    public void   Fault(Exception exception);
    public void   Dispose();
    public ValueTask DisposeAsync();
}
```

### Lifecycle sequence

```csharp
await using var runtime = buildResult.Runtime!;

// StartAsync: phases 0 → n, all nodes in ascending phase order
await runtime.StartAsync();

// Observe state
runtime.StateChanges.LinkTo(new ActionBlock<ApplicationStateChanged>(
    s => Console.WriteLine($"{s.Previous} → {s.Current}")));

// Stop gracefully: signals entry nodes, completion propagates through graph
runtime.Complete();
await runtime.Completion;

// DisposeAsync: disposes workflows, links, event collector, resource nodes
```

### State enum

```csharp
public enum ApplicationState { Idle, Starting, Running, Stopping, Stopped, Faulted }
```

---

## Workflow

One named group of nodes inside a runtime.

```csharp
public sealed class Workflow : IAsyncDisposable, IDisposable
{
    public WorkflowName                          Name         { get; }
    public IReadOnlyList<RuntimeNode>            Nodes        { get; }
    public WorkflowState                         State        { get; }
    public ISourceBlock<WorkflowStateChanged>    StateChanges { get; }
    public Task                                  Completion   { get; }

    public Task  StartAsync(CancellationToken ct = default);
    public void  Complete();
    public void  Fault(Exception exception);
}
```

---

## Phase ordering

Phases control **when** `StartAsync` is called during `ApplicationRuntime.StartAsync`.

```
Phase 0: resources.broker  (mqtt.connection — opens TCP connection)
Phase 1: main.trigger      (mqtt.trigger — subscribes AFTER broker is connected)
Phase 2: main.metrics      (starts collecting AFTER trigger has subscriptions)
```

Nodes with the same phase start in definition order within the phase.
Phase values can be negative if resources need to start before workflow nodes at phase 0.

Set phase in JSON:

```json
"broker": { "type": "mqtt.connection", "phase": 0 },
"trigger": { "type": "mqtt.trigger",   "phase": 1 }
```

Or in code:

```csharp
return RuntimeNode.Create(ctx.Address, node, phase: ctx.Definition.Phase);
```

---

## Completion propagation

```
source.Complete()
  → PropagateCompletion → filter input target completes
    → filter output (BroadcastBlock) completes
      → PropagateCompletion → sink input target completes
        → sink ActionBlock completes
          → RuntimeNode.Completion → Workflow.Completion → ApplicationRuntime.Completion
```

When a target has **multiple sources** (fan-in), the builder creates an
`InputCompletionLink` that waits for *all* sources to complete before completing
the shared target.

---

## NodeAddress

Identifies a node's position in the definition tree.

```csharp
public readonly record struct NodeAddress(string Scope, NodeName Node)
{
    // Returns a PortAddress for a given port on this node
    public PortAddress Port(PortName port) => new(Scope, Node, port);
}
```

- For resources: `Scope = "resources"`, `Node = resource name`
- For workflow nodes: `Scope = workflow name`, `Node = node name`
