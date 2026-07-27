# FluxFlow.Nodes

Minimal standalone TPL Dataflow node foundation. Components can use it without
Engine or Composition.

The package also owns the transport-neutral `FluxFlow.Data` namespace:

- `FlowContent` stores exact owned immutable bytes plus optional content type and
  encoding. Its versioned JSON representation preserves the exact bytes.
- `FlowError` carries a stable code, message, category, transient flag, and
  optional detached JSON details as ordinary workflow data.

The namespace deliberately remains `FluxFlow.Data` for source compatibility,
but these types now compile into the `FluxFlow.Nodes` assembly. Namespace and
assembly identity are separate concerns; consumers should reference only the
`FluxFlow.Nodes` package. No forwarding assembly or compatibility package is
provided.

## Message Contract

`FlowMessage<T>` contains exactly one typed value or `FlowError`, together with
`TraceId`, `MessageId`, optional `CausationId`/`CorrelationId`, timestamp, and
immutable ordinal string headers.

```csharp
var input = FlowMessage.Create("hello");
FlowMessage<int> output = input.With(input.Value.Length);
FlowMessage<int> failure = input.WithError<int>(
    new FlowError("text.invalid", "Invalid text.", "validation"));
```

Derived messages preserve trace, correlation, and headers, create a new message
identity, and point causation at the preceding message.

## Node Contract

`FlowNode<TInput,TOutput>` provides bounded Input, broadcast Output, Events,
Completion, and async disposal. Incoming errors are propagated without invoking
normal business processing. Exceptions from per-message processing become
`FlowError` output data. Override `HandlesErrors` only for deliberate recovery,
routing, logging, or translation components.

One bounded processing block owns intake and execution, so configured capacity,
parallelism, and ordering apply to one queue. Accepted outputs and diagnostics
flush before normal completion, and disposal remains idempotent.

`FlowSource<T>` provides broadcast Output, Events, one-start lifecycle, and
cancellation-aware completion. There is no universal Errors port. Events are
diagnostics; unrecoverable lifecycle faults remain observable on Completion.

Broadcast blocks share accepted immutable messages. FluxFlow does not
deep-clone arbitrary user payloads.
