# FluxFlow.Expressions.CSharp

Optional `IFlowExpressionEngine` implementation for host-configured expression
evaluation. Configure referenced types, constants, functions and parser options
once in the constructor; pass changing values as typed evaluation parameters.
Assignment is disabled by default. Expose only trusted capabilities: evaluating
user expressions is not a security sandbox. Do not retain or mutate the configured
interpreter; referenced functions and objects must be thread-safe.

Use `Compile<T>(expression, parameterTypes)` to parse once and evaluate repeatedly.
Generic mapping consumers can supply default parameter types in the constructor.
Application link compilation supplies input/payload/message and link parameter
types automatically. This adapter does not load types or assemblies from JSON.

For error-aware conditions, use the envelope, for example
`message.IsError || positive(input)` with a host-configured `positive(FlowValue)`
function. Canonical `input` and `payload` are `message.Value`: both are null for an
error envelope; the error is available through `message.Error`. Short-circuit
before calling a value-only helper. Read structured values through
`ToJsonElement()` or a host-provided helper.
