# FluxFlow.Components.Resilience

Standalone retry-controlled operation nodes over the transport-neutral
`FluxFlow.Resilience` policy library.

`FlowRetryNode<T>` accepts `FlowMessage<T>` on Input and emits
`FlowMessage<RetrySignal<T>>` on Output. Ack, Nak, and Cancel are
payload-independent signal inputs keyed by workflow trace identity. The
non-generic `FlowRetryNode` is the explicit schema-less JSON specialization.

One logical operation preserves its `TraceId`; each attempt has an internal
generation discriminator so stale feedback cannot settle a newer attempt.
Scheduled, acknowledged, rejected, cancelled, timed-out, and exhausted states
are typed retry outcomes. Invalid input or operational processing failure is an
in-band `FlowError` on the same Output.

The node owns pending attempt state only. The host owns clocks and jitter
resources. MQTT and other transports may reuse the core retry policy while
retaining their own failure classification and lifecycle.

## Composition

Install `FluxFlow.Components.Resilience.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
