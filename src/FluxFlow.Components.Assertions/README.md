# FluxFlow.Components.Assertions

Standalone expression-driven assertions. The node evaluates
immutable `FlowValue` messages and returns pass, fail, or expected evaluation
errors through one normal `FlowResult<T>` output. It does not require
`FluxFlow.Engine`, a registry, or a runtime host.

## Node Contract

| Node | Input | Output | Diagnostics |
|------|-------|--------|-------------|
| `FlowValueAssertionNode` | `FlowValue` | `FlowResult<FlowValueAssertionResult>` | `Events` |

Passing and failing rules are both successful result variants. Expression
evaluation failures and invalid message input are normal error results, so a
workflow can inspect `Kind`, `IsError`, and `Error` without a universal error
port. Unexpected implementation faults remain observable through `Completion`.

```csharp
var options = new FlowValueAssertionOptions
{
    Expression = "score >= 10",
    InputType = "order",
    Description = "score-check",
    FailureMessage = "Score too low."
};

await using var node = new FlowValueAssertionNode(
    options,
    expressionEngine,
    contextFactory);

var results = new BufferBlock<
    FlowMessage<FlowResult<FlowValueAssertionResult>>>();
node.Output.LinkTo(results);

var input = FlowValue.FromObject(new Dictionary<string, FlowValue>
{
    ["score"] = FlowValue.From(12L)
});

await node.Input.SendAsync(FlowMessage.Create(input));
var result = (await results.ReceiveAsync()).Payload;
```

The expression is compiled once during construction. The package does not
choose an expression language; the host supplies an `IFlowExpressionEngine`
from `FluxFlow.Mapping`.

## Result Contract

`AssertionResultKinds.Passed` and `AssertionResultKinds.Failed` carry a
`FlowValueAssertionResult` with the exact input instance, decision, description,
message, expression metadata, engine name, semantic input type, and timestamp.
A failed rule has `IsError = false`.

`MissingInput` and `EvaluationFailed` have `IsError = true` and a data-owned
`FlowError`. Error details use immutable `FlowValue` data and do not expose raw
exceptions. Expression failures do not stop later messages.

Output envelopes preserve correlation, trace, and headers while identifying the
consumed message through causation. Events use the same correlation id and the
injected `TimeProvider`.

`FlowResult<FlowValueAssertionResult>` is a real typed payload. It is not
implicitly unwrapped into `FlowValueAssertionResult`; downstream extraction or
routing requires an explicit result-aware component or mapper.

## Expression Context

By default, the expression engine receives the exact `FlowValue` as both
`input` and `value`. Supply an `IFlowMapContextFactory<FlowValue>` when an
expression needs additional data-shaped variables. Do not place clients,
mutable services, or secrets in expression contexts.

`FlowValueAssertionOptions` validates construction-time invariants: expression
is required, `InputType` cannot be empty, and `BoundedCapacity` must be positive.

## Composition

Add `FluxFlow.Components.Assertions.Composition` for canonical configuration or
fluent registration:

```csharp
services.AddKeyedSingleton<IFlowExpressionEngine>(
    "Resources.Expressions.Primary",
    expressionEngine);

registry.RegisterAssertion();
```

Parameterless registration owns canonical `data.assert`. Convert CLR inputs
explicitly at the application boundary and replace older Passed, Failed, and
Errors links with conditions over `Kind`, `IsError`, and `Error.Code`.
