# FluxFlow.Components.Resilience.Composition

`RegisterFlowRetry()` registers the canonical `flow.retry` component with flat
workflow options, explicit Ack/Nak/Cancel signal inputs, one normal result
output, addressable Events, and Designer metadata.

```json
{
  "Type": "flow.retry",
  "Strategy": "Exponential",
  "InitialDelayMilliseconds": 1000,
  "MaximumDelayMilliseconds": 30000,
  "MaximumAttempts": 5,
  "AttemptTimeoutMilliseconds": 10000,
  "Capacity": 128
}
```

`Clock` and `Jitter` are optional host-owned resources addressed through the
application `Resources` object. When `Name` is omitted, composition uses the
workflow/component address for diagnostics. Each runtime instance owns a
private attempt-header name so nested or adjacent retry components cannot
interpret one another's feedback.

`ResilienceComponentDesignMetadataProvider` describes the flat option surface,
fixed data and signal ports, result output, Events, and canonical host-owned
resource picker hints. It does not create resources or add renderer behavior.

Route only `retry.attempt` results to the operation being retried. The downstream
path returns an envelope derived with `FlowMessage.With(...)` to Ack, Nak, or
Cancel, preserving TraceId and the internal attempt discriminator. Scheduled and
terminal failures remain ordinary `FlowResult` data and expose `IsError`.
