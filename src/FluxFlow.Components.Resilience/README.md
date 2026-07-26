# FluxFlow.Components.Resilience

`FluxFlow.Components.Resilience` provides the standalone `FlowRetryNode`. It
coordinates workflow attempts with the transport-neutral `FluxFlow.Resilience`
state machine and `FluxFlow.Coordination` pending-exchange foundation.

## Contract

- `Input`: `FlowMessage<FlowValue>` starts one logical operation per `TraceId`.
- `Output`: `FlowMessage<FlowResult<RetrySignal>>` emits attempts, scheduled
  retries, completion, exhaustion, cancellation, and rejection as ordinary data.
- `Ack`, `Nak`, and `Cancel`: signal targets that consume the attempt envelope's
  stable `TraceId` and internal attempt header.
- `Events`: diagnostics for logging, metrics, and tracing; it is not state.

`FlowResult.IsError` identifies scheduled failures and terminal failure results.
Workflow links should route only `retry.attempt` results to the operation being
retried. The downstream path must preserve the attempt envelope headers when it
returns ACK, NAK, or Cancel. `FlowMessage.With(...)` does this automatically.

Late feedback cannot complete a newer attempt because each emitted attempt carries
an internal, component-scoped attempt discriminator. Workflow authors continue to
use `TraceId` as the logical operation identity.

The component does not own transport retry classification, broker acknowledgement,
or provider lifecycle. Those concerns remain in their protocol packages.

## Composition

The optional `FluxFlow.Components.Resilience.Composition` package registers
`flow.retry`, binds flat `FlowRetryOptions`, exposes explicit Ack/Nak/Cancel
signal metadata, and publishes Designer hints. The runtime package remains
usable directly without Composition, Designer, Hosting, or Engine.
