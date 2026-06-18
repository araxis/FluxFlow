# FluxFlow.Nodes

The minimal base every FluxFlow node is built on. A node is a self-contained TPL
Dataflow processor — you `new` it and link it; no engine, registry, or runtime.

## `FlowNode<TInput, TOutput>`

Derive from it and implement `ProcessAsync`. The base gives you four ports:

| Port | Block | Notes |
|------|-------|-------|
| `Input` | `BufferBlock<TInput>` | bounded intake — `SendAsync` applies backpressure |
| `Output` | `BroadcastBlock<TOutput>` | fan-out: link to as many downstream inputs as you like |
| `Errors` | `BroadcastBlock<FlowError>` | uniform error stream |
| `Events` | `BroadcastBlock<FlowEvent>` | uniform observability stream |

```csharp
public sealed class UppercaseNode : FlowNode<string, string>
{
    protected override Task ProcessAsync(string input)
    {
        Emit(input.ToUpperInvariant());
        return Task.CompletedTask;
    }
}

await using var node = new UppercaseNode();
node.Output.LinkTo(next.Input);
await node.Input.SendAsync("hello");
```

A throw inside `ProcessAsync` is caught and surfaced on `Errors` rather than
killing the pump. `Complete()` drains the input and completes the outputs;
`Fault(ex)` tears everything down; `Completion` tracks the lifecycle.

## Design notes

- **Outputs are broadcast** (latest-wins, no backpressure): a consumer that keeps
  up sees every message; one that falls badly behind may miss some. That is the
  deliberate trade for simplicity. A graph that genuinely must not drop should
  bridge that edge through its own bounded buffer (or a dedicated no-loss node).
- **Inputs are a bounded buffer**, so a node throttles its own intake.
- The kit owns no domain logic and no engine concepts — just the plumbing every
  node shares.
