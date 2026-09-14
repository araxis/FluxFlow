# FluxFlow.Components.Resilience

Standalone retry-controlled operation nodes over the transport-neutral
`FluxFlow.Resilience` policy library.

`FlowRetryNode` implements `IFlowComponent` directly. Construct it with options
and an optional clock/jitter source, then call `workflow.Add(name, node)`.
Input, Ack, Nak, and Cancel are bounded `ITargetBlock<FlowMessage>` inputs;
Output carries the same canonical envelope. Successful values materialize as
`RetrySignal<T>`; the typed record is a local payload helper, not another runtime.

Feedback ignores payload and matches trace identity plus the attempt header.
Use `attempt.With(JsonFlowValue.Null)` to retain those fields, then send it to Ack,
Nak, or Cancel. Native `SendAsync` reports admission, not settlement. Observe
Output for retry results and Events for ignored-feedback diagnostics.
Each input also supports native linking, completion, faults and cancellation.
Feedback belongs to `Ports.Inputs`, not terminal controls: it can produce output.

One logical operation preserves its `TraceId`; each attempt has an internal
generation discriminator so stale feedback cannot settle a newer attempt.
Scheduled, acknowledged, rejected, cancelled, timed-out, and exhausted states
are typed retry outcomes. Invalid input or operational processing failure is an
in-band `FlowError` on the same Output.

The node owns pending attempt state only. The host owns clocks and jitter
resources. MQTT and other transports may reuse the core retry policy while
retaining their own failure classification and lifecycle.

The component owns its completion task. Infrastructure failures
close input, feedback and output admission and remain observable on completion;
they do not become ordinary retry outcomes. Graceful shutdown cancels backoff
before joining attempt observers and still emits the stopped terminal outcome.
Completion first drains input and admitted feedback, then stops pending attempts
and joins their observers. Timer disposal failures do not skip output cleanup. Graceful output delivery
retains backpressure, so linked consumers must drain or detach during shutdown.

No arbitrary cyclic graph execution is enabled. Feedback can be supplied through
the addressed input without bypassing the application's existing cycle validation.

## Composition

Install `FluxFlow.Components.Resilience.Composition` for optional runtime registration and definition. This runtime package remains free of Composition, Designer, and Engine dependencies.
