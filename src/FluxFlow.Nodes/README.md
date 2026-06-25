# FluxFlow.Nodes

The minimal base every FluxFlow node is built on. A node is a self-contained TPL
Dataflow processor — you `new` it and link it; no engine, registry, or runtime.

## Messages

Every message travels in a `FlowMessage<T>` envelope: a strongly-typed
`CorrelationId` + the `Payload` (plus a per-hop `MessageId`, `Timestamp`, and a
`Headers` bag). It's immutable, so a broadcast can hand the same instance to many
consumers. Transform the payload with `With`, which keeps the correlation id and
headers — so correlation flows through a graph with no node copying it by hand.

```csharp
var message = FlowMessage.Create("hello");        // mints a CorrelationId
var next    = message.With(message.Payload.Length); // same id, new payload
```

`CorrelationId` is a guarded value type (non-empty) and serializes as a bare JSON
string, so envelopes persist cleanly.

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

- **Transform outputs are broadcast** (latest-wins, no backpressure): a consumer
  that keeps up sees every message; one that falls badly behind may miss some.
  That is the deliberate trade for simplicity.
- **Sources can opt into bounded output** with `FlowSourceOptions.OutputCapacity`
  and `EmitAsync`. Source loops should await `EmitAsync` when they expose a
  capacity option; callback-driven sources can keep using nonblocking `Emit`.
- **Inputs are a bounded buffer**, so a node throttles its own intake.
- The kit owns no domain logic and no engine concepts — just the plumbing every
  node shares.
