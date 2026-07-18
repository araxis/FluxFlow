# FluxFlow.Nodes

The minimal base every FluxFlow node is built on. A node is a self-contained TPL
Dataflow processor — you `new` it and link it; no engine, registry, or runtime.

## When To Use It

Use this package when you are authoring reusable nodes or linking node instances
directly in code. It owns the message envelope, node/source base classes, common
error and event contracts, and the bounded Dataflow plumbing shared by component
packages.

Do not use it as an application workspace model or resource registry. Hosts own
workflow files, resource lookup, concrete clients, stores, and any UI or
dashboard projection. Add `FluxFlow.Composition` only when a host wants fluent or
configuration-driven node composition.

## Messages

Every message travels in a `FlowMessage<T>` envelope. `CorrelationId` identifies
the business exchange, `TraceId` identifies one source delivery through the
graph, `MessageId` identifies the current hop, and nullable `CausationId` points
to the parent hop. Headers are immutable ordinal `FlowValue` entries from
`FluxFlow.Data`.

Transform the payload with `With`. It preserves correlation, trace, and headers,
creates a new message id and timestamp, and records the source message id as
causation. Assigned header dictionaries are copied, so later caller mutations
cannot change an existing envelope.

```csharp
var message = FlowMessage.Create("hello");
var next = message.With(message.Payload.Length);

Debug.Assert(next.CorrelationId == message.CorrelationId);
Debug.Assert(next.TraceId == message.TraceId);
Debug.Assert(next.CausationId == message.MessageId);
```

`CorrelationId` is a guarded value type (non-empty) and serializes as a bare JSON
string, so envelopes persist cleanly.

## Signal Targets

`IFlowSignalTarget` is a payload-independent input contract for signal-style
ports such as acknowledgement inputs. Its generic `SendAsync<T>` accepts any
normal `FlowMessage<T>` and returns whether the target accepted it. This keeps
the registration independent of payload type while preserving trace identity.

The abstraction does not route, correlate, persist, or own resources. A target
defines what acceptance means and uses `Completion` for its lifecycle. Typed
data ports should continue to expose `ITargetBlock<FlowMessage<T>>`.

## `FlowNode<TInput, TOutput>`

Derive from it and implement `ProcessAsync`. The base gives you four ports:

| Port | Block | Notes |
|------|-------|-------|
| `Input` | `BufferBlock<FlowMessage<TInput>>` | bounded intake — `SendAsync` applies backpressure |
| `Output` | `BroadcastBlock<FlowMessage<TOutput>>` | fan-out: link to as many downstream inputs as you like |
| `Errors` | `BroadcastBlock<FlowError>` | uniform error stream (carries the message's correlation id) |
| `Events` | `BroadcastBlock<FlowEvent>` | uniform observability stream |

```csharp
public sealed class UppercaseNode : FlowNode<string, string>
{
    protected override Task ProcessAsync(FlowMessage<string> message)
    {
        Emit(message.With(message.Payload.ToUpperInvariant()));
        return Task.CompletedTask;
    }
}

await using var node = new UppercaseNode();
node.Output.LinkTo(next.Input);
await node.Input.SendAsync(FlowMessage.Create("hello"));
```

A throw inside `ProcessAsync` is caught and surfaced on `Errors` rather than
killing the pump. `Complete()` drains the input and completes the outputs;
`Fault(ex)` tears everything down; `Completion` tracks the lifecycle.

## Design notes

- **Outputs are broadcast** (latest-wins): a consumer that keeps up sees every
  message; one that falls badly behind may miss some. That is the deliberate
  trade for simplicity.
- **Options validate on assignment**: `FlowNodeOptions` rejects non-positive
  input capacity and max-degree-of-parallelism values, and `FlowSourceOptions`
  rejects invalid output capacities while allowing `UnboundedOutputCapacity`.
- **Sources can opt into bounded broadcast output** with
  `FlowSourceOptions.OutputCapacity` and `EmitAsync`. Source loops should await
  `EmitAsync` when they expose a capacity option, but this is still broadcast
  output, not a durable queue or no-loss delivery guarantee. Callback-driven
  sources can keep using nonblocking `Emit`.
- **Inputs are a bounded buffer**, so a node throttles its own intake.
- **Source startup honors cancellation**: a pre-canceled `StartAsync` returns a
  canceled task without consuming the source's one-time start state, so a later
  uncanceled start can still run it.
- The kit owns no domain logic and no engine concepts — just the plumbing every
  node shares.
