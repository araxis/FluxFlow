# FluxFlow.Components.Logging

`Logger` accepts one `LogRequest` or a JSON array of requests through `Input`.
It writes through the borrowed `ILogger` in `LoggerOptions.Logger`, then publishes
the processed request on `Output`. Invalid requests and provider exceptions produce
ordinary `FlowError` messages. Array items execute in order, independently; an error
does not prevent later items from executing. An empty array produces no output.
Upstream errors pass through without being logged.

```json
{
  "LogLevel": "Information",
  "MessageTemplate": "Order {OrderId} took {Elapsed} ms",
  "Args": ["A12", 42],
  "Attributes": { "machine": "line-2", "successful": true }
}
```

Template arguments are positional and use standard .NET logging formatting, including
alignment and format specifiers. Argument count must match the template placeholders.
Attributes use a structured logging scope; the chosen provider must support scopes.
Scalar JSON values retain their native string, integer, double, boolean or null type.
Arrays become arrays of native values; nested objects become JSON text.
Typed requests can additionally carry a native `Exception`; it is excluded from JSON.
JSON requests can describe errors using ordinary attributes.
JSON output keeps the declared `LogRequest` property names. Attribute keys and nested
JSON values are preserved; JSON request property matching remains case-insensitive.

The component exposes the usual `Input`, broadcast `Output` and `Events` ports.
Output preserves message context through normal message derivation. For an array,
each item gets its own derived message and result. Results are published after the
array finishes processing; logging side effects are not transactional.
The component does not own or dispose the supplied logger.

Provider filtering still counts as successful processing. Ordinary `ILogger.Log`
does not acknowledge remote delivery. Optional OTLP export-error reporting belongs
to the dedicated resource configured outside this package.

See [configuration and OTLP setup](../../Registration/README.md).
