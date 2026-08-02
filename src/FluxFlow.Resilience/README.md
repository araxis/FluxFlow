# FluxFlow.Resilience

Transport-neutral retry policy and execution primitives. The package contains
no Dataflow blocks, workflow ports, MQTT categories, provider exceptions,
connection lifecycle, or configuration binding.

- `RetrySchedule` calculates overflow-safe fixed, linear, and exponential
  delays with a final delay cap and deterministic jitter sample.
- `RetryPlanner` applies maximum-attempt and maximum-duration budgets.
- `RetryStateMachine` exposes `Attempt`, `Wait`, `Complete`, and `Exhausted`
  transitions without performing transport work.
- `RetryExecutor` is an optional direct-call adapter with cancellable
  `TimeProvider` delays.
- `IRetryJitterSource` makes production randomness and deterministic tests
  explicit.

Component and adapter packages decide which outcomes are retryable. MQTT, for
example, retains MQTT error categories, reconnect suppression, subscription
restoration, reset behavior, and connection events while delegating generic
delay and budget decisions to this package.
